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
    private Sprite2D _mapBackground;
    private ColorRect _background;

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

    // Side buttons
    private Button _settingsBtn;
    private Button _runePageBtn;
    private Button _forgeBtn;

    // Line drawing
    private LineDrawer _lineDrawer;

    // State
    private MapRegion? _region;
    private readonly Dictionary<string, MapNodeIcon> _nodeIcons = new();
    private string? _selectedNodeId;
    private Vector2 _dragStart;
    private Vector2 _containerStartPos;
    private bool _isDragging;

    // Zoom
    private float _zoom = 1.0f;
    private const float MinZoom = 0.3f;
    private const float MaxZoom = 3.0f;
    private const float ZoomStep = 0.1f;

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
        // Deep warm brown background matching title screen
        _background = new ColorRect
        {
            Color = new Color(0.07f, 0.06f, 0.05f, 1f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f
        };
        AddChild(_background);

        // Vignette overlay
        var vignette = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.3f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(vignette);

        // Map container (pannable layer with background and nodes)
        _mapContainer = new Node2D();
        AddChild(_mapContainer);

        // Map background texture (parchment-style map with towns and terrain)
        _mapBackground = new Sprite2D();
        if (ResourceLoader.Exists("res://content/map/map_background.png"))
        {
            var tex = ResourceLoader.Load<Texture2D>("res://content/map/map_background.png");
            _mapBackground.Texture = tex;
            _mapBackground.Modulate = new Color(1, 1, 1, 0.6f); // 60% opacity — visible but subtle
        }
        _mapContainer.AddChild(_mapBackground);

        // Line drawer (edges between nodes)
        _lineDrawer = new LineDrawer();
        _mapContainer.AddChild(_lineDrawer);
    }

    // ── Top bar ──────────────────────────────────────────────────────────

    private void BuildTopBar()
    {
        // Top bar background
        var topBar = new ColorRect
        {
            Color = new Color(0.1f, 0.08f, 0.06f, 0.85f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.055f
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
            Text = "< Title",
            AnchorLeft = 0.01f, AnchorRight = 0.12f,
            AnchorTop = 0.002f, AnchorBottom = 0.053f
        };
        StyleButton(_backButton, 11, goldText: true);
        _backButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
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
        _forgeBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/forge/ForgeScene.tscn");
        AddChild(_forgeBtn);

        _runePageBtn = new Button
        {
            Text = "Rune Page",
            AnchorLeft = xL, AnchorRight = xL + btnW,
            AnchorTop = 0.86f, AnchorBottom = 0.93f
        };
        StyleButton(_runePageBtn, 11, goldText: false);
        _runePageBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/runepage/RunePageScene.tscn");
        AddChild(_runePageBtn);

        _settingsBtn = new Button
        {
            Text = "Settings",
            AnchorLeft = xL, AnchorRight = xL + btnW,
            AnchorTop = 0.93f, AnchorBottom = 1f
        };
        StyleButton(_settingsBtn, 11, goldText: false);
        _settingsBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/settings/SettingsScene.tscn");
        AddChild(_settingsBtn);
    }

    // ── Map graph ────────────────────────────────────────────────────────

    private void BuildMap()
    {
        string json = Godot.FileAccess.GetFileAsString("res://content/map/region_01.json");
        _region = MapLoader.LoadRegionFromString(json);
        if (_region == null) return;

        var iconScene = GD.Load<PackedScene>("res://scenes/components/MapNodeIcon.tscn");

        // Calculate map bounds for centering
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var node in _region.Nodes)
        {
            if (node.Position[0] < minX) minX = node.Position[0];
            if (node.Position[0] > maxX) maxX = node.Position[0];
            if (node.Position[1] < minY) minY = node.Position[1];
            if (node.Position[1] > maxY) maxY = node.Position[1];
        }

        float mapWidth = maxX - minX + 200;
        float mapHeight = maxY - minY + 200;
        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;

        // Position background sprite at the map center
        _mapBackground.Position = new Vector2(0, 0);
        // Scale background to cover the node area with some margin
        float bgScaleX = mapWidth / 1000f;
        float bgScaleY = mapHeight / 800f;
        _mapBackground.Scale = new Vector2(bgScaleX * 1.3f, bgScaleY * 1.3f);

        // Create node icons
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
            icon.Position = new Vector2(x, y);

            icon.NodeSelected += OnNodeSelected;
            _mapContainer.AddChild(icon);

            icon.Setup(mapNode.Id, displayName, mapNode.Type.ToString(), locked: true);
            _nodeIcons[mapNode.Id] = icon;
        }

        _lineDrawer.SetNodes(_region.Nodes, centerX, centerY);

        // Auto-frame: zoom to fit
        Vector2 viewport = GetViewportRect().Size;
        float fitZoomW = (viewport.X - 100f) / mapWidth;
        float fitZoomH = (viewport.Y - 140f) / mapHeight;
        _zoom = Mathf.Clamp(Mathf.Min(fitZoomW, fitZoomH), MinZoom, MaxZoom);

        _mapOffset = new Vector2(
            GetViewportRect().Size.X / 2f,
            GetViewportRect().Size.Y / 2f
        );
        _mapContainer.Position = _mapOffset;
        _mapContainer.Scale = new Vector2(_zoom, _zoom);
    }

    // ── Info panel ───────────────────────────────────────────────────────

    private void BuildInfoPanel()
    {
        _infoPanel = new Panel();
        _infoPanel.AnchorLeft = 0.05f;
        _infoPanel.AnchorRight = 0.55f;
        _infoPanel.AnchorTop = 0.65f;
        _infoPanel.AnchorBottom = 0.95f;

        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.1f, 0.07f, 0.95f),
            BorderColor = new Color(0.6f, 0.5f, 0.25f, 0.4f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 10, ContentMarginTop = 8,
            ContentMarginRight = 10, ContentMarginBottom = 8
        };
        _infoPanel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(_infoPanel);

        var infoVbox = new VBoxContainer();
        infoVbox.AnchorLeft = 0f; infoVbox.AnchorRight = 1f;
        infoVbox.AnchorTop = 0f; infoVbox.AnchorBottom = 1f;
        _infoPanel.AddChild(infoVbox);

        _infoName = new Label();
        _infoName.AddThemeFontSizeOverride("font_size", 18);
        _infoName.Modulate = new Color(0.9f, 0.82f, 0.55f, 1f); // gold
        infoVbox.AddChild(_infoName);

        _infoType = new Label();
        _infoType.AddThemeFontSizeOverride("font_size", 13);
        _infoType.Modulate = new Color(0.7f, 0.65f, 0.5f, 0.8f);
        infoVbox.AddChild(_infoType);

        infoVbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        _infoRewards = new Label();
        _infoRewards.AddThemeFontSizeOverride("font_size", 12);
        _infoRewards.Modulate = new Color(0.6f, 0.7f, 0.5f, 0.8f);
        _infoRewards.AutowrapMode = TextServer.AutowrapMode.Word;
        infoVbox.AddChild(_infoRewards);

        infoVbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });

        var buttonRow = new HBoxContainer();
        infoVbox.AddChild(buttonRow);

        _infoGoButton = new Button { Text = "Go" };
        StyleButton(_infoGoButton, 14);
        _infoGoButton.Pressed += OnGoButtonPressed;
        buttonRow.AddChild(_infoGoButton);

        buttonRow.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });

        _infoCloseButton = new Button { Text = "Close" };
        StyleButton(_infoCloseButton, 12, goldText: false);
        _infoCloseButton.Pressed += () => _infoPanel.Hide();
        buttonRow.AddChild(_infoCloseButton);

        _infoPanel.Hide();
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
        _selectedNodeId = nodeId;

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
            ? string.Join("\n", mapNode.Rewards)
            : "None";
        _infoRewards.Text = rewardsStr;

        bool isCleared = CampaignContext.Progression.IsNodeCleared(nodeId);
        bool isLocked = !IsNodeUnlocked(mapNode);
        bool isDig = mapNode.Type == MapNodeType.Dig;
        bool hasEncounter = mapNode.Encounter != null && CampaignContext.EncounterIndex.ContainsKey(mapNode.Encounter);
        _infoGoButton.Disabled = isCleared || isLocked || (!isDig && !hasEncounter);
        _infoGoButton.Text = isCleared ? "Done" : "Go";

        _infoPanel.Show();
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
        GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
    }

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
            return;
        }

        if (!isTap) return;

        // Don't handle taps on the info panel
        if (_infoPanel.Visible && _infoPanel.GetGlobalRect().HasPoint(screenPos))
            return;

        // Convert screen position to map container coordinates
        Vector2 localPos = (_mapContainer.ToLocal(screenPos) - _mapContainer.Position) / _zoom;

        // Find nearest node within tap distance
        string? nearestId = null;
        float nearestDist = 50f;
        foreach (var (id, icon) in _nodeIcons)
        {
            float dist = icon.Position.DistanceTo(localPos);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestId = id;
            }
        }

        if (nearestId != null)
        {
            OnNodeSelected(nearestId);
            GetViewport().SetInputAsHandled();
        }
    }
    private void SetZoom(float newZoom, Vector2 mousePos)
    {
        float oldZoom = _zoom;
        _zoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);

        Vector2 offset = mousePos - _mapContainer.Position;
        Vector2 newOffset = offset * (_zoom / oldZoom);
        _mapContainer.Position = mousePos - newOffset;
        _mapContainer.Scale = new Vector2(_zoom, _zoom);
    }
}