using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Campaign map screen — renders a node graph from a MapRegion JSON file.
/// Dark fantasy themed to match the title screen.
/// Touch targets are handled at container level so scaling doesn't break them.
/// </summary>
public partial class MapScene : Control
{
    // Map container (the pannable/zoomable surface)
    private Node2D _mapContainer;

    // Node info panel
    private Panel _infoPanel;
    private Label _infoName;
    private Label _infoType;
    private Label _infoRewards;
    private Button _infoGoButton;
    private Button _infoCloseButton;

    // Top bar
    private Button _backButton;
    private Label _shardLabel;

    // Deck chip
    private Control _deckChip = default!;
    private Label _deckChipLabel = default!;

    // Side buttons
    private Button _settingsBtn;
    private Button _runePageBtn;
    private Button _forgeBtn;
    private Button _reliquaryBtn;

    // Region name banner
    private Label _regionBanner;

    // Line drawing
    private LineDrawer _lineDrawer;

    // State
    private MapRegion? _region;
    private readonly Dictionary<string, MapNodeIcon> _nodeIcons = new();
    private readonly TapGuard _tap = new();
    private string? _selectedNodeId;
    private Vector2 _dragStart;
    private Vector2 _containerStartPos;
    private bool _isDragging;

    // Zoom
    private float _zoom = 1.0f;
    private float _minZoom = 0.6f;   // set dynamically: fit-to-viewport
    private const float MaxZoom = 3.0f;
    private const float ZoomStep = 0.1f;

    // Painted map plate (lives INSIDE the pannable container so nodes stay
    // glued to the terrain at any pan/zoom). Node positions in region JSON
    // are authored in plate pixel coordinates (1536×704).
    private Sprite2D _platePlate;
    private Vector2 _plateSize = new(1536, 704);

    // Touch pan state
    private int _touchDragId = -1;
    private Vector2 _touchDragStart;
    private bool _touchDragging;

    // Map center offset
    private Vector2 _mapOffset;

    public override void _Ready()
    {
        EnsureCampaignContext();
        BuildBackground();
        BuildTopBar();
        BuildSideButtons();
        BuildMap();
        BuildInfoPanel();
        UpdateAllLockStates();

        // ═══ MAP CAPTURE HOOK (--capture-map): select first unlocked node, capture, quit ═══
        if (CampaignContext.CaptureMapScreenshot)
        {
            // Provision a starter deck so the deck chip reads "Deck: Forgeguard Standard"
            CampaignContext.AddOrUpdateProfile("warrior", "The Fallow Reach");
            CampaignContext.EnsureStarterDeck("warrior");
            UpdateDeckChipText(); // refresh now that we have a starter deck

            // Seeded partial clear: pre-mark two early nodes as cleared so the capture
            // shows locked / available / cleared states simultaneously.
            string[] preClearedIds = { "r1_n01", "r1_n02" };
            foreach (var cid in preClearedIds)
                CampaignContext.Progression.MarkNodeCleared(cid);
            UpdateAllLockStates();

            var capTimer = new Godot.Timer();
            capTimer.OneShot = true;
            capTimer.WaitTime = 1.2f; // let map + icons render
            capTimer.Timeout += () =>
            {
                // Select the first unlocked, non-cleared node with an encounter
                string? targetId = null;
                if (_region != null)
                {
                    foreach (var mapNode in _region.Nodes)
                    {
                        if (CampaignContext.Progression.IsNodeCleared(mapNode.Id)) continue;
                        if (!IsNodeUnlocked(mapNode)) continue;
                        targetId = mapNode.Id;
                        break;
                    }
                    // Fallback: first node at all (so the capture shows a selection)
                    targetId ??= _region.Nodes.Count > 0 ? _region.Nodes[0].Id : null;
                }

                if (targetId != null)
                    OnNodeSelected(targetId);

                var snapTimer = new Godot.Timer();
                snapTimer.OneShot = true;
                snapTimer.WaitTime = 0.8f; // let info panel + selection ring render
                snapTimer.Timeout += () =>
                {
                    var suffix = CampaignContext.WideCaptureMode ? "_wide" : "";
                    if (CampaignContext.CaptureMapR2Screenshot)
                        suffix = "_r2" + suffix;
                    var img = GetViewport().GetTexture().GetImage();
                    if (img != null)
                        img.SavePng($"{ProjectPaths.Artifacts}/captures/map_test{suffix}.png");
                    DebugCapture.WriteLayoutJson(this, $"map_test{suffix}");
                    GD.Print($"[MAPCAPTURE] map_test{suffix}.png saved");

                    // TASK-UI-LINT-1: Dump layout JSON
                    DebugCapture.DumpLayoutJSON($"map_test{suffix}", this);
                    GetTree().Quit(0);
                };
                AddChild(snapTimer);
                snapTimer.Start();
            };
            AddChild(capTimer);
            capTimer.Start();
        }
        // ═══ END MAP CAPTURE HOOK ═══

        // ═══ FLOW TEST MAP CAPTURE (post-victory/defeat round-trip proof) ═══
        if (CampaignContext.CaptureFlowTestMap)
        {
            CampaignContext.CaptureFlowTestMap = false; // one-shot
            bool wasVictory = CampaignContext.CaptureVictoryOverlay;
            bool wasDefeat = CampaignContext.CaptureDefeatOverlay;
            string prefix = wasVictory ? "victory" : "defeat";
            var suffix = CampaignContext.WideCaptureMode ? "_wide" : "";

            GD.Print($"[FLOWTEST] Map reached after {prefix} overlay — capturing to prove round-trip");

            var flowTimer = new Godot.Timer();
            flowTimer.OneShot = true;
            flowTimer.WaitTime = 1.2f;
            flowTimer.Timeout += () =>
            {
                var img = GetViewport().GetTexture().GetImage();
                if (img != null)
                    img.SavePng($"{ProjectPaths.Artifacts}/captures/flow_{prefix}_map{suffix}.png");
                DebugCapture.WriteLayoutJson(this, $"flow_{prefix}_map{suffix}");
                GD.Print($"[FLOWTEST] flow_{prefix}_map{suffix}.png saved — round-trip complete");

                // TASK-UI-LINT-1: Dump layout JSON
                DebugCapture.DumpLayoutJSON($"flow_{prefix}_map{suffix}", this);
                GetTree().Quit(0);
            };
            AddChild(flowTimer);
            flowTimer.Start();
        }
        // ═══ END FLOW TEST MAP CAPTURE ═══

        // ═══ SOAK LOOP MAP AUTO-PLAY ═══
        if (CampaignContext.SoakActive)
        {
            GD.Print("[MAPSOAK] Soak active — will auto-select and challenge next unlocked node");
            var soakCapTimer = new Godot.Timer();
            soakCapTimer.OneShot = true;
            soakCapTimer.WaitTime = 1.2f;
            soakCapTimer.Timeout += () =>
            {
                // Find first unlocked, non-cleared encounter or dig node
                string? targetId = null;
                bool isDigTarget = false;
                if (_region != null)
                {
                    foreach (var mapNode in _region.Nodes)
                    {
                        if (CampaignContext.Progression.IsNodeCleared(mapNode.Id)) continue;
                        if (!IsNodeUnlocked(mapNode)) continue;
                        // Skip shrine/merchant nodes that don't have encounters
                        if (mapNode.Type == MapNodeType.Shrine || mapNode.Type == MapNodeType.Merchant)
                        {
                            // Mark these auto-cleared and skip
                            CampaignContext.Progression.MarkNodeCleared(mapNode.Id);
                            GD.Print($"[MAPSOAK] Auto-cleared non-encounter node: {mapNode.Id}");
                            continue;
                        }
                        targetId = mapNode.Id;
                        isDigTarget = mapNode.Type == MapNodeType.Dig;
                        break;
                    }
                }

                if (targetId == null)
                {
                    // All done — capture final map and quit
                    CampaignContext.SoakScreenLog.Add("map_region_cleared");
                    GD.Print("[MAPSOAK] All nodes cleared — capturing final map and quitting");
                    var img = GetViewport().GetTexture().GetImage();
                    if (img != null)
                        img.SavePng($"{ProjectPaths.Artifacts}/captures/soak_final_map_{CampaignContext.SoakSeedStr}.png");
                    GD.Print($"[MAPSOAK] soak_final_map saved for seed {CampaignContext.SoakSeedStr}");

                    // TASK-UI-LINT-1: Dump layout JSON for soak final map
                    DebugCapture.DumpLayoutJSON($"soak_final_map_{CampaignContext.SoakSeedStr}", this);
                    return;
                }

                // save_quit phase: quit after clearing SoakMaxNodes nodes
                if (CampaignContext.SoakMaxNodes > 0)
                {
                    int clearedCount = 0;
                    if (_region != null)
                    {
                        foreach (var n in _region.Nodes)
                        {
                            if (CampaignContext.Progression.IsNodeCleared(n.Id))
                                clearedCount++;
                        }
                    }
                    if (clearedCount >= CampaignContext.SoakMaxNodes)
                    {
                        GD.Print($"[MAPSOAK] Save/quit phase: {clearedCount} nodes cleared");
                        CampaignContext.SaveManager.Save();
                        if (CampaignContext.LoopSmokeTest)
                        {
                            GD.Print("[MAPSOAK] LoopSmokeTest active — stopping auto-nav (no quit) so LoopSmokeTest can continue to Reliquary/Forge");
                            return;
                        }
                        GetTree().Quit(0);
                        return;
                    }
                }

                CampaignContext.SoakScreenLog.Add($"map_select_{targetId}");
                GD.Print($"[MAPSOAK] Auto-selecting node: {targetId} (dig={isDigTarget})");

                // Clear defeat-retry flag for new node selection
                CampaignContext.SoakDefeatHasRetried = false;

                // Select the node
                OnNodeSelected(targetId);

                // For dig sites in soak mode: mark cleared and re-run the loop
                if (isDigTarget)
                {
                    var digTimer = new Godot.Timer();
                    digTimer.OneShot = true;
                    digTimer.WaitTime = 0.8f;
                    digTimer.Timeout += () =>
                    {
                        CampaignContext.Progression.MarkNodeCleared(targetId);
                        CampaignContext.Progression.Shards += 20; // dig site shard reward
                        CampaignContext.SaveManager.Save();
                        GD.Print($"[MAPSOAK] Dig site {targetId} resolved — marking cleared, returning to map");
                        // Reload map to re-evaluate unlock state
                        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                    };
                    AddChild(digTimer);
                    digTimer.Start();
                    return;
                }

                // For encounter nodes: wait for info panel, then press Challenge
                var goTimer = new Godot.Timer();
                goTimer.OneShot = true;
                goTimer.WaitTime = 0.8f;
                goTimer.Timeout += () =>
                {
                    // Skip tutorial encounters in soak mode — they block bot play
                    var node = _region.Nodes.FirstOrDefault(n => n.Id == targetId);
                    if (node != null && node.Encounter != null &&
                        CampaignContext.EncounterIndex.TryGetValue(node.Encounter, out var enc) &&
                        enc.IsTutorial && CampaignContext.SoakActive)
                    {
                        GD.Print($"[MAPSOAK] Skipping tutorial encounter {targetId} — marking cleared");
                        CampaignContext.Progression.MarkNodeCleared(targetId);
                        CampaignContext.SaveManager.Save();
                        // Reload map to re-evaluate unlock state
                        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
                        return;
                    }
                    GD.Print($"[MAPSOAK] Auto-pressing Challenge for node: {targetId}");
                    OnGoButtonPressed();
                };
                AddChild(goTimer);
                goTimer.Start();
            };
            AddChild(soakCapTimer);
            soakCapTimer.Start();
        }
        // ═══ END SOAK LOOP MAP AUTO-PLAY ═══
    }

    /// <summary>
    /// Ensure campaign data (encounters, save manager) is loaded.
    /// </summary>
    private void EnsureCampaignContext()
    {
        if (!CampaignContext.SaveManager.IsLoaded)
            CampaignContext.SaveManager.Initialize();

        if (CampaignContext.EncounterIndex.Count == 0)
        {
            CampaignContext.LoadEncounters();
            CampaignContext.LoadDigSites();
        }

        // Whatever route led here (new game, continue, deep link, capture),
        // the active class always has a playable deck before the map shows.
        var profile = CampaignContext.ActiveProfile;
        if (profile != null && !string.IsNullOrEmpty(profile.ClassId))
            CampaignContext.EnsureStarterDeck(profile.ClassId);
    }

    // ── Shared button styles ─────────────────────────────────────────────

    private StyleBoxFlat MakeBtnNormal() => new()
    {
        BgColor = new Color(0.2f, 0.15f, 0.1f, 1f),
        BorderColor = new Color(0.7f, 0.6f, 0.3f, 1f),
        BorderWidthLeft = 1, BorderWidthTop = 1,
        BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 10, ContentMarginTop = 4,
        ContentMarginRight = 10, ContentMarginBottom = 4
    };

    private StyleBoxFlat MakeBtnHover() => new()
    {
        BgColor = new Color(0.3f, 0.22f, 0.14f, 1f),
        BorderColor = new Color(0.9f, 0.78f, 0.45f, 1f),
        BorderWidthLeft = 1, BorderWidthTop = 1,
        BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 10, ContentMarginTop = 4,
        ContentMarginRight = 10, ContentMarginBottom = 4
    };

    private void StyleButton(Button btn, float fontSize = 12, bool goldText = true)
    {
        btn.AddThemeFontSizeOverride("font_size", (int)fontSize);
        var fc = goldText ? new Color(0.95f, 0.88f, 0.65f, 1f) : new Color(0.8f, 0.75f, 0.6f, 1f);
        var fd = new Color(0.4f, 0.35f, 0.25f, 0.5f);
        btn.AddThemeColorOverride("font_color", fc);
        btn.AddThemeColorOverride("font_disabled_color", fd);
        btn.AddThemeStyleboxOverride("normal", MakeBtnNormal());
        btn.AddThemeStyleboxOverride("hover", MakeBtnHover());
        btn.AddThemeStyleboxOverride("pressed", MakeBtnHover());
    }

    // ── Background ───────────────────────────────────────────────────────

    private void BuildBackground()
    {
        // Dark table-top backdrop behind the plate (visible only if the player
        // zooms out past the plate edges)
        var backdrop = new ColorRect
        {
            Color = new Color(0.07f, 0.055f, 0.04f, 1f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(backdrop);

        // Map container (pannable layer with the painted plate and nodes)
        _mapContainer = new Node2D();
        AddChild(_mapContainer);

        // The painted map plate rides INSIDE the container so node icons stay
        // glued to the terrain under every pan/zoom. Container origin = plate center.
        // The region title is baked into the plate's cartouche art.
        _platePlate = new Sprite2D { Centered = true, Position = Vector2.Zero };
        if (ResourceLoader.Exists("res://content/art/map/map_plate.png"))
        {
            var tex = ResourceLoader.Load<Texture2D>("res://content/art/map/map_plate.png");
            if (tex != null)
            {
                _platePlate.Texture = tex;
                _plateSize = tex.GetSize();
            }
            else
                GD.PrintErr("[ART-MISSING] map_plate.png: ResourceLoader.Load returned null");
        }
        else
        {
            GD.PrintErr("[ART-MISSING] map_plate.png: resource does not exist at res://content/art/map/map_plate.png");
        }
        _mapContainer.AddChild(_platePlate);

        // Tint applied in BuildMap() after the region skin is known

        // Line drawer (edges between nodes) — sits directly on map art
        _lineDrawer = new LineDrawer();
        _mapContainer.AddChild(_lineDrawer);

        // Region name banner — always visible overlay, outside the pannable container
        // The region name is painted into the lower half of the cartouche, under
        // the script line that belongs to the art. This is what
        // that ornate empty scroll was always drawn for. It lives inside the
        // pannable container so it stays welded to the banner under pan and
        // zoom, exactly like the map nodes.
        _regionBanner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Off,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(332, 34),
            Position = new Vector2(510 - 166, -189 - 17)
        };
        _regionBanner.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        ThemeTokens.ApplyHeaderFont(_regionBanner, 22);
        // Ink on parchment, not gold on black — it is sitting on the scroll now.
        _regionBanner.AddThemeColorOverride("font_color", new Color(0.24f, 0.16f, 0.09f, 0.96f));
        _regionBanner.AddThemeConstantOverride("outline_size", 0);
        if (_mapContainer != null)
            _mapContainer.AddChild(_regionBanner);
        else
            AddChild(_regionBanner);
    }

    // ── Top bar ──────────────────────────────────────────────────────────

    private void BuildTopBar()
    {
        // Top bar background
        var topBar = new ColorRect
        {
            Color = new Color(0.1f, 0.08f, 0.06f, 0.85f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.055f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(topBar);

        // Bottom edge line for top bar
        var barLine = new ColorRect
        {
            Color = new Color(0.6f, 0.5f, 0.25f, 0.25f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.055f, AnchorBottom = 0.057f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(barLine);

        // Back button
        _backButton = new Button
        {
            Text = "\u2190  Main Menu",
            AnchorLeft = 0.01f, AnchorRight = 0.12f,
            AnchorTop = 0.002f, AnchorBottom = 0.053f
        };
        StyleButton(_backButton, 11, goldText: true);
        _backButton.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        };
        AddChild(_backButton);

        // Shard display (top-right)
        _shardLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.7f, AnchorRight = 0.98f,
            AnchorTop = 0.002f, AnchorBottom = 0.053f
        };
        _shardLabel.AddThemeFontSizeOverride("font_size", 14);
        _shardLabel.Modulate = new Color(0.85f, 0.72f, 0.35f, 0.8f); // gold
        AddChild(_shardLabel);

        // Deck chip (top bar, right side — shows active deck name)
        _deckChip = new HBoxContainer
        {
            AnchorLeft = 0.85f, AnchorRight = 0.97f,
            AnchorTop = 0.008f, AnchorBottom = 0.047f
        };

        var chipPanel = new PanelContainer();
        var chipStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.16f, 0.12f, 0.9f),
            BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.5f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 6, ContentMarginTop = 2,
            ContentMarginRight = 6, ContentMarginBottom = 2
        };
        chipPanel.AddThemeStyleboxOverride("panel", chipStyle);

        var chipInner = new HBoxContainer();
        chipInner.AddThemeConstantOverride("separation", 4);

        // Colored class dot
        var dot = new ColorRect
        {
            CustomMinimumSize = new Vector2(8, 8),
            Color = GetClassColor(),
            MouseFilter = MouseFilterEnum.Ignore
        };
        chipInner.AddChild(dot);

        // Deck name label (Cinzel 10px)
        _deckChipLabel = new Label();
        _deckChipLabel.AddThemeFontSizeOverride("font_size", 10);
        ThemeTokens.ApplyHeaderFont(_deckChipLabel, 10);
        _deckChipLabel.MouseFilter = MouseFilterEnum.Ignore;
        chipInner.AddChild(_deckChipLabel);

        chipPanel.AddChild(chipInner);
        _deckChip.AddChild(chipPanel);

        // Tap overlay (transparent button covering the chip)
        var tapBtn = new Button
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f
        };
        var emptyStyle = new StyleBoxEmpty();
        tapBtn.AddThemeStyleboxOverride("normal", emptyStyle);
        tapBtn.AddThemeStyleboxOverride("hover", emptyStyle);
        tapBtn.AddThemeStyleboxOverride("pressed", emptyStyle);
        tapBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            ShowDeckPopup();
        };
        _deckChip.AddChild(tapBtn);

        AddChild(_deckChip);

        // Set initial chip text
        UpdateDeckChipText();
    }

    // ── Side buttons ─────────────────────────────────────────────────────

    private void BuildSideButtons()
    {
        float btnW = 0.13f;
        float xL = 0.01f;

        _forgeBtn = new Button
        {
            Text = "Forge",
            AnchorLeft = xL, AnchorRight = xL + btnW,
            AnchorTop = 0.79f, AnchorBottom = 0.86f
        };
        StyleButton(_forgeBtn, 11, goldText: false);
        _forgeBtn.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/forge/ForgeScene.tscn");
        };
        AddChild(_forgeBtn);

        _runePageBtn = new Button
        {
            Text = "Rune Page",
            AnchorLeft = xL, AnchorRight = xL + btnW,
            AnchorTop = 0.84f, AnchorBottom = 0.89f
        };
        StyleButton(_runePageBtn, 11, goldText: false);
        _runePageBtn.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/runepage/RunePageScene.tscn");
        };
        AddChild(_runePageBtn);

        _reliquaryBtn = new Button
        {
            Text = "Reliquary",
            AnchorLeft = xL, AnchorRight = xL + btnW,
            AnchorTop = 0.89f, AnchorBottom = 0.94f
        };
        StyleButton(_reliquaryBtn, 11, goldText: false);
        _reliquaryBtn.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/reliquary/ReliquaryScene.tscn");
        };
        AddChild(_reliquaryBtn);

        _settingsBtn = new Button
        {
            Text = "Settings",
            AnchorLeft = xL, AnchorRight = xL + btnW,
            AnchorTop = 0.94f, AnchorBottom = 1f
        };
        StyleButton(_settingsBtn, 11, goldText: false);
        _settingsBtn.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/settings/SettingsScene.tscn");
        };
        AddChild(_settingsBtn);
    }

    // ── Map graph ────────────────────────────────────────────────────────

    private void BuildMap()
    {
        string regionId = CampaignContext.GetRegionIdForMap();
        string json = Godot.FileAccess.GetFileAsString($"res://content/map/{regionId}.json");
        _region = MapLoader.LoadRegionFromString(json);
        if (_region == null) return;

        // Store the region's board skin for DuelScene to use
        CampaignContext.CurrentRegionSkinId = _region.BoardSkin ?? "default";
        GD.Print($"[MAP] Loaded region '{_region.Id}' with board_skin '{CampaignContext.CurrentRegionSkinId}'");

        // Apply tint to the plate now that we know the skin
        _platePlate.Modulate = ThemeTokens.GetSkinTint(CampaignContext.CurrentRegionSkinId);
        GD.Print($"[MAP] Applying plate tint for skin '{CampaignContext.CurrentRegionSkinId}'");

        var iconScene = GD.Load<PackedScene>("res://scenes/components/MapNodeIcon.tscn");

        // Node positions are authored in plate pixel coordinates (1536×704).
        // The container origin is the plate CENTER, so shift by half the plate.
        float centerX = _plateSize.X / 2f;
        float centerY = _plateSize.Y / 2f;

        // Create node icons — the JSON position is the medallion CENTER point
        // on the terrain; the icon's medallion center sits at local (40, 32).
        foreach (var mapNode in _region.Nodes)
        {
            var icon = iconScene.Instantiate<MapNodeIcon>();

            string displayName;
            if (mapNode.Encounter != null && CampaignContext.EncounterIndex.TryGetValue(mapNode.Encounter, out var enc))
                displayName = enc.Name;
            else
                displayName = mapNode.Type.ToString();

            float x = mapNode.Position[0] - centerX;
            float y = mapNode.Position[1] - centerY;
            icon.Position = new Vector2(x - 40f, y - 32f);

            icon.NodeSelected += OnNodeSelected;
            icon.Pressed += () => OnNodeSelected(mapNode.Id);
            _mapContainer.AddChild(icon);

            icon.Setup(mapNode.Id, displayName, mapNode.Type.ToString(), locked: true);
            _nodeIcons[mapNode.Id] = icon;
        }

        _lineDrawer.SetNodes(_region.Nodes, centerX, centerY);

        // Set region name in banner
        if (_region != null && _regionBanner != null)
            _regionBanner.Text = _region.Name.ToUpper();

        // Auto-frame: fill the screen with the plate (cover), like the old
        // full-bleed background — but now the nodes are welded to the art.
        Vector2 viewport = GetViewportRect().Size;
        float coverZoom = Mathf.Max(viewport.X / _plateSize.X, viewport.Y / _plateSize.Y);
        float fitZoom = Mathf.Min(viewport.X / _plateSize.X, viewport.Y / _plateSize.Y);
        _minZoom = fitZoom;                       // zoom out far enough to see the whole plate
        _zoom = Mathf.Clamp(coverZoom, _minZoom, MaxZoom);

        _mapOffset = viewport / 2f;
        _mapContainer.Position = _mapOffset;
        _mapContainer.Scale = new Vector2(_zoom, _zoom);
        ClampPan();
    }

    /// <summary>
    /// Keep the plate covering the viewport: no dead space past an edge while
    /// the plate is larger than the screen; center the axis when it is smaller.
    /// </summary>
    private void ClampPan()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 half = _plateSize * _zoom / 2f;
        Vector2 pos = _mapContainer.Position;

        if (half.X * 2f >= viewport.X)
            pos.X = Mathf.Clamp(pos.X, viewport.X - half.X, half.X);
        else
            pos.X = viewport.X / 2f;

        if (half.Y * 2f >= viewport.Y)
            pos.Y = Mathf.Clamp(pos.Y, viewport.Y - half.Y, half.Y);
        else
            pos.Y = viewport.Y / 2f;

        _mapContainer.Position = pos;
    }

    // ── Info panel ───────────────────────────────────────────────────────

    private void BuildInfoPanel()
    {
        // Bottom-RIGHT so it never overlaps the Forge/Rune Page/Settings
        // stack in the bottom-left. Sized to its content, not the map.
        _infoPanel = new Panel();
        _infoPanel.AnchorLeft = 0.665f;
        _infoPanel.AnchorRight = 0.985f;
        _infoPanel.AnchorTop = 0.665f;
        _infoPanel.AnchorBottom = 0.955f;

        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.085f, 0.07f, 0.05f, 0.96f),
            BorderColor = new Color(0.72f, 0.6f, 0.3f, 0.65f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            ContentMarginLeft = 16, ContentMarginTop = 12,
            ContentMarginRight = 16, ContentMarginBottom = 12,
            ShadowColor = new Color(0f, 0f, 0f, 0.5f),
            ShadowSize = 10
        };
        _infoPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_infoPanel);

        var infoVbox = new VBoxContainer();
        infoVbox.AnchorLeft = 0f; infoVbox.AnchorRight = 1f;
        infoVbox.AnchorTop = 0f; infoVbox.AnchorBottom = 1f;
        infoVbox.OffsetLeft = 16; infoVbox.OffsetRight = -16;
        infoVbox.OffsetTop = 12; infoVbox.OffsetBottom = -12;
        infoVbox.AddThemeConstantOverride("separation", 2);
        _infoPanel.AddChild(infoVbox);

        // Name — Cinzel gold
        _infoName = new Label();
        ThemeTokens.ApplyHeaderFont(_infoName, 19);
        _infoName.AddThemeColorOverride("font_color", new Color(0.9f, 0.82f, 0.55f, 1f));
        infoVbox.AddChild(_infoName);

        // Encounter type — small warm gray
        _infoType = new Label();
        _infoType.AddThemeFontSizeOverride("font_size", 12);
        _infoType.AddThemeColorOverride("font_color", new Color(0.72f, 0.66f, 0.52f, 0.9f));
        infoVbox.AddChild(_infoType);

        infoVbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // Divider
        var divider = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 1),
            Color = new Color(0.72f, 0.6f, 0.3f, 0.35f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        infoVbox.AddChild(divider);

        infoVbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        // Rewards — pretty text, not raw tokens
        _infoRewards = new Label();
        _infoRewards.AddThemeFontSizeOverride("font_size", 12);
        _infoRewards.AddThemeColorOverride("font_color", new Color(0.65f, 0.72f, 0.5f, 0.95f));
        _infoRewards.AutowrapMode = TextServer.AutowrapMode.Word;
        infoVbox.AddChild(_infoRewards);

        // Spacer pushes buttons to the panel bottom
        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        infoVbox.AddChild(spacer);

        var buttonRow = new HBoxContainer();
        buttonRow.Alignment = BoxContainer.AlignmentMode.End;
        buttonRow.AddThemeConstantOverride("separation", 10);
        infoVbox.AddChild(buttonRow);

        _infoCloseButton = new Button { Text = "Close", CustomMinimumSize = new Vector2(88, 44) };
        StyleButton(_infoCloseButton, 12, goldText: false);
        _infoCloseButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            HideInfoPanel();
        };
        buttonRow.AddChild(_infoCloseButton);

        _infoGoButton = new Button { Text = "Challenge", CustomMinimumSize = new Vector2(126, 44) };
        StyleButton(_infoGoButton, 14);
        _infoGoButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            OnGoButtonPressed();
        };
        buttonRow.AddChild(_infoGoButton);

        _infoPanel.Hide();
    }

    private void HideInfoPanel()
    {
        _infoPanel.Hide();
        if (_selectedNodeId != null && _nodeIcons.TryGetValue(_selectedNodeId, out var icon))
            icon.SetSelected(false);
        _selectedNodeId = null;
    }

    /// <summary>
    /// Turn a raw reward token ("shard:30", "fragment:verdant:2", "dig_charge:1")
    /// into player-readable text.
    /// </summary>
    private static string PrettifyReward(string raw)
    {
        var parts = raw.Split(':');
        switch (parts[0])
        {
            case "shard":
                return parts.Length > 1 ? $"{parts[1]} Shards" : "Shards";
            case "dig_charge":
            {
                string n = parts.Length > 1 ? parts[1] : "1";
                return n == "1" ? "1 Dig Charge" : $"{n} Dig Charges";
            }
            case "card":
                return "A Signature Card";
            case "fragment":
            {
                if (parts.Length > 2)
                {
                    string strata = parts[1].Length > 0
                        ? char.ToUpperInvariant(parts[1][0]) + parts[1][1..]
                        : parts[1];
                    return $"{parts[2]} {strata} Fragments";
                }
                return "Fragments";
            }
            default:
                return raw.Replace("_", " ").Replace(":", " ");
        }
    }

    // ── State updates ────────────────────────────────────────────────────

    private void UpdateAllLockStates()
    {
        if (_region == null) return;
        var prog = CampaignContext.Progression;

        foreach (var mapNode in _region.Nodes)
        {
            if (!_nodeIcons.TryGetValue(mapNode.Id, out var icon)) continue;

            if (prog.IsNodeCleared(mapNode.Id))
            {
                icon.SetCleared();
                continue;
            }

            bool unlocked = IsNodeUnlocked(mapNode);
            icon.SetLocked(!unlocked);
        }

        _shardLabel.Text = $"Shards: {prog.Shards}";
    }

    private bool IsNodeUnlocked(MapNode node)
    {
        return MapUnlockEvaluator.IsUnlocked(node, CampaignContext.Progression.ClearedNodes);
    }

    private void OnNodeSelected(string nodeId)
    {
        if (_region == null) return;

        // Deselect previous node
        if (_selectedNodeId != null && _nodeIcons.TryGetValue(_selectedNodeId, out var prevIcon))
            prevIcon.SetSelected(false);

        _selectedNodeId = nodeId;

        // Select new node
        if (_nodeIcons.TryGetValue(nodeId, out var icon))
            icon.SetSelected(true);

        var mapNode = _region.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (mapNode == null) return;

        string typeStr = mapNode.Type switch
        {
            MapNodeType.Duel => "Duel",
            MapNodeType.Elite => "Elite Encounter",
            MapNodeType.Warden => "Zone Warden",
            MapNodeType.WardenBoss => "Warden Boss",
            MapNodeType.Dig => "Dig Site",
            MapNodeType.Shrine => "Shrine",
            MapNodeType.Cache => "Hidden Cache",
            MapNodeType.Merchant => "Merchant",
            _ => mapNode.Type.ToString()
        };

        string displayName;
        if (mapNode.Type == MapNodeType.Dig)
        {
            if (mapNode.Encounter != null && CampaignContext.DigSiteIndex.TryGetValue(mapNode.Encounter, out var digSite))
                displayName = digSite.Name;
            else
                displayName = "Dig Site";
        }
        else if (mapNode.Encounter != null && CampaignContext.EncounterIndex.TryGetValue(mapNode.Encounter, out var enc))
            displayName = enc.Name;
        else
            displayName = (mapNode.Encounter ?? mapNode.Type.ToString()).Replace("_", " ");

        _infoName.Text = displayName;
        _infoType.Text = typeStr;

        string rewardsStr = mapNode.Rewards is { Count: > 0 }
            ? "Rewards:  " + string.Join("  ·  ", mapNode.Rewards.Select(PrettifyReward))
            : "Rewards:  —";
        _infoRewards.Text = rewardsStr;

        bool isCleared = CampaignContext.Progression.IsNodeCleared(nodeId);
        bool isLocked = !IsNodeUnlocked(mapNode);
        bool isDig = mapNode.Type == MapNodeType.Dig;
        bool isDuel = mapNode.Type is MapNodeType.Duel or MapNodeType.Elite or MapNodeType.Warden or MapNodeType.WardenBoss;
        bool hasEncounter = mapNode.Encounter != null && CampaignContext.EncounterIndex.ContainsKey(mapNode.Encounter);

        // TASK-AUDIO-HOOK-1: Unlock sound when selecting an unlocked, non-cleared node
        if (!isLocked && !isCleared)
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("unlock");

        _infoGoButton.Disabled = isCleared || isLocked || (!isDig && !hasEncounter);

        if (isCleared && isDuel)
            _infoGoButton.Text = "Done";
        else if (isDig)
            _infoGoButton.Text = "Dig";
        else if (isDuel)
            _infoGoButton.Text = "Challenge";
        else
            _infoGoButton.Text = "Go";

        _infoPanel.Show();

        GD.Print($"[MAP] Selected node {nodeId} ({displayName}) — type={mapNode.Type} cleared={isCleared} locked={isLocked} button={_infoGoButton.Text}");
    }

    private void OnGoButtonPressed()
    {
        if (_selectedNodeId == null || _region == null) return;

        var mapNode = _region.Nodes.FirstOrDefault(n => n.Id == _selectedNodeId);
        if (mapNode == null) return;

        CampaignContext.CurrentNodeId = mapNode.Id;

        if (mapNode.Type == MapNodeType.Dig)
        {
            CampaignContext.CurrentDigSiteId = mapNode.Encounter ?? "region_01_dig";
            GetTree().ChangeSceneToFile("res://scenes/dig/DigScene.tscn");
            return;
        }

        if (mapNode.Encounter == null) return;

        if (!CampaignContext.EncounterIndex.TryGetValue(mapNode.Encounter, out var encounterDef))
        {
            GD.PrintErr($"[MapScene] Unknown encounter: {mapNode.Encounter}");
            return;
        }

        CampaignContext.CurrentEncounter = encounterDef;

        // Vigor is always 25 — no dial needed.
        CampaignContext.MatchConfig = new MatchConfig();
        GD.Print($"[MapScene] Transitioning to duel (StartingVigor=25)");
        GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
    }

    /// <summary>
    /// Pre-duel starting vigor is always 25 — no dial needed.
    /// Transition directly to the duel scene.
    /// </summary>

    // ——— Input: pan and zoom (mouse + touch) ———
    // Map node click detection is in _unhandled_input so UI buttons
    // (top bar, sidebar) get first dibs on click events.

    public override void _Input(InputEvent @event)
    {
        // Mouse wheel zoom
        if (@event is InputEventMouseButton mouse && mouse.Pressed)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
            {
                SetZoom(_zoom + ZoomStep, mouse.Position);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mouse.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoom(_zoom - ZoomStep, mouse.Position);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // Mouse drag pan (middle or right button)
        if (@event is InputEventMouseButton mouseBtn)
        {
            bool panButton = mouseBtn.ButtonIndex == MouseButton.Middle || mouseBtn.ButtonIndex == MouseButton.Right;
            if (panButton && mouseBtn.Pressed && !_infoPanel.GetGlobalRect().HasPoint(mouseBtn.Position))
            {
                _dragStart = mouseBtn.Position;
                _containerStartPos = _mapContainer.Position;
                _isDragging = true;
                GetViewport().SetInputAsHandled();
            }
            else if (panButton && !mouseBtn.Pressed)
            {
                _isDragging = false;
            }
        }

        if (@event is InputEventMouseMotion motion && _isDragging)
        {
            Vector2 delta = motion.Position - _dragStart;
            _mapContainer.Position = _containerStartPos + delta;
            ClampPan();
        }

        // Pinch to zoom
        if (@event is InputEventMagnifyGesture magnify)
        {
            float newZoom = _zoom * magnify.Factor;
            SetZoom(newZoom, magnify.Position);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Handle click/tap on map nodes — only fires for events NOT consumed by
    /// UI buttons (top bar, sidebar, info panel). This keeps button clicks working.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        // Map node selection and touch pan: left click or touch not on any UI element
        bool isTap = false;
        Vector2 screenPos = Vector2.Zero;

        if (@event is InputEventMouseButton click && click.Pressed && click.ButtonIndex == MouseButton.Left)
        {
            isTap = true;
            screenPos = click.Position;
        }
        else if (@event is InputEventScreenTouch touch && touch.Pressed && _touchDragId == -1)
        {
            isTap = true;
            screenPos = touch.Position;
        }
        else if (@event is InputEventScreenTouch touchRelease && !touchRelease.Pressed && touchRelease.Index == _touchDragId)
        {
            _touchDragId = -1;
            _touchDragging = false;
            return;
        }
        else if (@event is InputEventScreenDrag drag && drag.Index == _touchDragId && _touchDragging)
        {
            Vector2 delta = drag.Position - _touchDragStart;
            _mapContainer.Position = _containerStartPos + delta;
            ClampPan();
            return;
        }

        if (!isTap) return;

        // One finger press is one press: a tap also arrives as an emulated mouse click.
        if (!_tap.Accept(@event)) return;

        // Don't handle taps on the info panel
        if (_infoPanel.Visible && _infoPanel.GetGlobalRect().HasPoint(screenPos))
            return;

        // Convert screen position to map container local coordinates
        // ToLocal already accounts for the Node2D's position and scale
        Vector2 localPos = _mapContainer.ToLocal(screenPos);

        // Find nearest node — use full icon rect (including label) for hit test,
        // not just a radius from origin. This ensures the label area and edges
        // are clickable on every tap.
        string? nearestId = null;
        float nearestDist = 120f; // generous fallback radius for the nearest edge
        foreach (var (id, icon) in _nodeIcons)
        {
            // Rect-based hit test: the icon's full button rect (140×150) in local space
            var iconRect = new Rect2(icon.Position, icon.Size);
            bool hit = iconRect.Grow(8).HasPoint(localPos);

            if (hit)
            {
                nearestId = id;
                nearestDist = 0; // direct hit, no distance tiebreaker needed
                break;
            }

            // Fallback: distance to nearest edge (for very small icons at far zoom)
            float dist = icon.Position.DistanceTo(localPos);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestId = id;
            }
        }

        GD.Print($"[MAP] Click at screen=({screenPos.X:F0},{screenPos.Y:F0}) local=({localPos.X:F0},{localPos.Y:F0}) nearest={nearestId ?? "none"} dist={nearestDist:F0}");

        if (nearestId != null)
        {
            OnNodeSelected(nearestId);
            GetViewport().SetInputAsHandled();
        }
    }
    private void SetZoom(float newZoom, Vector2 mousePos)
    {
        float oldZoom = _zoom;
        _zoom = Mathf.Clamp(newZoom, _minZoom, MaxZoom);

        Vector2 offset = mousePos - _mapContainer.Position;
        Vector2 newOffset = offset * (_zoom / oldZoom);
        _mapContainer.Position = mousePos - newOffset;
        _mapContainer.Scale = new Vector2(_zoom, _zoom);
        ClampPan();
    }

    // ── Deck chip helpers ───────────────────────────────────────────

    private Color GetClassColor()
    {
        string classId = GetActiveClass();
        return classId.ToLowerInvariant() switch
        {
            "warrior" => new Color(0.85f, 0.20f, 0.15f, 1f),
            "mage" => new Color(0.25f, 0.40f, 0.85f, 1f),
            "rogue" => new Color(0.20f, 0.70f, 0.25f, 1f),
            "hunter" => new Color(0.65f, 0.40f, 0.15f, 1f),
            "cleric" => new Color(0.95f, 0.85f, 0.40f, 1f),
            _ => new Color(0.60f, 0.50f, 0.25f, 1f)
        };
    }

    private string GetActiveClass()
    {
        return CampaignContext.ActiveProfile?.ClassId ?? CampaignContext.ChosenClass;
    }

    private void UpdateDeckChipText()
    {
        string deckName = "Deck: —";
        var profile = CampaignContext.ActiveProfile;
        if (profile != null && !string.IsNullOrEmpty(profile.ActiveDeckId))
        {
            var deck = CampaignContext.DeckLibrary.FirstOrDefault(d => d.DeckId == profile.ActiveDeckId);
            if (deck != null)
                deckName = $"Deck: {deck.Name}";
        }
        _deckChipLabel.Text = deckName;
    }

    private void ShowDeckPopup()
    {
        string classId = GetActiveClass();
        var decks = CampaignContext.GetDecksForClass(classId);

        var popup = new PanelContainer();
        var popupStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.10f, 0.07f, 0.95f),
            BorderColor = new Color(0.60f, 0.50f, 0.25f, 0.60f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10, ContentMarginTop = 8,
            ContentMarginRight = 10, ContentMarginBottom = 8
        };
        popup.AddThemeStyleboxOverride("panel", popupStyle);
        popup.CustomMinimumSize = new Vector2(250, 200);

        // Position near the chip
        var chipRect = _deckChip.GetGlobalRect();
        popup.Position = new Vector2(chipRect.Position.X - 60, chipRect.Position.Y + chipRect.Size.Y + 4);

        var vbox = new VBoxContainer();

        // Title
        var title = new Label { Text = "Select Deck", HorizontalAlignment = HorizontalAlignment.Center };
        title.Modulate = new Color(0.90f, 0.82f, 0.55f, 1f);
        ThemeTokens.ApplyHeaderFont(title, 14);
        vbox.AddChild(title);

        // Separator
        var sep = new ColorRect
        {
            CustomMinimumSize = new Vector2(0, 1),
            Color = new Color(0.60f, 0.50f, 0.25f, 0.40f)
        };
        vbox.AddChild(sep);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        // Deck buttons
        if (decks.Count == 0)
        {
            var noDecks = new Label
            {
                Text = "No decks saved yet.",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            noDecks.AddThemeFontSizeOverride("font_size", 11);
            noDecks.Modulate = new Color(0.70f, 0.65f, 0.50f, 0.80f);
            vbox.AddChild(noDecks);
        }
        else
        {
            foreach (var deck in decks)
            {
                var deckBtn = new Button
                {
                    Text = $"{deck.Name} ({deck.Cards.Count} cards)",
                    CustomMinimumSize = new Vector2(0, 28)
                };
                deckBtn.AddThemeFontSizeOverride("font_size", 11);
                string capturedDeckId = deck.DeckId;
                deckBtn.Pressed += () =>
                {
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                    if (CampaignContext.ActiveProfile != null)
                    {
                        CampaignContext.ActiveProfile.ActiveDeckId = capturedDeckId;
                        CampaignContext.SaveCampaignProfile();
                        UpdateDeckChipText();
                    }
                    popup.QueueFree();
                };
                vbox.AddChild(deckBtn);
            }
        }

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        // Close button
        var closeBtn = new Button { Text = "Close" };
        closeBtn.AddThemeFontSizeOverride("font_size", 10);
        closeBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            popup.QueueFree();
        };
        vbox.AddChild(closeBtn);

        popup.AddChild(vbox);
        AddChild(popup);
    }
}