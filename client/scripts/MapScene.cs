using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// Campaign map screen — renders a node graph from a MapRegion JSON file.
/// Supports pan (drag) and zoom (scroll wheel).
/// Nodes show type icon + lock state. Clicking a node shows info panel.
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
        _background = GetNode<ColorRect>("Background");
        _mapContainer = GetNode<Node2D>("MapContainer");

        // Line drawer (draws edges between nodes)
        _lineDrawer = new LineDrawer();
        _mapContainer.AddChild(_lineDrawer);

        // Info panel
        _infoPanel = GetNode<Panel>("InfoPanel");
        _infoName = _infoPanel.GetNode<Label>("VBox/InfoName");
        _infoType = _infoPanel.GetNode<Label>("VBox/InfoType");
        _infoRewards = _infoPanel.GetNode<Label>("VBox/InfoRewards");
        _infoGoButton = _infoPanel.GetNode<Button>("VBox/GoButton");
        _infoCloseButton = _infoPanel.GetNode<Button>("VBox/CloseButton");

        _infoGoButton.Pressed += OnGoButtonPressed;
        _infoCloseButton.Pressed += () => _infoPanel.Hide();

        _infoPanel.Hide();

        // Load region and build map
        LoadRegion();
        BuildMap();
    }

    private void LoadRegion()
    {
        string contentDir = ProjectSettings.GlobalizePath("res://") + "../content/map";
        string path = $"{contentDir}/region_01.json";
        _region = MapLoader.LoadRegion(path);
    }

    private void BuildMap()
    {
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
            string displayName = mapNode.Encounter ?? mapNode.Id;
            displayName = displayName.Replace("r1_", "").Replace("_", " ");
            // Capitalize first letter
            if (displayName.Length > 0)
                displayName = char.ToUpper(displayName[0]) + displayName[1..];

            // Default all nodes locked except the first one
            bool locked = mapNode.Id != "r1_n01";
            icon.Setup(mapNode.Id, displayName, mapNode.Type.ToString(), locked);

            // Position: offset so center of map is at (0, 0) in container space
            float x = mapNode.Position[0] - centerX;
            float y = mapNode.Position[1] - centerY;
            icon.Position = new Vector2(x, y);

            icon.NodeSelected += OnNodeSelected;
            _mapContainer.AddChild(icon);
            _nodeIcons[mapNode.Id] = icon;
        }

        // Tell the line drawer what nodes and connections exist
        _lineDrawer.SetNodes(_region.Nodes, centerX, centerY);

        // Set initial offset so map is centered in viewport
        _mapOffset = new Vector2(
            GetViewportRect().Size.X / 2f,
            GetViewportRect().Size.Y / 2f
        );
        _mapContainer.Position = _mapOffset;
    }

    private void OnNodeSelected(string nodeId)
    {
        if (_region == null) return;
        _selectedNodeId = nodeId;

        var mapNode = _region.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (mapNode == null) return;

        // Show info panel
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
        _infoName.Text = encounterStr.Replace("_", " ");
        if (_infoName.Text.Length > 0)
            _infoName.Text = char.ToUpper(_infoName.Text[0]) + _infoName.Text[1..];

        _infoType.Text = typeStr;

        string rewardsStr = mapNode.Rewards is { Count: > 0 }
            ? string.Join("\n", mapNode.Rewards)
            : "None";
        _infoRewards.Text = rewardsStr;

        _infoGoButton.Disabled = mapNode.Id != "r1_n01"; // only first node playable
        _infoPanel.Show();
    }

    private void OnGoButtonPressed()
    {
        if (_selectedNodeId == null) return;
        GD.Print($"[MapScene] Entering duel for node {_selectedNodeId}");
        // Future: transition to DuelScene with encounter deck for this node
    }

    // ——— Input: pan and zoom ———

    public override void _Input(InputEvent @event)
    {
        // Zoom with scroll wheel
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

        // Pan with right-click drag (or middle-click)
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

        // Pan with RMB drag on background (not on nodes)
        if (@event is InputEventMouseButton rmb && rmb.ButtonIndex == MouseButton.Right)
        {
            if (rmb.Pressed)
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

        // Drag movement
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

        // Zoom toward mouse position
        Vector2 offset = mousePos - _mapContainer.Position;
        Vector2 newOffset = offset * (_zoom / oldZoom);
        _mapContainer.Position = mousePos - newOffset;
        _mapContainer.Scale = new Vector2(_zoom, _zoom);
    }
}

/// <summary>
/// Draws connection lines between map nodes.
/// Must be a child of the map container so lines transform with pan/zoom.
/// </summary>
public partial class LineDrawer : Node2D
{
    private readonly List<(Vector2 from, Vector2 to)> _edges = new();

    public void SetNodes(List<MapNode> nodes, float centerX, float centerY)
    {
        var positions = new Dictionary<string, Vector2>();
        foreach (var node in nodes)
        {
            positions[node.Id] = new Vector2(
                node.Position[0] - centerX,
                node.Position[1] - centerY
            );
        }

        _edges.Clear();
        foreach (var node in nodes)
        {
            if (!positions.TryGetValue(node.Id, out var fromPos)) continue;
            foreach (var targetId in node.Connects)
            {
                if (positions.TryGetValue(targetId, out var toPos))
                {
                    _edges.Add((fromPos, toPos));
                }
            }
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var (from, to) in _edges)
        {
            DrawLine(from + new Vector2(36, 36), to + new Vector2(36, 36),
                new Color(0.4f, 0.4f, 0.5f, 0.6f), 2.0f);
        }
    }
}