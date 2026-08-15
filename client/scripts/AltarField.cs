using Godot;
using System;

namespace Runewake.Client;

/// <summary>
/// TASK-UI3b: The altar battlefield — draws the altar ellipse with border,
/// inner dashed ring, radial glow, and rune glyphs around the edge.
/// The ellipse provides the visual battlefield where lane slots sit on facing arcs.
/// Uses DrawPolygon for ellipse rendering (Godot 4.3 has no DrawEllipse on Control).
/// </summary>
public partial class AltarField : Control
{
    // Ellipse visual styling
    public Color BorderColor = new Color(0.34f, 0.29f, 0.17f); // #57492c
    public Color FillColor = new Color(0.18f, 0.15f, 0.09f, 0.35f);
    public Color DashedRingColor = new Color(0.34f, 0.29f, 0.17f, 0.25f);
    public Color GlowColor = new Color(0.34f, 0.29f, 0.17f, 0.06f);

    private const float BorderThickness = 2f;
    private const int Segments = 72;

    public override void _Draw()
    {
        Vector2 center = Size / 2;
        float rx = Mathf.Max(1f, Size.X / 2);
        float ry = Mathf.Max(1f, Size.Y / 2);

        // ── Fill ellipse via polygon ──
        var fillPoints = new Vector2[Segments];
        for (int i = 0; i < Segments; i++)
        {
            float angle = Mathf.Tau * i / Segments;
            fillPoints[i] = new Vector2(
                center.X + rx * Mathf.Cos(angle),
                center.Y + ry * Mathf.Sin(angle));
        }
        DrawColoredPolygon(fillPoints, FillColor);

        // ── Outer border — draw as connected line segments ──
        var prev = fillPoints[0];
        for (int i = 1; i <= Segments; i++)
        {
            var cur = fillPoints[i % Segments];
            DrawLine(prev, cur, BorderColor, BorderThickness);
            prev = cur;
        }

        // ── Inner dashed ring (88% scale, every other segment) ──
        float innerRx = rx * 0.88f;
        float innerRy = ry * 0.88f;
        for (int i = 0; i < Segments; i += 2)
        {
            float a1 = Mathf.Tau * i / Segments;
            float a2 = Mathf.Tau * Math.Min(i + 1, Segments) / Segments;
            DrawLine(
                new Vector2(center.X + innerRx * Mathf.Cos(a1), center.Y + innerRy * Mathf.Sin(a1)),
                new Vector2(center.X + innerRx * Mathf.Cos(a2), center.Y + innerRy * Mathf.Sin(a2)),
                DashedRingColor, 1f);
        }

        // ── Smooth radial glow (32 rings — no banding, TASK-UI3e) ──
        int glowRings = 32;
        float glowPeakAlpha = GlowColor.A;
        float alphaStep = glowPeakAlpha / glowRings;
        for (int ring = 0; ring < glowRings; ring++)
        {
            float t = (float)ring / glowRings;
            float grx = rx * (1f - t * 0.90f);
            float gry = ry * (1f - t * 0.90f);

            if (grx <= 1f || gry <= 1f) continue;

            var glowPoints = new Vector2[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float angle = Mathf.Tau * i / Segments;
                glowPoints[i] = new Vector2(
                    center.X + grx * Mathf.Cos(angle),
                    center.Y + gry * Mathf.Sin(angle));
            }
            DrawColoredPolygon(glowPoints, new Color(GlowColor.R, GlowColor.G, GlowColor.B, alphaStep));
        }

        // ── Inset shadow: darker gradient at bottom edge ──
        float shadowRx = rx * 0.95f;
        float shadowRy = ry * 0.85f;
        var shadowPoints = new Vector2[Segments];
        for (int i = 0; i < Segments; i++)
        {
            float angle = Mathf.Tau * i / Segments;
            // Only shadow the lower half (bottom portion of ellipse)
            float yScale = 1f - 0.08f * Mathf.Max(0f, Mathf.Sin(angle));
            shadowPoints[i] = new Vector2(
                center.X + shadowRx * Mathf.Cos(angle),
                center.Y + shadowRy * yScale * Mathf.Sin(angle));
        }
        DrawColoredPolygon(shadowPoints, new Color(0f, 0f, 0f, 0.12f));
    }
}