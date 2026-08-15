using Godot;
using System;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// TASK-UI3d: Atmosphere overlay — layered lighting, vignette, mist, and dust motes.
/// Rendered as a full-screen Control via _Draw that sits above all gameplay elements.
/// All visual values read from ThemeTokens so art can retune without touching code.
/// Input passes through (MouseFilter.Ignore) — this is purely cosmetic.
/// </summary>
public partial class AtmosphereOverlay : Control
{
    private readonly Random _rng = new(12345); // fixed seed for deterministic captures
    private Vector2[] _dustMotePositions = Array.Empty<Vector2>();
    private float[] _dustMoteRadii = Array.Empty<float>();
    private float[] _dustMoteAlphas = Array.Empty<float>();

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);

        // Generate static dust mote positions (fixed seed → deterministic for captures)
        _dustMotePositions = new Vector2[AtmosphereDustMoteCount];
        _dustMoteRadii = new float[AtmosphereDustMoteCount];
        _dustMoteAlphas = new float[AtmosphereDustMoteCount];

        for (int i = 0; i < AtmosphereDustMoteCount; i++)
        {
            _dustMotePositions[i] = new Vector2(
                (float)_rng.NextDouble(),
                (float)_rng.NextDouble());
            _dustMoteRadii[i] = AtmosphereDustMoteMinRadius +
                (float)_rng.NextDouble() * (AtmosphereDustMoteMaxRadius - AtmosphereDustMoteMinRadius);
            _dustMoteAlphas[i] = (0.3f + (float)_rng.NextDouble() * 0.7f) * AtmosphereDustMoteAlpha;
        }

        GD.Print("[ATMOSPHERE] TASK-UI3d: Atmosphere overlay ready");
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;

        float vw = Size.X;
        float vh = Size.Y;
        float diag = Mathf.Sqrt(vw * vw + vh * vh);

        // ════════════════════════════════════════
        // 1. Warm ember glow — lower-left corner
        // ════════════════════════════════════════
        DrawRadialGlow(
            center: new Vector2(vw * AtmosphereEmberCenterX, vh * AtmosphereEmberCenterY),
            maxRadius: diag * AtmosphereEmberRadius,
            baseColor: AtmosphereEmberGlow,
            peakAlpha: AtmosphereEmberAlpha);

        // ════════════════════════════════════════
        // 2. Cool moon glow — upper-right corner
        // ════════════════════════════════════════
        DrawRadialGlow(
            center: new Vector2(vw * AtmosphereMoonCenterX, vh * AtmosphereMoonCenterY),
            maxRadius: diag * AtmosphereMoonRadius,
            baseColor: AtmosphereMoonGlow,
            peakAlpha: AtmosphereMoonAlpha);

        // ════════════════════════════════════════
        // 3. Mist band — horizontal across mid-field
        // ════════════════════════════════════════
        float mistCenterY = vh * AtmosphereMistCenterY;
        float mistHalfH = vh * AtmosphereMistHeight * 0.5f;
        float mistTop = mistCenterY - mistHalfH;
        float mistBot = mistCenterY + mistHalfH;

        // Draw the mist band as 3 horizontal strips fading top→center→bottom
        var mistColor = new Color(AtmosphereMistColor.R, AtmosphereMistColor.G, AtmosphereMistColor.B, AtmosphereMistAlpha);
        DrawRect(new Rect2(0, mistTop, vw, mistHalfH * 2f), mistColor);

        // Softer edges: thin fade strips above and below
        var mistFadeColor = new Color(AtmosphereMistColor.R, AtmosphereMistColor.G, AtmosphereMistColor.B, AtmosphereMistAlpha * 0.5f);
        float fadeH = mistHalfH * 0.5f;
        DrawRect(new Rect2(0, mistTop - fadeH, vw, fadeH), mistFadeColor);
        DrawRect(new Rect2(0, mistBot, vw, fadeH), mistFadeColor);

        // ════════════════════════════════════════
        // 4. Vignette — subtle darkened edges via 3 soft strips per side (TASK-UI3e)
        // ════════════════════════════════════════
        float soft = AtmosphereVignetteSoftness;
        float totalA = AtmosphereVignetteAlpha;

        // Top vignette — 3 strips inward from top edge
        for (int i = 0; i < 3; i++)
        {
            float t = (float)i / 3f;
            float stripH = vh * soft * (0.03f + 0.02f * (1f - t));
            float stripY = vh * soft * t * 0.05f;
            float alpha = totalA * (1f - t) * 0.5f;
            DrawRect(new Rect2(0, stripY, vw, stripH),
                new Color(AtmosphereVignetteColor.R, AtmosphereVignetteColor.G, AtmosphereVignetteColor.B, alpha));
        }

        // Bottom vignette
        for (int i = 0; i < 3; i++)
        {
            float t = (float)i / 3f;
            float stripH = vh * soft * (0.03f + 0.02f * (1f - t));
            float stripY = vh - vh * soft * t * 0.05f - stripH;
            float alpha = totalA * (1f - t) * 0.5f;
            DrawRect(new Rect2(0, stripY, vw, stripH),
                new Color(AtmosphereVignetteColor.R, AtmosphereVignetteColor.G, AtmosphereVignetteColor.B, alpha));
        }

        // Left vignette
        for (int i = 0; i < 3; i++)
        {
            float t = (float)i / 3f;
            float stripW = vw * soft * (0.03f + 0.02f * (1f - t));
            float stripX = vw * soft * t * 0.05f;
            float alpha = totalA * (1f - t) * 0.5f;
            DrawRect(new Rect2(stripX, 0, stripW, vh),
                new Color(AtmosphereVignetteColor.R, AtmosphereVignetteColor.G, AtmosphereVignetteColor.B, alpha));
        }

        // Right vignette
        for (int i = 0; i < 3; i++)
        {
            float t = (float)i / 3f;
            float stripW = vw * soft * (0.03f + 0.02f * (1f - t));
            float stripX = vw - vw * soft * t * 0.05f - stripW;
            float alpha = totalA * (1f - t) * 0.5f;
            DrawRect(new Rect2(stripX, 0, stripW, vh),
                new Color(AtmosphereVignetteColor.R, AtmosphereVignetteColor.G, AtmosphereVignetteColor.B, alpha));
        }

        // ════════════════════════════════════════
        // 5. Dust motes — small floating particles
        // ════════════════════════════════════════
        for (int i = 0; i < _dustMotePositions.Length; i++)
        {
            float px = _dustMotePositions[i].X * vw;
            float py = _dustMotePositions[i].Y * vh;
            float radius = _dustMoteRadii[i];
            float alpha = _dustMoteAlphas[i];

            // Draw filled circle using polygon approximation
            int segs = Mathf.Max(12, Mathf.RoundToInt(radius * 6f));
            var circlePoints = new Vector2[segs];
            for (int s = 0; s < segs; s++)
            {
                float angle = Mathf.Tau * s / segs;
                circlePoints[s] = new Vector2(
                    px + radius * Mathf.Cos(angle),
                    py + radius * Mathf.Sin(angle));
            }
            DrawColoredPolygon(circlePoints, new Color(AtmosphereDustMoteColor.R, AtmosphereDustMoteColor.G, AtmosphereDustMoteColor.B, alpha));
        }
    }

    /// <summary>
    /// Draw a radial glow as concentric filled polygons fading outward.
    /// Uses 32 rings with uniform alpha steps for a smooth gradient (no banding — TASK-UI3e).
    /// Draws from outer to inner so alpha accumulates naturally toward the center.
    /// </summary>
    private void DrawRadialGlow(Vector2 center, float maxRadius, Color baseColor, float peakAlpha)
    {
        const int smoothRings = 32;
        float alphaStep = peakAlpha / smoothRings;

        for (int ring = 0; ring < smoothRings; ring++)
        {
            float t = (float)ring / smoothRings;
            float radius = maxRadius * (1f - t * 0.90f);

            if (radius <= 1f) continue;

            var points = new Vector2[36];
            for (int i = 0; i < 36; i++)
            {
                float angle = Mathf.Tau * i / 36;
                points[i] = new Vector2(
                    center.X + radius * Mathf.Cos(angle),
                    center.Y + radius * Mathf.Sin(angle));
            }

            DrawColoredPolygon(points, new Color(baseColor.R, baseColor.G, baseColor.B, alphaStep));
        }
    }
}