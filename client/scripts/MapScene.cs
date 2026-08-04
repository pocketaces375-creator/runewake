using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;

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
    private const float MinZoom = 0.4f;
    private const float MaxZoom = 2.5f;
    private const float ZoomStep = 0.1f;

    // Map center offset
    private Vector2 _mapOffset;

    public override void _Ready()
    {
        BuildUI();
        BuildMap();
        UpdateAllLockStates();
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
        string contentDir = ProjectSettings.GlobalizePath("res://") + "../content/map";
        string path = $"{contentDir}/region_01.json";
        _region = MapLoader.LoadRegion(path);
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

            // Default all nodes locked; UpdateAllLockStates will fix
            icon.Setup(mapNode.Id, displayName, mapNode.Type.ToString(), locked: true);

            float x = mapNode.Position[0] - centerX;
            float y = mapNode.Position[1] - centerY;
            icon.Position = new Vector2(x, y);

            icon.NodeSelected += OnNodeSelected;
            _mapContainer.AddChild(icon);
            _nodeIcons[mapNode.Id] = icon;
        }

        _lineDrawer.SetNodes(_region.Nodes, centerX, centerY);

        _mapOffset = new Vector2(
            GetViewportRect().Size.X / 2f,
            GetViewportRect().Size.Y / 2f
        );
        _mapContainer.Position = _mapOffset;
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
        // First node starts unlocked
        if (node.Id == "r1_n01") return true;

        // No unlock condition = locked
        if (node.Unlock == null) return false;

        // NODES_CLEARED: all prerequisite nodes must be cleared
        if (node.Unlock.Op == "NODES_CLEARED" && node.Unlock.Value is { Count: > 0 })
        {
            return node.Unlock.Value.All(p => CampaignContext.Progression.IsNodeCleared(p));
        }

        return false;
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

    // ——— Input: pan and zoom ———

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed)
        {
            if (mouse.ButtonIndex == MouseButton.WheelUp)
            {
                SetZoom(_zoom + ZoomStep, mouse.Position);
                GetViewport().SetInputAsHandled();
            }
            else if (mouse.ButtonIndex == MouseButton.WheelDown)
            {
                SetZoom(_zoom - ZoomStep, mouse.Position);
                GetViewport().SetInputAsHandled();
            }
        }

        if (@event is InputEventMouseButton mouseBtn)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Middle && mouseBtn.Pressed)
            {
                _dragStart = mouseBtn.Position;
                _containerStartPos = _mapContainer.Position;
                _isDragging = true;
                GetViewport().SetInputAsHandled();
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Middle && !mouseBtn.Pressed)
            {
                _isDragging = false;
            }
        }

        if (@event is InputEventMouseButton rmb && rmb.ButtonIndex == MouseButton.Right)
        {
            if (rmb.Pressed && !_infoPanel.GetGlobalRect().HasPoint(rmb.Position))
            {
                _dragStart = rmb.Position;
                _containerStartPos = _mapContainer.Position;
                _isDragging = true;
                GetViewport().SetInputAsHandled();
            }
            else
            {
                _isDragging = false;
            }
        }

        if (@event is InputEventMouseMotion motion && _isDragging)
        {
            Vector2 delta = motion.Position - _dragStart;
            _mapContainer.Position = _containerStartPos + delta;
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