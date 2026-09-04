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
/// ARMORY RAIL deck builder — replaces the Ancient Tome layout.
/// Top bar with search + strata filter chips.
/// Left: scrollable card grid using CardPlate template.
/// Right rail: deck name, mana curve, deck list, FORGE DECK button.
/// All styling follows the game's existing vocabulary.
/// </summary>
public partial class DeckBuilderScene : Control
{
    // ── Nodes ──
    private LineEdit _searchField;
    private Control _filterChipRow;
    private VBoxContainer _cardGrid;
    private ScrollContainer _gridScroll;
    private Label _deckNameLabel;
    private LineEdit _deckNameEdit;
    private Control _curveContainer;
    private Control _deckListContainer;
    private ScrollContainer _deckListScroll;
    private Label _countLabel;
    private ColorRect _countBar;
    private Button _forgeButton;
    private Button _backButton;
    private Control _topBar;
    private Control _leftPanel;
    private Control _rightRail;
    private Control _savedDecksContainer; // Load Deck section

    // Data
    private readonly List<CardDef> _allCards = new();
    private readonly List<string> _deckCardIds = new();
    private readonly List<CardDef> _coreCardIds = new(); // locked class core cards
    private ProgressionState? _saveState;
    private string _searchText = "";
    private int _selectedStrataIdx; // 0=All, 1-5=VERDANT..DAWN
    private string _deckName = "My Deck";

    // Locked core cards (set by ChooseYourPath flow)
    private readonly HashSet<string> _lockedCardIds = new();
    private List<string>? _pendingCoreCards; // set before _Ready, applied during _Ready

    private static readonly string[] StrataOptions = { "ALL", "VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN" };
    private static readonly Color[] StrataColors = {
        Gold, // ALL
        StrataVerdant, StrataEmber, StrataTide, StrataHollow, StrataDawn
    };

    // Capture mode
    private bool _captureMode;

    // Track unsaved changes
    private bool _modified;

    public override void _Ready()
    {
        // Ensure campaign data is loaded
        if (!CampaignContext.SaveManager.IsLoaded)
            CampaignContext.SaveManager.Initialize();
        if (CampaignContext.EncounterIndex.Count == 0)
        {
            CampaignContext.LoadEncounters();
            CampaignContext.LoadDigSites();
        }

        BuildArmoryUI();
        LoadCards();

        // Load existing deck from progression
        if (CampaignContext.Progression.DeckCardIds.Count > 0)
            _deckCardIds.AddRange(CampaignContext.Progression.DeckCardIds);

        // In capture mode, seed test deck
        if (_deckCardIds.Count == 0 && CampaignContext.AutoCaptureScreenshot && CampaignContext.CaptureDeckBuilderScreenshot)
        {
            SeedTestDeck();
            _captureMode = true;
        }

        RefreshCardGrid();
        RefreshDeckList();
        RefreshCurve();
        UpdateCount();
        RefreshSavedDecksList();

        // Apply core cards from CampaignContext (set by ChooseYourPath)
        if (CampaignContext.CoreCardIds != null && CampaignContext.CoreCardIds.Count > 0)
        {
            ApplyCoreCardsInternal(CampaignContext.CoreCardIds);
            CampaignContext.CoreCardIds = null;
        }

        // Default strata filter for cross-strata classes (ASTROLOGIST, OCCULTIST)
        string chosen = CampaignContext.ChosenClass;
        if (CampaignContext.CaptureOverrideStrataIdx >= 0)
            _selectedStrataIdx = CampaignContext.CaptureOverrideStrataIdx;
        else if (chosen == "astrologist" || chosen == "occultist" || string.IsNullOrEmpty(chosen))
            _selectedStrataIdx = 0; // ALL
        UpdateFilterChips();

        // Apply any core cards set via SetCoreCards before _Ready
        if (_pendingCoreCards != null)
        {
            ApplyCoreCardsInternal(_pendingCoreCards);
            _pendingCoreCards = null;
        }

        // Capture hook
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
                        string path = CampaignContext.WideCaptureMode
                            ? "/home/fictive/runewake/artifacts/captures/deck_test_wide.png"
                            : CampaignContext.PhoneCaptureMode
                                ? "/home/fictive/runewake/artifacts/captures/deck_test_phone.png"
                                : "/home/fictive/runewake/artifacts/captures/deck_test.png";
                        image.SavePng(path);
                        string baseName = System.IO.Path.GetFileNameWithoutExtension(path.Substring(path.LastIndexOf('/') + 1));
                        DebugCapture.WriteLayoutJson(this, baseName);
                        GD.Print($"[DeckBuilderScene] Captured to {path}");

                        // TASK-UI-LINT-1: Dump layout JSON
                        string deckBasename = CampaignContext.WideCaptureMode ? "deck_test_wide" : CampaignContext.PhoneCaptureMode ? "deck_test_phone" : "deck_test";
                        DebugCapture.DumpLayoutJSON(deckBasename, this);
                    }
                }
                GetTree().Quit(0);
            };
        }
    }

    public void SetSaveState(ProgressionState state)
    {
        _saveState = state;
        if (_cardGrid != null)
            RefreshCardGrid();
    }
    public List<string> GetDeckCardIds() => new(_deckCardIds);
    public void SetCoreCards(List<string> coreIds)
    {
        // If _Ready hasn't run yet, defer the work
        if (_cardGrid == null)
        {
            _pendingCoreCards = new List<string>(coreIds);
            return;
        }
        ApplyCoreCardsInternal(coreIds);
    }

    private void ApplyCoreCardsInternal(List<string> coreIds)
    {
        _coreCardIds.Clear();
        _lockedCardIds.Clear();
        foreach (var id in coreIds)
        {
            var def = _allCards.FirstOrDefault(c => c.Id == id);
            if (def != null)
            {
                _coreCardIds.Add(def);
                _lockedCardIds.Add(id);
                if (!_deckCardIds.Contains(id))
                    _deckCardIds.Add(id);
            }
        }
        RefreshDeckList();
        RefreshCurve();
        UpdateCount();
    }

    // ════════════════════════════════════════════════════════════════
    // ARMORY RAIL UI CONSTRUCTION
    // ════════════════════════════════════════════════════════════════

    private void BuildArmoryUI()
    {
        MouseFilter = MouseFilterEnum.Pass;

        // Dark background
        var bg = new ColorRect
        {
            Color = BgDark,
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        };
        AddChild(bg);

        // ── Top bar (64px) — HBox layout ──
                _topBar = new Control();
                _topBar.AnchorLeft = 0; _topBar.AnchorRight = 1;
                _topBar.AnchorTop = 0;
                _topBar.CustomMinimumSize = new Vector2(0, 64);
                _topBar.Size = new Vector2(GetViewportRect().Size.X, 64);
                AddChild(_topBar);

                // Top bar background
                var topBg = new ColorRect
                {
                    Color = SurfaceStone,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                topBg.SetAnchorsPreset(LayoutPreset.FullRect);
                _topBar.AddChild(topBg);

                // Inner HBox: [Title] [Search] [spacer] [Chips]
                var topBarRow = new HBoxContainer();
                topBarRow.SetAnchorsPreset(LayoutPreset.FullRect);
                topBarRow.AddThemeConstantOverride("separation", 8);
                topBarRow.OffsetLeft = 12;
                topBarRow.OffsetRight = -12;
                _topBar.AddChild(topBarRow);

                // Title
                var title = new Label
                {
                    Text = "DECK FORGE",
                    VerticalAlignment = VerticalAlignment.Center
                };
                ApplyHeaderFont(title, FontSubtitle);
                title.AddThemeColorOverride("font_color", Gold);
                title.SizeFlagsHorizontal = (SizeFlags)0;
                title.CustomMinimumSize = new Vector2(140, 64);
                topBarRow.AddChild(title);

                // ── Class banner (visible when campaign class is set) ──
                if (!string.IsNullOrEmpty(CampaignContext.ChosenClass))
                {
                    var classBanner = new HBoxContainer();
                    classBanner.SizeFlagsHorizontal = (SizeFlags)0;
                    classBanner.Alignment = BoxContainer.AlignmentMode.Center;
                    classBanner.AddThemeConstantOverride("separation", 4);
                    classBanner.CustomMinimumSize = new Vector2(0, 44);

                    // Strata dot for class
                    Color classDotColor = Gold; // fallback
                    string cls = CampaignContext.ChosenClass.ToLowerInvariant();
                    if (cls == "warrior") classDotColor = StrataEmber;
                    else if (cls == "necromancer" || cls == "occultist" || cls == "rogue") classDotColor = StrataHollow;
                    else if (cls == "druid") classDotColor = StrataVerdant;
                    else if (cls == "battlemage" || cls == "astrologist") classDotColor = StrataTide;
                    else if (cls == "paladin") classDotColor = StrataDawn;

                    var classDot = new ColorRect
                    {
                        Color = classDotColor,
                        CustomMinimumSize = new Vector2(8, 8),
                        Size = new Vector2(8, 8),
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    classBanner.AddChild(classDot);

                    var className = char.ToUpper(CampaignContext.ChosenClass[0]) + CampaignContext.ChosenClass.Substring(1);
                    var classLabel = new Label
                    {
                        Text = className,
                        VerticalAlignment = VerticalAlignment.Center,
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    classLabel.AddThemeFontSizeOverride("font_size", 12);
                    classLabel.AddThemeColorOverride("font_color", TextMuted);
                    classBanner.AddChild(classLabel);

                    topBarRow.AddChild(classBanner);
                }

                // Search field — max-width 250
                _searchField = new LineEdit
                {
                    PlaceholderText = "Search cards...",
                    CustomMinimumSize = new Vector2(140, 32)
                };
                _searchField.SizeFlagsHorizontal = (SizeFlags)0;
                _searchField.AddThemeColorOverride("font_color", TextPrimary);
                _searchField.AddThemeColorOverride("placeholder_color", TextMuted);
                _searchField.AddThemeStyleboxOverride("normal", new StyleBoxFlat
                {
                    BgColor = Color.FromHtml("#1C1712"),
                    BorderColor = BorderStandard,
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                    CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                    ContentMarginLeft = 8, ContentMarginTop = 4,
                    ContentMarginRight = 8, ContentMarginBottom = 4
                });
                _searchField.TextChanged += (text) => { _searchText = text; RefreshCardGrid(); };
                topBarRow.AddChild(_searchField);

                // Spacer — pushes chips to right
                var spacer = new Control();
                spacer.SizeFlagsHorizontal = (SizeFlags)3; // Expand + Fill
                topBarRow.AddChild(spacer);

                // Filter chips row (scrollable horizontally when overflow)
                var chipScroll = new ScrollContainer();
                chipScroll.SizeFlagsVertical = (SizeFlags)4; // Shrink Center
                chipScroll.SizeFlagsHorizontal = (SizeFlags)3; // Expand + Fill
                chipScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
                chipScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
                chipScroll.CustomMinimumSize = new Vector2(200, 44);
        _filterChipRow = new HBoxContainer();
        _filterChipRow.AddThemeConstantOverride("separation", 8);
        _filterChipRow.CustomMinimumSize = new Vector2(0, 44);
        _filterChipRow.SizeFlagsVertical = (SizeFlags)0;
        chipScroll.AddChild(_filterChipRow);
        topBarRow.AddChild(chipScroll);

        for (int i = 0; i < StrataOptions.Length; i++)
        {
            int idx = i;
            var chip = MakeFilterChip(StrataOptions[i], StrataColors[i], i);
            chip.Pressed += () => {
                _selectedStrataIdx = idx;
                UpdateFilterChips();
                RefreshCardGrid();
            };
            _filterChipRow.AddChild(chip);
        }

        // Right padding spacer — ensures last chip isn't clipped at edge on scroll overflow
        var rowEndPad = new Control();
        rowEndPad.CustomMinimumSize = new Vector2(8, 44);
        _filterChipRow.AddChild(rowEndPad);

        // ── Left panel (card grid) ──
        _leftPanel = new Control();
        _leftPanel.AnchorLeft = 0;
        _leftPanel.AnchorRight = 0.72f;
        _leftPanel.AnchorTop = 0;
        _leftPanel.AnchorBottom = 1;
        _leftPanel.OffsetTop = 64; // below top bar
        AddChild(_leftPanel);

        // Grid scroll area (with subtle scrollbar)
        _gridScroll = new ScrollContainer();
        _gridScroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _gridScroll.SizeFlagsHorizontal = (SizeFlags)3;
        _gridScroll.SizeFlagsVertical = (SizeFlags)3;
        _gridScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        // Touch tuning: small drags press cards, longer drags scroll the grid
        _gridScroll.ScrollDeadzone = 24;
        // Visible-but-elegant scrollbar: slim gold grabber on a faint track
        var vsb = _gridScroll.GetVScrollBar();
        vsb.CustomMinimumSize = new Vector2(8, 0);
        vsb.CustomStep = 120;
        vsb.AddThemeStyleboxOverride("scroll", new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.18f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        var grabber = new StyleBoxFlat
        {
            BgColor = new Color(0.83f, 0.72f, 0.45f, 0.55f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        var grabberHi = new StyleBoxFlat
        {
            BgColor = new Color(0.9f, 0.8f, 0.5f, 0.85f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        };
        vsb.AddThemeStyleboxOverride("grabber", grabber);
        vsb.AddThemeStyleboxOverride("grabber_highlight", grabberHi);
        vsb.AddThemeStyleboxOverride("grabber_pressed", grabberHi);
        // Smooth animated wheel scrolling (touch drag keeps native inertia)
        _gridScroll.GuiInput += OnGridScrollInput;
        _leftPanel.AddChild(_gridScroll);

        // Dynamic grid container (not GridContainer — we lay out rows manually for proper fill)
        _cardGrid = new VBoxContainer();
        _cardGrid.SizeFlagsHorizontal = (SizeFlags)3;
        _cardGrid.AddThemeConstantOverride("separation", 18);
        _cardGrid.CustomMinimumSize = new Vector2(0, 0);
        _gridScroll.AddChild(_cardGrid);

        // ── Right rail (fixed ~28% width) ──
        _rightRail = new Control();
        _rightRail.AnchorLeft = 0.72f;
        _rightRail.AnchorRight = 1;
        _rightRail.AnchorTop = 0;
        _rightRail.AnchorBottom = 1;
        _rightRail.OffsetTop = 64;
        _rightRail.OffsetLeft = 8;
        AddChild(_rightRail);

        // Rail background
        var railBg = new ColorRect
        {
            Color = SurfaceStone,
            MouseFilter = MouseFilterEnum.Ignore
        };
        railBg.SetAnchorsPreset(LayoutPreset.FullRect);
        _rightRail.AddChild(railBg);

        // Rail inner VBox
        var railVbox = new VBoxContainer();
        railVbox.SetAnchorsPreset(LayoutPreset.FullRect);
        railVbox.OffsetLeft = 8; railVbox.OffsetRight = -8;
        railVbox.OffsetTop = 8; railVbox.OffsetBottom = -8;
        railVbox.AddThemeConstantOverride("separation", 6);
        _rightRail.AddChild(railVbox);

        // Editable deck name with pencil icon
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 4);
        nameRow.CustomMinimumSize = new Vector2(0, 28);
        railVbox.AddChild(nameRow);

        _deckNameLabel = new Label
        {
            Text = _deckName,
            SizeFlagsHorizontal = (SizeFlags)3,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(_deckNameLabel, FontBody);
        _deckNameLabel.AddThemeColorOverride("font_color", Gold);
        nameRow.AddChild(_deckNameLabel);

        var pencilBtn = new Button
        {
            Text = "\u270E", // pencil
            Flat = true,
            CustomMinimumSize = new Vector2(24, 24)
        };
        pencilBtn.AddThemeFontSizeOverride("font_size", 14);
        pencilBtn.AddThemeColorOverride("font_color", TextMuted);
        pencilBtn.Pressed += () => {
            _deckNameLabel.Visible = false;
            _deckNameEdit = new LineEdit { Text = _deckName };
            _deckNameEdit.AddThemeColorOverride("font_color", Gold);
            _deckNameEdit.CustomMinimumSize = new Vector2(0, 24);
            _deckNameEdit.SizeFlagsHorizontal = (SizeFlags)3;
            _deckNameEdit.TextSubmitted += (text) => {
                _deckName = text;
                _deckNameLabel.Text = text;
                _deckNameLabel.Visible = true;
                _deckNameEdit.QueueFree();
            };
            nameRow.AddChild(_deckNameEdit);
            _deckNameEdit.GrabFocus();
        };
        nameRow.AddChild(pencilBtn);

        // Mana curve
        var curveLabel = new Label
        {
            Text = "Mana Curve",
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyBodyFont(curveLabel, FontSmall);
        curveLabel.AddThemeColorOverride("font_color", TextSecondary);
        railVbox.AddChild(curveLabel);

        _curveContainer = new Control();
        _curveContainer.CustomMinimumSize = new Vector2(0, 40);
        _curveContainer.SizeFlagsHorizontal = (SizeFlags)3;
        railVbox.AddChild(_curveContainer);

        // Deck list header
        var listHeader = new HBoxContainer();
        listHeader.AddThemeConstantOverride("separation", 4);
        listHeader.CustomMinimumSize = new Vector2(0, 20);
        railVbox.AddChild(listHeader);

        var listLabel = new Label
        {
            Text = "Deck List",
            SizeFlagsHorizontal = (SizeFlags)3,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyBodyFont(listLabel, FontSmall);
        listLabel.AddThemeColorOverride("font_color", TextSecondary);
        listHeader.AddChild(listLabel);

        _countLabel = new Label
        {
            Text = "0/30",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(_countLabel, FontSmall);
        _countLabel.AddThemeColorOverride("font_color", Gold);
        listHeader.AddChild(_countLabel);

        // Deck list scroll
        _deckListScroll = new ScrollContainer();
        _deckListScroll.ScrollDeadzone = 24;
        _deckListScroll.SizeFlagsVertical = (SizeFlags)3;
        _deckListScroll.SizeFlagsHorizontal = (SizeFlags)3;
        railVbox.AddChild(_deckListScroll);

        _deckListContainer = new VBoxContainer();
        _deckListContainer.SizeFlagsHorizontal = (SizeFlags)3;
        _deckListContainer.AddThemeConstantOverride("separation", 2);
        _deckListScroll.AddChild(_deckListContainer);

        // Count progress bar
        _countBar = new ColorRect
        {
            Color = Gold,
            CustomMinimumSize = new Vector2(0, 4),
            Size = new Vector2(0, 4),
            MouseFilter = MouseFilterEnum.Ignore
        };
        railVbox.AddChild(_countBar);

        // FORGE DECK button
        _forgeButton = new Button
        {
            Text = "FORGE DECK",
            Disabled = true,
            CustomMinimumSize = new Vector2(0, 40)
        };
        ApplyHeaderFont(_forgeButton, FontBody);
        _forgeButton.AddThemeFontOverride("font", GetHeaderFont(FontBody));
        _forgeButton.AddThemeFontSizeOverride("font_size", FontBody);
        _forgeButton.AddThemeColorOverride("font_color", Gold);
        _forgeButton.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = SurfaceStone,
            BorderColor = BorderStandard,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1
        });
        _forgeButton.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = new Color(0.25f, 0.22f, 0.18f, 1),
            BorderColor = Gold,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1
        });
        _forgeButton.AddThemeStyleboxOverride("disabled", new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.13f, 0.10f, 1),
            BorderColor = TextInactive,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1
        });
        _forgeButton.Pressed += OnSaveDeck;
        railVbox.AddChild(_forgeButton);

        // ── Saved decks section ──
        var savedHeader = new HBoxContainer();
        savedHeader.AddThemeConstantOverride("separation", 4);
        savedHeader.CustomMinimumSize = new Vector2(0, 20);
        railVbox.AddChild(savedHeader);

        var savedLabel = new Label
        {
            Text = "Load Saved Deck",
            SizeFlagsHorizontal = (SizeFlags)3,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyBodyFont(savedLabel, FontSmall);
        savedLabel.AddThemeColorOverride("font_color", TextSecondary);
        savedHeader.AddChild(savedLabel);

        var savedScroll = new ScrollContainer();
        savedScroll.ScrollDeadzone = 24;
        savedScroll.SizeFlagsVertical = (SizeFlags)3;
        savedScroll.SizeFlagsHorizontal = (SizeFlags)3;
        savedScroll.CustomMinimumSize = new Vector2(0, 80);
        railVbox.AddChild(savedScroll);

        _savedDecksContainer = new VBoxContainer();
        _savedDecksContainer.SizeFlagsHorizontal = (SizeFlags)3;
        _savedDecksContainer.AddThemeConstantOverride("separation", 2);
        savedScroll.AddChild(_savedDecksContainer);

        // Back button (bottom of rail)
        _backButton = new Button
        {
            Text = "\u2190 Back",
            Flat = true,
            CustomMinimumSize = new Vector2(0, 24)
        };
        _backButton.AddThemeFontSizeOverride("font_size", 12);
        _backButton.AddThemeColorOverride("font_color", TextMuted);
        _backButton.Pressed += () => OnBack();
        railVbox.AddChild(_backButton);
    }

    /// <summary>
    /// Create a strata filter chip button — one hit-testable control
    /// containing swatch + label, minimum 44x44 touch target, 8px padding.
    /// The inner HBox fills the entire button content area so that both
    /// the 8x8 swatch and the 11px label are vertically centered together.
    /// Selected state: filled with strata color at low alpha + 1px border.
    /// Pressed state: slightly brighter bg for tactile feedback.
    /// </summary>
    private Button MakeFilterChip(string label, Color accent, int idx)
    {
        var btn = new Button
        {
            Flat = false,
            Text = "" // custom content via children
        };

        btn.CustomMinimumSize = new Vector2(44, 44);

        // Chip normal style: 8px side padding, 4px vertical breathing room, 18px corner
        var normalStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#26201A"),
            BorderColor = Color.FromHtml("#4A4238"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ContentMarginLeft = 8, ContentMarginTop = 4,
            ContentMarginRight = 8, ContentMarginBottom = 4
        };
        var pressedStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#322C26"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18,
            CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
            ContentMarginLeft = 8, ContentMarginTop = 4,
            ContentMarginRight = 8, ContentMarginBottom = 4
        };
        btn.AddThemeStyleboxOverride("normal", normalStyle);
        btn.AddThemeStyleboxOverride("hover", normalStyle);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);

        btn.SetMeta("strata_idx", idx);
        btn.SetMeta("accent_color", accent);

        // ── Inner HBox fills the button content area so swatch+label
        //     are centered as a unit, not pinned to the top ──
        var inner = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = (SizeFlags)3, // Fill | Expand — fills button height
            Alignment = BoxContainer.AlignmentMode.Center
        };
        inner.AddThemeConstantOverride("separation", 6);
        btn.AddChild(inner);

        // ── Swatch — 8x8 ColorRect, vertically centered within the
        //     expanded HBox, which aligns it against label cap height ──
        var swatch = new ColorRect
        {
            Color = accent,
            CustomMinimumSize = new Vector2(8, 8),
            Size = new Vector2(8, 8),
            MouseFilter = MouseFilterEnum.Ignore
        };
        inner.AddChild(swatch);

        // ── Label — Cinzel 11px ──
        var chipFont = GetHeaderFont(11);
        var chipLabel = new Label
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = (SizeFlags)0,
            AutowrapMode = TextServer.AutowrapMode.Off
        };
        chipLabel.AddThemeFontSizeOverride("font_size", 11);
        chipLabel.AddThemeFontOverride("font", chipFont);
        chipLabel.AddThemeColorOverride("font_color", Color.FromHtml("#CFC4AE"));
        inner.AddChild(chipLabel);

        return btn;
    }

    private void UpdateFilterChips()
    {
        foreach (var child in _filterChipRow.GetChildren())
        {
            if (child is not Button btn) continue;

            int idx = (int)btn.GetMeta("strata_idx", -1);
            bool selected = idx == _selectedStrataIdx;

            // Determine the accent color for this chip
            Color accent;
            if (idx >= 0 && idx < StrataColors.Length)
                accent = StrataColors[idx];
            else
                accent = Gold; // fallback

            if (selected)
            {
                // Selected: fill with strata color at low alpha + 1px border in strata color
                var selectedStyle = new StyleBoxFlat
                {
                    BgColor = new Color(accent.R, accent.G, accent.B, 0.22f),
                    BorderColor = accent,
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18,
                    CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
                    ContentMarginLeft = 8, ContentMarginTop = 4,
                    ContentMarginRight = 8, ContentMarginBottom = 4
                };
                btn.AddThemeStyleboxOverride("normal", selectedStyle);
                btn.AddThemeStyleboxOverride("hover", selectedStyle);
                btn.AddThemeStyleboxOverride("pressed", selectedStyle);
            }
            else
            {
                // Normal: dark bg, muted border
                var normalStyle = new StyleBoxFlat
                {
                    BgColor = Color.FromHtml("#26201A"),
                    BorderColor = Color.FromHtml("#4A4238"),
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18,
                    CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
                    ContentMarginLeft = 8, ContentMarginTop = 4,
                    ContentMarginRight = 8, ContentMarginBottom = 4
                };
                var normalPressedStyle = new StyleBoxFlat
                {
                    BgColor = Color.FromHtml("#322C26"),
                    BorderColor = Color.FromHtml("#5A5048"),
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 18, CornerRadiusTopRight = 18,
                    CornerRadiusBottomLeft = 18, CornerRadiusBottomRight = 18,
                    ContentMarginLeft = 8, ContentMarginTop = 4,
                    ContentMarginRight = 8, ContentMarginBottom = 4
                };
                btn.AddThemeStyleboxOverride("normal", normalStyle);
                btn.AddThemeStyleboxOverride("hover", normalStyle);
                btn.AddThemeStyleboxOverride("pressed", normalPressedStyle);
            }

            // Update label color: gold when selected, muted when not
            foreach (var innerChild in btn.GetChildren())
            {
                if (innerChild is HBoxContainer hbox)
                {
                    foreach (var hboxChild in hbox.GetChildren())
                    {
                        if (hboxChild is Label lbl)
                        {
                            lbl.AddThemeColorOverride("font_color",
                                selected ? Color.FromHtml("#D4B84C") : Color.FromHtml("#CFC4AE"));
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Create a grid card item using the CardPlate template.
    /// </summary>
    private Control MakeGridCard(CardDef card, int ownedCount, int inDeckCount, float gridW)
    {
        float gridH = gridW * 219f / 150f; // 13:19 aspect ratio

        var container = new PanelContainer();
        container.CustomMinimumSize = new Vector2(gridW, gridH);
        container.SizeFlagsHorizontal = (SizeFlags)0;
        container.SizeFlagsVertical = (SizeFlags)0;
        container.MouseDefaultCursorShape = CursorShape.PointingHand;

        // Card face style
        var strataColor = StrataColor(card.Strata);
        var cardStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#332E28"),
            BorderColor = strataColor.Darkened(0.4f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0
        };
        container.AddThemeStyleboxOverride("panel", cardStyle);

        // Content area
        var content = new Control();
        content.SetAnchorsPreset(LayoutPreset.FullRect);
        container.AddChild(content);

        // ── Card art background (parchment tone, visible behind dark art) ──
        var artBg = new ColorRect
        {
            Color = CardArtColors.Parchment,
            MouseFilter = MouseFilterEnum.Ignore
        };
        artBg.SetAnchorsPreset(LayoutPreset.FullRect);
        content.AddChild(artBg);

        // ── Card art (full-bleed, cover-cropped) ──
        var artRect = new TextureRect();
        artRect.SetAnchorsPreset(LayoutPreset.FullRect);
        artRect.MouseFilter = MouseFilterEnum.Ignore;
        artRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        artRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        string artPath = $"res://content/art/{card.Id}.webp";
        if (ResourceLoader.Exists(artPath))
        {
            var texture = ResourceLoader.Load<Texture2D>(artPath);
            if (texture != null)
                artRect.Texture = texture;
            else
                artRect.Modulate = CardArtColors.Parchment;
        }
        else
        {
            artRect.Modulate = CardArtColors.Parchment;
            GD.Print($"[ART-MISSING] {card.Id}");
        }
        content.AddChild(artRect);

        // Cost badge
        var costBadge = new PanelContainer();
        costBadge.Position = new Vector2(0, 0);
        costBadge.Size = new Vector2(Mathf.Max(18, gridW * 0.17f), Mathf.Max(16, gridW * 0.17f * 0.85f));
        var costStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#1C1610"),
            BorderColor = Gold,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 2, ContentMarginTop = 1,
            ContentMarginRight = 2, ContentMarginBottom = 1
        };
        costBadge.AddThemeStyleboxOverride("panel", costStyle);
        var costLabel = new Label
        {
            Text = card.Cost.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(costLabel, FontSmall);
        costLabel.AddThemeColorOverride("font_color", Gold);
        costLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        costBadge.AddChild(costLabel);
        content.AddChild(costBadge);

        // CardPlate
        var plate = new CardPlate();
        content.AddChild(plate);
        plate.Setup(card.Name, card.Attack, card.Vigor, card.Strata, gridW, gridH);

        // xN badge (copies in current deck)
        if (inDeckCount > 0)
        {
            var inDeckBadge = new PanelContainer();
            inDeckBadge.Position = new Vector2(gridW - 32, 0);
            inDeckBadge.Size = new Vector2(30, 16);
            var inDeckStyle = new StyleBoxFlat
            {
                BgColor = Color.FromHtml("#1C2A18"),
                BorderColor = Color.FromHtml("#6F8F5A"),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                ContentMarginLeft = 2, ContentMarginTop = 0,
                ContentMarginRight = 2, ContentMarginBottom = 0
            };
            inDeckBadge.AddThemeStyleboxOverride("panel", inDeckStyle);
            var inDeckLabel = new Label
            {
                Text = $"x{inDeckCount}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyBodyFont(inDeckLabel, FontTiny);
            inDeckLabel.AddThemeColorOverride("font_color", Color.FromHtml("#6F8F5A"));
            inDeckLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            inDeckBadge.AddChild(inDeckLabel);
            content.AddChild(inDeckBadge);
        }

        // Compute how many saved decks this card appears in
        int countInDecks = 0;
        var progression = CampaignContext.Progression;
        foreach (var (deckName, cardIds) in progression.SavedDecks)
        {
            if (cardIds != null && cardIds.Contains(card.Id))
                countInDecks++;
        }
        bool isAtLimit = ownedCount <= inDeckCount;
        bool isUnowned = ownedCount == 0;
        if (isUnowned)
            container.Modulate = new Color(1, 1, 1, 0.4f);
        else if (isAtLimit)
            container.Modulate = new Color(1, 1, 1, 0.55f);

        // Owned / in-decks label
        if (ownedCount > 0 || countInDecks > 0)
        {
            var ownedLabel = new Label
            {
                Text = $"owned {ownedCount} · in {countInDecks} decks",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            ownedLabel.AddThemeFontSizeOverride("font_size", 7);
            ownedLabel.AddThemeColorOverride("font_color", Color.FromHtml("#A09080"));
            ownedLabel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
            ownedLabel.AnchorLeft = 0; ownedLabel.AnchorRight = 1;
            ownedLabel.AnchorBottom = 1;
            ownedLabel.AnchorTop = 0.9f;
            content.AddChild(ownedLabel);
        }

        // Click to add
        var clickArea = new Button();
        clickArea.SetAnchorsPreset(LayoutPreset.FullRect);
        clickArea.MouseDefaultCursorShape = CursorShape.PointingHand;
        var transparent = new StyleBoxFlat { BgColor = Colors.Transparent };
        clickArea.AddThemeStyleboxOverride("normal", transparent);
        clickArea.AddThemeStyleboxOverride("hover", transparent);
        clickArea.AddThemeStyleboxOverride("pressed", transparent);
        clickArea.AddThemeStyleboxOverride("disabled", transparent);
        container.AddChild(clickArea);

        clickArea.Disabled = isUnowned || isAtLimit;
        clickArea.Pressed += () => AddToDeck(card.Id);

        // Hover: gold border
        bool isHovered = false;
        container.MouseEntered += () =>
        {
            if (!isUnowned && !isAtLimit)
            {
                isHovered = true;
                var hoverStyle = (StyleBoxFlat)cardStyle.Duplicate();
                hoverStyle.BorderColor = Gold;
                container.AddThemeStyleboxOverride("panel", hoverStyle);
            }
        };
        container.MouseExited += () =>
        {
            isHovered = false;
            container.RemoveThemeStyleboxOverride("panel");
        };

        // Press: gold border + lift shadow
        clickArea.ButtonDown += () =>
        {
            if (!isUnowned && !isAtLimit)
            {
                var pressStyle = (StyleBoxFlat)cardStyle.Duplicate();
                pressStyle.BorderColor = Gold;
                pressStyle.ShadowSize = 6;
                pressStyle.ShadowColor = new Color(0, 0, 0, 0.35f);
                pressStyle.ShadowOffset = new Vector2(0, 2);
                container.AddThemeStyleboxOverride("panel", pressStyle);
            }
        };
        clickArea.ButtonUp += () =>
        {
            if (isHovered && !isUnowned && !isAtLimit)
            {
                var hoverStyle = (StyleBoxFlat)cardStyle.Duplicate();
                hoverStyle.BorderColor = Gold;
                container.AddThemeStyleboxOverride("panel", hoverStyle);
            }
            else
                container.RemoveThemeStyleboxOverride("panel");
        };

        // Long-press to inspect (using pressed-hold detection)
        // For simplicity, we rely on the existing card detail popup

        return container;
    }

    // ════════════════════════════════════════════════════════════════
    // CARD LOADING
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    // REFRESH
    // ════════════════════════════════════════════════════════════════

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

    private void RefreshCardGrid(bool preserveScroll = false)
    {
        int keepScroll = preserveScroll && _gridScroll != null ? _gridScroll.ScrollVertical : 0;

        foreach (var child in _cardGrid.GetChildren())
            child.QueueFree();

        string strata = StrataOptions[_selectedStrataIdx];

        var filtered = _allCards
            .Where(c => strata == "ALL" || c.Strata.ToString() == strata)
            .Where(c => string.IsNullOrEmpty(_searchText) ||
                c.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                c.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Cost)
            .ThenBy(c => c.Name)
            .ToList();

        if (filtered.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No cards match your filters.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            emptyLabel.AddThemeColorOverride("font_color", TextMuted);
            emptyLabel.CustomMinimumSize = new Vector2(200, 60);
            _cardGrid.AddChild(emptyLabel);
            return;
        }

        // Compute dynamic columns from available width — proportionally scaled
        float availWidth = _leftPanel.Size.X - 40; // 20px margins each side
        if (availWidth <= 0) availWidth = 800;

        float gap = 18f;
        float ratio = GetViewportRect().Size.Y / 1080f;
        float baseCellW = 150f;
        float cellW = baseCellW * Mathf.Max(0.6f, Mathf.Min(1.4f, ratio));
        int columns = Mathf.Max(1, Mathf.FloorToInt((availWidth + gap) / (cellW + gap)));

        // Actual card width to fill available space evenly
        float cardW = (availWidth - (columns - 1) * gap) / columns;

        for (int i = 0; i < filtered.Count; i += columns)
        {
            // CenterContainer horizontally centers its single child (the HBox row)
            var rowOuter = new CenterContainer();
            rowOuter.SizeFlagsHorizontal = (SizeFlags)3;
            _cardGrid.AddChild(rowOuter);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));
            rowOuter.AddChild(row);

            for (int j = 0; j < columns && i + j < filtered.Count; j++)
            {
                var card = filtered[i + j];
                int owned = CampaignContext.Progression.Collection.TryGetValue(card.Id, out var ownedCount) ? ownedCount : 0;
                int inDeck = _deckCardIds.Count(id => id == card.Id);
                var item = MakeGridCard(card, owned, inDeck, cardW);
                row.AddChild(item);
            }
        }

        if (preserveScroll && keepScroll > 0)
            RestoreGridScroll(keepScroll);
    }

    private void RefreshDeckList()
    {
        foreach (var child in _deckListContainer.GetChildren())
            child.QueueFree();

        var grouped = _deckCardIds
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (cardId, count) in grouped)
        {
            var def = LookupCard(cardId);
            if (def == null) continue;

            bool isLocked = _lockedCardIds.Contains(cardId);

            var row = new PanelContainer();
            row.CustomMinimumSize = new Vector2(0, 28);
            row.SizeFlagsHorizontal = (SizeFlags)3;
            row.MouseDefaultCursorShape = isLocked ? CursorShape.Arrow : CursorShape.PointingHand;

            var rowStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.13f, 0.10f, 0.4f),
                BorderColor = isLocked ? Gold : Colors.Transparent,
                BorderWidthLeft = 2, BorderWidthTop = 0,
                BorderWidthRight = 0, BorderWidthBottom = 0,
                CornerRadiusTopLeft = 3, CornerRadiusBottomLeft = 3
            };
            row.AddThemeStyleboxOverride("panel", rowStyle);

            var hbox = new HBoxContainer();
            hbox.AnchorLeft = 0; hbox.AnchorRight = 1;
            hbox.AnchorTop = 0; hbox.AnchorBottom = 1;
            hbox.OffsetLeft = 4;
            hbox.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(hbox);

            // Cost chip
            var costChip = new Label
            {
                Text = def.Cost.ToString(),
                CustomMinimumSize = new Vector2(20, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            ApplyHeaderFont(costChip, FontTiny);
            costChip.AddThemeColorOverride("font_color", Gold);
            hbox.AddChild(costChip);

            // Name
            var nameLabel = new Label
            {
                Text = def.Name,
                SizeFlagsHorizontal = (SizeFlags)3,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyBodyFont(nameLabel, FontSmall);
            nameLabel.AddThemeColorOverride("font_color", isLocked ? Gold : TextPrimary);
            hbox.AddChild(nameLabel);

            // Lock icon or xN
            if (isLocked)
            {
                var lockLabel = new Label
                {
                    Text = "\uD83D\uDD12",
                    CustomMinimumSize = new Vector2(18, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                ApplyBodyFont(lockLabel, FontTiny);
                lockLabel.AddThemeColorOverride("font_color", Gold);
                hbox.AddChild(lockLabel);
            }
            else
            {
                var countLabel = new Label
                {
                    Text = $"x{count}",
                    CustomMinimumSize = new Vector2(18, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                ApplyBodyFont(countLabel, FontTiny);
                countLabel.AddThemeColorOverride("font_color", TextMuted);
                hbox.AddChild(countLabel);
            }

            // Click to remove (only non-locked cards)
            if (!isLocked)
            {
                var clickArea = new Button();
                clickArea.SetAnchorsPreset(LayoutPreset.FullRect);
                var transparent = new StyleBoxFlat { BgColor = Colors.Transparent };
                clickArea.AddThemeStyleboxOverride("normal", transparent);
                clickArea.AddThemeStyleboxOverride("hover", transparent);
                clickArea.AddThemeStyleboxOverride("pressed", transparent);
                row.AddChild(clickArea);
                clickArea.Pressed += () => RemoveFromDeck(cardId);
            }

            _deckListContainer.AddChild(row);
        }

        if (grouped.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "Your deck is empty.\nAdd cards from the left panel.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyBodyFont(emptyLabel, FontSmall);
            emptyLabel.AddThemeColorOverride("font_color", TextMuted);
            _deckListContainer.AddChild(emptyLabel);
        }
    }

    private void RefreshCurve()
    {
        foreach (var child in _curveContainer.GetChildren())
            child.QueueFree();

        int[] curve = new int[8]; // costs 0-7+
        foreach (var id in _deckCardIds)
        {
            var def = LookupCard(id);
            if (def != null)
            {
                int idx = Mathf.Clamp(def.Cost, 0, 7);
                curve[idx]++;
            }
        }

        int maxCount = Math.Max(1, curve.Max());

        var curveHbox = new HBoxContainer();
        curveHbox.SetAnchorsPreset(LayoutPreset.FullRect);
        curveHbox.AddThemeConstantOverride("separation", 2);
        _curveContainer.AddChild(curveHbox);

        for (int i = 0; i < 8; i++)
        {
            var col = new VBoxContainer();
            col.SizeFlagsVertical = (SizeFlags)3;
            col.SizeFlagsHorizontal = (SizeFlags)3;
            col.AddThemeConstantOverride("separation", 1);
            curveHbox.AddChild(col);

            // Bar
            var bar = new ColorRect
            {
                Color = Gold,
                SizeFlagsHorizontal = (SizeFlags)3,
                SizeFlagsVertical = (SizeFlags)3,
                CustomMinimumSize = new Vector2(0, 0)
            };
            float pct = (float)curve[i] / maxCount;
            bar.Size = new Vector2(0, Mathf.Max(2, pct * 36f));
            bar.AnchorBottom = 0; // grow from bottom
            col.AddChild(bar);

            // Label
            var label = new Label
            {
                Text = i == 7 ? "7+" : i.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsHorizontal = (SizeFlags)3
            };
            ApplyBodyFont(label, FontTiny);
            label.AddThemeColorOverride("font_color", TextMuted);
            col.AddChild(label);
        }
    }

    private void UpdateCount()
    {
        int unique = _deckCardIds.Distinct().Count();
        int total = _deckCardIds.Count;
        _countLabel.Text = $"{total}/30";

        // Progress bar
        float pct = Mathf.Clamp((float)total / 30f, 0f, 1f);
        _countBar.Size = new Vector2(pct * _rightRail.Size.X, 4);
        _countBar.Color = total >= 30 ? Moss : Gold;

        // Forge button state
        var result = DeckValidator.Validate(_deckCardIds, LookupCard);
        _forgeButton.Disabled = !result.IsValid;
        _forgeButton.Text = result.IsValid ? "FORGE DECK" : $"{total}/30";
    }

    // ════════════════════════════════════════════════════════════════
    // ADD / REMOVE
    // ════════════════════════════════════════════════════════════════

    private void AddToDeck(string cardId)
    {
        if (_lockedCardIds.Contains(cardId)) return;
        var result = DeckValidator.CanAdd(_deckCardIds, cardId, LookupCard);
        if (!result.IsValid) return;

        _deckCardIds.Add(cardId);
        _modified = true;
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
    // BACK — with unsaved-changes confirmation
    // ════════════════════════════════════════════════════════════════

    private void OnBack()
    {
        if (!_modified)
        {
            GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
            return;
        }

        // Stone confirmation dialog
        var dialog = new PanelContainer();
        dialog.Name = "ConfirmDialog";
        dialog.Position = new Vector2(GetViewportRect().Size.X / 2f - 140, GetViewportRect().Size.Y / 2f - 50);
        dialog.Size = new Vector2(280, 100);
        dialog.MouseFilter = MouseFilterEnum.Pass;
        dialog.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.13f, 0.10f, 0.95f),
            BorderColor = BorderStandard,
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 8, ContentMarginTop = 8,
            ContentMarginRight = 8, ContentMarginBottom = 8
        });

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 6);
        dialog.AddChild(vbox);

        var msg = new Label
        {
            Text = "Unsaved changes will be lost.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyBodyFont(msg, FontSmall);
        msg.AddThemeColorOverride("font_color", TextPrimary);
        vbox.AddChild(msg);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        btnRow.SizeFlagsHorizontal = (SizeFlags)3;
        btnRow.SizeFlagsVertical = (SizeFlags)3;
        vbox.AddChild(btnRow);

        // Keep editing button
        var keepBtn = new Button
        {
            Text = "Keep editing",
            SizeFlagsHorizontal = (SizeFlags)3,
            CustomMinimumSize = new Vector2(0, 32)
        };
        keepBtn.AddThemeColorOverride("font_color", Gold);
        keepBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = SurfaceStone,
            BorderColor = BorderStandard,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        keepBtn.Pressed += () =>
        {
            if (IsInstanceValid(dialog))
                dialog.QueueFree();
        };
        btnRow.AddChild(keepBtn);

        // Discard button
        var discardBtn = new Button
        {
            Text = "Discard",
            SizeFlagsHorizontal = (SizeFlags)3,
            CustomMinimumSize = new Vector2(0, 32)
        };
        discardBtn.AddThemeColorOverride("font_color", TextMuted);
        discardBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.08f, 0.05f, 1),
            BorderColor = BorderSubtle,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        discardBtn.Pressed += () =>
        {
            if (IsInstanceValid(dialog))
                dialog.QueueFree();
            GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        };
        btnRow.AddChild(discardBtn);

        AddChild(dialog);
    }

    // ════════════════════════════════════════════════════════════════
    // SAVE — with name prompt & overwrite protection
    // ════════════════════════════════════════════════════════════════

    private void OnSaveDeck()
    {
        var validation = DeckValidator.Validate(_deckCardIds, LookupCard);
        if (!validation.IsValid) return;

        ShowSaveNameDialog();
    }

    /// <summary>
    /// Show a stone-themed dialog prompting for the deck name.
    /// On confirm, checks for overwrite conflicts, then persists.
    /// </summary>
    private void ShowSaveNameDialog()
    {
        var dialog = new PanelContainer();
        dialog.Name = "SaveNameDialog";
        float vw = GetViewportRect().Size.X;
        float vh = GetViewportRect().Size.Y;
        dialog.Position = new Vector2(vw / 2f - 150, vh / 2f - 70);
        dialog.Size = new Vector2(300, 140);
        dialog.MouseFilter = MouseFilterEnum.Pass;
        dialog.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.13f, 0.10f, 0.97f),
            BorderColor = Gold,
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12, ContentMarginTop = 12,
            ContentMarginRight = 12, ContentMarginBottom = 12
        });

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 8);
        dialog.AddChild(vbox);

        var titleLabel = new Label
        {
            Text = "Name Your Deck",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        ApplyHeaderFont(titleLabel, FontBody);
        titleLabel.AddThemeColorOverride("font_color", Gold);
        vbox.AddChild(titleLabel);

        var nameEdit = new LineEdit
        {
            Text = _deckName,
            PlaceholderText = "Enter deck name...",
            CustomMinimumSize = new Vector2(0, 28)
        };
        nameEdit.AddThemeColorOverride("font_color", TextPrimary);
        nameEdit.AddThemeColorOverride("placeholder_color", TextMuted);
        nameEdit.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#1C1712"),
            BorderColor = BorderStandard,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 6, ContentMarginTop = 2,
            ContentMarginRight = 6, ContentMarginBottom = 2
        });
        vbox.AddChild(nameEdit);
        nameEdit.GrabFocus();
        nameEdit.SelectAll();

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        btnRow.SizeFlagsHorizontal = (SizeFlags)3;
        btnRow.SizeFlagsVertical = (SizeFlags)3;
        vbox.AddChild(btnRow);

        var cancelBtn = new Button
        {
            Text = "Cancel",
            SizeFlagsHorizontal = (SizeFlags)3,
            CustomMinimumSize = new Vector2(0, 32)
        };
        cancelBtn.AddThemeColorOverride("font_color", TextMuted);
        cancelBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.18f, 0.15f, 1),
            BorderColor = BorderSubtle,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        cancelBtn.Pressed += () =>
        {
            if (IsInstanceValid(dialog)) dialog.QueueFree();
        };
        btnRow.AddChild(cancelBtn);

        var okBtn = new Button
        {
            Text = "Save",
            SizeFlagsHorizontal = (SizeFlags)3,
            CustomMinimumSize = new Vector2(0, 32)
        };
        okBtn.AddThemeColorOverride("font_color", Gold);
        okBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = SurfaceStone,
            BorderColor = Gold,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        okBtn.Pressed += () =>
        {
            string newName = nameEdit.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            _deckName = newName;
            _deckNameLabel.Text = _deckName;
            if (IsInstanceValid(dialog)) dialog.QueueFree();

            // Check overwrite
            if (CampaignContext.Progression.SavedDecks.ContainsKey(_deckName))
                ShowOverwriteConfirmDialog();
            else
                PersistDeck();
        };
        btnRow.AddChild(okBtn);

        // Submit on Enter
        nameEdit.TextSubmitted += (text) =>
        {
            string newName = text.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            _deckName = newName;
            _deckNameLabel.Text = _deckName;
            if (IsInstanceValid(dialog)) dialog.QueueFree();

            if (CampaignContext.Progression.SavedDecks.ContainsKey(_deckName))
                ShowOverwriteConfirmDialog();
            else
                PersistDeck();
        };

        AddChild(dialog);
    }

    /// <summary>
    /// Confirm dialog for overwriting an existing saved deck.
    /// </summary>
    private void ShowOverwriteConfirmDialog()
    {
        var dialog = new PanelContainer();
        dialog.Name = "OverwriteDialog";
        float vw = GetViewportRect().Size.X;
        float vh = GetViewportRect().Size.Y;
        dialog.Position = new Vector2(vw / 2f - 150, vh / 2f - 60);
        dialog.Size = new Vector2(300, 120);
        dialog.MouseFilter = MouseFilterEnum.Pass;
        dialog.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.13f, 0.10f, 0.97f),
            BorderColor = Gold,
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 12, ContentMarginTop = 12,
            ContentMarginRight = 12, ContentMarginBottom = 12
        });

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 8);
        dialog.AddChild(vbox);

        var msg = new Label
        {
            Text = $"A deck named \"{_deckName}\" already exists.\nOverwrite it?",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsVertical = (SizeFlags)1
        };
        ApplyBodyFont(msg, FontSmall);
        msg.AddThemeColorOverride("font_color", TextPrimary);
        vbox.AddChild(msg);

        var btnRow = new HBoxContainer();
        btnRow.AddThemeConstantOverride("separation", 8);
        btnRow.SizeFlagsHorizontal = (SizeFlags)3;
        btnRow.SizeFlagsVertical = (SizeFlags)3;
        vbox.AddChild(btnRow);

        var cancelBtn = new Button
        {
            Text = "Cancel",
            SizeFlagsHorizontal = (SizeFlags)3,
            CustomMinimumSize = new Vector2(0, 32)
        };
        cancelBtn.AddThemeColorOverride("font_color", TextMuted);
        cancelBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.18f, 0.15f, 1),
            BorderColor = BorderSubtle,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        cancelBtn.Pressed += () =>
        {
            if (IsInstanceValid(dialog)) dialog.QueueFree();
        };
        btnRow.AddChild(cancelBtn);

        var overwriteBtn = new Button
        {
            Text = "Overwrite",
            SizeFlagsHorizontal = (SizeFlags)3,
            CustomMinimumSize = new Vector2(0, 32)
        };
        overwriteBtn.AddThemeColorOverride("font_color", Color.FromHtml("#E8A040"));
        overwriteBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.25f, 0.15f, 0.05f, 1),
            BorderColor = Color.FromHtml("#C08030"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        overwriteBtn.Pressed += () =>
        {
            if (IsInstanceValid(dialog)) dialog.QueueFree();
            PersistDeck(); // overwrite
        };
        btnRow.AddChild(overwriteBtn);

        AddChild(dialog);
    }

    /// <summary>
    /// Persist the current deck to ProgressionState.SavedDecks + legacy paths.
    /// </summary>
    private void PersistDeck()
    {
        string classId = CampaignContext.ChosenClass;
        if (string.IsNullOrEmpty(classId))
            classId = CampaignContext.Profiles.Count > 0 ? CampaignContext.Profiles[0].ClassId : "warrior";

        // Save to ProgressionState.SavedDecks (v2 schema)
        var prog = CampaignContext.Progression;
        prog.SavedDecks[_deckName] = new List<string>(_deckCardIds);

        // Legacy: also update the single-slot DeckCardIds for backward compat
        prog.DeckCardIds.Clear();
        prog.DeckCardIds.AddRange(_deckCardIds);
        CampaignContext.PlayerDeckIds.Clear();
        CampaignContext.PlayerDeckIds.AddRange(_deckCardIds);

        // Account-wide JSON deck library
        CampaignContext.SaveDeck(_deckName, classId, _deckCardIds);

        // Update the active profile
        string deckId = $"{classId}_{_deckName.ToLowerInvariant().Replace(" ", "_")}";
        if (CampaignContext.ActiveProfile != null)
        {
            CampaignContext.ActiveProfile.ActiveDeckId = deckId;
            CampaignContext.SaveCampaignProfile();
        }

        // Persist SQLite
        CampaignContext.SaveManager.Save();
        _modified = false;

        ShowToast("Deck saved.");
        RefreshSavedDecksList();
    }

    /// <summary>
    /// Refresh the saved-decks section in the right rail.
    /// Reads from ProgressionState.SavedDecks.
    /// </summary>
    private void RefreshSavedDecksList()
    {
        if (_savedDecksContainer == null) return;
        foreach (var child in _savedDecksContainer.GetChildren())
            child.QueueFree();

        var savedDecks = CampaignContext.Progression.SavedDecks;
        if (savedDecks.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No saved decks yet.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2(0, 24)
            };
            ApplyBodyFont(emptyLabel, FontTiny);
            emptyLabel.AddThemeColorOverride("font_color", TextMuted);
            _savedDecksContainer.AddChild(emptyLabel);
            return;
        }

        foreach (var (deckName, cardIds) in savedDecks)
        {
            var row = new PanelContainer();
            row.CustomMinimumSize = new Vector2(0, 28);
            row.SizeFlagsHorizontal = (SizeFlags)3;
            row.MouseDefaultCursorShape = CursorShape.PointingHand;

            var rowStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.13f, 0.10f, 0.4f),
                BorderColor = Color.FromHtml("#5A5048"),
                BorderWidthLeft = 2, BorderWidthTop = 0,
                BorderWidthRight = 0, BorderWidthBottom = 0,
                CornerRadiusTopLeft = 3, CornerRadiusBottomLeft = 3
            };
            row.AddThemeStyleboxOverride("panel", rowStyle);

            var hbox = new HBoxContainer();
            hbox.AnchorLeft = 0; hbox.AnchorRight = 1;
            hbox.AnchorTop = 0; hbox.AnchorBottom = 1;
            hbox.OffsetLeft = 4;
            hbox.MouseFilter = MouseFilterEnum.Ignore;
            row.AddChild(hbox);

            var nameLabel = new Label
            {
                Text = deckName,
                SizeFlagsHorizontal = (SizeFlags)3,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyBodyFont(nameLabel, FontSmall);
            nameLabel.AddThemeColorOverride("font_color", Gold);
            hbox.AddChild(nameLabel);

            var countLabel = new Label
            {
                Text = $"{cardIds.Count}/30",
                CustomMinimumSize = new Vector2(28, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            ApplyBodyFont(countLabel, FontTiny);
            countLabel.AddThemeColorOverride("font_color", TextMuted);
            hbox.AddChild(countLabel);

            // Click to load
            var clickArea = new Button();
            clickArea.SetAnchorsPreset(LayoutPreset.FullRect);
            clickArea.MouseDefaultCursorShape = CursorShape.PointingHand;
            clickArea.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = Colors.Transparent });
            clickArea.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = Colors.Transparent });
            clickArea.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = Colors.Transparent });
            row.AddChild(clickArea);

            // Capture the deck name for the closure
            string capturedName = deckName;
            List<string> capturedCards = cardIds;
            clickArea.Pressed += () => LoadDeck(capturedName, capturedCards);

            _savedDecksContainer.AddChild(row);
        }
    }

    /// <summary>
    /// Load a saved deck into the builder. Shows unsaved-changes guard if modified.
    /// </summary>
    private void LoadDeck(string deckName, List<string> cardIds)
    {
        if (_modified)
        {
            // Show unsaved-changes confirmation before loading
            var dialog = new PanelContainer();
            dialog.Name = "LoadConfirmDialog";
            float vw = GetViewportRect().Size.X;
            float vh = GetViewportRect().Size.Y;
            dialog.Position = new Vector2(vw / 2f - 150, vh / 2f - 60);
            dialog.Size = new Vector2(300, 120);
            dialog.MouseFilter = MouseFilterEnum.Pass;
            dialog.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.15f, 0.13f, 0.10f, 0.97f),
                BorderColor = Gold,
                BorderWidthLeft = 2, BorderWidthTop = 2,
                BorderWidthRight = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
                ContentMarginLeft = 12, ContentMarginTop = 12,
                ContentMarginRight = 12, ContentMarginBottom = 12
            });

            var vbox = new VBoxContainer();
            vbox.SetAnchorsPreset(LayoutPreset.FullRect);
            vbox.AddThemeConstantOverride("separation", 8);
            dialog.AddChild(vbox);

            var msg = new Label
            {
                Text = "Load this deck?\nUnsaved changes will be lost.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ApplyBodyFont(msg, FontSmall);
            msg.AddThemeColorOverride("font_color", TextPrimary);
            vbox.AddChild(msg);

            var btnRow = new HBoxContainer();
            btnRow.AddThemeConstantOverride("separation", 8);
            btnRow.SizeFlagsHorizontal = (SizeFlags)3;
            btnRow.SizeFlagsVertical = (SizeFlags)3;
            vbox.AddChild(btnRow);

            var cancelBtn = new Button
            {
                Text = "Cancel",
                SizeFlagsHorizontal = (SizeFlags)3,
                CustomMinimumSize = new Vector2(0, 32)
            };
            cancelBtn.AddThemeColorOverride("font_color", TextMuted);
            cancelBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = new Color(0.2f, 0.18f, 0.15f, 1),
                BorderColor = BorderSubtle,
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
            });
            cancelBtn.Pressed += () =>
            {
                if (IsInstanceValid(dialog)) dialog.QueueFree();
            };
            btnRow.AddChild(cancelBtn);

            var loadBtn = new Button
            {
                Text = "Load",
                SizeFlagsHorizontal = (SizeFlags)3,
                CustomMinimumSize = new Vector2(0, 32)
            };
            loadBtn.AddThemeColorOverride("font_color", Gold);
            loadBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = SurfaceStone,
                BorderColor = Gold,
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
            });
            loadBtn.Pressed += () =>
            {
                if (IsInstanceValid(dialog)) dialog.QueueFree();
                DoLoadDeck(deckName, cardIds);
            };
            btnRow.AddChild(loadBtn);

            AddChild(dialog);
        }
        else
        {
            DoLoadDeck(deckName, cardIds);
        }
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

        ShowToast($"Loaded: {deckName}");
    }

    /// <summary>
    /// Show a brief gold toast message centered on screen.
    /// </summary>
    private void ShowToast(string message)
    {
        var toast = new PanelContainer();
        toast.Name = "Toast";
        toast.Position = new Vector2(GetViewportRect().Size.X / 2f - 100, GetViewportRect().Size.Y / 2f - 20);
        toast.Size = new Vector2(200, 40);
        toast.MouseFilter = MouseFilterEnum.Ignore;
        toast.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.18f, 0.14f, 0.9f),
            BorderColor = Gold,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginTop = 6,
            ContentMarginRight = 12, ContentMarginBottom = 6
        });

        var toastLabel = new Label
        {
            Text = message,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(toastLabel, FontBody);
        toastLabel.AddThemeColorOverride("font_color", Gold);
        toastLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        toast.AddChild(toastLabel);

        AddChild(toast);

        // Fade out after 1s
        var tween = CreateTween();
        tween.TweenInterval(0.8f);
        tween.TweenProperty(toast, "modulate", new Color(1, 1, 1, 0), 0.3f);
        tween.TweenCallback(Callable.From(() =>
        {
            if (IsInstanceValid(toast))
                toast.QueueFree();
        }));
    }
}