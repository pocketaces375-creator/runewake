using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// RELIQUARY — FABLE-040: the Codex of Runes.
///
/// Trikzos: "give you the feeling of the Pokédex… see all the different runes in existence,
/// some greyed out which you haven't collected… still see the card's art, but maybe not what
/// they can do — question marks, or written in a mystery language. Aramaic / Egyptian looking."
///
/// Every card in the game has a number and a place here. Found cards show in full colour with
/// their count. Cards you haven't found show their art in stone-grey, their name band covered
/// by a strip of ancient script (AncientScript — drawn, not a font), their numbers hidden. Tap a
/// card for the ledger page: the card large, its name / type / cost / rules — or, for an
/// unfound card, the same page written in the old tongue with only its number readable. ◀ ▶
/// walk the ledger; Grind lives on the page for spare copies. A second tab lists the relics.
/// </summary>
public partial class ReliquaryScene : Control
{
    private static string CaptureDir()
    {
        var projectDir = ProjectSettings.GlobalizePath("res://");
        return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(projectDir))!, "artifacts", "captures");
    }

    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private static readonly Color MutedInk = new(0.62f, 0.57f, 0.47f);
    private static readonly Color Rule = new(0.79f, 0.66f, 0.30f, 0.22f);
    private static readonly Color Violet = Color.FromHtml("#9C7BD1");
    private static readonly Color StoneInk = new(0.80f, 0.74f, 0.62f);
    private const float TopH = 104f, BarH = 84f;

    private static readonly string[] StrataOptions = { "ALL", "VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN" };
    private static readonly string[] StrataLabels = { "All", "Verdant", "Ember", "Tide", "Hollow", "Dawn" };
    private static readonly Color[] StrataColors = { Gold, StrataVerdant, StrataEmber, StrataTide, StrataHollow, StrataDawn };
    private static readonly Strata[] StrataValues = { Strata.VERDANT, Strata.EMBER, Strata.TIDE, Strata.HOLLOW, Strata.DAWN };

    // ── state ──
    private List<CardDef> _allCards = new();
    private readonly Dictionary<string, int> _number = new();
    private List<CardDef> _filteredCards = new();
    private int _selectedStrataIdx;
    private int _found;          // 0 all · 1 found · 2 missing
    private bool _relicsTab;
    private bool _captureTriggered;

    // ── nodes ──
    private HBoxContainer _strataRow = null!, _foundRow = null!, _tabRow = null!;
    private Label _progressText = null!;
    private ColorRect _progressBar = null!;
    private Control _progressTrack = null!;
    private Label _dustLabel = null!;
    private ScrollContainer _gridScroll = null!;
    private VBoxContainer _cardGrid = null!;
    private DragScroll? _gridDrag;
    private Control? _inspectOverlay;
    private int _inspectIdx = -1;

    public override void _Ready() => SceneGuard.Build(this, "ReliquaryScene", Build, "res://scenes/main/Main.tscn", "Back to title");

    private void Build()
    {
        if (!CampaignContext.SaveManager.IsLoaded) CampaignContext.SaveManager.Initialize();
        var vp = GetViewportRect().Size;

        var bg = new ColorRect { Color = Color.FromHtml("#0B0A09"), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // ── Header ──
        var top = new Control { Position = Vector2.Zero, Size = new Vector2(vp.X, TopH), MouseFilter = MouseFilterEnum.Pass };
        AddChild(top);
        var topBg = new ColorRect { Color = new Color(0.082f, 0.072f, 0.061f), MouseFilter = MouseFilterEnum.Ignore };
        topBg.SetAnchorsPreset(LayoutPreset.FullRect);
        top.AddChild(topBg);

        var back = Plate("◀  Back", false, 76, 260);
        back.Position = new Vector2(40, (TopH - 76) / 2);
        back.Pressed += () => { Click(); GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn"); };
        top.AddChild(back);

        var kicker = new Label { Text = "THE CODEX OF RUNES", HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, 14), Size = new Vector2(vp.X, 26), MouseFilter = MouseFilterEnum.Ignore };
        kicker.AddThemeFontOverride("font", GetHeaderFont(22)); kicker.AddThemeFontSizeOverride("font_size", 22);
        kicker.AddThemeColorOverride("font_color", new Color(0.75f, 0.68f, 0.52f));
        top.AddChild(kicker);
        var title = new Label { Text = "Reliquary", HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, 34), Size = new Vector2(vp.X, 64), MouseFilter = MouseFilterEnum.Ignore };
        title.AddThemeFontOverride("font", GetCardNameFont(54)); title.AddThemeFontSizeOverride("font_size", 54);
        title.AddThemeColorOverride("font_color", Gold);
        top.AddChild(title);

        var shop = Plate("Shop  ▶", true, 76, 260);
        shop.Position = new Vector2(vp.X - 40 - 260, (TopH - 76) / 2);
        shop.Pressed += () => { Click(); GetTree().ChangeSceneToFile("res://scenes/shop/CardShopScene.tscn"); };
        top.AddChild(shop);
        _dustLabel = new Label { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Position = new Vector2(vp.X - 40 - 260 - 24 - 360, 0), Size = new Vector2(360, TopH), MouseFilter = MouseFilterEnum.Ignore };
        _dustLabel.AddThemeFontOverride("font", GetBodyFont(30)); _dustLabel.AddThemeFontSizeOverride("font_size", 30);
        _dustLabel.AddThemeColorOverride("font_color", Violet);
        top.AddChild(_dustLabel);

        // ── Filter bar ──
        var bar = new Control { Position = new Vector2(0, TopH), Size = new Vector2(vp.X, BarH), MouseFilter = MouseFilterEnum.Pass };
        AddChild(bar);
        bar.AddChild(new ColorRect { Color = Rule, Position = new Vector2(0, 0), Size = new Vector2(vp.X, 1), MouseFilter = MouseFilterEnum.Ignore });
        var barRow = new HBoxContainer { Position = new Vector2(40, 0), Size = new Vector2(vp.X - 80, BarH), MouseFilter = MouseFilterEnum.Pass };
        barRow.AddThemeConstantOverride("separation", 36);
        bar.AddChild(barRow);

        _tabRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _tabRow.AddThemeConstantOverride("separation", 8);
        barRow.AddChild(_tabRow);
        _tabRow.AddChild(Toggle("Cards", () => { _relicsTab = false; Refresh(); }));
        _tabRow.AddChild(Toggle("Relics", () => { _relicsTab = true; Refresh(); }));

        _strataRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _strataRow.AddThemeConstantOverride("separation", 10);
        barRow.AddChild(_strataRow);
        for (int i = 0; i < StrataOptions.Length; i++)
        {
            int idx = i;
            _strataRow.AddChild(Toggle(StrataLabels[i], () => { _selectedStrataIdx = idx; Refresh(); }, i > 0 ? StrataColors[i] : null));
        }

        _foundRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _foundRow.AddThemeConstantOverride("separation", 8);
        barRow.AddChild(_foundRow);
        _foundRow.AddChild(Toggle("Everything", () => { _found = 0; Refresh(); }));
        _foundRow.AddChild(Toggle("Found", () => { _found = 1; Refresh(); }));
        _foundRow.AddChild(Toggle("Missing", () => { _found = 2; Refresh(); }));

        barRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

        var prog = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(360, 0), MouseFilter = MouseFilterEnum.Ignore };
        prog.AddThemeConstantOverride("separation", 8);
        barRow.AddChild(prog);
        _progressText = new Label { HorizontalAlignment = HorizontalAlignment.Right, MouseFilter = MouseFilterEnum.Ignore };
        _progressText.AddThemeFontOverride("font", GetBodyFont(26)); _progressText.AddThemeFontSizeOverride("font_size", 26);
        _progressText.AddThemeColorOverride("font_color", Parchment);
        prog.AddChild(_progressText);
        _progressTrack = new Control { CustomMinimumSize = new Vector2(360, 8), MouseFilter = MouseFilterEnum.Ignore };
        var trackBg = new ColorRect { Color = new Color(1, 1, 1, 0.07f), MouseFilter = MouseFilterEnum.Ignore };
        trackBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _progressTrack.AddChild(trackBg);
        _progressBar = new ColorRect { Color = Gold, Position = Vector2.Zero, Size = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore };
        _progressTrack.AddChild(_progressBar);
        prog.AddChild(_progressTrack);

        // ── The shelves ──
        var shelf = new Control { Position = new Vector2(0, TopH + BarH), Size = new Vector2(vp.X, vp.Y - TopH - BarH), MouseFilter = MouseFilterEnum.Pass };
        AddChild(shelf);
        var light = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = new GradientTexture2D
            {
                Width = 256, Height = 256, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0f), FillTo = new Vector2(0.5f, 1.05f),
                Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { new Color(0.55f, 0.44f, 0.26f, 0.18f), new Color(0, 0, 0, 0) } },
            },
        };
        light.SetAnchorsPreset(LayoutPreset.FullRect);
        shelf.AddChild(light);
        _gridScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _gridScroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _gridDrag = DragScroll.Attach(_gridScroll);
        var vsb = _gridScroll.GetVScrollBar();
        vsb.CustomMinimumSize = new Vector2(10, 0);
        vsb.AddThemeStyleboxOverride("scroll", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.18f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 });
        var grab = new StyleBoxFlat { BgColor = new Color(0.83f, 0.72f, 0.45f, 0.55f), CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5 };
        vsb.AddThemeStyleboxOverride("grabber", grab); vsb.AddThemeStyleboxOverride("grabber_highlight", grab); vsb.AddThemeStyleboxOverride("grabber_pressed", grab);
        shelf.AddChild(_gridScroll);
        _cardGrid = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cardGrid.AddThemeConstantOverride("separation", 34);
        _gridScroll.AddChild(_cardGrid);

        LoadAllCards();
        if (CampaignContext.CaptureOverrideStrataIdx >= 0) _selectedStrataIdx = CampaignContext.CaptureOverrideStrataIdx;
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (_captureTriggered || !CampaignContext.CaptureReliquaryScreenshot) return;
        _captureTriggered = true;
        WriteCapture();
        GetTree().Quit();
    }

    // ════════════════════════════════════════════════════════════════
    //  Data
    // ════════════════════════════════════════════════════════════════

    private void LoadAllCards()
    {
        _allCards.Clear();
        foreach (var pack in new[] { "verdant", "ember", "tide", "hollow", "dawn" })
        {
            string json = Godot.FileAccess.GetFileAsString($"res://content/cards/{pack}.json");
            _allCards.AddRange(CardLoader.LoadPackFromString(json));
        }
        // the ledger runs strata by strata, cheap to dear, so neighbours on the shelf belong together
        _allCards = _allCards.Where(c => c.Type != CardType.TOKEN).OrderBy(c => Array.IndexOf(StrataValues, c.Strata)).ThenBy(c => c.Cost).ThenBy(c => c.Name).ToList();
        _number.Clear();
        for (int i = 0; i < _allCards.Count; i++) _number[_allCards[i].Id] = i + 1;
    }

    private static int Owned(string id) => CampaignContext.Progression.Collection.GetValueOrDefault(id, 0);

    private void Refresh()
    {
        foreach (var t in _tabRow.GetChildren().OfType<Button>()) Style(t, (t.GetIndex() == 1) == _relicsTab, null);
        int si = 0;
        foreach (var t in _strataRow.GetChildren().OfType<Button>()) { Style(t, si == _selectedStrataIdx, si > 0 ? StrataColors[si] : null); si++; }
        int fi = 0;
        foreach (var t in _foundRow.GetChildren().OfType<Button>()) { Style(t, fi == _found, null); fi++; }
        _foundRow.Visible = !_relicsTab;

        var progression = CampaignContext.Progression;
        _dustLabel.Text = $"✦ {progression.RuneDust} Rune Dust";
        int foundAll = _allCards.Count(c => Owned(c.Id) > 0);
        _progressText.Text = $"{foundAll} / {_allCards.Count} runes found";
        CallDeferred(nameof(FitProgress), (float)foundAll / Mathf.Max(1, _allCards.Count));

        foreach (var c in _cardGrid.GetChildren()) c.QueueFree();
        _gridDrag?.Halt();
        _gridScroll.ScrollVertical = 0;
        if (_relicsTab) BuildRelicShelves(); else BuildCardShelves();
    }

    private void FitProgress(float pct) { if (IsInstanceValid(_progressBar)) _progressBar.Size = new Vector2(pct * _progressTrack.Size.X, 8); }

    // ════════════════════════════════════════════════════════════════
    //  Card shelves
    // ════════════════════════════════════════════════════════════════

    private void BuildCardShelves()
    {
        var progression = CampaignContext.Progression;
        IEnumerable<CardDef> q = _allCards;
        if (_selectedStrataIdx > 0) q = q.Where(c => c.Strata == StrataValues[_selectedStrataIdx - 1]);
        if (_found == 1) q = q.Where(c => Owned(c.Id) > 0);
        if (_found == 2) q = q.Where(c => Owned(c.Id) == 0);
        _filteredCards = q.ToList();

        var vp = GetViewportRect().Size;
        float avail = vp.X - 80 - 14;
        float gap = 30f;
        int cols = 7;
        float cardW = Mathf.Min((avail - (cols - 1) * gap) / cols, 300f);
        float cardH = cardW * 608f / 416f;
        const float tagH = 40f, capH = 36f;

        if (_filteredCards.Count == 0)
        {
            var empty = new Label { Text = _found == 1 ? "Nothing found here yet. Go delving." : "Nothing missing here — every rune of this kind is yours.", HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(0, 200), MouseFilter = MouseFilterEnum.Ignore };
            empty.AddThemeFontOverride("font", GetBodyFont(30)); empty.AddThemeFontSizeOverride("font_size", 30);
            empty.AddThemeColorOverride("font_color", MutedInk);
            _cardGrid.AddChild(empty);
            return;
        }

        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore });
        for (int i = 0; i < _filteredCards.Count; i += cols)
        {
            var rowOuter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
            _cardGrid.AddChild(rowOuter);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));
            rowOuter.AddChild(row);
            for (int j = 0; j < cols; j++)
            {
                if (i + j >= _filteredCards.Count) { row.AddChild(new Control { CustomMinimumSize = new Vector2(cardW, 1), MouseFilter = MouseFilterEnum.Ignore }); continue; }
                int idx = i + j;
                row.AddChild(CardCell(_filteredCards[idx], idx, cardW, cardH, tagH, capH));
            }
        }
        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 60), MouseFilter = MouseFilterEnum.Ignore });

        foreach (var card in _filteredCards)
            if (Owned(card.Id) > 0) progression.MarkCardSeen(card.Id);
    }

    private Control CardCell(CardDef card, int idx, float cardW, float cardH, float tagH, float capH)
    {
        int owned = Owned(card.Id);
        bool found = owned > 0;
        var cell = new Control { CustomMinimumSize = new Vector2(cardW, tagH + cardH + capH), MouseFilter = MouseFilterEnum.Ignore };

        // number tag
        var tag = new Label { Text = $"№ {_number[card.Id]:000}", Position = new Vector2(4, 0), Size = new Vector2(cardW - 8, tagH - 8), VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        tag.AddThemeFontOverride("font", GetHeaderFont(22)); tag.AddThemeFontSizeOverride("font_size", 22);
        tag.AddThemeColorOverride("font_color", found ? Gold : new Color(0.55f, 0.50f, 0.42f));
        cell.AddChild(tag);
        if (found && !CampaignContext.Progression.IsCardSeen(card.Id))
        {
            var nw = new Label { Text = "NEW", HorizontalAlignment = HorizontalAlignment.Right, Position = new Vector2(cardW - 90, 0), Size = new Vector2(86, tagH - 8), VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            nw.AddThemeFontOverride("font", GetHeaderFont(20)); nw.AddThemeFontSizeOverride("font_size", 20);
            nw.AddThemeColorOverride("font_color", Color.FromHtml("#8AC4FF"));
            cell.AddChild(nw);
        }

        var face = new Control { Position = new Vector2(0, tagH), Size = new Vector2(cardW, cardH), PivotOffset = new Vector2(cardW / 2, cardH / 2), MouseFilter = MouseFilterEnum.Ignore };
        cell.AddChild(face);
        var halo = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        halo.SetAnchorsPreset(LayoutPreset.FullRect);
        halo.OffsetLeft = 6; halo.OffsetRight = -6; halo.OffsetTop = 8; halo.OffsetBottom = -4;
        var shadowBox = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ShadowColor = new Color(0, 0, 0, found ? 0.6f : 0.35f), ShadowSize = 20, ShadowOffset = new Vector2(0, 10), CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14 };
        var glowBox = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ShadowColor = new Color(0.93f, 0.76f, 0.36f, 0.5f), ShadowSize = 24, CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14 };
        halo.AddThemeStyleboxOverride("panel", shadowBox);
        face.AddChild(halo);

        var plate = new CardPlate();
        face.AddChild(plate);
        plate.Setup(card.Id, card.Attack, card.Vigor, cardW, cardH, card.Cost);
        if (found) plate.Showcase();
        else Veil(plate, card, cardW, cardH, face);

        var cap = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Position = new Vector2(0, tagH + cardH + 2), Size = new Vector2(cardW, capH - 2), MouseFilter = MouseFilterEnum.Ignore };
        cap.AddThemeFontOverride("font", GetBodyFont(24)); cap.AddThemeFontSizeOverride("font_size", 24);
        if (found) { cap.Text = owned == 1 ? "One copy" : $"×{owned}"; cap.AddThemeColorOverride("font_color", new Color(0.78f, 0.72f, 0.58f)); }
        else { cap.Text = ""; }
        cell.AddChild(cap);

        var hit = new Button { FocusMode = FocusModeEnum.None, Position = new Vector2(0, tagH), Size = new Vector2(cardW, cardH), MouseDefaultCursorShape = CursorShape.PointingHand };
        var empty = new StyleBoxEmpty();
        foreach (var k in new[] { "normal", "hover", "pressed", "disabled", "focus" }) hit.AddThemeStyleboxOverride(k, empty);
        cell.AddChild(hit);
        Tween? lift = null;
        void Lift(float sc, bool glow)
        {
            if (!IsInstanceValid(face)) return;
            halo.AddThemeStyleboxOverride("panel", glow ? glowBox : shadowBox);
            lift?.Kill();
            if (CampaignContext.ReduceMotion) { face.Scale = Vector2.One * sc; return; }
            lift = face.CreateTween();
            lift.TweenProperty(face, "scale", Vector2.One * sc, 0.12f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }
        hit.MouseEntered += () => Lift(1.035f, true);
        hit.MouseExited += () => Lift(1f, false);
        hit.ButtonDown += () => Lift(0.975f, true);
        hit.ButtonUp += () => Lift(hit.IsHovered() ? 1.035f : 1f, hit.IsHovered());
        hit.Pressed += () => { if (_gridDrag?.Dragged != true) ShowCardInspect(idx); };
        return cell;
    }

    /// <summary>Grey the face, cover the name with script, hide the numbers.</summary>
    private static void Veil(CardPlate plate, CardDef card, float w, float h, Control face)
    {
        plate.Veil();
        plate.HideNumerals();
        var np = plate.SlotRect("name_plate");
        if (np.Size.X > 0)
        {
            // the band is baked into the face, so it is painted over: a matte stone strip with the gold rule kept
            var band = new ColorRect { Color = new Color(0.075f, 0.068f, 0.060f, 1f), Position = np.Position + new Vector2(0, np.Size.Y * 0.08f), Size = new Vector2(np.Size.X, np.Size.Y * 0.98f), MouseFilter = MouseFilterEnum.Ignore };
            face.AddChild(band);
            face.AddChild(new AncientScript { Seed = card.Name, Glyphs = Mathf.Clamp(card.Name.Length / 2 + 3, 6, 12), Position = np.Position + new Vector2(np.Size.X * 0.05f, np.Size.Y * 0.06f), Size = new Vector2(np.Size.X * 0.90f, np.Size.Y), Ink = new Color(0.86f, 0.80f, 0.66f) });
        }
        foreach (var key in new[] { "attack_badge", "vigor_badge", "cost_badge" })
        {
            var r = plate.SlotRect(key);
            if (r.Size.X <= 0) continue;
            float gs = key == "cost_badge" ? r.Size.X * 0.46f : r.Size.X * 0.62f;
            var c = r.Position + r.Size / 2 + (key == "cost_badge" ? new Vector2(-r.Size.X * 0.03f, -r.Size.Y * 0.02f) : new Vector2(0, -r.Size.Y * 0.04f));
            face.AddChild(new AncientScript { Seed = card.Id + key, Glyphs = 1, Position = c - new Vector2(gs / 2, gs / 2), Size = new Vector2(gs, gs), Ink = new Color(0.86f, 0.80f, 0.68f) });
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Relic shelves
    // ════════════════════════════════════════════════════════════════

    private void BuildRelicShelves()
    {
        var all = ArtifactRegistry.GetAll().OrderBy(a => a.Class).ThenBy(a => a.SlotPool).ThenBy(a => a.Name).ToList();
        if (_selectedStrataIdx > 0)
        {
            var wanted = StrataValues[_selectedStrataIdx - 1];
            all = all.Where(a => ClassStrata(a.Class) == wanted).ToList();
        }
        var known = new HashSet<string>(CampaignContext.Profiles.Select(p => p.ClassId.ToLowerInvariant()));
        if (!string.IsNullOrEmpty(CampaignContext.ChosenClass)) known.Add(CampaignContext.ChosenClass.ToLowerInvariant());
        if (all.Count == 0)
        {
            var empty = new Label { Text = "No relics of that strata.", HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(0, 200), MouseFilter = MouseFilterEnum.Ignore };
            empty.AddThemeFontOverride("font", GetBodyFont(30)); empty.AddThemeFontSizeOverride("font_size", 30);
            empty.AddThemeColorOverride("font_color", MutedInk);
            _cardGrid.AddChild(empty);
            return;
        }
        var vp = GetViewportRect().Size;
        int cols = 7; float gap = 30f;
        float tileW = Mathf.Min((vp.X - 94 - (cols - 1) * gap) / cols, 300f);
        float artH = tileW, textH = 96f;
        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8), MouseFilter = MouseFilterEnum.Ignore });
        for (int i = 0; i < all.Count; i += cols)
        {
            var rowOuter = new CenterContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
            _cardGrid.AddChild(rowOuter);
            var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));
            rowOuter.AddChild(row);
            for (int j = 0; j < cols; j++)
            {
                if (i + j >= all.Count) { row.AddChild(new Control { CustomMinimumSize = new Vector2(tileW, 1), MouseFilter = MouseFilterEnum.Ignore }); continue; }
                var a = all[i + j];
                bool mine = known.Contains(a.Class.ToLowerInvariant());
                var cell = new Control { CustomMinimumSize = new Vector2(tileW, artH + textH), MouseFilter = MouseFilterEnum.Ignore };
                var frame = new PanelContainer { Position = Vector2.Zero, Size = new Vector2(tileW, artH), MouseFilter = MouseFilterEnum.Ignore };
                frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = new Color(0.06f, 0.055f, 0.05f), BorderColor = mine ? new Color(0.79f, 0.66f, 0.30f, 0.6f) : new Color(0.35f, 0.32f, 0.28f, 0.6f),
                    BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                    CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
                    ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 16, ShadowOffset = new Vector2(0, 8),
                });
                cell.AddChild(frame);
                string artPath = $"res://content/art/artifacts/{a.Id}.webp";
                if (ResourceLoader.Exists(artPath))
                {
                    var art = new TextureRect { Texture = ResourceLoader.Load<Texture2D>(artPath), StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(4, 4), Size = new Vector2(tileW - 8, artH - 8) };
                    if (!mine) art.Modulate = new Color(0.5f, 0.48f, 0.45f);
                    cell.AddChild(art);
                }
                var nm = new Label { HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, artH + 8), Size = new Vector2(tileW, 36), MouseFilter = MouseFilterEnum.Ignore };
                if (mine)
                {
                    nm.Text = a.Name;
                    nm.AddThemeFontOverride("font", GetHeaderFont(24)); nm.AddThemeFontSizeOverride("font_size", 24);
                    nm.AddThemeColorOverride("font_color", Parchment);
                    cell.AddChild(nm);
                }
                else cell.AddChild(new AncientScript { Seed = a.Name, Glyphs = Mathf.Clamp(a.Name.Length / 2 + 2, 4, 10), Position = new Vector2(tileW * 0.1f, artH + 8), Size = new Vector2(tileW * 0.8f, 36), Ink = StoneInk });
                var sub = new Label { Text = $"{Cap(a.Class)}  ·  {Cap(a.SlotPool)}", HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, artH + 48), Size = new Vector2(tileW, 30), MouseFilter = MouseFilterEnum.Ignore };
                sub.AddThemeFontOverride("font", GetBodyFont(22)); sub.AddThemeFontSizeOverride("font_size", 22);
                sub.AddThemeColorOverride("font_color", MutedInk);
                cell.AddChild(sub);
                row.AddChild(cell);
            }
        }
        _cardGrid.AddChild(new Control { CustomMinimumSize = new Vector2(0, 60), MouseFilter = MouseFilterEnum.Ignore });
    }

    private static Strata? ClassStrata(string cls) => cls.ToLowerInvariant() switch
    {
        "warrior" => Strata.EMBER, "druid" => Strata.VERDANT, "battlemage" or "astrologist" => Strata.TIDE,
        "necromancer" or "rogue" or "occultist" => Strata.HOLLOW, "paladin" => Strata.DAWN, _ => null,
    };

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1);

    // ════════════════════════════════════════════════════════════════
    //  The ledger page
    // ════════════════════════════════════════════════════════════════

    private void ShowCardInspect(int idx)
    {
        if (idx < 0 || idx >= _filteredCards.Count) return;
        Click();
        DismissInspect();
        _inspectIdx = idx;
        var card = _filteredCards[idx];
        int owned = Owned(card.Id);
        bool found = owned > 0;
        var vp = GetViewportRect().Size;

        _inspectOverlay = new Control { MouseFilter = MouseFilterEnum.Stop, ZIndex = 30 };
        _inspectOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_inspectOverlay);
        var dim = new ColorRect { Color = new Color(0.02f, 0.02f, 0.015f, 0.86f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        _inspectOverlay.AddChild(dim);

        float pw = 1640f, ph = 900f;
        var page = new PanelContainer { Position = new Vector2((vp.X - pw) / 2, (vp.Y - ph) / 2), Size = new Vector2(pw, ph) };
        page.AddThemeStyleboxOverride("panel", Glass(0));
        _inspectOverlay.AddChild(page);
        var canvas = new Control { MouseFilter = MouseFilterEnum.Pass };
        page.AddChild(canvas);

        // the card, large
        float cw = 470f, ch = cw * 608f / 416f;
        var face = new Control { Position = new Vector2(64, (ph - ch) / 2), Size = new Vector2(cw, ch), MouseFilter = MouseFilterEnum.Ignore };
        canvas.AddChild(face);
        var shadow = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        shadow.SetAnchorsPreset(LayoutPreset.FullRect);
        shadow.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), ShadowColor = new Color(0, 0, 0, 0.65f), ShadowSize = 30, ShadowOffset = new Vector2(0, 14), CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18 });
        face.AddChild(shadow);
        var plate = new CardPlate();
        face.AddChild(plate);
        plate.Setup(card.Id, card.Attack, card.Vigor, cw, ch, card.Cost);
        if (found) plate.Showcase(); else Veil(plate, card, cw, ch, face);

        // the words
        float tx = 64 + cw + 64, tw = pw - tx - 64, y = 56;
        var num = Body($"№ {_number[card.Id]:000}   ·   {idx + 1} of {_filteredCards.Count} on this shelf", 24, MutedInk);
        num.AddThemeFontOverride("font", GetHeaderFont(24));
        Put(canvas, num, tx, y, tw, 32); y += 44;

        if (found)
        {
            var name = Body(card.Name, 52, Gold);
            name.AddThemeFontOverride("font", GetCardNameFont(52));
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            Put(canvas, name, tx, y, tw, 70); y += 80;
        }
        else
        {
            canvas.AddChild(new AncientScript { Seed = card.Name, Glyphs = Mathf.Clamp(card.Name.Length / 2 + 2, 5, 12), Centered = false, Position = new Vector2(tx, y), Size = new Vector2(Mathf.Min(tw, 620), 66), Ink = Gold });
            y += 80;
        }

        var chips = new HBoxContainer { Position = new Vector2(tx, y), MouseFilter = MouseFilterEnum.Ignore };
        chips.AddThemeConstantOverride("separation", 12);
        canvas.AddChild(chips);
        int sidx = Array.IndexOf(StrataValues, card.Strata) + 1;
        chips.AddChild(Chip(Cap(card.Strata.ToString().ToLowerInvariant()), sidx > 0 ? StrataColors[sidx] : Gold));
        chips.AddChild(Chip(FormatCardType(card.Type), Gold));
        chips.AddChild(Chip(Cap(card.Rarity.ToString().ToLowerInvariant()), Gold));
        if (found)
        {
            chips.AddChild(Chip($"Cost {card.Cost}", Gold));
            if (card.Type is CardType.CREATURE && card.Attack.HasValue && card.Vigor.HasValue)
                chips.AddChild(Chip($"{card.Attack} / {card.Vigor}", Gold));
        }
        y += 72;

        canvas.AddChild(new ColorRect { Color = Rule, Position = new Vector2(tx, y), Size = new Vector2(tw, 1), MouseFilter = MouseFilterEnum.Ignore });
        y += 22;

        if (found)
        {
            string rules = "";
            try
            {
                var parts = new List<string>();
                if (card.Keywords.Count > 0) parts.Add(string.Join("  ·  ", card.Keywords.Select(RulesTextRenderer.FormatKeyword)));
                string ab = RulesTextRenderer.RenderAbilityTextOnly(card);
                if (!string.IsNullOrWhiteSpace(ab)) parts.Add(ab.Trim());
                rules = string.Join("\n", parts);
            }
            catch (Exception ex) { GD.PrintErr($"[Reliquary] rules text: {ex.Message}"); }
            if (string.IsNullOrWhiteSpace(rules)) rules = card.Type == CardType.CREATURE ? "No special rules. It simply fights." : "";
            var rl = Body(rules.Trim(), 30, Parchment);
            rl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            rl.VerticalAlignment = VerticalAlignment.Top;
            int lines = 1 + rules.Count(ch => ch == '\n') + rules.Length / 70;
            float rh = Mathf.Clamp(lines * 40f + 10f, 50f, 230f);
            Put(canvas, rl, tx, y, tw, rh); y += rh + 14;
            if (!string.IsNullOrWhiteSpace(card.Flavor))
            {
                var fl = Body($"“{card.Flavor.Trim()}”", 27, MutedInk);
                fl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                fl.VerticalAlignment = VerticalAlignment.Top;
                Put(canvas, fl, tx, y, tw, 100); y += 110;
            }
            var own = Body(owned == 1 ? "You hold one copy." : $"You hold {owned} copies.", 28, new Color(0.78f, 0.72f, 0.58f));
            Put(canvas, own, tx, y, tw, 36); y += 44;
            int inDecks = CampaignContext.Progression.SavedDecks.Count(kv => kv.Value != null && kv.Value.Contains(card.Id));
            if (inDecks > 0) { Put(canvas, Body(inDecks == 1 ? "In one of your forged decks." : $"In {inDecks} of your forged decks.", 26, MutedInk), tx, y, tw, 34); y += 40; }
        }
        else
        {
            // rules in the old tongue
            canvas.AddChild(new AncientScript { Seed = card.Id + "rules", Centered = false, LineHeight = 44, LastLineFill = 0.45f, Position = new Vector2(tx, y), Size = new Vector2(tw, 44 * 4), Ink = new Color(0.72f, 0.66f, 0.54f) });
            y += 44 * 4 + 24;
            canvas.AddChild(new AncientScript { Seed = card.Flavor ?? card.Id, Centered = false, LineHeight = 38, LastLineFill = 0.7f, Position = new Vector2(tx, y), Size = new Vector2(tw * 0.8f, 38 * 2), Ink = new Color(0.55f, 0.50f, 0.42f) });
            y += 38 * 2 + 30;
            var hint = Body("Not yet found. Its secrets stay in the old tongue until you hold one — keep delving, keep fighting.", 28, new Color(0.78f, 0.72f, 0.58f));
            hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            hint.VerticalAlignment = VerticalAlignment.Top;
            Put(canvas, hint, tx, y, tw, 80);
        }

        // bottom row: prev · grind · close · next
        var rowB = new HBoxContainer { Position = new Vector2(tx, ph - 56 - 84), Size = new Vector2(tw, 84), MouseFilter = MouseFilterEnum.Pass };
        rowB.AddThemeConstantOverride("separation", 16);
        canvas.AddChild(rowB);
        var prev = Plate("◀", false, 84, 110); prev.Disabled = idx == 0; prev.Pressed += () => ShowCardInspect(idx - 1); rowB.AddChild(prev);
        rowB.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        if (found && owned > 1)
        {
            int value = ProgressionState.GetRuneDustValue(card.Rarity);
            bool can = CampaignContext.Progression.CanGrindCard(card.Id, CampaignContext.Progression.SavedDecks, out var why);
            var grind = Plate(can ? $"Grind a copy  ·  +{value} dust" : "Grind a copy", false, 84, 420);
            grind.Disabled = !can || value <= 0;
            grind.TooltipText = can ? "" : why ?? "";
            grind.Pressed += () => { Click(); ShowGrindConfirm(card, value); };
            rowB.AddChild(grind);
        }
        var close = Plate("Close", true, 84, 260); close.Pressed += () => { Click(); DismissInspect(); }; rowB.AddChild(close);
        rowB.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        var next = Plate("▶", false, 84, 110); next.Disabled = idx >= _filteredCards.Count - 1; next.Pressed += () => ShowCardInspect(idx + 1); rowB.AddChild(next);

        if (found) CampaignContext.Progression.MarkCardSeen(card.Id);
    }

    private void ShowGrindConfirm(CardDef card, int value)
    {
        if (_inspectOverlay == null) return;
        var vp = GetViewportRect().Size;
        var over = new Control { MouseFilter = MouseFilterEnum.Stop, ZIndex = 40 };
        over.SetAnchorsPreset(LayoutPreset.FullRect);
        _inspectOverlay.AddChild(over);
        var dim = new ColorRect { Color = new Color(0.04f, 0.02f, 0.06f, 0.75f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        over.AddChild(dim);
        float w = 1000, h = 400;
        var card2 = new PanelContainer { Position = new Vector2((vp.X - w) / 2, (vp.Y - h) / 2), Size = new Vector2(w, h) };
        card2.AddThemeStyleboxOverride("panel", Glass(48, new Color(0.61f, 0.48f, 0.82f, 0.7f)));
        over.AddChild(card2);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 20);
        card2.AddChild(body);
        var t = Body("GRIND A COPY", 40, Violet); t.AddThemeFontOverride("font", GetHeaderFont(40)); t.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(t);
        var m = Body($"One copy of {card.Name} becomes {value} Rune Dust. You'll still hold the rest.", 30, Parchment); m.AutowrapMode = TextServer.AutowrapMode.WordSmart; m.HorizontalAlignment = HorizontalAlignment.Center; body.AddChild(m);
        body.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 28);
        body.AddChild(row);
        var keep = Plate("Keep it", true, 84, 300); keep.Pressed += () => { Click(); over.QueueFree(); }; row.AddChild(keep);
        var go = Plate("Grind", false, 84, 300);
        go.Pressed += () =>
        {
            Click();
            int added = CampaignContext.Progression.GrindCard(card.Id, CampaignContext.Progression.SavedDecks);
            if (added > 0)
            {
                CampaignContext.SaveManager?.Save();
                GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("coins");
                int keepIdx = _inspectIdx;
                Refresh();
                if (keepIdx >= 0 && keepIdx < _filteredCards.Count && _filteredCards[keepIdx].Id == card.Id) ShowCardInspect(keepIdx);
                else DismissInspect();
                Toast($"+{added} Rune Dust");
            }
            else over.QueueFree();
        };
        row.AddChild(go);
    }

    private void DismissInspect()
    {
        if (_inspectOverlay != null && IsInstanceValid(_inspectOverlay)) _inspectOverlay.QueueFree();
        _inspectOverlay = null;
        _inspectIdx = -1;
    }

    // ════════════════════════════════════════════════════════════════
    //  Bits
    // ════════════════════════════════════════════════════════════════

    private static void Put(Control parent, Control c, float x, float y, float w, float h) { c.Position = new Vector2(x, y); c.Size = new Vector2(w, h); parent.AddChild(c); }

    private static Label Body(string text, int size, Color colour)
    {
        var l = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", GetBodyFont(size)); l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", colour);
        return l;
    }

    private static Control Chip(string text, Color accent)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(accent.R, accent.G, accent.B, 0.14f), BorderColor = new Color(accent.R, accent.G, accent.B, 0.65f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 22, CornerRadiusTopRight = 22, CornerRadiusBottomLeft = 22, CornerRadiusBottomRight = 22,
            ContentMarginLeft = 20, ContentMarginRight = 20, ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        p.AddChild(Body(text, 26, new Color(0.92f, 0.87f, 0.74f)));
        return p;
    }

    private static StyleBoxFlat Glass(int margin, Color? border = null) => new()
    {
        BgColor = new Color(0.082f, 0.072f, 0.061f, 0.96f),
        BorderColor = border ?? new Color(0.79f, 0.66f, 0.30f, 0.38f),
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18, CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
        ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 32,
        ContentMarginLeft = margin, ContentMarginRight = margin, ContentMarginTop = margin, ContentMarginBottom = margin,
    };

    private static Button Toggle(string text, Action onPress, Color? swatch = null)
    {
        var b = new Button { FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 56), MouseDefaultCursorShape = CursorShape.PointingHand };
        var inner = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        inner.SetAnchorsPreset(LayoutPreset.FullRect);
        inner.OffsetLeft = 18; inner.OffsetRight = -18;
        inner.AddThemeConstantOverride("separation", 10);
        b.AddChild(inner);
        float extra = 0;
        if (swatch is { } sc)
        {
            var sw = new ColorRect { Color = sc, CustomMinimumSize = new Vector2(14, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
            inner.AddChild(sw);
            extra = 24;
        }
        var l = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", GetBodyFont(27)); l.AddThemeFontSizeOverride("font_size", 27);
        inner.AddChild(l);
        b.CustomMinimumSize = new Vector2(GetBodyFont(27).GetStringSize(text, HorizontalAlignment.Left, -1, 27).X + 36 + extra, 56);
        b.Pressed += () => onPress();
        return b;
    }

    private void Style(Button b, bool selected, Color? accent)
    {
        var a = accent ?? Gold;
        StyleBoxFlat Box(bool hover) => new()
        {
            BgColor = selected ? new Color(a.R, a.G, a.B, 0.28f) : hover ? new Color(1, 1, 1, 0.06f) : new Color(1, 1, 1, 0.03f),
            BorderColor = selected ? a : new Color(0.79f, 0.66f, 0.30f, hover ? 0.5f : 0.25f),
            BorderWidthLeft = selected ? 2 : 1, BorderWidthRight = selected ? 2 : 1, BorderWidthTop = selected ? 2 : 1, BorderWidthBottom = selected ? 2 : 1,
            CornerRadiusTopLeft = 28, CornerRadiusTopRight = 28, CornerRadiusBottomLeft = 28, CornerRadiusBottomRight = 28,
        };
        b.AddThemeStyleboxOverride("normal", Box(false));
        b.AddThemeStyleboxOverride("hover", Box(true));
        b.AddThemeStyleboxOverride("pressed", Box(true));
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        foreach (var l in b.FindChildren("*", "Label", true, false).OfType<Label>())
            l.AddThemeColorOverride("font_color", selected ? Color.FromHtml("#F2DFA6") : new Color(0.78f, 0.72f, 0.60f));
    }

    private static Button Plate(string text, bool primary, float h, float w)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(w, h), Size = new Vector2(w, h), FocusMode = FocusModeEnum.None };
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

    private void Toast(string message)
    {
        var vp = GetViewportRect().Size;
        var toast = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 50 };
        var box = Glass(0, Violet); box.ContentMarginLeft = 34; box.ContentMarginRight = 34; box.ContentMarginTop = 16; box.ContentMarginBottom = 16;
        toast.AddThemeStyleboxOverride("panel", box);
        var l = Body(message, 30, Color.FromHtml("#F2DFA6"));
        toast.AddChild(l);
        AddChild(toast);
        toast.Position = new Vector2(vp.X / 2 - 160, vp.Y - 150);
        var tween = toast.CreateTween();
        tween.TweenInterval(1.6f);
        tween.TweenProperty(toast, "modulate", new Color(1, 1, 1, 0), 0.35f);
        tween.TweenCallback(Callable.From(() => { if (IsInstanceValid(toast)) toast.QueueFree(); }));
    }

    private void Click() => GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");

    private static string FormatCardType(CardType type) => type switch
    {
        CardType.CREATURE => "Creature", CardType.RITUAL => "Ritual", CardType.RELIC => "Relic",
        CardType.CURSE => "Curse", CardType.TOKEN => "Token", CardType.ARTIFACT => "Artifact", _ => "?",
    };

    // ════════════════════════════════════════════════════════════════
    //  Capture
    // ════════════════════════════════════════════════════════════════

    private void WriteCapture()
    {
        var basename = CampaignContext.CaptureReliquaryBasename;
        var img = GetViewport().GetTexture().GetImage();
        if (img == null) { GD.PrintErr("[ReliquaryScene] Failed to capture: GetImage() returned null"); return; }
        string captureDir = CaptureDir();
        string path = System.IO.Path.Combine(captureDir, $"{basename}.png");
        img.SavePng(path);
        DebugCapture.WriteLayoutJson(this, basename);
        DebugCapture.DumpLayoutJSON(basename, this);
        var meta = new System.Text.StringBuilder();
        meta.Append("{\n");
        meta.Append($"  \"capture_type\": \"{basename}\",\n");
        meta.Append($"  \"view_width\": {(int)GetViewportRect().Size.X},\n");
        meta.Append($"  \"view_height\": {(int)GetViewportRect().Size.Y},\n");
        meta.Append($"  \"strata_filter_idx\": {_selectedStrataIdx},\n");
        meta.Append($"  \"grid_cards_shown\": {_filteredCards.Count},\n");
        meta.Append("  \"grid_columns\": 7\n");
        meta.Append("}\n");
        using (var writer = new System.IO.StreamWriter(System.IO.Path.Combine(captureDir, $"{basename}.meta.json")))
            writer.Write(meta.ToString());
        GD.Print($"[ReliquaryScene] Saved {path}");
    }

    /// <summary>Called from Main.cs when CaptureReliquaryScreenshot is set: one and a half seconds, then capture and quit.</summary>
    public void OnCaptureReady()
    {
        var capTimer = GetTree().CreateTimer(1.5f);
        capTimer.Timeout += () => { WriteCapture(); GetTree().Quit(); };
    }
}
