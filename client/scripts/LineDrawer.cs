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
        // Hand-inked trail look: dark sepia dashes with a faint parchment
        // under-stroke, drawn between exact medallion centers.
        var under = new Color(0.9f, 0.82f, 0.6f, 0.18f);
        var ink = new Color(0.16f, 0.11f, 0.06f, 0.75f);
        foreach (var (from, to) in _edges)
        {
            // Trim the ends so dashes don't poke out from under the medallions
            Vector2 dir = (to - from).Normalized();
            Vector2 a = from + dir * 26f;
            Vector2 b = to - dir * 26f;
            DrawLine(a, b, under, 5.0f);
            DrawDashedLine(a, b, ink, 2.4f, 9.0f);
        }
    }
}