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

        // Apply core cards from CampaignContext (set by ChooseYourPath)
        if (CampaignContext.CoreCardIds != null && CampaignContext.CoreCardIds.Count > 0)
        {
            ApplyCoreCardsInternal(CampaignContext.CoreCardIds);
            CampaignContext.CoreCardIds = null;
        }

        // Default strata filter for cross-strata classes (RANGER, OCCULTIST)
        string chosen = CampaignContext.ChosenClass;
        if (chosen == "ranger" || chosen == "occultist" || string.IsNullOrEmpty(chosen))
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
                            : "/home/fictive/runewake/artifacts/captures/deck_test.png";
                        image.SavePng(path);
                        GD.Print($"[DeckBuilderScene] Captured to {path}");
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

        // ── Top bar (64px) ──
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

        // Title
        var title = new Label
        {
            Text = "DECK FORGE",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };
        ApplyHeaderFont(title, FontSubtitle);
        title.AddThemeColorOverride("font_color", Gold);
        title.Position = new Vector2(16, 0);
        title.Size = new Vector2(200, 64);
        _topBar.AddChild(title);

        // Search field
        _searchField = new LineEdit
        {
            PlaceholderText = "Search cards...",
            CustomMinimumSize = new Vector2(200, 32),
            SizeFlagsHorizontal = (SizeFlags)3
        };
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
        _searchField.Position = new Vector2(220, 16);
        _searchField.Size = new Vector2(200, 32);
        _topBar.AddChild(_searchField);

        // Filter chips row (scrollable at narrow widths)
        var chipScroll = new ScrollContainer();
        chipScroll.SizeFlagsHorizontal = (SizeFlags)3;
        chipScroll.SizeFlagsVertical = (SizeFlags)0;
        chipScroll.HorizontalScrollMode = (ScrollContainer.ScrollMode)1;
        chipScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _filterChipRow = new HBoxContainer();
        _filterChipRow.AddThemeConstantOverride("separation", 8);
        _filterChipRow.CustomMinimumSize = new Vector2(0, 44);
        _filterChipRow.SizeFlagsVertical = (SizeFlags)0;
        chipScroll.AddChild(_filterChipRow);
        chipScroll.Position = new Vector2(440, 10);
        chipScroll.Size = new Vector2(600, 44);
        _topBar.AddChild(chipScroll);

        for (int i = 0; i < StrataOptions.Length; i++)
        {
            int idx = i;
            var chip = MakeFilterChip(StrataOptions[i], StrataColors[i], i == 0);
            chip.Pressed += () => {
                _selectedStrataIdx = idx;
                UpdateFilterChips();
                RefreshCardGrid();
            };
            _filterChipRow.AddChild(chip);
        }

        // ── Left panel (card grid) ──
        _leftPanel = new Control();
        _leftPanel.AnchorLeft = 0;
        _leftPanel.AnchorRight = 0.72f;
        _leftPanel.AnchorTop = 0;
        _leftPanel.AnchorBottom = 1;
        _leftPanel.OffsetTop = 64; // below top bar
        AddChild(_leftPanel);

        // Grid scroll area
        _gridScroll = new ScrollContainer();
        _gridScroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _gridScroll.SizeFlagsHorizontal = (SizeFlags)3;
        _gridScroll.SizeFlagsVertical = (SizeFlags)3;
        _leftPanel.AddChild(_gridScroll);

        // Dynamic grid container (not GridContainer — we lay out rows manually for proper fill)
        _cardGrid = new VBoxContainer();
        _cardGrid.SizeFlagsHorizontal = (SizeFlags)3;
        _cardGrid.AddThemeConstantOverride("separation", 8);
        _cardGrid.CustomMinimumSize = new Vector2(0, 0);
        _gridScroll.AddChild(_cardGrid);

        // ── Right rail (fixed ~320px at 1152) ──
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
                _deckNameEdit = null;
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
    /// Create a strata filter chip (pill-shaped, >= 44px touch target).
    /// </summary>
    private Button MakeFilterChip(string label, Color accent, bool selected)
    {
        var btn = new Button
        {
            Flat = false,
            Text = "", // we build custom content
            CustomMinimumSize = new Vector2(0, 44)
        };

        // Pill shape style — full rounded corners
        var baseStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.15f, 0.13f, 0.10f, 1),
            BorderColor = selected ? Gold : BorderSubtle,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 22, CornerRadiusTopRight = 22,
            CornerRadiusBottomLeft = 22, CornerRadiusBottomRight = 22,
            ContentMarginLeft = 10, ContentMarginTop = 0,
            ContentMarginRight = 14, ContentMarginBottom = 0
        };
        btn.AddThemeStyleboxOverride("normal", baseStyle);
        btn.AddThemeStyleboxOverride("hover", baseStyle);
        btn.AddThemeStyleboxOverride("pressed", baseStyle);

        // Inner HBox: dot + label
        var inner = new HBoxContainer();
        inner.SetAnchorsPreset(LayoutPreset.FullRect);
        inner.MouseFilter = MouseFilterEnum.Ignore;
        inner.AddThemeConstantOverride("separation", 6);
        btn.AddChild(inner);

        // Strata color dot
        var dot = new ColorRect
        {
            Color = accent,
            CustomMinimumSize = new Vector2(10, 10),
            Size = new Vector2(10, 10),
            MouseFilter = MouseFilterEnum.Ignore
        };
        inner.AddChild(dot);

        // Label with Cinzel font
        var chipLabel = new Label
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        ApplyHeaderFont(chipLabel, FontBody);
        chipLabel.AddThemeColorOverride("font_color", selected ? Gold : TextMuted);
        inner.AddChild(chipLabel);

        return btn;
    }

    private void UpdateFilterChips()
    {
        foreach (var child in _filterChipRow.GetChildren())
        {
            if (child is Button btn)
            {
                int idx = Array.IndexOf(StrataOptions, btn.Text);
                // With the new layout, btn.Text is "" — find the label inside
                if (idx < 0 && btn.GetChildCount() > 0)
                {
                    // Get the inner HBox's child label
                    var inner = btn.GetChild(0) as HBoxContainer;
                    if (inner != null && inner.GetChildCount() > 1)
                    {
                        var chipLabel = inner.GetChild(1) as Label;
                        if (chipLabel != null)
                        {
                            idx = Array.IndexOf(StrataOptions, chipLabel.Text);
                        }
                    }
                }

                bool selected = idx >= 0 && idx == _selectedStrataIdx;

                // Update button style
                var style = new StyleBoxFlat
                {
                    BgColor = selected ? new Color(0.2f, 0.18f, 0.14f, 1) : new Color(0.15f, 0.13f, 0.10f, 1),
                    BorderColor = selected ? Gold : BorderSubtle,
                    BorderWidthLeft = 1, BorderWidthTop = 1,
                    BorderWidthRight = 1, BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 22, CornerRadiusTopRight = 22,
                    CornerRadiusBottomLeft = 22, CornerRadiusBottomRight = 22,
                    ContentMarginLeft = 10, ContentMarginTop = 0,
                    ContentMarginRight = 14, ContentMarginBottom = 0
                };
                btn.AddThemeStyleboxOverride("normal", style);
                btn.AddThemeStyleboxOverride("hover", style);
                btn.AddThemeStyleboxOverride("pressed", style);

                // Update label color
                if (btn.GetChildCount() > 0 && btn.GetChild(0) is HBoxContainer innerHbox
                    && innerHbox.GetChildCount() > 1 && innerHbox.GetChild(1) is Label lbl)
                {
                    lbl.AddThemeColorOverride("font_color", selected ? Gold : TextMuted);
                }
            }
        }
    }

    /// <summary>
    /// Create a grid card item using the CardPlate template.
    /// </summary>
    private Control MakeGridCard(CardDef card, int ownedCount, int inDeckCount, float gridW)
    {
        float gridH = gridW * 152f / 104f; // maintain aspect ratio

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

        // ── Card art (full-bleed, cover-cropped) ──
        var artRect = new TextureRect();
        artRect.SetAnchorsPreset(LayoutPreset.FullRect);
        artRect.MouseFilter = MouseFilterEnum.Ignore;
        artRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
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

        // Dim if at copy limit or unowned
        bool isAtLimit = ownedCount <= inDeckCount;
        bool isUnowned = ownedCount == 0;
        if (isUnowned)
            container.Modulate = new Color(1, 1, 1, 0.4f);
        else if (isAtLimit)
            container.Modulate = new Color(1, 1, 1, 0.55f);

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

    private void RefreshCardGrid()
    {
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

        // Compute dynamic columns from available width
        float availWidth = _leftPanel.Size.X - 16; // 8px padding each side
        if (availWidth <= 0) availWidth = 800; // fallback before layout

        float gap = 8f;
        float targetCardW = 110f;
        int columns = Mathf.Max(2, Mathf.FloorToInt((availWidth + gap) / (targetCardW + gap)));
        columns = Mathf.Min(columns, 6);

        // Card width = (availWidth - (columns-1) * gap) / columns
        float cardW = (availWidth - (columns - 1) * gap) / columns;
        cardW = Mathf.Min(cardW, 140f); // don't get too wide

        for (int i = 0; i < filtered.Count; i += columns)
        {
            var row = new HBoxContainer();
            row.SizeFlagsHorizontal = (SizeFlags)3;
            row.AddThemeConstantOverride("separation", Mathf.RoundToInt(gap));
            row.CustomMinimumSize = new Vector2(0, 0);
            _cardGrid.AddChild(row);

            for (int j = 0; j < columns && i + j < filtered.Count; j++)
            {
                var card = filtered[i + j];
                int owned = 1;
                int inDeck = _deckCardIds.Count(id => id == card.Id);
                var item = MakeGridCard(card, owned, inDeck, cardW);
                row.AddChild(item);
            }
        }
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
        RefreshCardGrid();
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
        RefreshCardGrid();
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
    // SAVE
    // ════════════════════════════════════════════════════════════════

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
        _modified = false;

        // Gold toast
        ShowToast("Deck forged.");

        // Return after brief delay
        var timer = GetTree().CreateTimer(1.0f);
        timer.Timeout += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
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