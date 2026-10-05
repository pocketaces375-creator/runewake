using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using static ThemeTokens;

namespace Runewake.Client;

// ── Parchment placeholder color for missing card art ──
internal static class CardArtColors
{
    internal static readonly Color Parchment = Color.FromHtml("#D4C4A0");
    internal static readonly Color ParchmentDark = Color.FromHtml("#A89870");
}

/// <summary>
/// DECK FORGE — FABLE-040 rebuild of the chrome around the display case.
///
/// Left 70%: the display case (FABLE-039) — five cards across, grab-to-scroll, tap to add.
/// Top bar: title, class, a real search field, and the five strata filters as pills.
/// Right rail (one glass panel): the deck's name (tap to rename), a big count with a bar, the
/// curve, the list (tap a row to take one copy out), then FORGE DECK, LOAD A DECK and BACK as
/// full-width plates you can actually hit. Dialogs are centred glass cards with big type.
///
/// Trikzos: "The UI on the right is not easy to interact with though. Back button is super tiny."
/// </summary>
public partial class DeckBuilderScene : Control
{
    // ── Nodes ──
    private LineEdit _searchField = null!;
    private HBoxContainer _filterChipRow = null!;
    private VBoxContainer _cardGrid = null!;
    private ScrollContainer _gridScroll = null!;
    private Label _deckNameLabel = null!;
    private CurveBars _curve = null!;
    // FABLE-053: inspector (left), deck tray (bottom of the middle), stats rail (right)
    private PanelContainer _inspector = null!;
    private VBoxContainer _inspBody = null!;
    private string? _selectedId;
    private Control _tray = null!;
    private static bool s_trayOpen = true;   // remembered for the session
    private Label _statAvg = null!, _statTypes = null!, _factionLegend = null!;
    private FactionBar _factionBar = null!;
    private VBoxContainer _doesList = null!;
    private float _midX, _midW;
    private const float InspW = 470f, RailW = 520f, Mg = 24f, Gp = 18f, TrayOpenH = 340f, TrayClosedH = 70f, CaptionH = 30f;
    private Label _countLabel = null!;
    private Label _countHint = null!;
    private ColorRect _countBar = null!;
    private Control _countTrack = null!;
    private Button _forgeButton = null!;
    private Control _leftPanel = null!;
    private Control _rightRail = null!;
    private Control? _savedDecksContainer;   // only inside the Load dialog now
    private DragScroll? _gridDrag, _savedDrag;

    // Data
    private readonly List<CardDef> _allCards = new();
    private readonly List<string> _deckCardIds = new();
    private readonly List<CardDef> _coreCardIds = new();
    private ProgressionState? _saveState;
    private string _searchText = "";
    private int _selectedStrataIdx;
    private string _deckName = "My Deck";
    private readonly HashSet<string> _lockedCardIds = new();
    private List<string>? _pendingCoreCards;

    // FABLE-DROP-1: "Synergy" = the High-synergy shelf — every card that suits your class, best first
    private static readonly string[] StrataOptions = { "ALL", "SYNERGY", "VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN" };
    private static readonly string[] StrataLabels = { "All", "Synergy", "Verdant", "Ember", "Tide", "Hollow", "Dawn" };
    private static readonly Color[] StrataColors = { Gold, Gold, StrataVerdant, StrataEmber, StrataTide, StrataHollow, StrataDawn };

    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color MutedInk = new(0.62f, 0.57f, 0.47f);
    private static readonly Color Rule = new(0.79f, 0.66f, 0.30f, 0.22f);
    private const float TopH = 104f;

    private bool _captureMode;
    private bool _modified;

    public override void _Ready() => SceneGuard.Build(this, "DeckBuilderScene", Build, "res://scenes/main/Main.tscn", "Back to title");

    private void Build()
    {
        if (!CampaignContext.SaveManager.IsLoaded)
            CampaignContext.SaveManager.Initialize();
        if (CampaignContext.EncounterIndex.Count == 0)
        {
            CampaignContext.LoadEncounters();
            CampaignContext.LoadDigSites();
        }

        BuildUI();
        LoadCards();

        if (CampaignContext.Progression.DeckCardIds.Count > 0)
            _deckCardIds.AddRange(CampaignContext.Progression.DeckCardIds);

        if (_deckCardIds.Count == 0 && CampaignContext.AutoCaptureScreenshot && CampaignContext.CaptureDeckBuilderScreenshot)
        {
            SeedTestDeck();
            _captureMode = true;
        }

        if (CampaignContext.CoreCardIds != null && CampaignContext.CoreCardIds.Count > 0)
        {
            ApplyCoreCardsInternal(CampaignContext.CoreCardIds);
            CampaignContext.CoreCardIds = null;
        }
        if (_pendingCoreCards != null)
        {
            ApplyCoreCardsInternal(_pendingCoreCards);
            _pendingCoreCards = null;
        }

        string chosen = CampaignContext.ChosenClass;
        if (CampaignContext.CaptureOverrideStrataIdx >= 0)
            _selectedStrataIdx = CampaignContext.CaptureOverrideStrataIdx;
        else
            _selectedStrataIdx = 0;
        UpdateFilterChips();

        RefreshCardGrid();
        RefreshDeckList();
        RefreshCurve();
        UpdateCount();

        if (_captureMode)
        {
            var capTimer = GetTree().CreateTimer(0.8f);
            capTimer.Timeout += () =>
            {
                if (CampaignContext.AutoCaptureScreenshot)
                {
                    var image = GetViewport().GetTexture().GetImage();
                    if (image != null)
                    {
                        string baseName = CampaignContext.WideCaptureMode ? "deck_test_wide" : CampaignContext.PhoneCaptureMode ? "deck_test_phone" : "deck_test";
                        string path = ProjectPaths.Artifacts + $"/captures/{baseName}.png";
                        image.SavePng(path);
                        DebugCapture.WriteLayoutJson(this, baseName);
                        GD.Print($"[DeckBuilderScene] Captured to {path}");
                        DebugCapture.DumpLayoutJSON(baseName, this);
                    }
                }
                GetTree().Quit(0);
            };
        }
    }

    public void SetSaveState(ProgressionState state)
    {
        _saveState = state;
        if (_cardGrid != null) RefreshCardGrid();
    }
    public List<string> GetDeckCardIds() => new(_deckCardIds);
    public void SetCoreCards(List<string> coreIds)
    {
        if (_cardGrid == null) { _pendingCoreCards = new List<string>(coreIds); return; }
        ApplyCoreCardsInternal(coreIds);
    }

    private void ApplyCoreCardsInternal(List<string> coreIds)
    {
        _coreCardIds.Clear();
        _lockedCardIds.Clear();
        foreach (var id in coreIds)
        {
            var def = _allCards.FirstOrDefault(c => c.Id == id);
            if (def == null) continue;
            _coreCardIds.Add(def);
            _lockedCardIds.Add(id);
            if (!_deckCardIds.Contains(id)) _deckCardIds.Add(id);
        }
        RefreshDeckList();
        RefreshCurve();
        UpdateCount();
    }

    // ════════════════════════════════════════════════════════════════
    //  Layout
    // ════════════════════════════════════════════════════════════════

    private void BuildUI()
    {
        MouseFilter = MouseFilterEnum.Pass;
        var vp = GetViewportRect().Size;

        var bg = new ColorRect { Color = Color.FromHtml("#0B0A09"), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // ── Top bar ──
        var top = new Control { Position = Vector2.Zero, Size = new Vector2(vp.X, TopH), MouseFilter = MouseFilterEnum.Pass };
        AddChild(top);
        var topBg = new ColorRect { Color = new Color(0.082f, 0.072f, 0.061f), MouseFilter = MouseFilterEnum.Ignore };
        topBg.SetAnchorsPreset(LayoutPreset.FullRect);
        top.AddChild(topBg);
        top.AddChild(new ColorRect { Color = Rule, Position = new Vector2(0, TopH - 1), Size = new Vector2(vp.X, 1), MouseFilter = MouseFilterEnum.Ignore });

        var row = new HBoxContainer { Position = new Vector2(40, 0), Size = new Vector2(vp.X - 80, TopH), MouseFilter = MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 28);
        top.AddChild(row);

        var title = new Label { Text = "DECK FORGE", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontOverride("font", GetHeaderFont(44)); title.AddThemeFontSizeOverride("font_size", 44);
        title.AddThemeColorOverride("font_color", Gold);
        row.AddChild(title);

        if (!string.IsNullOrEmpty(CampaignContext.ChosenClass))
        {
            string cls = CampaignContext.ChosenClass.ToLowerInvariant();
            Color dot = cls switch
            {
                "warrior" => StrataEmber, "necromancer" or "occultist" or "rogue" => StrataHollow, "druid" => StrataVerdant,
                "battlemage" or "astrologist" => StrataTide, "paladin" => StrataDawn, _ => Gold,
            };
            row.AddChild(Pill(char.ToUpperInvariant(cls[0]) + cls.Substring(1), dot, false, 30));
        }

        _searchField = new LineEdit { PlaceholderText = "Search cards", CustomMinimumSize = new Vector2(420, 64), SizeFlagsVertical = SizeFlags.ShrinkCenter, ClearButtonEnabled = true };
        StyleEdit(_searchField, 30);
        _searchField.TextChanged += t => { _searchText = t; RefreshCardGrid(); };
        row.AddChild(_searchField);

        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        _filterChipRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _filterChipRow.AddThemeConstantOverride("separation", 12);
        row.AddChild(_filterChipRow);
        for (int i = 0; i < StrataOptions.Length; i++)
        {
            int idx = i;
            var chip = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 58), MouseDefaultCursorShape = CursorShape.PointingHand };
            chip.SetMeta("strata_idx", idx);
            var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            inner.SetAnchorsPreset(LayoutPreset.FullRect);
            inner.OffsetLeft = 18; inner.OffsetRight = -18;
            inner.AddThemeConstantOverride("separation", 10);
            chip.AddChild(inner);
            if (i > 0)
            {
                var sw = new Control { CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
                var swRect = new ColorRect { Color = StrataColors[i], MouseFilter = MouseFilterEnum.Ignore };
                swRect.SetAnchorsPreset(LayoutPreset.FullRect);
                sw.AddChild(swRect);
                inner.AddChild(sw);
            }
            var lbl = new Label { Text = StrataLabels[i], VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            lbl.AddThemeFontOverride("font", GetBodyFont(28)); lbl.AddThemeFontSizeOverride("font_size", 28);
            inner.AddChild(lbl);
            // the button's own text is empty; size it from the label
            chip.CustomMinimumSize = new Vector2(lbl.GetThemeFont("font").GetStringSize(StrataLabels[i], HorizontalAlignment.Left, -1, 28).X + 36 + (i > 0 ? 24 : 0), 58);
            chip.Pressed += () => { Click(); _selectedStrataIdx = idx; UpdateFilterChips(); RefreshCardGrid(); };
            _filterChipRow.AddChild(chip);
        }

        // ── FABLE-053 layout: inspector | display case over the deck tray | stats rail ──
        _midX = Mg + InspW + Gp;
        _midW = vp.X - _midX - (RailW + Mg + 16f);

        // Left: the inspector — the tapped card, large, with everything it does
        _inspector = new PanelContainer { Position = new Vector2(Mg, TopH + 20), Size = new Vector2(InspW, vp.Y - TopH - 44), MouseFilter = MouseFilterEnum.Stop };
        _inspector.AddThemeStyleboxOverride("panel", Glass(26));
        AddChild(_inspector);
        _inspBody = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _inspBody.AddThemeConstantOverride("separation", 10);
        _inspector.AddChild(_inspBody);

        // Middle: the display case
        _leftPanel = new Control { Position = new Vector2(_midX, TopH), MouseFilter = MouseFilterEnum.Pass };
        AddChild(_leftPanel);
        var caseLight = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = new GradientTexture2D
            {
                Width = 256, Height = 256, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.0f), FillTo = new Vector2(0.5f, 1.05f),
                Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(0.55f, 0.44f, 0.26f, 0.20f), new Color(0, 0, 0, 0) } },
            },
        };
        caseLight.SetAnchorsPreset(LayoutPreset.FullRect);
        _leftPanel.AddChild(caseLight);

        _gridScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto };
        _gridScroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _gridDrag = DragScroll.Attach(_gridScroll);
        var vsb = _gridScroll.GetVScrollBar();
        vsb.CustomMinimumSize = new Vector2(10, 0);
        vsb.AddThemeStyleboxOverride("scroll", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.18f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 });
        var grab = new StyleBoxFlat { BgColor = new Color(0.83f, 0.72f, 0.45f, 0.55f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        var grabHi = (StyleBoxFlat)grab.Duplicate(); grabHi.BgColor = new Color(0.9f, 0.8f, 0.5f, 0.85f);
        vsb.AddThemeStyleboxOverride("grabber", grab);
        vsb.AddThemeStyleboxOverride("grabber_highlight", grabHi);
        vsb.AddThemeStyleboxOverride("grabber_pressed", grabHi);
        _gridScroll.GuiInput += OnGridScrollInput;
        _leftPanel.AddChild(_gridScroll);

        _cardGrid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cardGrid.AddThemeConstantOverride("separation", 14);
        _gridScroll.AddChild(_cardGrid);

        // Middle, bottom: the deck tray (30 slots, or a slim bar when minimised)
        _tray = new Control { MouseFilter = MouseFilterEnum.Pass };
        AddChild(_tray);
        LayoutMiddle();

        // ── Right rail: the numbers ──
        float railX = vp.X - RailW - Mg, railW = RailW;
        _rightRail = new PanelContainer { Position = new Vector2(railX, TopH + 20), Size = new Vector2(railW, vp.Y - TopH - 44), MouseFilter = MouseFilterEnum.Stop };
        ((PanelContainer)_rightRail).AddThemeStyleboxOverride("panel", Glass());
        AddChild(_rightRail);

        var rail = new VBoxContainer();
        rail.AddThemeConstantOverride("separation", 0);
        _rightRail.AddChild(rail);
        VBoxContainer Section(float top = 0)
        {
            var m = new MarginContainer();
            m.AddThemeConstantOverride("margin_left", 32); m.AddThemeConstantOverride("margin_right", 32); m.AddThemeConstantOverride("margin_top", (int)top);
            rail.AddChild(m);
            var v = new VBoxContainer();
            v.AddThemeConstantOverride("separation", 8);
            m.AddChild(v);
            return v;
        }

        // name + count
        var nameSec = Section(20);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 12);
        nameSec.AddChild(nameRow);
        _deckNameLabel = new Label { Text = _deckName, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
        _deckNameLabel.AddThemeFontOverride("font", GetCardNameFont(38)); _deckNameLabel.AddThemeFontSizeOverride("font_size", 38);
        _deckNameLabel.AddThemeColorOverride("font_color", Gold);
        nameRow.AddChild(_deckNameLabel);
        var rename = Quiet("✎  Rename", 170, 52, 24);
        rename.Pressed += () => { Click(); ShowRenameDialog(); };
        nameRow.AddChild(rename);

        var countRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 56) };
        countRow.AddThemeConstantOverride("separation", 14);
        nameSec.AddChild(countRow);
        _countLabel = new Label { Text = "0 / 30", VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
        _countLabel.AddThemeFontOverride("font", GetHeaderFont(46)); _countLabel.AddThemeFontSizeOverride("font_size", 46);
        _countLabel.AddThemeColorOverride("font_color", Parchment);
        countRow.AddChild(_countLabel);
        _countHint = new Label { Text = "cards", VerticalAlignment = VerticalAlignment.Bottom, SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
        _countHint.AddThemeFontOverride("font", GetBodyFont(24)); _countHint.AddThemeFontSizeOverride("font_size", 24);
        _countHint.AddThemeColorOverride("font_color", MutedInk);
        countRow.AddChild(_countHint);

        _countTrack = new Control { CustomMinimumSize = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        var trackBg = new ColorRect { Color = new Color(1, 1, 1, 0.06f), MouseFilter = MouseFilterEnum.Ignore };
        trackBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _countTrack.AddChild(trackBg);
        _countBar = new ColorRect { Color = Gold, Position = Vector2.Zero, Size = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        _countTrack.AddChild(_countBar);
        nameSec.AddChild(_countTrack);

        // two stat tiles
        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 10);
        Section(12).AddChild(tiles);
        Label Tile(string caption)
        {
            var t = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 78), MouseFilter = MouseFilterEnum.Ignore };
            t.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.25f), BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.30f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10 });
            var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
            v.AddThemeConstantOverride("separation", 0);
            t.AddChild(v);
            var val = new Label { Text = "–", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            val.AddThemeFontOverride("font", GetHeaderFont(34)); val.AddThemeFontSizeOverride("font_size", 34);
            val.AddThemeColorOverride("font_color", Color.FromHtml("#F3DE95"));
            v.AddChild(val);
            var cap = new Label { Text = caption, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            cap.AddThemeFontOverride("font", GetHeaderFont(17)); cap.AddThemeFontSizeOverride("font_size", 17);
            cap.AddThemeColorOverride("font_color", MutedInk);
            v.AddChild(cap);
            tiles.AddChild(t);
            return val;
        }
        _statAvg = Tile("AVG COST");
        _statTypes = Tile("CRT · RIT · RELIC");

        // curve
        var curveSec = Section(12);
        curveSec.AddChild(SmallHeader("CURVE"));
        _curve = new CurveBars { CustomMinimumSize = new Vector2(0, 112), MouseFilter = MouseFilterEnum.Ignore };
        curveSec.AddChild(_curve);

        // factions
        var facSec = Section(10);
        facSec.AddChild(SmallHeader("FACTIONS"));
        _factionBar = new FactionBar { CustomMinimumSize = new Vector2(0, 14), MouseFilter = MouseFilterEnum.Ignore };
        facSec.AddChild(_factionBar);
        _factionLegend = new Label { MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        _factionLegend.AddThemeFontOverride("font", GetBodyFont(23)); _factionLegend.AddThemeFontSizeOverride("font_size", 23);
        _factionLegend.AddThemeColorOverride("font_color", new Color(0.80f, 0.74f, 0.61f));
        facSec.AddChild(_factionLegend);

        // what it does
        var doesSec = Section(10);
        doesSec.AddChild(SmallHeader("WHAT IT DOES"));
        _doesList = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _doesList.AddThemeConstantOverride("separation", 2);
        doesSec.AddChild(_doesList);

        rail.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        // plates
        var btnSec = Section(10);
        btnSec.AddThemeConstantOverride("separation", 12);
        _forgeButton = Plate("Forge deck", true, 72);
        _forgeButton.Pressed += () => { Click(); OnSaveDeck(); };
        btnSec.AddChild(_forgeButton);
        var pair = new HBoxContainer();
        pair.AddThemeConstantOverride("separation", 12);
        btnSec.AddChild(pair);
        var loadBtn = Plate("Load a deck", false, 64); loadBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        loadBtn.AddThemeFontSizeOverride("font_size", 28);
        loadBtn.Pressed += () => { Click(); ShowLoadDialog(); };
        pair.AddChild(loadBtn);
        var back = Plate("◀  Back", false, 64); back.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        back.AddThemeFontSizeOverride("font_size", 28);
        back.Pressed += () => { Click(); OnBack(); };
        pair.AddChild(back);
        rail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 22) });
    }

    /// <summary>FABLE-053: size the display case and the tray for the tray's current state.</summary>
    private void LayoutMiddle()
    {
        var vp = GetViewportRect().Size;
        float trayH = s_trayOpen ? TrayOpenH : TrayClosedH;
        float trayY = vp.Y - 20f - trayH;
        _leftPanel.Position = new Vector2(_midX, TopH);
        _leftPanel.Size = new Vector2(_midW, trayY - 10f - TopH);
        _tray.Position = new Vector2(_midX, trayY);
        _tray.Size = new Vector2(_midW, trayH);
    }

    private void ToggleTray()
    {
        Click();
        s_trayOpen = !s_trayOpen;
        LayoutMiddle();
        RefreshCardGrid(preserveScroll: true);
        RefreshDeckList();
    }

    private void UpdateFilterChips()
    {
        foreach (var child in _filterChipRow.GetChildren())
        {
            if (child is not Button btn) continue;
            int idx = (int)btn.GetMeta("strata_idx", -1);
            bool sel = idx == _selectedStrataIdx;
            var accent = idx >= 0 && idx < StrataColors.Length ? StrataColors[idx] : Gold;
            btn.AddThemeStyleboxOverride("normal", PillBox(accent, sel, false));
            btn.AddThemeStyleboxOverride("hover", PillBox(accent, sel, true));
            btn.AddThemeStyleboxOverride("pressed", PillBox(accent, true, true));
            btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            foreach (var l in btn.FindChildren("*", "Label", true, false).OfType<Label>())
                l.AddThemeColorOverride("font_color", sel ? Color.FromHtml("#F2DFA6") : new Color(0.78f, 0.72f, 0.60f));
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  The display case
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// FABLE-039: one card in the display case.
    ///
    /// The baked Runestone face IS the card (frame, name band, shields, cost diamond), so there
    /// is no extra box around it — just a soft shadow under it and a warm glow when you touch it.
    /// Owned cards sit at full colour with a gentle vibrance lift; cards you haven't found yet are
    /// shown dimmed (not see-through, which made the whole case look muddy) with a small
    /// "Not yet found" caption. Copies in the current deck show as a gold "×N" pill.
    /// </summary>
    private Control MakeGridCard(CardDef card, int ownedCount, int inDeckCount, float gridW)
    {
        float gridH = gridW * 608f / 416f;      // the bake's aspect
        const float captionH = CaptionH;

        bool isUnowned = ownedCount == 0;
        bool isAtLimit = !isUnowned && ownedCount <= inDeckCount;

        var cell = new Control
        {
            CustomMinimumSize = new Vector2(gridW, gridH + captionH),
            SizeFlagsHorizontal = 0, SizeFlagsVertical = 0,
            MouseFilter = MouseFilterEnum.Ignore,
        };

        // everything that lifts on touch lives in `face`, so the container never fights the scale
        var face = new Control
        {
            Position = Vector2.Zero, Size = new Vector2(gridW, gridH),
            PivotOffset = new Vector2(gridW / 2, gridH / 2),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        cell.AddChild(face);

        var shadowBox = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.0f),
            ShadowColor = new Color(0, 0, 0, 0.60f), ShadowSize = 22, ShadowOffset = new Vector2(0, 10),
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
        };
        var glowBox = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.0f),
            ShadowColor = new Color(0.93f, 0.76f, 0.36f, 0.55f), ShadowSize = 26,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
        };
        var halo = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        halo.SetAnchorsPreset(LayoutPreset.FullRect);
        halo.OffsetLeft = 6; halo.OffsetRight = -6; halo.OffsetTop = 8; halo.OffsetBottom = -4;
        halo.AddThemeStyleboxOverride("panel", shadowBox);
        face.AddChild(halo);

        var plate = new CardPlate();
        face.AddChild(plate);
        plate.Setup(card.Id, card.Attack, card.Vigor, gridW, gridH, card.Cost);
        if (!isUnowned) plate.Showcase();

        if (isUnowned)
            face.Modulate = new Color(0.46f, 0.44f, 0.42f, 1f);   // dimmed, still solid

        // in-deck pill, top-right (the cost diamond owns top-left)
        if (inDeckCount > 0)
        {
            var pill = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            pill.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.10f, 0.08f, 0.05f, 0.92f),
                BorderColor = Gold, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16,
                ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 2, ContentMarginBottom = 2,
            });
            var pl = new Label { Text = $"×{inDeckCount}", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            ApplyHeaderFont(pl, Mathf.RoundToInt(gridW * 0.10f));
            pl.AddThemeColorOverride("font_color", Color.FromHtml("#F2DFA6"));
            pill.AddChild(pl);
            face.AddChild(pill);
            pill.Position = new Vector2(gridW - gridW * 0.30f, gridW * 0.05f);
        }

        // FABLE-053: the card shown in the inspector wears a gold ring
        if (card.Id == _selectedId)
        {
            var ring = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            ring.SetAnchorsPreset(LayoutPreset.FullRect);
            ring.OffsetLeft = -7; ring.OffsetTop = -7; ring.OffsetRight = 7; ring.OffsetBottom = 7;
            ring.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0), BorderColor = Color.FromHtml("#F3DE95"),
                BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
                CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            });
            face.AddChild(ring);
        }

        // caption under the card
        var caption = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0, gridH + 4), Size = new Vector2(gridW, captionH - 4),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        ApplyBodyFont(caption, Mathf.RoundToInt(Mathf.Clamp(gridW * 0.12f, 18f, 24f)));
        if (isUnowned) { caption.Text = "Not yet found"; caption.AddThemeColorOverride("font_color", Color.FromHtml("#7A6F60")); }
        else if (isAtLimit) { caption.Text = ownedCount == 1 ? "In your deck" : $"All {ownedCount} in your deck"; caption.AddThemeColorOverride("font_color", Color.FromHtml("#9DBB84")); }
        else { caption.Text = $"Owned ×{ownedCount}"; caption.AddThemeColorOverride("font_color", Color.FromHtml("#BFB097")); }
        cell.AddChild(caption);

        // Tap to add
        var clickArea = new Button { FocusMode = FocusModeEnum.None, MouseDefaultCursorShape = CursorShape.PointingHand };
        clickArea.Position = Vector2.Zero;
        clickArea.Size = new Vector2(gridW, gridH);
        var transparent = new StyleBoxEmpty();
        clickArea.AddThemeStyleboxOverride("normal", transparent);
        clickArea.AddThemeStyleboxOverride("hover", transparent);
        clickArea.AddThemeStyleboxOverride("pressed", transparent);
        clickArea.AddThemeStyleboxOverride("disabled", transparent);
        clickArea.AddThemeStyleboxOverride("focus", transparent);
        cell.AddChild(clickArea);

        // FABLE-053: tap shows the card in the inspector; tap it again (or ADD TO DECK) to add a copy
        bool canAdd = !isUnowned && !isAtLimit;
        clickArea.Pressed += () => { if (_gridDrag?.Dragged != true) OnGridTap(card.Id); };

        Tween? lift = null;
        void Lift(float scale, bool glow)
        {
            if (!IsInstanceValid(face)) return;
            halo.AddThemeStyleboxOverride("panel", glow ? glowBox : shadowBox);
            lift?.Kill();
            if (CampaignContext.ReduceMotion) { face.Scale = Vector2.One * scale; return; }
            lift = face.CreateTween();
            lift.TweenProperty(face, "scale", Vector2.One * scale, 0.12f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        clickArea.MouseEntered += () => { if (canAdd) Lift(1.035f, true); };
        clickArea.MouseExited += () => Lift(1f, false);
        clickArea.ButtonDown += () => { if (canAdd) Lift(0.975f, true); };
        clickArea.ButtonUp += () => Lift(clickArea.IsHovered() && canAdd ? 1.035f : 1f, clickArea.IsHovered() && canAdd);

        return cell;
    }


    private void LoadCards()
    {
        _allCards.Clear();
        var packs = new[] {
            "res://content/cards/verdant.json", "res://content/cards/ember.json",
            "res://content/cards/tide.json", "res://content/cards/hollow.json",
            "res://content/cards/dawn.json"
        };
        foreach (var pack in packs)
        {
            string json = Godot.FileAccess.GetFileAsString(pack);
            _allCards.AddRange(CardLoader.LoadPackFromString(json));
        }
    }

    private void SeedTestDeck()
    {
        GD.Print("[DeckBuilderScene] Seeding test deck (capture mode)");
        string[] testIds = {
            "vrd_c_root_warden", "vrd_c_verdant_sproutling", "vrd_c_thornbark_defender",
            "vrd_r_bloomweaver", "vrd_u_grove_healer", "vrd_x_heartwood_relic",
            "vrd_c_wildwood_stalker", "vrd_u_canopy_archer", "vrd_u_saphoof_charger",
            "vrd_u_elder_treant", "emb_c_ember_hound", "emb_c_cinder_runner",
            "emb_c_forgeguard_berserker", "emb_u_wildfire_adept", "emb_u_lava_serpent",
            "tid_c_tidal_scholar", "tid_c_deep_one", "tid_c_silt_reader",
            "tid_u_brine_witch", "hol_c_skeletal_reaver", "hol_c_gravewrit_thrall",
            "hol_c_ossuary_guard", "dwn_r_sealing_light", "dwn_c_dawn_warder",
            "dwn_c_sunblade_recruit", "dwn_u_purifying_light", "dwn_c_golden_retainer",
            "dwn_c_dawnbreaker_charger", "dwn_u_steadfast_bulwark", "tid_c_abyssal_gaze"
        };
        _deckCardIds.AddRange(testIds);
        GD.Print($"[DeckBuilderScene] Seeded {_deckCardIds.Count} cards");
    }

    private CardDef? LookupCard(string id) => _allCards.FirstOrDefault(c => c.Id == id);

    // ── Smooth scrolling state ──
    private Tween _gridScrollTween;
    private float _gridScrollTarget = -1f;

    /// <summary>
    /// Animated mouse-wheel scrolling for the card grid. Consumes the raw
    /// wheel event (so the ScrollContainer's instant jump never runs) and
    /// tweens toward an accumulating target for a seamless glide.
    /// Touch drag/fling is untouched — that keeps native inertia.
    /// </summary>
    private void OnGridScrollInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mb || !mb.Pressed) return;
        if (mb.ButtonIndex != MouseButton.WheelUp && mb.ButtonIndex != MouseButton.WheelDown) return;

        var bar = _gridScroll.GetVScrollBar();
        float max = Mathf.Max(0f, (float)(bar.MaxValue - bar.Page));
        float step = 170f * (mb.Factor > 0f ? mb.Factor : 1f);
        float from = _gridScrollTarget >= 0f ? _gridScrollTarget : _gridScroll.ScrollVertical;
        _gridScrollTarget = Mathf.Clamp(
            from + (mb.ButtonIndex == MouseButton.WheelUp ? -step : step), 0f, max);

        _gridDrag?.Halt();
        _gridScrollTween?.Kill();
        _gridScrollTween = CreateTween();
        _gridScrollTween.TweenProperty(_gridScroll, "scroll_vertical", (int)_gridScrollTarget, 0.16f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _gridScrollTween.Finished += () => _gridScrollTarget = -1f;

        _gridScroll.AcceptEvent();
    }

    /// <summary>
    /// Restore the grid's scroll offset after a rebuild, once the new layout
    /// has settled (two frames: QueueFree flush + container re-layout).
    /// Without this, every card tap yanked the list back to the top.
    /// </summary>
    private async void RestoreGridScroll(int value)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (IsInstanceValid(_gridScroll))
            _gridScroll.ScrollVertical = value;
    }

    /// <summary>FABLE-DROP-1: the class whose High-synergy shelf the "Synergy" chip shows.</summary>
    private static string SynergyClass()
    {
        string c = CampaignContext.ChosenClass;
        if (string.IsNullOrEmpty(c) && CampaignContext.Profiles.Count > 0) c = CampaignContext.Profiles[0].ClassId;
        return string.IsNullOrEmpty(c) ? "warrior" : c.ToLowerInvariant();
    }

    private void RefreshCardGrid(bool preserveScroll = false)
    {
        int keepScroll = preserveScroll && _gridScroll != null ? _gridScroll.ScrollVertical : 0;

        foreach (var child in _cardGrid.GetChildren())
            child.QueueFree();

        string strata = StrataOptions[_selectedStrataIdx];

        bool synergy = strata == "SYNERGY";
        IEnumerable<CardDef> source = _allCards.Where(c => c.Type != CardType.TOKEN);
        if (synergy)
            source = Synergy.Shelf(SynergyClass(), source);
        var filtered = source
            .Where(c => synergy || strata == "ALL" || c.Strata.ToString() == strata)
            .Where(c => string.IsNullOrEmpty(_searchText) ||
                c.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        if (!synergy)
            filtered = filtered.OrderBy(c => c.Cost).ThenBy(c => c.Name);
        var filteredList = filtered.ToList();

        if (filteredList.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No cards match your filters.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (synergy) emptyLabel.Text = "No cards suit your class yet — new drops land here first.";
            emptyLabel.AddThemeColorOverride("font_color", TextMuted);
            emptyLabel.CustomMinimumSize = new Vector2(200, 60);
            _cardGrid.AddChild(emptyLabel);
            return;
        }

        // first visit: show the first card you own in the inspector
        if (_selectedId == null || LookupCard(_selectedId) == null)
            _selectedId = (filtered.FirstOrDefault(c => CampaignContext.Progression.Collection.ContainsKey(c.Id)) ?? filtered.First()).Id;

        // FABLE-053: size the cards so whole rows fit — two with the deck tray open, three minimised.
        float availWidth = _leftPanel.Size.X - 40;
        if (availWidth <= 0) availWidth = 800;
        float gap = 26f, rowSep = 14f, topPad = 12f;
        int rowsWanted = s_trayOpen ? 2 : 3;
        float byHeight = ((_leftPanel.Size.Y - topPad - rowsWanted * (CaptionH + rowSep)) / rowsWanted) * 416f / 608f;
        float byWidth = (availWidth - 5 * gap) / 6f;
        float cardW = Mathf.Clamp(Mathf.Min(byHeight, byWidth), 120f, 416f);
        int columns = Mathf.Clamp(Mathf.FloorToInt((availWidth + gap) / (cardW + gap)), 3, 8);
        _cardGrid.AddThemeConstantOverride("separation", Mathf.RoundToInt(rowSep));
        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, topPad), MouseFilter = MouseFilterEnum.Ignore });

        for (int i = 0; i < filteredList.Count; i += columns)
        {
            // CenterContainer horizontally centers its single child (the HBox row)
            var rowOuter = new CenterContainer();
            rowOuter.SizeFlagsHorizontal = (SizeFlags)3;
            _cardGrid.AddChild(rowOuter);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));
            rowOuter.AddChild(row);

            for (int j = 0; j < columns && i + j < filteredList.Count; j++)
            {
                var card = filteredList[i + j];
                int owned = CampaignContext.Progression.Collection.TryGetValue(card.Id, out var ownedCount) ? ownedCount : 0;
                int inDeck = _deckCardIds.Count(id => id == card.Id);
                var item = MakeGridCard(card, owned, inDeck, cardW);
                row.AddChild(item);
            }
        }

        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 30), MouseFilter = MouseFilterEnum.Ignore });

        ShowInspector();

        if (preserveScroll && keepScroll > 0)
            RestoreGridScroll(keepScroll);
        else
            _gridDrag?.Halt();
    }


    // ════════════════════════════════════════════════════════════════
    //  Rail refreshes
    // ════════════════════════════════════════════════════════════════

    /// <summary>FABLE-053: the deck lives in the tray under the display case, and its numbers in the rail.</summary>
    private void RefreshDeckList()
    {
        BuildTray();
        RefreshStats();
    }

    private List<string> SortedDeck() => _deckCardIds
        .Select(id => (id, def: LookupCard(id))).Where(x => x.def != null)
        .OrderBy(x => x.def!.Cost).ThenBy(x => x.def!.Name).Select(x => x.id).ToList();

    private void BuildTray()
    {
        foreach (var c in _tray.GetChildren()) c.QueueFree();
        var size = _tray.Size;
        var bg = new Panel { MouseFilter = MouseFilterEnum.Stop };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        bg.AddThemeStyleboxOverride("panel", Glass());
        _tray.AddChild(bg);

        var deck = SortedDeck();
        int max = DeckRules.MaxSize;
        int open = Mathf.Max(0, max - deck.Count);

        Button ToggleButton(string text, Vector2 pos, Vector2 sz)
        {
            var t = Quiet(text, sz.X, sz.Y, 20);
            t.Position = pos; t.Size = sz;
            t.AddThemeFontOverride("font", GetHeaderFont(18)); t.AddThemeFontSizeOverride("font_size", 18);
            t.AddThemeColorOverride("font_color", Color.FromHtml("#F3DE95"));
            t.Pressed += ToggleTray;
            _tray.AddChild(t);
            return t;
        }
        Label Text(string text, Font font, int px, Color col, Vector2 pos, Vector2 sz)
        {
            var l = new Label { Text = text, Position = pos, Size = sz, VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", font); l.AddThemeFontSizeOverride("font_size", px);
            l.AddThemeColorOverride("font_color", col);
            _tray.AddChild(l);
            return l;
        }

        if (!s_trayOpen)
        {
            // slim bar: title, a fanned stack of the deck, open slots, SHOW DECK
            Text($"YOUR DECK  ·  {deck.Count} / {max}", GetHeaderFont(22), 22, Gold, new Vector2(26, 0), new Vector2(300, size.Y));
            float mh = size.Y - 22f, mw = mh * 416f / 608f, x0 = 340f;
            float room = size.X - 240f - 200f - x0;   // leave space for the open-slots note and SHOW DECK
            float step = deck.Count > 1 ? Mathf.Min(mw - 8f, (room - mw) / (deck.Count - 1)) : mw;
            for (int i = 0; i < deck.Count; i++)
                _tray.AddChild(Mini(deck[i], new Vector2(x0 + i * step, 11f), new Vector2(mw, mh), removable: false));
            Text(open > 0 ? $"+ {open} open slots" : "deck full", GetBodyFont(24), 24, MutedInk,
                new Vector2(x0 + (deck.Count > 0 ? (deck.Count - 1) * step + mw + 24f : 0f), 0), new Vector2(200, size.Y));
            ToggleButton("SHOW DECK  ▴", new Vector2(size.X - 214, (size.Y - 46) / 2), new Vector2(190, 46));
            return;
        }

        // open: title block on the left, 3 rows of 10 on the right
        const int cols = 10, rows = 3; const float gap = 10f, pad = 22f;
        float sh = (size.Y - 2 * pad - (rows - 1) * gap) / rows, sw = sh * 416f / 608f;
        float blockW = cols * sw + (cols - 1) * gap;
        float bx = size.X - pad - blockW;
        Text("YOUR DECK", GetHeaderFont(24), 24, Gold, new Vector2(30, 24), new Vector2(bx - 50, 34));
        Text($"{deck.Count}", GetHeaderFont(60), 60, Color.FromHtml("#F3DE95"), new Vector2(30, 62), new Vector2(120, 76)).AutowrapMode = TextServer.AutowrapMode.Off;
        float cw = GetHeaderFont(60).GetStringSize($"{deck.Count}", HorizontalAlignment.Left, -1, 60).X;
        Text($"/ {max}", GetHeaderFont(32), 32, MutedInk, new Vector2(30 + cw + 14, 80), new Vector2(120, 56));
        Text(open > 0 ? $"{open} open slots" : "Deck full", GetBodyFont(28), 28, new Color(0.80f, 0.74f, 0.61f), new Vector2(30, 150), new Vector2(bx - 50, 36));
        Text("tap a card to take it out", GetBodyFont(23), 23, MutedInk, new Vector2(30, 186), new Vector2(bx - 50, 32));
        ToggleButton("MINIMIZE  ▾", new Vector2(30, size.Y - 24 - 48), new Vector2(190, 48));

        for (int i = 0; i < cols * rows; i++)
        {
            int r = i / cols, c = i % cols;
            var pos = new Vector2(bx + c * (sw + gap), pad + r * (sh + gap));
            if (i < deck.Count) _tray.AddChild(Mini(deck[i], pos, new Vector2(sw, sh), removable: true));
            else
            {
                var slot = new Panel { Position = pos, Size = new Vector2(sw, sh), MouseFilter = MouseFilterEnum.Ignore };
                slot.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = new Color(0, 0, 0, 0.18f), BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.22f),
                    BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                    CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                });
                _tray.AddChild(slot);
            }
        }
    }

    /// <summary>A small card face in the tray. Tap to take one copy out (core cards stay).</summary>
    private Control Mini(string cardId, Vector2 pos, Vector2 size, bool removable)
    {
        bool locked = _lockedCardIds.Contains(cardId);
        var b = new Button { Position = pos, Size = size, FocusMode = FocusModeEnum.None, ClipContents = true,
            MouseFilter = removable ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore,
            MouseDefaultCursorShape = removable && !locked ? CursorShape.PointingHand : CursorShape.Arrow };
        var clear = new StyleBoxEmpty();
        foreach (var st in new[] { "normal", "hover", "pressed", "disabled", "focus" }) b.AddThemeStyleboxOverride(st, clear);
        string ip = $"res://content/art/cards_baked/{cardId}.webp";
        var tex = ResourceLoader.Exists(ip) ? ResourceLoader.Load<Texture2D>(ip) : null;
        // ExpandMode before Size: with the default KeepSize, Size is clamped up to the texture's 416x608
        var art = new TextureRect { Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        art.Size = size;
        b.AddChild(art);
        var ring = new Panel { Size = size, MouseFilter = MouseFilterEnum.Ignore };
        ring.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0), BorderColor = locked ? Gold : new Color(0, 0, 0, 0.55f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        });
        b.AddChild(ring);
        if (removable && !locked)
        {
            b.Pressed += () => { Click(); RemoveFromDeck(cardId); };
            b.MouseEntered += () => b.Modulate = new Color(1.15f, 1.1f, 1f);
            b.MouseExited += () => b.Modulate = Colors.White;
        }
        else if (removable)
            b.Pressed += () => Toast("Core cards stay in this deck.");
        return b;
    }

    /// <summary>FABLE-053: average cost, the type split, factions and what the deck does.</summary>
    private void RefreshStats()
    {
        var defs = _deckCardIds.Select(LookupCard).Where(d => d != null).Select(d => d!).ToList();
        _statAvg.Text = defs.Count == 0 ? "–" : (defs.Average(d => d.Cost)).ToString("0.0");
        int crt = defs.Count(d => d.Type == CardType.CREATURE);
        int rit = defs.Count(d => d.Type == CardType.RITUAL);
        int rel = defs.Count(d => d.Type == CardType.RELIC || d.Type == CardType.ARTIFACT);
        _statTypes.Text = $"{crt} / {rit} / {rel}";

        var fac = new List<(Color col, int n, string name)>();
        for (int i = 1; i < StrataOptions.Length; i++)
        {
            int n = defs.Count(d => d.Strata.ToString() == StrataOptions[i]);
            if (n > 0) fac.Add((StrataColors[i], n, StrataLabels[i]));
        }
        _factionBar.Segments = fac.Select(f => (f.col, f.n)).ToList();
        _factionBar.QueueRedraw();
        _factionLegend.Text = fac.Count == 0 ? "No cards yet" : string.Join("   ", fac.Select(f => $"● {f.name} {f.n}"));

        foreach (var c in _doesList.GetChildren()) c.QueueFree();
        bool Has(CardDef d, Func<EffectDef, bool> pred) => d.Abilities.Any(a => a.Effects.Any(pred));
        bool HitsEnemy(EffectDef e) => e.Target != null && (e.Target.Scope == Scope.ENEMY_CREATURE || e.Target.Scope == Scope.ANY_CREATURE);
        var rows = new List<(string k, int v)>
        {
            ("Card draw", defs.Count(d => Has(d, e => e.Op == Op.DRAW || e.Op == Op.EXCAVATE))),
            ("Removal", defs.Count(d => Has(d, e => (e.Op is Op.DAMAGE or Op.DESTROY or Op.BOUNCE or Op.SILENCE) && HitsEnemy(e)))),
        };
        foreach (var g in defs.SelectMany(d => d.Keywords).GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(2))
            rows.Add((RulesTextRenderer.FormatKeyword(g.Key), g.Count()));
        foreach (var (k, v) in rows)
        {
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            var kl = new Label { Text = k, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            kl.AddThemeFontOverride("font", GetBodyFont(26)); kl.AddThemeFontSizeOverride("font_size", 26);
            kl.AddThemeColorOverride("font_color", new Color(0.80f, 0.74f, 0.61f));
            var vl = new Label { Text = v.ToString(), MouseFilter = MouseFilterEnum.Ignore };
            vl.AddThemeFontOverride("font", GetHeaderFont(24)); vl.AddThemeFontSizeOverride("font_size", 24);
            vl.AddThemeColorOverride("font_color", Color.FromHtml("#F3DE95"));
            row.AddChild(kl); row.AddChild(vl);
            _doesList.AddChild(row);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  FABLE-053: the inspector
    // ════════════════════════════════════════════════════════════════

    private int OwnedCount(string id) => CampaignContext.Progression.Collection.TryGetValue(id, out var n) ? n : 0;

    private void OnGridTap(string cardId)
    {
        if (cardId != _selectedId)
        {
            Click();
            _selectedId = cardId;
            RefreshCardGrid(preserveScroll: true);
            return;
        }
        // second tap on the card already shown = add a copy
        int owned = OwnedCount(cardId), inDeck = _deckCardIds.Count(i => i == cardId);
        if (owned == 0) { Toast("You haven't found this card yet."); return; }
        if (inDeck >= owned) { Toast(owned == 1 ? "Your only copy is already in the deck." : $"All {owned} copies are already in the deck."); return; }
        AddToDeck(cardId);
    }

    private static readonly FontFile? ItalicFont = ResourceLoader.Load<FontFile>("res://assets/fonts/CormorantGaramond-Italic.ttf");

    private void ShowInspector()
    {
        if (_inspBody == null) return;
        foreach (var c in _inspBody.GetChildren()) c.QueueFree();
        var def = _selectedId != null ? LookupCard(_selectedId) : null;
        float w = InspW - 52f;
        float availH = _inspector.Size.Y - 52f;

        if (def == null)
        {
            var hint = new Label { Text = "Tap a card to see what it does.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(w, 0), MouseFilter = MouseFilterEnum.Ignore };
            hint.AddThemeFontOverride("font", GetBodyFont(30)); hint.AddThemeFontSizeOverride("font_size", 30);
            hint.AddThemeColorOverride("font_color", MutedInk);
            _inspBody.AddChild(hint);
            return;
        }

        int owned = OwnedCount(def.Id), inDeck = _deckCardIds.Count(i => i == def.Id);
        bool locked = _lockedCardIds.Contains(def.Id);

        // the card itself
        float pw = 240f, ph = pw * 608f / 416f;
        var holder = new Control { CustomMinimumSize = new Vector2(w, ph), MouseFilter = MouseFilterEnum.Ignore };
        var plate = new CardPlate();
        holder.AddChild(plate);
        plate.Setup(def.Id, def.Attack, def.Vigor, pw, ph, def.Cost);
        plate.Position = new Vector2((w - pw) / 2f, 0);
        if (owned > 0) plate.Showcase(); else plate.Modulate = new Color(0.55f, 0.53f, 0.50f);
        _inspBody.AddChild(holder);

        Label L(string text, Font font, int px, Color col, bool wrap = true)
        {
            var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(w, 0), MouseFilter = MouseFilterEnum.Ignore };
            if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.AddThemeFontOverride("font", font); l.AddThemeFontSizeOverride("font_size", px);
            l.AddThemeColorOverride("font_color", col);
            _inspBody.AddChild(l);
            return l;
        }

        int si = Array.IndexOf(StrataOptions, def.Strata.ToString());
        Color sc = si > 0 ? StrataColors[si].Lightened(0.35f) : Gold;
        string type = def.Type switch { CardType.RITUAL => "RITUAL", CardType.RELIC => "RELIC", CardType.ARTIFACT => "ARTIFACT", CardType.TOKEN => "TOKEN", _ => "CREATURE" };
        string stats = def.Attack.HasValue && def.Vigor.HasValue && def.Type == CardType.CREATURE ? $"  ·  {def.Attack}/{def.Vigor}" : "";
        L($"{def.Strata}  ·  {type}  ·  COST {def.Cost}{stats}".ToUpperInvariant(), GetHeaderFont(18), 18, sc, wrap: false);

        var name = L(def.Name, GetCardNameFont(40), 40, Color.FromHtml("#F0C85E"), wrap: false);
        var nf = name.GetThemeFont("font"); int npx = 40;
        while (npx > 26 && nf.GetStringSize(def.Name, HorizontalAlignment.Left, -1, npx).X > w) npx--;
        name.AddThemeFontSizeOverride("font_size", npx);

        var rule = new ColorRect { Color = Rule, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore };
        _inspBody.AddChild(rule);

        string rules = RulesTextRenderer.RenderAbilityTextOnly(def);
        Label? rulesL = string.IsNullOrWhiteSpace(rules) ? null : L(rules, GetBodyFont(32), 32, Color.FromHtml("#F2E8D2"));

        var reminders = new List<Label>();
        foreach (var kw in def.Keywords)
        {
            var chipRow = new CenterContainer { CustomMinimumSize = new Vector2(w, 0), MouseFilter = MouseFilterEnum.Ignore };
            var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            chip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.79f, 0.66f, 0.30f, 0.08f), BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.55f),
                BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
                ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 2, ContentMarginBottom = 2,
            });
            var cl = new Label { Text = RulesTextRenderer.FormatKeyword(kw).ToUpperInvariant(), MouseFilter = MouseFilterEnum.Ignore };
            cl.AddThemeFontOverride("font", GetHeaderFont(18)); cl.AddThemeFontSizeOverride("font_size", 18);
            cl.AddThemeColorOverride("font_color", Color.FromHtml("#F3DE95"));
            chip.AddChild(cl);
            chipRow.AddChild(chip);
            _inspBody.AddChild(chipRow);
            string rem = RulesSlab.KeywordReminder(kw, def.Type);
            if (!string.IsNullOrEmpty(rem)) reminders.Add(L(rem, GetBodyFont(25), 25, new Color(0.73f, 0.67f, 0.55f)));
        }
        // FABLE-SKILLS-1: game terms the abilities use (Excavate, Bury, Burn, Stun, Sigil, Tribute…) explained too
        foreach (var term in RulesTextRenderer.KeywordReminderLines(def).Skip(def.Keywords.Count))
            reminders.Add(L(term, GetBodyFont(25), 25, new Color(0.73f, 0.67f, 0.55f)));

        _inspBody.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        Label? flavor = null;
        if (!string.IsNullOrWhiteSpace(def.Flavor))
            flavor = L($"“{def.Flavor}”", ItalicFont ?? GetBodyFont(24), 24, new Color(0.55f, 0.49f, 0.38f));

        string btnText = owned == 0 ? "Not yet found"
            : locked ? "Core card"
            : inDeck >= owned ? (owned == 1 ? "In your deck" : $"All {owned} in your deck")
            : inDeck > 0 ? $"Add another  ·  {inDeck} in deck" : "Add to deck";
        var add = Plate(btnText, true, 76);
        add.AddThemeFontSizeOverride("font_size", 28);
        add.Disabled = owned == 0 || locked || inDeck >= owned;
        string id = def.Id;
        add.Pressed += () => AddToDeck(id);
        _inspBody.AddChild(add);

        // shrink the words (never the card) until everything fits the panel
        float Height(Label l)
        {
            var f = l.GetThemeFont("font"); int px = l.GetThemeFontSize("font_size");
            float lh = f.GetHeight(px);
            if (l.AutowrapMode == TextServer.AutowrapMode.Off) return lh;
            return Mathf.Max(1, Mathf.RoundToInt(f.GetMultilineStringSize(l.Text, HorizontalAlignment.Left, w, px).Y / lh)) * lh;
        }
        float sep = _inspBody.GetThemeConstant("separation");
        float Total()
        {
            float t = ph + 1 + 76 + Height(name) + 24;   // card, rule, button, name, kicker
            if (rulesL != null) t += Height(rulesL);
            t += def.Keywords.Count * 30;                 // chips
            foreach (var r in reminders) t += Height(r);
            if (flavor != null) t += Height(flavor);
            int n = 5 + (rulesL != null ? 1 : 0) + def.Keywords.Count + reminders.Count + (flavor != null ? 1 : 0);
            return t + n * sep;
        }
        int step = 0;
        while (Total() > availH && step < 10)
        {
            step++;
            if (rulesL != null) rulesL.AddThemeFontSizeOverride("font_size", 32 - step);
            foreach (var r in reminders) r.AddThemeFontSizeOverride("font_size", 25 - step);
            if (flavor != null) flavor.AddThemeFontSizeOverride("font_size", 24 - step);
        }
        if (flavor != null && Total() > availH) flavor.Visible = false;
    }

    private void RefreshCurve()
    {
        int[] curve = new int[8];
        foreach (var id in _deckCardIds)
        {
            var def = LookupCard(id);
            if (def != null) curve[Mathf.Clamp(def.Cost, 0, 7)]++;
        }
        _curve.Counts = curve;
        _curve.QueueRedraw();
    }

    private void UpdateCount()
    {
        int total = _deckCardIds.Count;
        _countLabel.Text = $"{total} / {DeckRules.MaxSize}";
        var result = DeckValidator.Validate(_deckCardIds, LookupCard);
        _countHint.Text = result.IsValid ? "ready to forge" : total < DeckRules.MinSize ? $"{DeckRules.MinSize - total} more to go" : result.Errors.FirstOrDefault() ?? "";
        float pct = Mathf.Clamp((float)total / DeckRules.MaxSize, 0f, 1f);
        _countBar.Size = new Vector2(pct * _countTrack.Size.X, 10);
        _countBar.Color = result.IsValid ? Moss : Gold;
        // the track has no width until the first layout pass
        CallDeferred(nameof(FitCountBar));
        _forgeButton.Disabled = !result.IsValid;
    }

    private void FitCountBar()
    {
        if (!IsInstanceValid(_countBar)) return;
        float pct = Mathf.Clamp((float)_deckCardIds.Count / DeckRules.MaxSize, 0f, 1f);
        _countBar.Size = new Vector2(pct * _countTrack.Size.X, 10);
    }

    // ════════════════════════════════════════════════════════════════
    //  Add / remove
    // ════════════════════════════════════════════════════════════════

    private void AddToDeck(string cardId)
    {
        if (_lockedCardIds.Contains(cardId)) return;
        var result = DeckValidator.CanAdd(_deckCardIds, cardId, LookupCard);
        if (!result.IsValid) { Toast(result.Errors.FirstOrDefault() ?? "Can't add that card."); return; }
        _deckCardIds.Add(cardId);
        _modified = true;
        Click();
        RefreshCardGrid(preserveScroll: true);
        RefreshDeckList();
        RefreshCurve();
        UpdateCount();
    }

    private void RemoveFromDeck(string cardId)
    {
        if (_lockedCardIds.Contains(cardId)) return;
        int idx = _deckCardIds.LastIndexOf(cardId);
        if (idx < 0) return;
        _deckCardIds.RemoveAt(idx);
        _modified = true;
        RefreshCardGrid(preserveScroll: true);
        RefreshDeckList();
        RefreshCurve();
        UpdateCount();
    }

    // ════════════════════════════════════════════════════════════════
    //  Dialogs
    // ════════════════════════════════════════════════════════════════

    private void OnBack()
    {
        if (!_modified) { GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn"); return; }
        var (overlay, body) = Modal(1000, 420);
        body.AddChild(ModalTitle("LEAVE THE FORGE?"));
        body.AddChild(ModalText("This deck has changes you haven't forged. Leave now and they're lost."));
        var rowB = ButtonRow(body);
        var keep = Plate("Keep working", true, 84, 380); keep.Pressed += () => { Click(); overlay.QueueFree(); }; rowB.AddChild(keep);
        var leave = Plate("Leave anyway", false, 84, 380); leave.Pressed += () => { Click(); overlay.QueueFree(); GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn"); }; rowB.AddChild(leave);
    }

    private void OnSaveDeck()
    {
        var validation = DeckValidator.Validate(_deckCardIds, LookupCard);
        if (!validation.IsValid) { Toast(validation.Errors.FirstOrDefault() ?? "The deck isn't ready."); return; }
        ShowSaveNameDialog();
    }

    private void ShowRenameDialog()
    {
        var (overlay, body) = Modal(1000, 440);
        body.AddChild(ModalTitle("NAME YOUR DECK"));
        var edit = ModalEdit(_deckName);
        body.AddChild(edit);
        var rowB = ButtonRow(body);
        var cancel = Plate("Cancel", false, 84, 300); cancel.Pressed += () => { Click(); overlay.QueueFree(); }; rowB.AddChild(cancel);
        var ok = Plate("Rename", true, 84, 300);
        void Commit() { string n = edit.Text.Trim(); if (n.Length == 0) return; _deckName = n; _deckNameLabel.Text = n; _modified = true; overlay.QueueFree(); }
        ok.Pressed += () => { Click(); Commit(); }; rowB.AddChild(ok);
        edit.TextSubmitted += _ => Commit();
        edit.CallDeferred(Control.MethodName.GrabFocus);
        edit.SelectAll();
    }

    private void ShowSaveNameDialog()
    {
        var (overlay, body) = Modal(1000, 460);
        body.AddChild(ModalTitle("FORGE THIS DECK"));
        body.AddChild(ModalText("Give it a name. It becomes your active deck and shows up in the Arena and online."));
        var edit = ModalEdit(_deckName);
        body.AddChild(edit);
        var rowB = ButtonRow(body);
        var cancel = Plate("Cancel", false, 84, 300); cancel.Pressed += () => { Click(); overlay.QueueFree(); }; rowB.AddChild(cancel);
        var ok = Plate("Forge", true, 84, 300);
        void Commit()
        {
            string n = edit.Text.Trim();
            if (n.Length == 0) return;
            _deckName = n; _deckNameLabel.Text = n;
            overlay.QueueFree();
            if (CampaignContext.Progression.SavedDecks.ContainsKey(_deckName)) ShowOverwriteConfirmDialog();
            else PersistDeck();
        }
        ok.Pressed += () => { Click(); Commit(); }; rowB.AddChild(ok);
        edit.TextSubmitted += _ => Commit();
        edit.CallDeferred(Control.MethodName.GrabFocus);
        edit.SelectAll();
    }

    private void ShowOverwriteConfirmDialog()
    {
        var (overlay, body) = Modal(1000, 420);
        body.AddChild(ModalTitle("REPLACE IT?"));
        body.AddChild(ModalText($"You already have a deck called \"{_deckName}\". Forging replaces it."));
        var rowB = ButtonRow(body);
        var cancel = Plate("Keep the old one", false, 84, 380); cancel.Pressed += () => { Click(); overlay.QueueFree(); }; rowB.AddChild(cancel);
        var ok = Plate("Replace", true, 84, 300); ok.Pressed += () => { Click(); overlay.QueueFree(); PersistDeck(); }; rowB.AddChild(ok);
    }

    private void ShowLoadDialog()
    {
        var saved = CampaignContext.Progression.SavedDecks;
        var (overlay, body) = Modal(1100, 760);
        body.AddChild(ModalTitle("YOUR SAVED DECKS"));
        if (saved.Count == 0)
        {
            body.AddChild(ModalText("Nothing forged yet. Build a deck of 30 and press Forge deck."));
            body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        }
        else
        {
            var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            body.AddChild(scroll);
            var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 12);
            scroll.AddChild(list);
            _savedDecksContainer = list;
            _savedDrag = DragScroll.Attach(scroll);
            foreach (var (deckName, cardIds) in saved.OrderBy(k => k.Key))
            {
                var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 84), MouseDefaultCursorShape = CursorShape.PointingHand };
                var box = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.03f), BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.22f), BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12, CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12 };
                var hov = (StyleBoxFlat)box.Duplicate(); hov.BgColor = new Color(0.79f, 0.66f, 0.30f, 0.10f); hov.BorderColor = new Color(0.79f, 0.66f, 0.30f, 0.5f);
                b.AddThemeStyleboxOverride("normal", box); b.AddThemeStyleboxOverride("hover", hov); b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed()); b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
                list.AddChild(b);
                var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                inner.SetAnchorsPreset(LayoutPreset.FullRect);
                inner.OffsetLeft = 26; inner.OffsetRight = -26;
                b.AddChild(inner);
                var nm = new Label { Text = deckName, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                nm.AddThemeFontOverride("font", GetBodyFont(32)); nm.AddThemeFontSizeOverride("font_size", 32);
                nm.AddThemeColorOverride("font_color", Parchment);
                inner.AddChild(nm);
                var ct = new Label { Text = $"{cardIds.Count} cards", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
                ct.AddThemeFontOverride("font", GetBodyFont(26)); ct.AddThemeFontSizeOverride("font_size", 26);
                ct.AddThemeColorOverride("font_color", MutedInk);
                inner.AddChild(ct);
                string cn = deckName; var cc = cardIds;
                b.Pressed += () => { if (_savedDrag?.Dragged == true) return; Click(); overlay.QueueFree(); LoadDeck(cn, cc); };
            }
        }
        var rowB = ButtonRow(body);
        var close = Plate("Close", true, 84, 300); close.Pressed += () => { Click(); overlay.QueueFree(); }; rowB.AddChild(close);
    }

    private void LoadDeck(string deckName, List<string> cardIds)
    {
        if (!_modified) { DoLoadDeck(deckName, cardIds); return; }
        var (overlay, body) = Modal(1000, 420);
        body.AddChild(ModalTitle("LOAD THIS DECK?"));
        body.AddChild(ModalText("The deck you're working on has changes you haven't forged. They'll be lost."));
        var rowB = ButtonRow(body);
        var cancel = Plate("Cancel", false, 84, 300); cancel.Pressed += () => { Click(); overlay.QueueFree(); }; rowB.AddChild(cancel);
        var ok = Plate("Load it", true, 84, 300); ok.Pressed += () => { Click(); overlay.QueueFree(); DoLoadDeck(deckName, cardIds); }; rowB.AddChild(ok);
    }

    private void DoLoadDeck(string deckName, List<string> cardIds)
    {
        _deckCardIds.Clear();
        _deckCardIds.AddRange(cardIds);
        _deckName = deckName;
        _deckNameLabel.Text = deckName;
        _modified = false;
        RefreshCardGrid();
        RefreshDeckList();
        RefreshCurve();
        UpdateCount();
        Toast($"Loaded {deckName}");
    }

    private void PersistDeck()
    {
        string classId = CampaignContext.ChosenClass;
        if (string.IsNullOrEmpty(classId))
            classId = CampaignContext.Profiles.Count > 0 ? CampaignContext.Profiles[0].ClassId : "warrior";

        var prog = CampaignContext.Progression;
        prog.SavedDecks[_deckName] = new List<string>(_deckCardIds);
        prog.DeckCardIds.Clear();
        prog.DeckCardIds.AddRange(_deckCardIds);
        CampaignContext.PlayerDeckIds.Clear();
        CampaignContext.PlayerDeckIds.AddRange(_deckCardIds);
        CampaignContext.SaveDeck(_deckName, classId, _deckCardIds);
        string deckId = $"{classId}_{_deckName.ToLowerInvariant().Replace(" ", "_")}";
        if (CampaignContext.ActiveProfile != null)
        {
            CampaignContext.ActiveProfile.ActiveDeckId = deckId;
            CampaignContext.SaveCampaignProfile();
        }
        CampaignContext.SaveManager.Save();
        _modified = false;
        Toast($"{_deckName} forged — it's your active deck now.");
    }

    // ════════════════════════════════════════════════════════════════
    //  Bits
    // ════════════════════════════════════════════════════════════════

    private static Color TypeColour(CardType t) => t switch
    {
        CardType.RITUAL => Color.FromHtml("#2C6098"),
        CardType.RELIC or CardType.ARTIFACT => Color.FromHtml("#704896"),
        CardType.CURSE => Color.FromHtml("#6A3A3A"),
        _ => Color.FromHtml("#427A38"),
    };

    private static StyleBoxFlat Glass(int margin = 0, Color? border = null) => new()
    {
        BgColor = new Color(0.082f, 0.072f, 0.061f, 0.90f),
        BorderColor = border ?? new Color(0.79f, 0.66f, 0.30f, 0.38f),
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
        ShadowColor = new Color(0, 0, 0, 0.55f), ShadowSize = 24,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
    };

    private static Control SmallHeader(string text, string? hint = null)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        var row = new HBoxContainer();
        v.AddChild(row);
        var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", GetHeaderFont(24)); l.AddThemeFontSizeOverride("font_size", 24);
        l.AddThemeColorOverride("font_color", Gold);
        row.AddChild(l);
        if (hint != null)
        {
            var h = new Label { Text = hint, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
            h.AddThemeFontOverride("font", GetBodyFont(22)); h.AddThemeFontSizeOverride("font_size", 22);
            h.AddThemeColorOverride("font_color", MutedInk);
            row.AddChild(h);
        }
        v.AddChild(new ColorRect { Color = Rule, CustomMinimumSize = new Vector2(0, 1), MouseFilter = MouseFilterEnum.Ignore });
        return v;
    }

    private static StyleBoxFlat PillBox(Color accent, bool selected, bool hover) => new()
    {
        BgColor = selected ? new Color(accent.R, accent.G, accent.B, 0.28f) : hover ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.03f),
        BorderColor = selected ? accent : new Color(0.79f, 0.66f, 0.30f, hover ? 0.5f : 0.25f),
        BorderWidthLeft = selected ? 2 : 1, BorderWidthRight = selected ? 2 : 1, BorderWidthTop = selected ? 2 : 1, BorderWidthBottom = selected ? 2 : 1,
        CornerRadiusTopLeft = 29, CornerRadiusTopRight = 29, CornerRadiusBottomLeft = 29, CornerRadiusBottomRight = 29,
    };

    private static Control Pill(string text, Color accent, bool selected, int size)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var box = PillBox(accent, selected, false);
        box.ContentMarginLeft = 20; box.ContentMarginRight = 20; box.ContentMarginTop = 8; box.ContentMarginBottom = 8;
        p.AddThemeStyleboxOverride("panel", box);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 10);
        p.AddChild(row);
        var sw = new ColorRect { Color = accent, CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(sw);
        var l = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", GetBodyFont(size)); l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", new Color(0.9f, 0.84f, 0.68f));
        row.AddChild(l);
        return p;
    }

    private static void StyleEdit(LineEdit e, int size)
    {
        e.AddThemeFontOverride("font", GetBodyFont(size)); e.AddThemeFontSizeOverride("font_size", size);
        e.AddThemeColorOverride("font_color", new Color(0.95f, 0.92f, 0.85f));
        e.AddThemeColorOverride("font_placeholder_color", new Color(0.5f, 0.47f, 0.4f));
        e.AddThemeColorOverride("caret_color", Gold);
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.055f, 0.045f), BorderColor = new Color(0.5f, 0.42f, 0.24f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 18, ContentMarginRight = 18,
        };
        var focus = (StyleBoxFlat)box.Duplicate(); focus.BorderColor = Gold;
        e.AddThemeStyleboxOverride("normal", box);
        e.AddThemeStyleboxOverride("focus", focus);
    }

    private static Button Plate(string text, bool primary, float h, float w = 0)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h), FocusMode = FocusModeEnum.None };
        if (w > 0) b.Size = new Vector2(w, h);
        b.AddThemeFontOverride("font", GetButtonFont(32)); b.AddThemeFontSizeOverride("font_size", 32);
        b.AddThemeColorOverride("font_color", primary ? Color.FromHtml("#F2DFA6") : Color.FromHtml("#D8CBB0"));
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.42f, 0.36f));
        b.AddThemeStyleboxOverride("normal", primary ? MenuButtons.PrimaryNormal() : MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("hover", primary ? MenuButtons.PrimaryHover() : MenuButtons.Hover());
        b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        b.AddThemeStyleboxOverride("disabled", MenuButtons.QuietNormal());
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        MenuButtons.Animate(b);
        return b;
    }

    private static Button Quiet(string text, float w, float h, int size)
    {
        var b = Plate(text, false, h, w);
        b.AddThemeFontOverride("font", GetButtonFont(size)); b.AddThemeFontSizeOverride("font_size", size);
        b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return b;
    }

    private (Control overlay, VBoxContainer body) Modal(float w, float h)
    {
        var vp = GetViewportRect().Size;
        var overlay = new Control { MouseFilter = MouseFilterEnum.Stop, ZIndex = 30 };
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);
        var dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.015f, 0.80f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        overlay.AddChild(dim);
        var card = new PanelContainer { Position = new Vector2((vp.X - w) / 2, (vp.Y - h) / 2), Size = new Vector2(w, h) };
        card.AddThemeStyleboxOverride("panel", Glass(48));
        overlay.AddChild(card);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 22);
        card.AddChild(body);
        return (overlay, body);
    }

    private static Label ModalTitle(string text)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", GetHeaderFont(42)); l.AddThemeFontSizeOverride("font_size", 42);
        l.AddThemeColorOverride("font_color", Gold);
        return l;
    }

    private static Label ModalText(string text)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", GetBodyFont(30)); l.AddThemeFontSizeOverride("font_size", 30);
        l.AddThemeColorOverride("font_color", Parchment);
        return l;
    }

    private static LineEdit ModalEdit(string text)
    {
        var e = new LineEdit { Text = text, PlaceholderText = "Deck name", CustomMinimumSize = new Vector2(0, 80), MaxLength = 28, Alignment = HorizontalAlignment.Center };
        StyleEdit(e, 34);
        return e;
    }

    private static HBoxContainer ButtonRow(VBoxContainer body)
    {
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 28);
        body.AddChild(row);
        return row;
    }

    private void Toast(string message)
    {
        var vp = GetViewportRect().Size;
        var toast = new PanelContainer { Name = "Toast", MouseFilter = MouseFilterEnum.Ignore, ZIndex = 40 };
        var box = Glass(0, Gold); box.ContentMarginLeft = 34; box.ContentMarginRight = 34; box.ContentMarginTop = 16; box.ContentMarginBottom = 16;
        toast.AddThemeStyleboxOverride("panel", box);
        var l = new Label { Text = message, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", GetBodyFont(30)); l.AddThemeFontSizeOverride("font_size", 30);
        l.AddThemeColorOverride("font_color", Color.FromHtml("#F2DFA6"));
        toast.AddChild(l);
        AddChild(toast);
        toast.CallDeferred(nameof(CenterToast));
        void Fade()
        {
            var tween = toast.CreateTween();
            tween.TweenInterval(1.6f);
            tween.TweenProperty(toast, "modulate", new Color(1, 1, 1, 0), 0.35f);
            tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(toast)) toast.QueueFree(); }));
        }
        Fade();
    }

    private void CenterToast()
    {
        var toast = GetNodeOrNull<Control>("Toast");
        if (toast == null) return;
        var vp = GetViewportRect().Size;
        toast.Position = new Vector2(_midX + (_midW - toast.Size.X) / 2, _tray.Position.Y - toast.Size.Y - 24);
    }

    private void Click() => GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
}

/// <summary>FABLE-040: a small type-coloured cost diamond for list rows.</summary>
public partial class CostDiamond : Control
{
    public int Cost { get; set; }
    public Color Colour { get; set; } = Color.FromHtml("#427A38");
    public override void _Draw()
    {
        var c = Size / 2; float r = Mathf.Min(Size.X, Size.Y) / 2 - 2;
        var pts = new[] { c + new Vector2(0, -r), c + new Vector2(r, 0), c + new Vector2(0, r), c + new Vector2(-r, 0) };
        DrawColoredPolygon(pts, Colour);
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[3], pts[0] }, ThemeTokens.Gold, 2f, true);
        var font = ThemeTokens.GetButtonFont(22);
        string t = Cost.ToString();
        var sz = font.GetStringSize(t, HorizontalAlignment.Left, -1, 22);
        DrawString(font, new Vector2(c.X - sz.X / 2, c.Y + sz.Y / 2 - font.GetDescent(22) - 1), t, HorizontalAlignment.Left, -1, 22, new Color(0.96f, 0.93f, 0.86f));
    }
}

/// <summary>FABLE-040: the mana curve as eight rounded bars with counts on top and costs below.</summary>
public partial class CurveBars : Control
{
    public int[] Counts { get; set; } = new int[8];
    public override void _Draw()
    {
        int n = Counts.Length; if (n == 0) return;
        int max = Mathf.Max(1, Counts.Max());
        float gap = 10f, labelH = 28f, topH = 32f;
        float w = (Size.X - gap * (n - 1)) / n;
        float barMax = Size.Y - labelH - topH;
        var small = ThemeTokens.GetBodyFont(22);
        var num = ThemeTokens.GetButtonFont(22);
        for (int i = 0; i < n; i++)
        {
            float x = i * (w + gap);
            float h = Counts[i] == 0 ? 4f : Mathf.Max(8f, barMax * Counts[i] / max);
            var rect = new Rect2(x, topH + barMax - h, w, h);
            var box = new StyleBoxFlat { BgColor = Counts[i] == 0 ? new Color(1, 1, 1, 0.08f) : ThemeTokens.Gold, CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 };
            DrawStyleBox(box, rect);
            if (Counts[i] > 0)
            {
                string t = Counts[i].ToString();
                var sz = num.GetStringSize(t, HorizontalAlignment.Left, -1, 22);
                DrawString(num, new Vector2(x + w / 2 - sz.X / 2, rect.Position.Y - 10), t, HorizontalAlignment.Left, -1, 22, new Color(0.96f, 0.93f, 0.86f));
            }
            string lbl = i == n - 1 ? $"{i}+" : i.ToString();
            var ls = small.GetStringSize(lbl, HorizontalAlignment.Left, -1, 22);
            DrawString(small, new Vector2(x + w / 2 - ls.X / 2, Size.Y - 4), lbl, HorizontalAlignment.Left, -1, 22, new Color(0.62f, 0.57f, 0.47f));
        }
    }
}

/// <summary>FABLE-053: the deck's faction mix as one rounded bar split by colour.</summary>
public partial class FactionBar : Control
{
    public List<(Color col, int n)> Segments { get; set; } = new();
    public override void _Draw()
    {
        int total = Segments.Sum(s => s.n);
        if (total == 0)
        {
            DrawStyleBox(new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.06f), CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7 }, new Rect2(Vector2.Zero, Size));
            return;
        }
        float x = 0;
        foreach (var (col, n) in Segments)
        {
            float w = Size.X * n / total;
            DrawStyleBox(new StyleBoxFlat { BgColor = col.Lightened(0.15f), CornerRadiusTopLeft = 7, CornerRadiusTopRight = 7, CornerRadiusBottomLeft = 7, CornerRadiusBottomRight = 7 }, new Rect2(x, 0, Mathf.Max(4f, w - 3f), Size.Y));
            x += w;
        }
    }
}
