using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

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