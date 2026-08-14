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
    private const int Segments = 36;
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
            peakAlpha: AtmosphereEmberAlpha,
            rings: 6);

        // ════════════════════════════════════════
        // 2. Cool moon glow — upper-right corner
        // ════════════════════════════════════════
        DrawRadialGlow(
            center: new Vector2(vw * AtmosphereMoonCenterX, vh * AtmosphereMoonCenterY),
            maxRadius: diag * AtmosphereMoonRadius,
            baseColor: AtmosphereMoonGlow,
            peakAlpha: AtmosphereMoonAlpha,
            rings: 6);

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
        // 4. Vignette — darkened edges via 4 gradient strips
        // ════════════════════════════════════════
        float soft = AtmosphereVignetteSoftness;
        float vignetteStep = AtmosphereVignetteAlpha / 5f;

        // Top vignette
        for (int i = 0; i < 5; i++)
        {
            float t = 1f - (float)i / 5f;
            float stripH = vh * soft * (0.04f + 0.02f * t);
            float stripY = vh * soft * i * 0.04f;
            float alpha = vignetteStep * (5 - i);
            DrawRect(new Rect2(0, stripY, vw, stripH),
                new Color(AtmosphereVignetteColor.R, AtmosphereVignetteColor.G, AtmosphereVignetteColor.B, alpha));
        }

        // Bottom vignette
        for (int i = 0; i < 5; i++)
        {
            float t = 1f - (float)i / 5f;
            float stripH = vh * soft * (0.04f + 0.02f * t);
            float stripY = vh - vh * soft * i * 0.04f - stripH;
            float alpha = vignetteStep * (5 - i);
            DrawRect(new Rect2(0, stripY, vw, stripH),
                new Color(AtmosphereVignetteColor.R, AtmosphereVignetteColor.G, AtmosphereVignetteColor.B, alpha));
        }

        // Left vignette
        for (int i = 0; i < 5; i++)
        {
            float t = 1f - (float)i / 5f;
            float stripW = vw * soft * (0.04f + 0.02f * t);
            float stripX = vw * soft * i * 0.04f;
            float alpha = vignetteStep * (5 - i);
            DrawRect(new Rect2(stripX, 0, stripW, vh),
                new Color(AtmosphereVignetteColor.R, AtmosphereVignetteColor.G, AtmosphereVignetteColor.B, alpha));
        }

        // Right vignette
        for (int i = 0; i < 5; i++)
        {
            float t = 1f - (float)i / 5f;
            float stripW = vw * soft * (0.04f + 0.02f * t);
            float stripX = vw - vw * soft * i * 0.04f - stripW;
            float alpha = vignetteStep * (5 - i);
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
            var circlePoints = new Vector2[Segments];
            for (int s = 0; s < Segments; s++)
            {
                float angle = Mathf.Tau * s / Segments;
                circlePoints[s] = new Vector2(
                    px + radius * Mathf.Cos(angle),
                    py + radius * Mathf.Sin(angle));
            }
            DrawColoredPolygon(circlePoints, new Color(AtmosphereDustMoteColor.R, AtmosphereDustMoteColor.G, AtmosphereDustMoteColor.B, alpha));
        }
    }

    /// <summary>
    /// Draw a radial glow as concentric filled polygons fading outward.
    /// </summary>
    private void DrawRadialGlow(Vector2 center, float maxRadius, Color baseColor, float peakAlpha, int rings)
    {
        for (int ring = 0; ring < rings; ring++)
        {
            float t = (float)ring / rings;
            float radius = maxRadius * (1f - t * 0.85f); // inner = full, outer = 15% radius
            float alpha = peakAlpha * (1f - t * 0.8f);

            if (alpha <= 0f) continue;

            var points = new Vector2[Segments];
            for (int i = 0; i < Segments; i++)
            {
                float angle = Mathf.Tau * i / Segments;
                points[i] = new Vector2(
                    center.X + radius * Mathf.Cos(angle),
                    center.Y + radius * Mathf.Sin(angle));
            }

            DrawColoredPolygon(points, new Color(baseColor.R, baseColor.G, baseColor.B, alpha));
        }
    }
}