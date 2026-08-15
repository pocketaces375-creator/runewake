using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Deck builder — Ancient Tome two-page spread.
/// LEFT page: card collection as painted bestiary entries with ribbon-bookmark filters.
/// RIGHT page: deck manifest with red-ink DK1 validation annotations.
/// All colors via ThemeTokens. Add/remove drift animations (≤0.4s).
/// Reuses existing data/filter/save logic — no persistence rewrite.
/// </summary>
public partial class DeckBuilderScene : Control
{
    // ── Nodes ────────────────────────────────────────────────────
    private VBoxContainer _collectionList;
    private VBoxContainer _deckList;
    private Label _deckCountLabel;
    private Label _validationAnnotations;  // red-ink notes on manifest page
    private Button _saveButton;
    private Button _backButton;

    // Filter state (ribbon toggles)
    private readonly Button[] _strataRibbons = new Button[6]; // All + 5 strata
    private readonly Button[] _typeRibbons = new Button[4];   // All + 3 types
    private readonly Button[] _costRibbons = new Button[10];  // cost 0-9
    private int _selectedStrataIdx;
    private int _selectedTypeIdx;
    private int _selectedCost = -1; // -1 = any cost

    // Left-page navigation
    private int _collectionPage;
    private const int CardsPerPage = 16;
    private const int TotalPages = 10; // enough for all ~150 cards
    private Label _pageLabel;
    private Button _prevPageBtn;
    private Button _nextPageBtn;

    // Drift animation targets (where cards fly to/from)
    private Control _driftFrom;
    private Control _driftTo;

    // Data
    private readonly List<CardDef> _allCards = new();
    private readonly List<string> _deckCardIds = new();
    private ProgressionState? _saveState;
    private string? _selectedCardId;

    private static readonly string[] StrataOptions = { "All", "VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN" };
    private static readonly string[] TypeOptions = { "All", "CREATURE", "RITUAL", "RELIC" };

    public override void _Ready()
    {
        // Ensure campaign data is loaded (standalone export guard)
        if (!CampaignContext.SaveManager.IsLoaded)
            CampaignContext.SaveManager.Initialize();
        if (CampaignContext.EncounterIndex.Count == 0)
        {
            CampaignContext.LoadEncounters();
            CampaignContext.LoadDigSites();
        }

        BuildTomeUI();
        LoadCards();
        if (CampaignContext.Progression.DeckCardIds.Count > 0)
            _deckCardIds.AddRange(CampaignContext.Progression.DeckCardIds);
        
        // In capture mode, if the progression deck was wiped by save init, seed it directly
        if (_deckCardIds.Count == 0 && CampaignContext.AutoCaptureScreenshot && CampaignContext.CaptureDeckBuilderScreenshot)
        {
            SeedTestDeck();
        }
        
        Refresh();
        // ═══ CAPTURE HOOK (deck builder) ═══
        if (CampaignContext.AutoCaptureScreenshot && CampaignContext.CaptureDeckBuilderScreenshot)
        {
            GD.Print("[DeckBuilderScene] Capture mode active — will capture in 1.5s");
            var capTimer = GetTree().CreateTimer(0.8f);
            capTimer.Timeout += () =>
            {
                // Write meta.json with page rects for gate validation
                var meta = new System.Text.StringBuilder();
                meta.Append("{\n");
                meta.Append("  \"capture_type\": \"deck_test\",\n");
                meta.Append("  \"left_page_rect\": { \"x\": " +
                    (int)(GetViewportRect().Size.X * 0.02f) + ", \"y\": " +
                    (int)(GetViewportRect().Size.Y * 0.02f) + ", \"w\": " +
                    (int)(GetViewportRect().Size.X * 0.455f) + ", \"h\": " +
                    (int)(GetViewportRect().Size.Y * 0.96f) + " },\n");
                meta.Append("  \"right_page_rect\": { \"x\": " +
                    (int)(GetViewportRect().Size.X * 0.525f) + ", \"y\": " +
                    (int)(GetViewportRect().Size.Y * 0.02f) + ", \"w\": " +
                    (int)(GetViewportRect().Size.X * 0.455f) + ", \"h\": " +
                    (int)(GetViewportRect().Size.Y * 0.96f) + " },\n");
                meta.Append("  \"spine_rect\": { \"x\": " +
                    (int)(GetViewportRect().Size.X * 0.48f) + ", \"y\": " +
                    (int)(GetViewportRect().Size.Y * 0.02f) + ", \"w\": " +
                    (int)(GetViewportRect().Size.X * 0.04f) + ", \"h\": " +
                    (int)(GetViewportRect().Size.Y * 0.96f) + " },\n");
                // Ribbon row area
                meta.Append("  \"ribbon_rect\": { \"x\": " +
                    (int)(GetViewportRect().Size.X * 0.02f) + ", \"y\": " +
                    (int)(GetViewportRect().Size.Y * 0.08f) + ", \"w\": " +
                    (int)(GetViewportRect().Size.X * 0.455f) + ", \"h\": " +
                    (int)(GetViewportRect().Size.Y * 0.05f) + " },\n");
                // Validation annotations area on right page
                meta.Append("  \"validation_rect\": { \"x\": " +
                    (int)(GetViewportRect().Size.X * 0.53f) + ", \"y\": " +
                    (int)(GetViewportRect().Size.Y * 0.08f) + ", \"w\": " +
                    (int)(GetViewportRect().Size.X * 0.44f) + ", \"h\": " +
                    (int)(GetViewportRect().Size.Y * 0.10f) + " },\n");
                meta.Append("  \"expected_deck_count\": " + _deckCardIds.Count + ",\n");
                meta.Append("  \"expected_validation_errors\": 1\n");
                meta.Append("}\n");

                var metaPath = "/home/fictive/runewake/artifacts/captures/deck_test.meta.json";
                using (var writer = new System.IO.StreamWriter(metaPath))
                {
                    writer.Write(meta.ToString());
                }
                GD.Print("[DeckBuilderScene] deck_test.meta.json saved");

                // Take screenshot
                var image = GetViewport().GetTexture().GetImage();
                var pngPath = "/home/fictive/runewake/artifacts/captures/deck_test.png";
                image.SavePng(pngPath);
                GD.Print("[DeckBuilderScene] deck_test.png saved");

                GetTree().Quit(0);
            };
        }
    }

    public void SetSaveState(ProgressionState state) { _saveState = state; RefreshCollection(); }
    public List<string> GetDeckCardIds() => new(_deckCardIds);

    // ════════════════════════════════════════════════════════════
    // TOME UI CONSTRUCTION
    // ════════════════════════════════════════════════════════════

    private void BuildTomeUI()
    {
        MouseFilter = MouseFilterEnum.Pass;

        // ── Root: tome background (full-screen weathered paper) ──
        var tomeBg = new ColorRect
        {
            Color = ThemeTokens.CardFace,  // weathered paper
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        };
        AddChild(tomeBg);

        // ── Spine (center strip, aged leather) ──
        var spine = new ColorRect
        {
            Color = ThemeTokens.SurfaceMetal,  // aged leather tone
            AnchorLeft = 0.48f, AnchorRight = 0.52f,
            AnchorTop = 0, AnchorBottom = 1,
            Modulate = new Color(0.9f, 0.75f, 0.4f, 0.35f)
        };
        AddChild(spine);

        // Spine decorative line left
        var spineLineL = new ColorRect
        {
            Color = ThemeTokens.Gold,
            AnchorLeft = 0.48f, AnchorRight = 0.49f,
            AnchorTop = 0.02f, AnchorBottom = 0.98f,
            Modulate = new Color(1f, 0.85f, 0.5f, 0.2f)
        };
        AddChild(spineLineL);

        // Spine decorative line right
        var spineLineR = new ColorRect
        {
            Color = ThemeTokens.Gold,
            AnchorLeft = 0.51f, AnchorRight = 0.52f,
            AnchorTop = 0.02f, AnchorBottom = 0.98f,
            Modulate = new Color(1f, 0.85f, 0.5f, 0.2f)
        };
        AddChild(spineLineR);

        // ── Page texture overlay (subtle grain) ──
        var pageOverlay = new ColorRect
        {
            Color = ThemeTokens.TextPrimary,
            AnchorLeft = 0.02f, AnchorRight = 0.98f,
            AnchorTop = 0.005f, AnchorBottom = 0.005f,
            Modulate = new Color(1f, 0.96f, 0.85f, 0.04f)
        };
        AddChild(pageOverlay);

        // ── LEFT PAGE (collection — 48% width) ──
        var leftPage = new PanelContainer();
        leftPage.AnchorLeft = 0.02f; leftPage.AnchorRight = 0.475f;
        leftPage.AnchorTop = 0.02f; leftPage.AnchorBottom = 0.98f;
        var leftStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.92f, 0.87f, 0.78f, 0.12f),
            CornerRadiusTopLeft = 6, CornerRadiusBottomLeft = 6
        };
        leftPage.AddThemeStyleboxOverride("panel", leftStyle);
        AddChild(leftPage);

        var leftVbox = new VBoxContainer();
        leftVbox.AnchorLeft = 0; leftVbox.AnchorRight = 1;
        leftVbox.AnchorTop = 0; leftVbox.AnchorBottom = 1;
        leftVbox.AddThemeConstantOverride("separation", 2);
        leftPage.AddChild(leftVbox);

        // Page header
        var leftHeader = new Label
        {
            Text = "Bestiary — Card Collection",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = ThemeTokens.TextPrimary
        };
        ThemeTokens.ApplyHeaderFont(leftHeader, ThemeTokens.FontSubtitle);
        leftHeader.CustomMinimumSize = new Vector2(0, 26);
        leftVbox.AddChild(leftHeader);

        // Ribbon bookmark row (filters)
        var ribbonRow = new HBoxContainer();
        ribbonRow.CustomMinimumSize = new Vector2(0, 18);
        ribbonRow.AddThemeConstantOverride("separation", 1);
        leftVbox.AddChild(ribbonRow);

        // Strata ribbons
        var strataGroup = new HBoxContainer();
        strataGroup.AddThemeConstantOverride("separation", 1);
        for (int i = 0; i < StrataOptions.Length; i++)
        {
            var rb = MakeRibbonButton(StrataOptions[i], i == 0);
            int idx = i;
            rb.Pressed += () => SelectStrataRibbon(idx);
            _strataRibbons[i] = rb;
            strataGroup.AddChild(rb);
        }
        ribbonRow.AddChild(strataGroup);

        // Type ribbons
        var typeGroup = new HBoxContainer();
        typeGroup.AddThemeConstantOverride("separation", 1);
        for (int i = 0; i < TypeOptions.Length; i++)
        {
            var rb = MakeRibbonButton(TypeOptions[i], i == 0);
            int idx = i;
            rb.Pressed += () => SelectTypeRibbon(idx);
            _typeRibbons[i] = rb;
            typeGroup.AddChild(rb);
        }
        ribbonRow.AddChild(typeGroup);

        // Cost filter row (second row of ribbons)
        var costRow = new HBoxContainer();
        costRow.CustomMinimumSize = new Vector2(0, 16);
        costRow.AddThemeConstantOverride("separation", 1);
        leftVbox.AddChild(costRow);

        costRow.AddChild(new Label
        {
            Text = "Cost:",
            Modulate = ThemeTokens.TextMuted
        });

        var costAll = MakeRibbonButton("Any", true);
        costAll.Pressed += () => { _selectedCost = -1; RefreshCollection(); UpdateRibbonStates(); };
        costRow.AddChild(costAll);
        _costRibbons[0] = costAll; // slot 0 = separator, we use -1 sentinel

        for (int i = 0; i <= 9; i++)
        {
            int cost = i;
            var rb = MakeRibbonButton(cost.ToString(), false);
            rb.Pressed += () => { _selectedCost = cost; RefreshCollection(); UpdateRibbonStates(); };
            costRow.AddChild(rb);
            _costRibbons[i] = rb;
        }

        // Scrollable collection area
        var colScroll = new ScrollContainer();
        colScroll.SizeFlagsVertical = (Control.SizeFlags)3;
        colScroll.SizeFlagsHorizontal = (Control.SizeFlags)3;
        leftVbox.AddChild(colScroll);

        _collectionList = new VBoxContainer();
        _collectionList.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _collectionList.AddThemeConstantOverride("separation", 3);
        colScroll.AddChild(_collectionList);

        // Page navigation (corners of left page)
        var navRow = new HBoxContainer();
        navRow.CustomMinimumSize = new Vector2(0, 22);
        navRow.AddThemeConstantOverride("separation", 4);
        leftVbox.AddChild(navRow);

        _prevPageBtn = new Button { Text = "\u276E" };
        _prevPageBtn.AddThemeFontSizeOverride("font_size", 12);
        _prevPageBtn.Modulate = ThemeTokens.Gold;
        _prevPageBtn.Pressed += () => { if (_collectionPage > 0) { _collectionPage--; RefreshCollection(); } };
        navRow.AddChild(_prevPageBtn);

        _pageLabel = new Label
        {
            Text = "Page 1",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Modulate = ThemeTokens.TextMuted
        };
        ThemeTokens.ApplyHeaderFont(_pageLabel, ThemeTokens.FontTiny);
        navRow.AddChild(_pageLabel);

        _nextPageBtn = new Button { Text = "\u276F" };
        _nextPageBtn.AddThemeFontSizeOverride("font_size", 12);
        _nextPageBtn.Modulate = ThemeTokens.Gold;
        _nextPageBtn.Pressed += () => { _collectionPage++; RefreshCollection(); };
        navRow.AddChild(_nextPageBtn);

        // ── RIGHT PAGE (deck manifest — 48% width) ──
        var rightPage = new PanelContainer();
        rightPage.AnchorLeft = 0.525f; rightPage.AnchorRight = 0.98f;
        rightPage.AnchorTop = 0.02f; rightPage.AnchorBottom = 0.98f;
        var rightStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.94f, 0.90f, 0.82f, 0.10f),
            CornerRadiusTopRight = 6, CornerRadiusBottomRight = 6
        };
        rightPage.AddThemeStyleboxOverride("panel", rightStyle);
        AddChild(rightPage);

        var rightVbox = new VBoxContainer();
        rightVbox.AnchorLeft = 0; rightVbox.AnchorRight = 1;
        rightVbox.AnchorTop = 0; rightVbox.AnchorBottom = 1;
        rightVbox.AddThemeConstantOverride("separation", 3);
        rightPage.AddChild(rightVbox);

        // Manifest header
        var rightHeader = new HBoxContainer();
        rightHeader.CustomMinimumSize = new Vector2(0, 26);
        rightVbox.AddChild(rightHeader);

        var manifestTitle = new Label
        {
            Text = "Deck Manifest",
            Modulate = ThemeTokens.TextPrimary
        };
        ThemeTokens.ApplyHeaderFont(manifestTitle, ThemeTokens.FontSubtitle);
        manifestTitle.SizeFlagsHorizontal = (Control.SizeFlags)3;
        rightHeader.AddChild(manifestTitle);

        _deckCountLabel = new Label
        {
            Text = "0 of 40",
            Modulate = ThemeTokens.TextSecondary
        };
        ThemeTokens.ApplyHeaderFont(_deckCountLabel, ThemeTokens.FontSmall);
        rightHeader.AddChild(_deckCountLabel);

        // Red-ink validation annotations (DK1 strings)
        _validationAnnotations = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.Word,
            Modulate = ThemeTokens.Ember // red ink
        };
        ThemeTokens.ApplyBodyFont(_validationAnnotations, ThemeTokens.FontTiny);
        _validationAnnotations.CustomMinimumSize = new Vector2(0, 16);
        rightVbox.AddChild(_validationAnnotations);

        // Scrollable deck list
        var deckScroll = new ScrollContainer();
        deckScroll.SizeFlagsVertical = (Control.SizeFlags)3;
        deckScroll.SizeFlagsHorizontal = (Control.SizeFlags)3;
        rightVbox.AddChild(deckScroll);

        _deckList = new VBoxContainer();
        _deckList.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _deckList.AddThemeConstantOverride("separation", 2);
        deckScroll.AddChild(_deckList);

        // ── Bottom bar (save + back) ──
        var bottomBar = new HBoxContainer();
        bottomBar.AnchorLeft = 0.02f; bottomBar.AnchorRight = 0.98f;
        bottomBar.AnchorTop = 1; bottomBar.AnchorBottom = 1;
        bottomBar.OffsetTop = -32;
        bottomBar.AddThemeConstantOverride("separation", 8);
        AddChild(bottomBar);

        _backButton = new Button { Text = "\u2190 Back" };
        _backButton.Modulate = ThemeTokens.TextSecondary;
        _backButton.AddThemeFontSizeOverride("font_size", 12);
        _backButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        bottomBar.AddChild(_backButton);

        _saveButton = new Button { Text = "Save Deck (0/30)", Disabled = true };
        _saveButton.SizeFlagsHorizontal = (Control.SizeFlags)3;
        _saveButton.AddThemeFontSizeOverride("font_size", 13);
        _saveButton.Modulate = ThemeTokens.Gold;
        _saveButton.Pressed += OnSaveDeck;
        bottomBar.AddChild(_saveButton);

        // Drift animation reference points
        _driftFrom = leftPage;
        _driftTo = rightPage;
    }

    // ——— Ribbon Button Factory ——— //

    private Button MakeRibbonButton(string label, bool active)
    {
        var btn = new Button
        {
            Text = label,
            CustomMinimumSize = new Vector2(0, 18),
            Flat = true
        };
        btn.AddThemeFontSizeOverride("font_size", 10);
        if (active)
        {
            btn.Modulate = ThemeTokens.Gold;
            btn.AddThemeColorOverride("font_color", ThemeTokens.Gold);
            // Subtle background tint for the active ribbon
            var activeBg = new StyleBoxFlat
            {
                BgColor = new Color(0.9f, 0.75f, 0.4f, 0.12f),
                BorderColor = ThemeTokens.Gold,
                BorderWidthBottom = 1
            };
            btn.AddThemeStyleboxOverride("normal", activeBg);
            btn.AddThemeStyleboxOverride("hover", activeBg);
        }
        else
        {
            btn.Modulate = ThemeTokens.TextMuted;
            btn.AddThemeColorOverride("font_color", ThemeTokens.TextMuted);
        }
        return btn;
    }

    private void UpdateRibbonStates()
    {
        for (int i = 0; i < _strataRibbons.Length; i++)
        {
            bool active = i == _selectedStrataIdx;
            _strataRibbons[i].Modulate = active ? ThemeTokens.Gold : ThemeTokens.TextMuted;
            _strataRibbons[i].AddThemeColorOverride("font_color", active ? ThemeTokens.Gold : ThemeTokens.TextMuted);
        }
        for (int i = 0; i < _typeRibbons.Length; i++)
        {
            bool active = i == _selectedTypeIdx;
            _typeRibbons[i].Modulate = active ? ThemeTokens.Gold : ThemeTokens.TextMuted;
            _typeRibbons[i].AddThemeColorOverride("font_color", active ? ThemeTokens.Gold : ThemeTokens.TextMuted);
        }
        _costRibbons[0].Modulate = _selectedCost == -1 ? ThemeTokens.Gold : ThemeTokens.TextMuted;
        _costRibbons[0].AddThemeColorOverride("font_color", _selectedCost == -1 ? ThemeTokens.Gold : ThemeTokens.TextMuted);
        for (int i = 0; i <= 9; i++)
        {
            bool active = _selectedCost == i;
            _costRibbons[i].Modulate = active ? ThemeTokens.Gold : ThemeTokens.TextMuted;
            _costRibbons[i].AddThemeColorOverride("font_color", active ? ThemeTokens.Gold : ThemeTokens.TextMuted);
        }
    }

    private void SelectStrataRibbon(int idx)
    {
        _selectedStrataIdx = idx;
        RefreshCollection();
        UpdateRibbonStates();
    }

    private void SelectTypeRibbon(int idx)
    {
        _selectedTypeIdx = idx;
        RefreshCollection();
        UpdateRibbonStates();
    }

    // ════════════════════════════════════════════════════════════
    // CARD LOADING (unchanged from original)
    // ════════════════════════════════════════════════════════════

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

    /// <summary>
    /// Seed a 31-card deck with one duplicate for capture test.
    /// Used when SaveManager.Initialize wiped DebugCapture's preseeded data.
    /// </summary>
    private void SeedTestDeck()
    {
        GD.Print("[DeckBuilderScene] Seeding test deck (save was wiped by init)");
        _deckCardIds.AddRange(new[]
        {
            "vrd_c_root_warden",
            "vrd_c_verdant_sproutling",
            "vrd_c_thornbark_defender",
            "vrd_r_bloomweaver",
            "vrd_u_grove_healer",
            "vrd_x_heartwood_relic",
            "vrd_c_wildwood_stalker",
            "vrd_u_canopy_archer",
            "vrd_u_saphoof_charger",
            "vrd_u_elder_treant",
            "emb_c_ember_hound",
            "emb_c_cinder_runner",
            "emb_c_forgeguard_berserker",
            "emb_u_wildfire_adept",
            "emb_u_lava_serpent",
            "tid_c_tidal_scholar",
            "tid_c_deep_one",
            "tid_c_silt_reader",
            "tid_u_brine_witch",
            "hol_c_skeletal_reaver",
            "hol_c_gravewrit_thrall",
            "hol_c_ossuary_guard",
            "dwn_r_sealing_light",
            "dwn_c_dawn_warder",
            "dwn_c_sunblade_recruit",
            "dwn_u_purifying_light",
            "dwn_c_golden_retainer",
            "dwn_c_dawnbreaker_charger",
            "dwn_u_steadfast_bulwark",
            "tid_c_abyssal_gaze",
            "vrd_c_root_warden"   // intentional duplicate → DK1 error: "duplicate: Root Warden"
        });
        GD.Print($"[DeckBuilderScene] Seeded {_deckCardIds.Count} cards (one duplicate)");
    }

    // ════════════════════════════════════════════════════════════
    // REFRESH
    // ════════════════════════════════════════════════════════════

    public void Refresh()
    {
        RefreshCollection();
        RefreshDeck();
        RefreshValidation();
    }

    private void RefreshCollection()
    {
        foreach (var child in _collectionList.GetChildren())
            child.QueueFree();

        string strata = StrataOptions[_selectedStrataIdx];
        string type = TypeOptions[_selectedTypeIdx];

        var filtered = _allCards
            .Where(c => strata == "All" || c.Strata.ToString() == strata)
            .Where(c => type == "All" || c.Type.ToString() == type)
            .Where(c => _selectedCost < 0 || c.Cost == _selectedCost)
            .OrderBy(c => c.Cost)
            .ThenBy(c => c.Name)
            .ToList();

        // Pagination
        int totalPages = Math.Max(1, (filtered.Count + CardsPerPage - 1) / CardsPerPage);
        _collectionPage = Math.Clamp(_collectionPage, 0, totalPages - 1);
        int start = _collectionPage * CardsPerPage;
        var pageCards = filtered.Skip(start).Take(CardsPerPage).ToList();

        _pageLabel.Text = $"Page {_collectionPage + 1}/{totalPages}";
        _prevPageBtn.Disabled = _collectionPage <= 0;
        _nextPageBtn.Disabled = _collectionPage >= totalPages - 1;

        foreach (var card in pageCards)
        {
            int owned = _saveState?.Collection.GetValueOrDefault(card.Id, 0) ?? 0;
            int inDeck = _deckCardIds.Count(id => id == card.Id);

            var item = MakeBestiaryEntry(card.Id, card.Name, card.Cost,
                card.Type.ToString(), card.Strata.ToString(), card.Rarity.ToString(),
                owned, inDeck, AddToDeck);
            _collectionList.AddChild(item);
        }

        if (pageCards.Count == 0)
        {
            _collectionList.AddChild(new Label
            {
                Text = "No cards match your filters.",
                Modulate = ThemeTokens.TextMuted
            });
        }
    }

    private void RefreshDeck()
    {
        foreach (var child in _deckList.GetChildren())
            child.QueueFree();

        var grouped = _deckCardIds
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        int total = grouped.Values.Sum();
        _deckCountLabel.Text = $"{total} / {DeckRules.MaxSize}";

        foreach (var (cardId, count) in grouped)
        {
            var def = _allCards.FirstOrDefault(c => c.Id == cardId);
            if (def == null) continue;

            var item = MakeManifestEntry(cardId, def.Name, def.Cost,
                def.Strata.ToString(), count, RemoveFromDeck);
            _deckList.AddChild(item);
        }

        if (grouped.Count == 0)
        {
            _deckList.AddChild(new Label
            {
                Text = "Your deck is empty. Choose cards from the Bestiary.",
                Modulate = ThemeTokens.TextMuted
            });
        }
    }

    private void RefreshValidation()
    {
        var result = DeckValidator.Validate(_deckCardIds, LookupCard);
        var lines = new List<string>();

        if (result.Errors.Count > 0)
        {
            lines.AddRange(result.Errors);
        }

        // Per-card errors (duplicates, etc.)
        foreach (var (cardId, error) in result.PerCardErrors)
        {
            var def = LookupCard(cardId);
            string name = def?.Name ?? cardId;
            lines.Add($"{name}: {error}");
        }

        if (lines.Count == 0 && result.IsValid)
        {
            _validationAnnotations.Text = "";
        }
        else
        {
            _validationAnnotations.Text = string.Join("\n", lines);
            _validationAnnotations.Modulate = ThemeTokens.Ember; // red ink
        }

        _saveButton.Disabled = !result.IsValid;
        _saveButton.Text = result.IsValid
            ? "Save Deck"
            : $"Save ({_deckCardIds.Count}/{DeckRules.MaxSize})";
    }

    // ════════════════════════════════════════════════════════════
    // BESTIARY ENTRY (left page — illustrated card)
    // ════════════════════════════════════════════════════════════

    private Control MakeBestiaryEntry(string id, string name, int cost,
        string typeStr, string strata, string rarity, int ownedCount, int inDeckCount,
        Action<string>? onAction)
    {
        var container = new PanelContainer();
        container.CustomMinimumSize = new Vector2(0, 42);
        container.SizeFlagsHorizontal = (Control.SizeFlags)3;
        container.MouseDefaultCursorShape = CursorShape.PointingHand;

        int remaining = ownedCount - inDeckCount;
        bool isUnowned = ownedCount == 0;
        bool noCopiesLeft = remaining <= 0 && !isUnowned;

        // Background — parchment-tone with strata left accent
        var strataColor = strata.ToUpperInvariant() switch
        {
            "VERDANT" => ThemeTokens.StrataVerdant,
            "EMBER" => ThemeTokens.StrataEmber,
            "TIDE" => ThemeTokens.StrataTide,
            "HOLLOW" => ThemeTokens.StrataHollow,
            "DAWN" => ThemeTokens.StrataDawn,
            _ => ThemeTokens.TextMuted
        };

        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.98f, 0.95f, 0.88f, 0.08f),
            BorderColor = strataColor,
            BorderWidthLeft = 3,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = 0,
            CornerRadiusTopLeft = 3,
            CornerRadiusBottomLeft = 3
        };
        container.AddThemeStyleboxOverride("panel", bgStyle);

        // Row layout
        var hbox = new HBoxContainer();
        hbox.AnchorLeft = 0; hbox.AnchorRight = 1;
        hbox.AnchorTop = 0; hbox.AnchorBottom = 1;
        hbox.OffsetLeft = 10;
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        container.AddChild(hbox);

        // Cost badge (gold coin circle)
        var costBadge = new Label
        {
            Text = cost.ToString(),
            CustomMinimumSize = new Vector2(22, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = ThemeTokens.Amber
        };
        ThemeTokens.ApplyHeaderFont(costBadge, ThemeTokens.FontSmall);
        hbox.AddChild(costBadge);

        // Name (inked manuscript style)
        var nameLabel = new Label
        {
            Text = name,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Modulate = ThemeTokens.TextPrimary
        };
        ThemeTokens.ApplyBodyFont(nameLabel, ThemeTokens.FontSmall);
        hbox.AddChild(nameLabel);

        // Type + rarity badge
        string badgeText = $"{typeStr[..Math.Min(2, typeStr.Length)]} {RarityChar(rarity)}";
        var badgeLabel = new Label
        {
            Text = badgeText,
            CustomMinimumSize = new Vector2(28, 0),
            Modulate = ThemeTokens.TextSecondary
        };
        ThemeTokens.ApplyBodyFont(badgeLabel, ThemeTokens.FontTiny);
        hbox.AddChild(badgeLabel);

        // Count badge (remaining copies)
        if (!isUnowned && !noCopiesLeft)
        {
            var countBadge = new Label
            {
                Text = $"\u00d7{remaining}",
                CustomMinimumSize = new Vector2(18, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = ThemeTokens.TextMuted
            };
            ThemeTokens.ApplyBodyFont(countBadge, ThemeTokens.FontTiny);
            hbox.AddChild(countBadge);
        }

        // Dim if unowned or no copies left
        if (isUnowned)
            container.Modulate = new Color(1, 1, 1, 0.4f);
        else if (noCopiesLeft)
            container.Modulate = new Color(1, 1, 1, 0.6f);

        // Engine grey-out for illegal adds
        string? reason = CanAddCard(id);
        var clickArea = new Button();
        clickArea.AnchorLeft = 0; clickArea.AnchorRight = 1;
        clickArea.AnchorTop = 0; clickArea.AnchorBottom = 1;
        clickArea.MouseDefaultCursorShape = CursorShape.PointingHand;
        var transparentStyle = new StyleBoxFlat { BgColor = Colors.Transparent };
        clickArea.AddThemeStyleboxOverride("normal", transparentStyle);
        clickArea.AddThemeStyleboxOverride("hover", transparentStyle);
        clickArea.AddThemeStyleboxOverride("pressed", transparentStyle);
        clickArea.AddThemeStyleboxOverride("disabled", transparentStyle);
        container.AddChild(clickArea);

        clickArea.Disabled = reason != null || isUnowned;
        if (reason != null) clickArea.TooltipText = reason;

        if (onAction != null)
            clickArea.Pressed += () => onAction.Invoke(id);

        return container;
    }

    // ——— Manifest Entry (right page — inked list) ——— //

    private Control MakeManifestEntry(string id, string name, int cost,
        string strata, int count, Action<string>? onAction)
    {
        var container = new PanelContainer();
        container.CustomMinimumSize = new Vector2(0, 28);
        container.SizeFlagsHorizontal = (Control.SizeFlags)3;
        container.MouseDefaultCursorShape = CursorShape.PointingHand;

        var strataColor = strata.ToUpperInvariant() switch
        {
            "VERDANT" => ThemeTokens.StrataVerdant,
            "EMBER" => ThemeTokens.StrataEmber,
            "TIDE" => ThemeTokens.StrataTide,
            "HOLLOW" => ThemeTokens.StrataHollow,
            "DAWN" => ThemeTokens.StrataDawn,
            _ => ThemeTokens.TextMuted
        };

        var bgStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.95f, 0.90f, 0.82f, 0.06f),
            BorderColor = strataColor,
            BorderWidthLeft = 2,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = 0
        };
        container.AddThemeStyleboxOverride("panel", bgStyle);

        var hbox = new HBoxContainer();
        hbox.AnchorLeft = 0; hbox.AnchorRight = 1;
        hbox.AnchorTop = 0; hbox.AnchorBottom = 1;
        hbox.OffsetLeft = 6;
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        container.AddChild(hbox);

        // Count badge
        var countLabel = new Label
        {
            Text = $"\u00d7{count}",
            CustomMinimumSize = new Vector2(24, 0),
            Modulate = ThemeTokens.TextSecondary
        };
        ThemeTokens.ApplyBodyFont(countLabel, ThemeTokens.FontTiny);
        hbox.AddChild(countLabel);

        // Card name
        var nameLabel = new Label
        {
            Text = name,
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            Modulate = ThemeTokens.TextPrimary
        };
        ThemeTokens.ApplyBodyFont(nameLabel, ThemeTokens.FontSmall);
        hbox.AddChild(nameLabel);

        // Cost
        var costLabel = new Label
        {
            Text = cost.ToString(),
            CustomMinimumSize = new Vector2(16, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Modulate = ThemeTokens.Amber
        };
        ThemeTokens.ApplyHeaderFont(costLabel, ThemeTokens.FontSmall);
        hbox.AddChild(costLabel);

        // Check for per-card validation errors → red ink annotation
        var result = DeckValidator.Validate(_deckCardIds, LookupCard);
        if (result.PerCardErrors.TryGetValue(id, out var error))
        {
            var errIcon = new Label
            {
                Text = "\u2620",  // skull — red ink
                CustomMinimumSize = new Vector2(14, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = ThemeTokens.Ember
            };
            ThemeTokens.ApplyHeaderFont(errIcon, ThemeTokens.FontTiny);
            errIcon.TooltipText = error;
            hbox.AddChild(errIcon);
        }

        // Click to remove
        var clickArea = new Button();
        clickArea.AnchorLeft = 0; clickArea.AnchorRight = 1;
        clickArea.AnchorTop = 0; clickArea.AnchorBottom = 1;
        clickArea.MouseDefaultCursorShape = CursorShape.PointingHand;
        var transparentStyle = new StyleBoxFlat { BgColor = Colors.Transparent };
        clickArea.AddThemeStyleboxOverride("normal", transparentStyle);
        clickArea.AddThemeStyleboxOverride("hover", transparentStyle);
        clickArea.AddThemeStyleboxOverride("pressed", transparentStyle);
        clickArea.AddThemeStyleboxOverride("disabled", transparentStyle);
        container.AddChild(clickArea);

        if (onAction != null)
            clickArea.Pressed += () => onAction.Invoke(id);

        return container;
    }

    private static string RarityChar(string rarity) => rarity switch
    {
        "COMMON" => "C", "UNCOMMON" => "U",
        "RARE" => "R", "RELIC" => "L",
        _ => "?"
    };

    // ════════════════════════════════════════════════════════════
    // VALIDATION (unchanged)
    // ════════════════════════════════════════════════════════════

    private string? CanAddCard(string cardId)
    {
        var result = DeckValidator.CanAdd(_deckCardIds, cardId, LookupCard);
        if (!result.IsValid && result.PerCardErrors.ContainsKey(cardId))
            return result.PerCardErrors[cardId];
        return null;
    }

    private CardDef? LookupCard(string id) => _allCards.FirstOrDefault(c => c.Id == id);

    // ════════════════════════════════════════════════════════════
    // ADD / REMOVE with drift animation
    // ════════════════════════════════════════════════════════════

    private void AddToDeck(string cardId)
    {
        if (CanAddCard(cardId) != null) return;

        var def = LookupCard(cardId);
        if (def == null) return;

        _deckCardIds.Add(cardId);

        // Drift animation: create a flying card representation
        var flyer = new Label
        {
            Text = $"{def.Name}",
            Modulate = ThemeTokens.TextPrimary,
            Size = new Vector2(100, 24)
        };
        ThemeTokens.ApplyHeaderFont(flyer, ThemeTokens.FontSmall);
        AddChild(flyer);

        // Start position (center of left page)
        flyer.Position = new Vector2(
            _driftFrom.GetRect().Position.X + _driftFrom.GetRect().Size.X / 2 - 50,
            _driftFrom.GetRect().Position.Y + _driftFrom.GetRect().Size.Y / 2
        );

        // End position (deck list area on right page)
        Vector2 targetPos = new Vector2(
            _driftTo.GetRect().Position.X + 10,
            _driftTo.GetRect().Position.Y + 40
        );

        var tween = CreateTween();
        tween.SetParallel(false);
        tween.TweenProperty(flyer, "position", targetPos, 0.35f)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(flyer, "modulate", new Color(1, 1, 1, 0), 0.1f);
        tween.TweenCallback(Callable.From(() =>
        {
            flyer.QueueFree();
            Refresh();
        }));
        tween.Play();

        // Refresh UI immediately so the deck updates behind the animation
        RefreshDeck();
        RefreshValidation();
    }

    private void RemoveFromDeck(string cardId)
    {
        int idx = _deckCardIds.LastIndexOf(cardId);
        if (idx < 0) return;

        var def = LookupCard(cardId);

        _deckCardIds.RemoveAt(idx);

        // Reverse drift animation
        if (def != null)
        {
            var flyer = new Label
            {
                Text = $"{def.Name}",
                Modulate = ThemeTokens.TextPrimary,
                Size = new Vector2(100, 24)
            };
            ThemeTokens.ApplyHeaderFont(flyer, ThemeTokens.FontSmall);
            AddChild(flyer);

            // Start from deck list area
            flyer.Position = new Vector2(
                _driftTo.GetRect().Position.X + 10,
                _driftTo.GetRect().Position.Y + 40
            );

            // Fly back to left page
            Vector2 targetPos = new Vector2(
                _driftFrom.GetRect().Position.X + _driftFrom.GetRect().Size.X / 2 - 50,
                _driftFrom.GetRect().Position.Y + _driftFrom.GetRect().Size.Y / 2
            );

            var tween = CreateTween();
            tween.SetParallel(false);
            tween.TweenProperty(flyer, "position", targetPos, 0.35f)
                 .SetTrans(Tween.TransitionType.Quad)
                 .SetEase(Tween.EaseType.InOut);
            tween.TweenProperty(flyer, "modulate", new Color(1, 1, 1, 0), 0.1f);
            tween.TweenCallback(Callable.From(() =>
            {
                flyer.QueueFree();
                Refresh();
            }));
            tween.Play();
        }

        RefreshDeck();
        RefreshValidation();
    }

    // ════════════════════════════════════════════════════════════
    // SAVE (unchanged)
    // ════════════════════════════════════════════════════════════

    private void OnSaveDeck()
    {
        var validation = DeckValidator.Validate(_deckCardIds, LookupCard);
        if (!validation.IsValid) return;

        var prog = CampaignContext.Progression;
        prog.DeckCardIds.Clear();
        prog.DeckCardIds.AddRange(_deckCardIds);
        CampaignContext.PlayerDeckIds.Clear();
        CampaignContext.PlayerDeckIds.AddRange(_deckCardIds);
        CampaignContext.SaveManager.Save();

        _saveButton.Text = "Saved!";
        var timer = GetTree().CreateTimer(1.5f);
        timer.Timeout += () => { _saveButton.Text = "Save Deck"; };
    }
}