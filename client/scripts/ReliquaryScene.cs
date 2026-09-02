using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// The Reliquary — collection browser scene.
/// Shows all cards in a scrollable grid with strata filter chips,
/// owned-count badges, NEW badges (cleared on view), and tap-to-inspect.
/// Unowned cards render as dark silhouettes with the card name only.
/// </summary>
public partial class ReliquaryScene : Control
{
    // ── Nodes ──
    private Control _filterChipRow;
    private GridContainer _cardGrid = default!;
    private ScrollContainer _gridScroll;
    private Label _titleLabel;
    private Label _collectionCount;
    private Button _backButton;
    private Control? _inspectOverlay;
    private int _selectedStrataIdx; // 0=All, 1-5=VERDANT..DAWN
    private bool _captureTriggered = false;

    // ── Constants (same as DeckBuilderScene) ──
    private static readonly string[] StrataOptions = { "ALL", "VERDANT", "EMBER", "TIDE", "HOLLOW", "DAWN" };
    private static readonly Color[] StrataColors =
    {
        Colors.White, // ALL
        new Color(0.2f, 0.7f, 0.3f), // VERDANT
        new Color(0.9f, 0.3f, 0.1f), // EMBER
        new Color(0.2f, 0.5f, 0.8f), // TIDE
        new Color(0.5f, 0.2f, 0.5f), // HOLLOW
        new Color(0.9f, 0.8f, 0.2f), // DAWN
    };

    private static readonly Strata[] StrataValues =
    {
        Strata.VERDANT,
        Strata.EMBER,
        Strata.TIDE,
        Strata.HOLLOW,
        Strata.DAWN,
    };

    // ── Card data cache ──
    private List<CardDef> _allCards = new();
    private List<CardDef> _filteredCards = new();

    public override void _Ready()
    {
        // ——— Full-screen background ———
        var bg = new ColorRect
        {
            Color = Color.FromHtml("#0A0806"),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(bg);

        // ——— Top bar ———
        var topBar = new ColorRect
        {
            Color = new Color(0.1f, 0.08f, 0.06f, 0.85f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.08f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(topBar);

        var barLine = new ColorRect
        {
            Color = new Color(0.6f, 0.5f, 0.25f, 0.25f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.08f, AnchorBottom = 0.083f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(barLine);

        // ——— Back button ———
        _backButton = new Button
        {
            Text = "< Back",
            AnchorLeft = 0.01f, AnchorRight = 0.10f,
            AnchorTop = 0.005f, AnchorBottom = 0.075f
        };
        _backButton.AddThemeFontSizeOverride("font_size", 12);
        _backButton.AddThemeColorOverride("font_color", Color.FromHtml("#CFC4AE"));
        _backButton.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        _backButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            NavigateBack();
        };
        AddChild(_backButton);

        // ——— Title "RELIQUARY" ———
        _titleLabel = new Label
        {
            Text = "RELIQUARY",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.2f, AnchorRight = 0.6f,
            AnchorTop = 0.005f, AnchorBottom = 0.075f,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        ApplyHeaderFont(_titleLabel, 24);
        _titleLabel.AddThemeColorOverride("font_color", Color.FromHtml("#D4B84C"));
        AddChild(_titleLabel);

        // ——— Collection count (top-right) ———
        _collectionCount = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.7f, AnchorRight = 0.98f,
            AnchorTop = 0.005f, AnchorBottom = 0.075f,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        _collectionCount.AddThemeFontSizeOverride("font_size", 13);
        _collectionCount.AddThemeColorOverride("font_color", new Color(0.85f, 0.72f, 0.35f, 0.8f));
        AddChild(_collectionCount);

        // ——— Filter chip row (below top bar) ———
        BuildFilterChips();

        // ——— Scrollable card grid ———
        _gridScroll = new ScrollContainer();
        _gridScroll.AnchorLeft = 0.02f; _gridScroll.AnchorRight = 0.98f;
        _gridScroll.AnchorTop = 0.13f; _gridScroll.AnchorBottom = 1f;
        _gridScroll.SizeFlagsHorizontal = SizeFlags.Fill;
        _gridScroll.SizeFlagsVertical = SizeFlags.Fill;
        _gridScroll.ScrollDeadzone = 24;
        AddChild(_gridScroll);

        _cardGrid = new GridContainer();
        _cardGrid.AddThemeConstantOverride("separation", 8);
        _cardGrid.Columns = 5;
        _cardGrid.SizeFlagsHorizontal = SizeFlags.Fill;
        _cardGrid.SizeFlagsVertical = SizeFlags.Fill;
        _cardGrid.CustomMinimumSize = new Vector2(0, 0);
        _gridScroll.AddChild(_cardGrid);

        // ——— Load cards ———
        LoadAllCards();
        RefreshGrid();

        // Capture hook: if reliquary capture mode, capture on first _Process frame
        // to ensure viewport is fully initialized
        if (CampaignContext.CaptureReliquaryScreenshot)
        {
            GD.Print("[ReliquaryScene] Capture mode active — will capture on first _Process");
        }
    }

    public override void _Process(double delta)
    {
        if (_captureTriggered) return;
        if (!CampaignContext.CaptureReliquaryScreenshot) return;
        _captureTriggered = true;

        GD.Print("[ReliquaryScene] _Process capture triggered");
        var suffix = CampaignContext.WideCaptureMode ? "_wide" : "";
        var img = GetViewport().GetTexture().GetImage();
        if (img != null)
        {
            string path = $"/home/fictive/runewake/artifacts/captures/reliquary_test{suffix}.png";
            img.SavePng(path);
            GD.Print($"[ReliquaryScene] Saved {path}");

            var meta = new System.Text.StringBuilder();
            meta.Append("{\n");
            meta.Append($"  \"capture_type\": \"reliquary_test{suffix}\",\n");
            meta.Append($"  \"view_width\": {(int)GetViewportRect().Size.X},\n");
            meta.Append($"  \"view_height\": {(int)GetViewportRect().Size.Y},\n");
            meta.Append($"  \"strata_filter_idx\": {_selectedStrataIdx},\n");
            meta.Append($"  \"grid_cards_shown\": {_filteredCards.Count},\n");
            meta.Append($"  \"grid_columns\": 5\n");
            meta.Append("}\n");

            string metaPath = $"/home/fictive/runewake/artifacts/captures/reliquary_test{suffix}.meta.json";
            using (var writer = new System.IO.StreamWriter(metaPath))
                writer.Write(meta.ToString());
            GD.Print($"[ReliquaryScene] Saved {metaPath}");
        }
        else
        {
            GD.PrintErr("[ReliquaryScene] Failed to capture: GetImage() returned null");
        }
        GetTree().Quit();
    }

    private void BuildFilterChips()
    {
        // Container for the chip row with background strip
        var chipStrip = new ColorRect
        {
            Color = new Color(0.08f, 0.065f, 0.04f, 0.5f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.083f, AnchorBottom = 0.13f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(chipStrip);

        var chipScroll = new ScrollContainer();
        chipScroll.AnchorLeft = 0.05f; chipScroll.AnchorRight = 0.95f;
        chipScroll.AnchorTop = 0.083f; chipScroll.AnchorBottom = 0.13f;
        chipScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
        chipScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        chipScroll.CustomMinimumSize = new Vector2(200, 44);
        AddChild(chipScroll);

        _filterChipRow = new HBoxContainer();
        _filterChipRow.AddThemeConstantOverride("separation", 8);
        _filterChipRow.CustomMinimumSize = new Vector2(0, 44);
        _filterChipRow.SizeFlagsVertical = (SizeFlags)0;
        chipScroll.AddChild(_filterChipRow);

        // Default strata filter
        if (CampaignContext.CaptureOverrideStrataIdx >= 0)
            _selectedStrataIdx = CampaignContext.CaptureOverrideStrataIdx;
        else
            _selectedStrataIdx = 0; // ALL

        for (int i = 0; i < StrataOptions.Length; i++)
        {
            int idx = i;
            var chip = MakeFilterChip(StrataOptions[i], StrataColors[i], i);
            chip.Pressed += () =>
            {
                _selectedStrataIdx = idx;
                UpdateFilterChips();
                RefreshGrid();
            };
            _filterChipRow.AddChild(chip);
        }

        UpdateFilterChips();
    }

    private Button MakeFilterChip(string label, Color accent, int idx)
    {
        var btn = new Button { Flat = false, Text = "" };
        btn.CustomMinimumSize = new Vector2(44, 44);

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

        var inner = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsVertical = (SizeFlags)3,
            Alignment = BoxContainer.AlignmentMode.Center
        };
        inner.AddThemeConstantOverride("separation", 6);
        btn.AddChild(inner);

        var swatch = new ColorRect
        {
            Color = accent,
            CustomMinimumSize = new Vector2(8, 8),
            Size = new Vector2(8, 8),
            MouseFilter = MouseFilterEnum.Ignore
        };
        inner.AddChild(swatch);

        var chipFont = ThemeTokens.GetHeaderFont(11);
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

            Color accent;
            if (idx >= 0 && idx < StrataColors.Length)
                accent = StrataColors[idx];
            else
                accent = Gold;

            if (selected)
            {
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

            // Update label color
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

    private void LoadAllCards()
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

        // Sort: by strata then card ID for stable ordering
        _allCards = _allCards.OrderBy(c => c.Strata.ToString()).ThenBy(c => c.Id).ToList();
    }

    private void RefreshGrid()
    {
        // Clear existing grid items
        foreach (var child in _cardGrid.GetChildren())
            child.QueueFree();

        // Filter cards by strata
        _filteredCards.Clear();
        if (_selectedStrataIdx == 0)
        {
            _filteredCards.AddRange(_allCards);
        }
        else
        {
            int strataIdx = _selectedStrataIdx - 1;
            if (strataIdx >= 0 && strataIdx < StrataValues.Length)
            {
                Strata target = StrataValues[strataIdx];
                _filteredCards = _allCards.Where(c => c.Strata == target).ToList();
            }
        }

        var progression = CampaignContext.Progression;
        int ownedCount = progression.Collection.Count;

        // Update collection count
        int totalCards = CardRegistry.GetAll().Count;
        _collectionCount.Text = $"Owned {ownedCount} / {totalCards}";

        // Build grid tiles
        float cardW = 180f;
        float cardH = 260f;

        foreach (var card in _filteredCards)
        {
            int count = progression.Collection.GetValueOrDefault(card.Id, 0);
            bool isOwned = count > 0;
            bool isNew = isOwned && !progression.IsCardSeen(card.Id);

            var tile = BuildCardTile(card, cardW, cardH, count, isOwned, isNew);
            _cardGrid.AddChild(tile);
        }

        // Spacer to fill grid if not full
        int remaining = _filteredCards.Count % 5;
        if (remaining > 0)
        {
            int spacerCount = 5 - remaining;
            for (int i = 0; i < spacerCount && i < 5; i++)
            {
                var spacer = new Control
                {
                    CustomMinimumSize = new Vector2(cardW, cardH),
                    MouseFilter = MouseFilterEnum.Ignore
                };
                _cardGrid.AddChild(spacer);
            }
        }

        // Mark all shown cards as seen (NEW badge cleared on view)
        foreach (var card in _filteredCards)
        {
            int count = progression.Collection.GetValueOrDefault(card.Id, 0);
            if (count > 0)
                progression.MarkCardSeen(card.Id);
        }
    }

    private Control BuildCardTile(CardDef card, float cardW, float cardH, int ownedCount, bool isOwned, bool isNew)
    {
        // ── Outer container (clickable) ──
        var container = new Panel
        {
            CustomMinimumSize = new Vector2(cardW, cardH),
            Size = new Vector2(cardW, cardH),
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.10f, 0.08f, 1f),
            BorderColor = isOwned ? new Color(0.72f, 0.6f, 0.3f, 0.5f) : new Color(0.25f, 0.22f, 0.18f, 0.5f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0
        };
        container.AddThemeStyleboxOverride("panel", cardStyle);

        // ── Content container ──
        var content = new Control
        {
            Size = new Vector2(cardW, cardH),
            MouseFilter = MouseFilterEnum.Ignore
        };
        container.AddChild(content);

        // ── Art texture ──
        var artRect = new TextureRect
        {
            Size = new Vector2(cardW, cardH),
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore
        };

        if (isOwned)
        {
            // Try loading art
            string artPath = $"res://content/art/{card.Id}.webp";
            if (ResourceLoader.Exists(artPath, nameof(Texture2D)))
            {
                var tex = ResourceLoader.Load<Texture2D>(artPath);
                if (tex != null)
                    artRect.Texture = tex;
            }
            artRect.Modulate = Colors.White;
        }
        else
        {
            // Unowned: dark silhouette
            artRect.Modulate = new Color(0.08f, 0.06f, 0.04f, 1f); // near-black silhouette
        }
        content.AddChild(artRect);

        // ── CardPlate overlay (name band, stat rail, cost rune) ──
        if (isOwned)
        {
            var plate = new CardPlate();
            content.AddChild(plate);
            plate.Setup(card.Name, card.Attack, card.Vigor, card.Strata, cardW, cardH, card.Cost);
        }
        else
        {
            // Unowned: name only at center
            var nameLabel = new Label
            {
                Text = card.Name,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = new Vector2(cardW - 8, cardH),
                Position = new Vector2(4, 0),
                MouseFilter = MouseFilterEnum.Ignore
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 11);
            nameLabel.AddThemeColorOverride("font_color", new Color(0.35f, 0.32f, 0.25f, 0.7f)); // dim
            ApplyHeaderFont(nameLabel, 11);
            content.AddChild(nameLabel);
        }

        // ── Owned count badge (bottom-left) ──
        if (ownedCount > 0)
        {
            var countBadge = new PanelContainer();
            countBadge.Position = new Vector2(4, cardH - 20);
            countBadge.Size = new Vector2(Mathf.Min(50, cardW * 0.3f), 16);
            var countStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.08f, 0.12f, 0.18f, 0.8f),
                BorderColor = new Color(0.3f, 0.5f, 0.7f, 0.6f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                ContentMarginLeft = 3, ContentMarginTop = 0,
                ContentMarginRight = 3, ContentMarginBottom = 0
            };
            countBadge.AddThemeStyleboxOverride("panel", countStyle);
            var countLabel = new Label
            {
                Text = $"x{ownedCount}",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            countLabel.AddThemeFontSizeOverride("font_size", 9);
            countLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 1f, 0.9f));
            countLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            countBadge.AddChild(countLabel);
            content.AddChild(countBadge);
        }

        // ── NEW badge (top-left, only for newly seen cards) ──
        if (isNew)
        {
            var newBadge = new PanelContainer();
            newBadge.Position = new Vector2(4, 4);
            newBadge.Size = new Vector2(Mathf.Min(36, cardW * 0.25f), 16);
            var newStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.8f, 0.6f, 0.05f, 0.9f),
                BorderColor = new Color(1f, 0.85f, 0.2f, 0.8f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                ContentMarginLeft = 3, ContentMarginTop = 0,
                ContentMarginRight = 3, ContentMarginBottom = 0
            };
            newBadge.AddThemeStyleboxOverride("panel", newStyle);
            var newLabel = new Label
            {
                Text = "NEW",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            newLabel.AddThemeFontSizeOverride("font_size", 8);
            newLabel.AddThemeColorOverride("font_color", Colors.Black);
            newLabel.SetAnchorsPreset(LayoutPreset.FullRect);
            newBadge.AddChild(newLabel);
            content.AddChild(newBadge);
        }

        // ── Click to inspect ──
        var clickArea = new Button();
        clickArea.SetAnchorsPreset(LayoutPreset.FullRect);
        clickArea.MouseDefaultCursorShape = CursorShape.PointingHand;
        var transparent = new StyleBoxFlat { BgColor = Colors.Transparent };
        clickArea.AddThemeStyleboxOverride("normal", transparent);
        clickArea.AddThemeStyleboxOverride("hover", transparent);
        clickArea.AddThemeStyleboxOverride("pressed", transparent);
        clickArea.AddThemeStyleboxOverride("disabled", transparent);
        container.AddChild(clickArea);

        string capturedCardId = card.Id;
        clickArea.Pressed += () => ShowCardInspect(capturedCardId);

        return container;
    }

    private void ShowCardInspect(string cardId)
    {
        var card = CardRegistry.Get(cardId);
        if (card == null) return;

        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        // Remove existing inspect overlay if any
        if (_inspectOverlay != null && IsInstanceValid(_inspectOverlay))
        {
            _inspectOverlay.QueueFree();
            _inspectOverlay = null;
        }

        // ── Full-screen dim + inspect panel ──
        _inspectOverlay = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.65f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Stop
        };
        AddChild(_inspectOverlay);
        MoveChild(_inspectOverlay, GetChildCount() - 1);

        // ── Card detail panel — 400px+ inspection ──
        var panel = new PanelContainer
        {
            AnchorLeft = 0.30f, AnchorRight = 0.70f,
            AnchorTop = 0.10f, AnchorBottom = 0.95f,
            MouseFilter = MouseFilterEnum.Stop
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.08f, 0.06f, 0.97f),
            BorderColor = new Color(0.72f, 0.6f, 0.3f, 0.65f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 16, ContentMarginTop = 16,
            ContentMarginRight = 16, ContentMarginBottom = 16,
            ShadowColor = new Color(0f, 0f, 0f, 0.5f),
            ShadowSize = 16
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        _inspectOverlay.AddChild(panel);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.Fill,
            SizeFlagsVertical = SizeFlags.Fill
        };
        panel.AddChild(vbox);

        // Large art area (400px+ size card)
        var largeArt = new TextureRect
        {
            CustomMinimumSize = new Vector2(0, 320),
            SizeFlagsHorizontal = SizeFlags.Fill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore
        };
        string artPath = $"res://content/art/{card.Id}.webp";
        if (ResourceLoader.Exists(artPath, nameof(Texture2D)))
        {
            var tex = ResourceLoader.Load<Texture2D>(artPath);
            if (tex != null)
                largeArt.Texture = tex;
        }
        vbox.AddChild(largeArt);

        // Name
        var nameLabel = new Label
        {
            Text = card.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.Fill,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        ApplyHeaderFont(nameLabel, 22);
        nameLabel.AddThemeColorOverride("font_color", Color.FromHtml("#F0E8D0"));
        vbox.AddChild(nameLabel);

        // Type line: "Creature · Verdant · Common"
        var typeLabel = new Label
        {
            Text = $"{FormatCardType(card.Type)} · {card.Strata} · {card.Rarity}",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.Fill,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        typeLabel.AddThemeFontSizeOverride("font_size", 12);
        typeLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.66f, 0.52f, 0.9f));
        vbox.AddChild(typeLabel);

        // Stats row (attack/vigor) — only for creatures
        if (card.Type is CardType.CREATURE or CardType.TOKEN)
        {
            var statsHbox = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.Fill,
                Alignment = BoxContainer.AlignmentMode.Center,
            };
            statsHbox.AddThemeConstantOverride("separation", 20);
            vbox.AddChild(statsHbox);

            if (card.Attack.HasValue)
            {
                var atkLabel = new Label
                {
                    Text = $"ATK {card.Attack.Value}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
                };
                atkLabel.AddThemeFontSizeOverride("font_size", 16);
                atkLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.2f));
                statsHbox.AddChild(atkLabel);
            }

            if (card.Vigor.HasValue)
            {
                var vigLabel = new Label
                {
                    Text = $"VIG {card.Vigor.Value}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
                };
                vigLabel.AddThemeFontSizeOverride("font_size", 16);
                vigLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 0.3f));
                statsHbox.AddChild(vigLabel);
            }
        }

        // Cost
        var costLabel = new Label
        {
            Text = $"Cost: {card.Cost}",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.Fill,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        costLabel.AddThemeFontSizeOverride("font_size", 13);
        costLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.7f, 0.5f));
        vbox.AddChild(costLabel);

        // Owned count
        var progression = CampaignContext.Progression;
        int count = progression.Collection.GetValueOrDefault(card.Id, 0);
        if (count > 0)
        {
            var ownedLabel = new Label
            {
                Text = $"Owned: {count}",
                HorizontalAlignment = HorizontalAlignment.Center,
                SizeFlagsHorizontal = SizeFlags.Fill,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
            };
            ownedLabel.AddThemeFontSizeOverride("font_size", 13);
            ownedLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.7f, 1f));
            vbox.AddChild(ownedLabel);
        }

        // Spacer
        vbox.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        // Close button
        var closeBtn = new Button
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(120, 36),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
            SizeFlagsVertical = SizeFlags.ShrinkEnd
        };
        closeBtn.AddThemeFontSizeOverride("font_size", 13);
        closeBtn.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
        closeBtn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        var closeNormal = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#3A3530"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 4,
            ContentMarginRight = 16, ContentMarginBottom = 4
        };
        var closeHover = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#4A4540"),
            BorderColor = Color.FromHtml("#C9A84C"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginTop = 4,
            ContentMarginRight = 16, ContentMarginBottom = 4
        };
        closeBtn.AddThemeStyleboxOverride("normal", closeNormal);
        closeBtn.AddThemeStyleboxOverride("hover", closeHover);
        closeBtn.Pressed += () => DismissInspect();
        vbox.AddChild(closeBtn);
    }

    private void DismissInspect()
    {
        if (_inspectOverlay != null && IsInstanceValid(_inspectOverlay))
        {
            _inspectOverlay.QueueFree();
            _inspectOverlay = null;
        }
    }

    private void NavigateBack()
    {
        // Navigate back based on where we came from
        // The calling scene determines the return point — default is title screen
        GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
    }

    private static string FormatCardType(CardType type) => type switch
    {
        CardType.CREATURE => "Creature",
        CardType.RITUAL => "Ritual",
        CardType.RELIC => "Relic",
        CardType.CURSE => "Curse",
        CardType.TOKEN => "Token",
        _ => "?"
    };

    // ════════════════════════════════════════════════════════════
    // CAPTURE HOOK: auto-navigate to Reliquary for capture
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Wait for one frame, then capture and quit.
    /// Called from Main.cs when CaptureReliquaryScreenshot is set.
    /// </summary>
    public void OnCaptureReady()
    {
        var capTimer = new Godot.Timer();
        capTimer.OneShot = true;
        capTimer.WaitTime = 1.5f;
        capTimer.Timeout += () =>
        {
            var suffix = CampaignContext.WideCaptureMode ? "_wide" : "";
            var img = GetViewport().GetTexture().GetImage();
            if (img != null)
            {
                string path = $"/home/fictive/runewake/artifacts/captures/reliquary_test{suffix}.png";
                img.SavePng(path);
                GD.Print($"[ReliquaryScene] Saved {path}");

                // Write meta.json
                var meta = new System.Text.StringBuilder();
                meta.Append("{\n");
                meta.Append($"  \"capture_type\": \"reliquary_test{suffix}\",\n");
                meta.Append($"  \"view_width\": {(int)GetViewportRect().Size.X},\n");
                meta.Append($"  \"view_height\": {(int)GetViewportRect().Size.Y},\n");
                meta.Append($"  \"strata_filter_idx\": {_selectedStrataIdx},\n");
                meta.Append($"  \"grid_cards_shown\": {_filteredCards.Count},\n");
                meta.Append($"  \"grid_columns\": 5\n");
                meta.Append("}\n");

                string metaPath = $"/home/fictive/runewake/artifacts/captures/reliquary_test{suffix}.meta.json";
                using (var writer = new System.IO.StreamWriter(metaPath))
                    writer.Write(meta.ToString());
                GD.Print($"[ReliquaryScene] Saved {metaPath}");
            }
            GetTree().Quit();
        };
        AddChild(capTimer);
        capTimer.Start();
    }
}