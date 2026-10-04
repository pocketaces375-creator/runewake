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
    private VBoxContainer _deckListContainer = null!;
    private ScrollContainer _deckListScroll = null!;
    private Label _countLabel = null!;
    private Label _countHint = null!;
    private ColorRect _countBar = null!;
    private Control _countTrack = null!;
    private Button _forgeButton = null!;
    private Control _leftPanel = null!;
    private Control _rightRail = null!;
    private Control? _savedDecksContainer;   // only inside the Load dialog now
    private DragScroll? _gridDrag, _deckDrag, _savedDrag;

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
    private const float RailFrac = 0.70f;

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

        // ── Left: the display case ──
        _leftPanel = new Control { Position = new Vector2(0, TopH), Size = new Vector2(vp.X * RailFrac, vp.Y - TopH), MouseFilter = MouseFilterEnum.Pass };
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
        _cardGrid.AddThemeConstantOverride("separation", 30);
        _gridScroll.AddChild(_cardGrid);

        // ── Right rail ──
        float railX = vp.X * RailFrac + 16, railW = vp.X - railX - 24;
        _rightRail = new PanelContainer { Position = new Vector2(railX, TopH + 20), Size = new Vector2(railW, vp.Y - TopH - 44), MouseFilter = MouseFilterEnum.Stop };
        ((PanelContainer)_rightRail).AddThemeStyleboxOverride("panel", Glass());
        AddChild(_rightRail);

        var rail = new VBoxContainer();
        rail.AddThemeConstantOverride("separation", 0);
        _rightRail.AddChild(rail);
        VBoxContainer Section(float top = 0)
        {
            var m = new MarginContainer();
            m.AddThemeConstantOverride("margin_left", 36); m.AddThemeConstantOverride("margin_right", 36); m.AddThemeConstantOverride("margin_top", (int)top);
            rail.AddChild(m);
            var v = new VBoxContainer();
            v.AddThemeConstantOverride("separation", 8);
            m.AddChild(v);
            return v;
        }

        // name + count
        var nameSec = Section(30);
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 16);
        nameSec.AddChild(nameRow);
        _deckNameLabel = new Label { Text = _deckName, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
        _deckNameLabel.AddThemeFontOverride("font", GetCardNameFont(40)); _deckNameLabel.AddThemeFontSizeOverride("font_size", 40);
        _deckNameLabel.AddThemeColorOverride("font_color", Gold);
        nameRow.AddChild(_deckNameLabel);
        var rename = Quiet("✎  Rename", 190, 56, 26);
        rename.Pressed += () => { Click(); ShowRenameDialog(); };
        nameRow.AddChild(rename);

        var countRow = new HBoxContainer { CustomMinimumSize = new Vector2(0, 60) };
        countRow.AddThemeConstantOverride("separation", 16);
        nameSec.AddChild(countRow);
        _countLabel = new Label { Text = "0 / 30", VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore };
        _countLabel.AddThemeFontOverride("font", GetHeaderFont(48)); _countLabel.AddThemeFontSizeOverride("font_size", 48);
        _countLabel.AddThemeColorOverride("font_color", Parchment);
        countRow.AddChild(_countLabel);
        _countHint = new Label { Text = "cards", VerticalAlignment = VerticalAlignment.Bottom, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        _countHint.AddThemeFontOverride("font", GetBodyFont(26)); _countHint.AddThemeFontSizeOverride("font_size", 26);
        _countHint.AddThemeColorOverride("font_color", MutedInk);
        countRow.AddChild(_countHint);

        _countTrack = new Control { CustomMinimumSize = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        var trackBg = new ColorRect { Color = new Color(1, 1, 1, 0.06f), MouseFilter = MouseFilterEnum.Ignore };
        trackBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _countTrack.AddChild(trackBg);
        _countBar = new ColorRect { Color = Gold, Position = Vector2.Zero, Size = new Vector2(0, 10), MouseFilter = MouseFilterEnum.Ignore };
        _countTrack.AddChild(_countBar);
        nameSec.AddChild(_countTrack);

        // curve
        var curveSec = Section(22);
        curveSec.AddChild(SmallHeader("CURVE"));
        _curve = new CurveBars { CustomMinimumSize = new Vector2(0, 104), MouseFilter = MouseFilterEnum.Ignore };
        curveSec.AddChild(_curve);

        // list
        var listSec = Section(22);
        listSec.AddChild(SmallHeader("DECK LIST", "tap a card to take one out"));
        rail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _deckListScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _deckDrag = DragScroll.Attach(_deckListScroll);
        rail.AddChild(_deckListScroll);
        _deckListContainer = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _deckListContainer.AddThemeConstantOverride("separation", 4);
        _deckListScroll.AddChild(_deckListContainer);

        // plates
        var btnSec = Section(18);
        btnSec.AddThemeConstantOverride("separation", 12);
        _forgeButton = Plate("Forge deck", true, 84);
        _forgeButton.Pressed += () => { Click(); OnSaveDeck(); };
        btnSec.AddChild(_forgeButton);
        var pair = new HBoxContainer();
        pair.AddThemeConstantOverride("separation", 12);
        btnSec.AddChild(pair);
        var loadBtn = Plate("Load a deck", false, 72); loadBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        loadBtn.Pressed += () => { Click(); ShowLoadDialog(); };
        pair.AddChild(loadBtn);
        var back = Plate("◀  Back", false, 72); back.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        back.Pressed += () => { Click(); OnBack(); };
        pair.AddChild(back);
        rail.AddChild(new Control { CustomMinimumSize = new Vector2(0, 28) });
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
        const float captionH = 40f;

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

        // caption under the card
        var caption = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(0, gridH + 4), Size = new Vector2(gridW, captionH - 4),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        ApplyBodyFont(caption, Mathf.RoundToInt(Mathf.Clamp(gridW * 0.085f, 18f, 28f)));
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

        bool canAdd = !isUnowned && !isAtLimit;
        clickArea.Disabled = !canAdd;
        clickArea.Pressed += () => { if (_gridDrag?.Dragged != true) AddToDeck(card.Id); };

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

        // Compute dynamic columns from available width — proportionally scaled
        float availWidth = _leftPanel.Size.X - 40; // 20px margins each side
        if (availWidth <= 0) availWidth = 800;

        // FABLE-039: a display case, not a spreadsheet — five across on a phone in landscape
        // (was nine), each card ~300px wide. The baked faces are 416px, so this stays sharp;
        // any bigger and they'd start to soften.
        float gap = 34f;
        float ratio = GetViewportRect().Size.Y / 1080f;
        float cellW = 300f * Mathf.Clamp(ratio, 0.6f, 1.4f);
        int columns = Mathf.Clamp(Mathf.RoundToInt((availWidth + gap) / (cellW + gap)), 3, 6);
        float cardW = Mathf.Min((availWidth - (columns - 1) * gap) / columns, 416f);
        _cardGrid.AddThemeConstantOverride("separation", 30);
        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 14), MouseFilter = MouseFilterEnum.Ignore });

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

        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 60), MouseFilter = MouseFilterEnum.Ignore });

        if (preserveScroll && keepScroll > 0)
            RestoreGridScroll(keepScroll);
        else
            _gridDrag?.Halt();
    }


    // ════════════════════════════════════════════════════════════════
    //  Rail refreshes
    // ════════════════════════════════════════════════════════════════

    private void RefreshDeckList()
    {
        foreach (var child in _deckListContainer.GetChildren()) child.QueueFree();

        var grouped = _deckCardIds.GroupBy(id => id).Select(g => (id: g.Key, n: g.Count(), def: LookupCard(g.Key)))
            .Where(x => x.def != null).OrderBy(x => x.def!.Cost).ThenBy(x => x.def!.Name).ToList();

        if (grouped.Count == 0)
        {
            var empty = new Label { Text = "Your deck is empty.\nTap cards on the left to add them.", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(0, 160), MouseFilter = MouseFilterEnum.Ignore };
            empty.AddThemeFontOverride("font", GetBodyFont(28)); empty.AddThemeFontSizeOverride("font_size", 28);
            empty.AddThemeColorOverride("font_color", MutedInk);
            _deckListContainer.AddChild(empty);
            return;
        }

        foreach (var (cardId, count, defN) in grouped)
        {
            var def = defN!;
            bool locked = _lockedCardIds.Contains(cardId);
            var wrap = new MarginContainer();
            wrap.AddThemeConstantOverride("margin_left", 24); wrap.AddThemeConstantOverride("margin_right", 24);
            _deckListContainer.AddChild(wrap);

            var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 62), Disabled = locked, MouseDefaultCursorShape = locked ? CursorShape.Arrow : CursorShape.PointingHand };
            var box = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.03f), CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10, CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10 };
            var hov = (StyleBoxFlat)box.Duplicate(); hov.BgColor = new Color(0.79f, 0.66f, 0.30f, 0.10f);
            var prs = (StyleBoxFlat)box.Duplicate(); prs.BgColor = new Color(0.79f, 0.66f, 0.30f, 0.18f);
            b.AddThemeStyleboxOverride("normal", box); b.AddThemeStyleboxOverride("hover", hov); b.AddThemeStyleboxOverride("pressed", prs); b.AddThemeStyleboxOverride("disabled", box);
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            wrap.AddChild(b);

            var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            inner.SetAnchorsPreset(LayoutPreset.FullRect);
            inner.OffsetLeft = 12; inner.OffsetRight = -14;
            inner.AddThemeConstantOverride("separation", 14);
            b.AddChild(inner);

            inner.AddChild(new CostDiamond { Cost = def.Cost, Colour = TypeColour(def.Type), CustomMinimumSize = new Vector2(44, 44), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore });
            var name = new Label { Text = def.Name, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
            name.AddThemeFontOverride("font", GetBodyFont(29)); name.AddThemeFontSizeOverride("font_size", 29);
            name.AddThemeColorOverride("font_color", locked ? Gold : Parchment);
            inner.AddChild(name);
            var tag = new Label { Text = locked ? "core" : $"×{count}", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(64, 0), MouseFilter = MouseFilterEnum.Ignore };
            tag.AddThemeFontOverride("font", locked ? GetBodyFont(24) : GetHeaderFont(26)); tag.AddThemeFontSizeOverride("font_size", locked ? 24 : 26);
            tag.AddThemeColorOverride("font_color", locked ? Gold : new Color(0.85f, 0.78f, 0.60f));
            inner.AddChild(tag);

            string captured = cardId;
            if (!locked) b.Pressed += () => { if (_deckDrag?.Dragged != true) { Click(); RemoveFromDeck(captured); } };
        }
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
        toast.Position = new Vector2((vp.X * RailFrac - toast.Size.X) / 2, vp.Y - 150);
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
