using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-040: writing in a language nobody has read for an age.
///
/// The Codex shows cards you haven't found with their names and rules in this script instead of
/// English. There is no font for it — a font means a real script, and on a phone with no such
/// font installed it would be boxes — so the glyphs are drawn here: each one is two to four
/// strokes from a small alphabet of carved marks (uprights, slants, bars, hooks, arcs, points),
/// joined by a faint baseline the way Aramaic and hieratic hands run. The same text always
/// draws the same way because the strokes come from a hash of it, so a card's hidden name is
/// consistent everywhere it appears.
/// </summary>
public partial class AncientScript : Control
{
    /// <summary>What the glyphs stand for. Only its hash matters.</summary>
    public string Seed { get; set; } = "";
    /// <summary>How many glyphs; 0 = as many as fit.</summary>
    public int Glyphs { get; set; }
    public Color Ink { get; set; } = new(0.86f, 0.78f, 0.60f);
    public bool Centered { get; set; } = true;
    /// <summary>Multi-line: wrap into rows of this height (0 = single line using the whole height).</summary>
    public float LineHeight { get; set; }
    /// <summary>0–1: how much of the last line is written (so a paragraph ends short).</summary>
    public float LastLineFill { get; set; } = 0.6f;

    public AncientScript() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        float lineH = LineHeight > 0 ? LineHeight : Size.Y;
        int lines = LineHeight > 0 ? Mathf.Max(1, Mathf.FloorToInt(Size.Y / LineHeight)) : 1;
        uint h = Hash(Seed);
        for (int line = 0; line < lines; line++)
        {
            float glyphH = lineH * 0.62f;
            float cell = glyphH * 0.80f;
            int fit = Mathf.Max(1, Mathf.FloorToInt(Size.X / cell));
            int n = Glyphs > 0 && lines == 1 ? Mathf.Min(Glyphs, fit) : fit;
            if (lines > 1 && line == lines - 1) n = Mathf.Max(2, Mathf.RoundToInt(fit * LastLineFill));
            float total = n * cell;
            float x0 = Centered && lines == 1 ? (Size.X - total) / 2f : 0f;
            float top = line * lineH + (lineH - glyphH) / 2f;
            float width = Mathf.Max(1.2f, glyphH * 0.085f);
            var faint = new Color(Ink, Ink.A * 0.35f);
            // the running baseline
            DrawLine(new Vector2(x0, top + glyphH * 0.92f), new Vector2(x0 + total - cell * 0.25f, top + glyphH * 0.92f), faint, width * 0.6f);
            for (int i = 0; i < n; i++)
            {
                uint g = Next(ref h) ^ (uint)(line * 977 + i * 131);
                Glyph(new Vector2(x0 + i * cell + cell * 0.12f, top), cell * 0.76f, glyphH, g, width);
            }
        }
    }

    private void Glyph(Vector2 o, float w, float hgt, uint g, float width)
    {
        int strokes = 2 + (int)(g % 3);
        uint r = g;
        for (int s = 0; s < strokes; s++)
        {
            uint k = Next(ref r);
            int kind = (int)(k % 9);
            float t = ((k >> 8) % 100) / 100f;
            var a = Ink;
            switch (kind)
            {
                case 0: // upright
                    DrawLine(o + new Vector2(w * (0.2f + 0.6f * t), hgt * 0.1f), o + new Vector2(w * (0.2f + 0.6f * t), hgt * 0.9f), a, width);
                    break;
                case 1: // slant
                    DrawLine(o + new Vector2(w * 0.15f, hgt * (0.85f - 0.4f * t)), o + new Vector2(w * 0.85f, hgt * (0.15f + 0.4f * t)), a, width);
                    break;
                case 2: // bar
                    DrawLine(o + new Vector2(w * 0.1f, hgt * (0.15f + 0.7f * t)), o + new Vector2(w * 0.9f, hgt * (0.15f + 0.7f * t)), a, width);
                    break;
                case 3: // hook
                    DrawPolyline(new[] { o + new Vector2(w * 0.25f, hgt * 0.15f), o + new Vector2(w * 0.25f, hgt * 0.75f), o + new Vector2(w * 0.7f, hgt * 0.9f) }, a, width);
                    break;
                case 4: // arc
                    DrawArc(o + new Vector2(w * 0.5f, hgt * (0.35f + 0.3f * t)), w * 0.32f, Mathf.Pi * (0.1f + t), Mathf.Pi * (1.1f + t), 10, a, width);
                    break;
                case 5: // point
                    DrawCircle(o + new Vector2(w * (0.3f + 0.4f * t), hgt * (0.2f + 0.6f * (1 - t))), width * 1.1f, a);
                    break;
                case 6: // chevron
                    DrawPolyline(new[] { o + new Vector2(w * 0.15f, hgt * 0.5f), o + new Vector2(w * 0.5f, hgt * (0.15f + 0.3f * t)), o + new Vector2(w * 0.85f, hgt * 0.5f) }, a, width);
                    break;
                case 7: // tail below the line
                    DrawPolyline(new[] { o + new Vector2(w * 0.6f, hgt * 0.5f), o + new Vector2(w * 0.6f, hgt * 0.95f), o + new Vector2(w * 0.35f, hgt * 1.05f) }, a, width);
                    break;
                default: // stem with a foot
                    DrawPolyline(new[] { o + new Vector2(w * 0.45f, hgt * 0.15f), o + new Vector2(w * 0.45f, hgt * 0.9f), o + new Vector2(w * 0.8f, hgt * 0.9f) }, a, width);
                    break;
            }
        }
    }

    private static uint Hash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h == 0 ? 1u : h;
        }
    }

    private static uint Next(ref uint x)
    {
        unchecked { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x; }
    }
}
