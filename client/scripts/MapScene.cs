using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Campaign map screen — renders a node graph from a MapRegion JSON file.
/// Uses CampaignContext.SaveManager for live lock/clear state from SQLite.
/// "Go" button transitions to DuelScene with the encounter's config.
/// </summary>
public partial class MapScene : Control
{
    // Map container (the pannable/zoomable surface)
    private Node2D _mapContainer;
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
    private const float MinZoom = 0.35f;
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
        BuildUI();
        BuildMap();
        UpdateAllLockStates();

        // Tutorial: if at Runes_OpenRunePage step, show a rune page button
        CheckTutorialRuneStep();
    }

    /// <summary>
    /// Ensure campaign data (encounters, save manager) is loaded.
    /// In normal flow this is already done by the title screen; this guard
    /// makes the map screen standalone-capable (e.g. for testing/export of the
    /// scene directly) without double-initializing the save manager.
    /// </summary>
    private void EnsureCampaignContext()
    {
        if (!CampaignContext.SaveManager.IsLoaded)
        {
            CampaignContext.SaveManager.Initialize();
        }

        if (CampaignContext.EncounterIndex.Count == 0)
        {
            CampaignContext.LoadEncounters();
            CampaignContext.LoadDigSites();
        }
    }

    private void BuildUI()
    {
        // Background
        _background = new ColorRect
        {
            Color = new Color(0.08f, 0.08f, 0.12f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f
        };
        AddChild(_background);

        // Map container
        _mapContainer = new Node2D();
        AddChild(_mapContainer);

        // Line drawer
        _lineDrawer = new LineDrawer();
        _mapContainer.AddChild(_lineDrawer);

        // Back button (top-left)
        _backButton = new Button
        {
            Text = "< Title",
            AnchorLeft = 0f, AnchorRight = 0.12f,
            AnchorTop = 0f, AnchorBottom = 0.05f
        };
        _backButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        AddChild(_backButton);

        // Shard display (top-right)
        _shardLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            AnchorLeft = 0.7f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.05f,
            VerticalAlignment = VerticalAlignment.Center
        };
        _shardLabel.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_shardLabel);

        // Info panel
        _infoPanel = new Panel();
        _infoPanel.AnchorLeft = 0.1f;
        _infoPanel.AnchorRight = 0.5f;
        _infoPanel.AnchorTop = 0.7f;
        _infoPanel.AnchorBottom = 0.95f;
        AddChild(_infoPanel);

        var infoVbox = new VBoxContainer();
        infoVbox.AnchorLeft = 0f; infoVbox.AnchorRight = 1f;
        infoVbox.AnchorTop = 0f; infoVbox.AnchorBottom = 1f;
        _infoPanel.AddChild(infoVbox);

        _infoName = new Label();
        _infoName.AddThemeFontSizeOverride("font_size", 22);
        infoVbox.AddChild(_infoName);

        _infoType = new Label { Modulate = new Color(0.6f, 0.6f, 0.7f) };
        infoVbox.AddChild(_infoType);

        _infoRewards = new Label { Modulate = new Color(0.5f, 0.6f, 0.5f) };
        infoVbox.AddChild(_infoRewards);

        var buttonRow = new HBoxContainer();
        infoVbox.AddChild(buttonRow);

        _infoGoButton = new Button { Text = "Go" };
        _infoGoButton.Pressed += OnGoButtonPressed;
        buttonRow.AddChild(_infoGoButton);

        _infoCloseButton = new Button { Text = "Close" };
        _infoCloseButton.Pressed += () => _infoPanel.Hide();
        buttonRow.AddChild(_infoCloseButton);

        _infoPanel.Hide();
    }

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

        float mapWidth = maxX - minX + 160;
        float mapHeight = maxY - minY + 160;
        float centerX = (minX + maxX) / 2f;
        float centerY = (minY + maxY) / 2f;

        // Create node icons
        foreach (var mapNode in _region.Nodes)
        {
            var icon = iconScene.Instantiate<MapNodeIcon>();

            // Determine display name
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

            // Setup after AddChild so _Ready has run (child node refs are valid)
            icon.Setup(mapNode.Id, displayName, mapNode.Type.ToString(), locked: true);
            _nodeIcons[mapNode.Id] = icon;
        }

        _lineDrawer.SetNodes(_region.Nodes, centerX, centerY);

        // Auto-frame: zoom to fit the whole map in the viewport with padding
        Vector2 viewport = GetViewportRect().Size;
        float fitZoomW = (viewport.X - 80f) / mapWidth;
        float fitZoomH = (viewport.Y - 120f) / mapHeight;
        _zoom = Mathf.Clamp(Mathf.Min(fitZoomW, fitZoomH), MinZoom, MaxZoom);

        _mapOffset = new Vector2(
            GetViewportRect().Size.X / 2f,
            GetViewportRect().Size.Y / 2f
        );
        _mapContainer.Position = _mapOffset;
        _mapContainer.Scale = new Vector2(_zoom, _zoom);
    }

    private void UpdateAllLockStates()
    {
        if (_region == null) return;
        var prog = CampaignContext.Progression;

        foreach (var mapNode in _region.Nodes)
        {
            if (!_nodeIcons.TryGetValue(mapNode.Id, out var icon)) continue;

            // Mark cleared
            if (prog.IsNodeCleared(mapNode.Id))
            {
                icon.SetCleared();
                continue;
            }

            // Check unlock conditions
            bool unlocked = IsNodeUnlocked(mapNode);
            icon.SetLocked(!unlocked);
        }

        _shardLabel.Text = $"Shards: {prog.Shards}";
    }

    private bool IsNodeUnlocked(MapNode node)
    {
        // Delegate to the engine evaluator so the UI and the tests share one source of truth.
        // Progression.ClearedNodes is the set of cleared node IDs.
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

        string encounterStr = mapNode.Encounter ?? "—";
        string displayName;
        if (mapNode.Type == MapNodeType.Dig)
        {
            // Show dig site name from the first dig site in the node's encounter field (or default label)
            if (mapNode.Encounter != null && CampaignContext.DigSiteIndex.TryGetValue(mapNode.Encounter, out var digSite))
                displayName = digSite.Name;
            else
                displayName = "Dig Site";
        }
        else if (mapNode.Encounter != null && CampaignContext.EncounterIndex.TryGetValue(mapNode.Encounter, out var enc))
            displayName = enc.Name;
        else
            displayName = encounterStr.Replace("_", " ");

        _infoName.Text = displayName;
        _infoType.Text = typeStr;

        string rewardsStr = mapNode.Rewards is { Count: > 0 }
            ? string.Join("\n", mapNode.Rewards)
            : "None";
        _infoRewards.Text = rewardsStr;

        // Go button: disabled if node is cleared or locked
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
            // Navigate to dig scene
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

        // ——— Touch input ———
        // Single-finger drag to pan
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed && _touchDragId == -1)
            {
                _touchDragId = touch.Index;
                _touchDragStart = touch.Position;
                _containerStartPos = _mapContainer.Position;
                _touchDragging = true;
            }
            else if (!touch.Pressed && touch.Index == _touchDragId)
            {
                _touchDragId = -1;
                _touchDragging = false;
            }
        }
        else if (@event is InputEventScreenDrag drag && drag.Index == _touchDragId && _touchDragging)
        {
            Vector2 delta = drag.Position - _touchDragStart;
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

    private void SetZoom(float newZoom, Vector2 mousePos)
    {
        float oldZoom = _zoom;
        _zoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);

        Vector2 offset = mousePos - _mapContainer.Position;
        Vector2 newOffset = offset * (_zoom / oldZoom);
        _mapContainer.Position = mousePos - newOffset;
        _mapContainer.Scale = new Vector2(_zoom, _zoom);
    }

    /// <summary>
    /// If the tutorial is at Runes_OpenRunePage, add a rune page button to the map.
    /// </summary>
    private void CheckTutorialRuneStep()
    {
        if (CampaignContext.Tutorial?.CurrentStep == TutorialStep.Runes_OpenRunePage
            && !CampaignContext.Tutorial.IsComplete)
        {
            // Add a rune page button that advances the tutorial
            var runeBtn = new Button
            {
                Text = "Rune Page (Tutorial)",
                AnchorLeft = 0.3f, AnchorRight = 0.7f,
                AnchorTop = 0.5f, AnchorBottom = 0.6f,
            };
            runeBtn.Pressed += () =>
            {
                CampaignContext.Tutorial.CurrentStep = TutorialStep.Runes_EquipRune;
                // Fire StepChanged on the TutorialController
                var ctrl = GetNodeOrNull<TutorialController>("/root/TutorialController");
                if (ctrl != null)
                {
                    ctrl.Advance();
                }
                GetTree().ChangeSceneToFile("res://scenes/rune/RunePageScene.tscn");
            };
            AddChild(runeBtn);
        }
    }
}