#!/usr/bin/env python3
"""Batch apply all PLAY-UNBLOCK-1 patches and commit."""
import subprocess, os, re

cs = "/home/fictive/runewake/client/scripts/DuelScene.cs"
hs = "/home/fictive/runewake/client/scripts/HandCard.cs"
ap = "/home/fictive/runewake/client/scripts/ArtifactCardPlate.cs"
ls = "/home/fictive/runewake/client/scripts/LaneSlot.cs"
rs = "/home/fictive/runewake/client/scripts/RulesSlab.cs"

def patch_file(path, old, new):
    """Use sed for a simple replacement."""
    # Escape for sed
    old_esc = old.replace('"', '\\"').replace('\n', '\\n').replace('/', '\\/')
    new_esc = new.replace('"', '\\"').replace('\n', '\\n').replace('/', '\\/')
    # Can use Python instead
    with open(path) as f:
        content = f.read()
    if old not in content:
        print(f"FAIL: pattern not found in {path}")
        return False
    content = content.replace(old, new)
    with open(path, 'w') as f:
        f.write(content)
    print(f"PATCHED: {path}")
    return True

# Read all files
files = {}
for p in [hs, ls, cs, ap, rs]:
    with open(p) as f:
        files[p] = f.read()

# ═══ A1: HandCard selection ──
hs_c = files[hs]

# Select branch
hs_c = hs_c.replace(
    'AddThemeStyleboxOverride("panel", _selectedStyle);\n            ZIndex = 10;\n            var tween = CreateTween();\n            tween.TweenProperty(this, "position:y", -20f, 0.12f)\n                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);\n            tween.Parallel();\n            tween.TweenProperty(this, "modulate", new Color(1.15f, 1.1f, 1.0f, 1), 0.12f);',
    'AddThemeStyleboxOverride("panel", _selectedStyle);\n            ZIndex = 10;\n            var tween = CreateTween();\n            float lift = 24f * (GetViewportRect().Size.Y / 1080f);\n            tween.TweenProperty(this, "position:y", -lift, 0.12f)\n                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);\n            tween.Parallel();\n            tween.TweenProperty(this, "scale", new Vector2(1.08f, 1.08f), 0.12f);\n            tween.Parallel();\n            tween.TweenProperty(this, "modulate", new Color(1.15f, 1.1f, 1.0f, 1), 0.12f);'
)

# Deselect branch
hs_c = hs_c.replace(
    'AddThemeStyleboxOverride("panel", cardStyle);\n            var tween = CreateTween();\n            tween.TweenProperty(this, "position:y", 0f, 0.1f)\n                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);\n            tween.Parallel();\n            tween.TweenProperty(this, "modulate", Colors.White, 0.1f);\n            tween.TweenCallback(Callable.From(() => ZIndex = 1));',
    'AddThemeStyleboxOverride("panel", cardStyle);\n            var tween = CreateTween();\n            tween.TweenProperty(this, "position:y", 0f, 0.1f)\n                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);\n            tween.Parallel();\n            tween.TweenProperty(this, "scale", Vector2.One, 0.1f);\n            tween.Parallel();\n            tween.TweenProperty(this, "modulate", Colors.White, 0.1f);\n            tween.TweenCallback(Callable.From(() => ZIndex = 1));'
)

# A3: Drag preview
hs_c = hs_c.replace(
    'public override Variant _GetDragData(Vector2 atPosition)\n    {\n        _dragStarted = true;\n        var preview = new Label();\n        preview.Text = CardName;\n        preview.Size = new Vector2(80, 24);\n        preview.Modulate = new Color(1, 1, 1, 0.7f);\n        SetDragPreview(preview);',
    'public override Variant _GetDragData(Vector2 atPosition)\n    {\n        _dragStarted = true;\n        var preview = new TextureRect();\n        preview.Texture = _artRect.Texture;\n        preview.Size = new Vector2(Size.X * 0.9f, Size.Y * 0.9f);\n        preview.Modulate = new Color(1, 1, 1, 0.85f);\n        preview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;\n        SetDragPreview(preview);'
)
files[hs] = hs_c

# ═══ A2: LaneSlot gold pulse ──
ls_c = files[ls]
ls_c = ls_c.replace(
    'public void Highlight()\n    {\n        _faceLabel.Visible = false;\n        Modulate = new Color(1, 1, 0.8f, 1);\n    }',
    'public void Highlight()\n    {\n        _faceLabel.Visible = false;\n        Modulate = new Color(1, 1, 0.8f, 1);\n    }\n\n    public void HighlightGoldPulse()\n    {\n        _faceLabel.Visible = false;\n        var tween = CreateTween().SetLoops();\n        tween.TweenProperty(this, "modulate", new Color(1f, 0.85f, 0.3f, 1), 0.6f)\n            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);\n        tween.TweenProperty(this, "modulate", new Color(1, 1, 0.8f, 1), 0.6f)\n            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);\n    }'
)
files[ls] = ls_c

# ═══ DuelScene: UpdatePlayHighlights → HighlightGoldPulse ──
cs_c = files[cs]
cs_c = cs_c.replace(
    '                        slot.Highlight();',
    '                        slot.HighlightGoldPulse();'
)
files[cs] = cs_c

# ═══ B1: Remove ARTIFACT tag from ARTF ──
ap_c = files[ap]

# Remove lazy init marker change
ap_c = ap_c.replace(
    'if (_artifactTag == null)',
    'if (_nameBandBg == null)'
)

# Remove _artifactTag creation
old_tag_block = """            // ── Artifact type tag (top of card, inside root-bound rim) ──
            // TASK-ARTIFACT-TRAY-1: empty tag is a deliberate edge-outline — no placeholder word
            _artifactTag = new Label
            {
                Text = "",
                MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _artifactTag.AddThemeColorOverride("font_color", ArtifactTagColor);
            _artifactTag.AddThemeConstantOverride("outline_size", 1);
            _artifactTag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.6f));
            AddChild(_artifactTag);

"""
ap_c = ap_c.replace(old_tag_block, "")

# Remove _artifactTag field
ap_c = ap_c.replace(
    '    private Label? _artifactTag;\n    private Label? _cardName;',
    '    private Label? _cardName;'
)

# Remove _artifactTag layout lines
ap_c = ap_c.replace(
    "        _artifactTag.Position = new Vector2(bandPx, 0);",
    ""
)
ap_c = ap_c.replace(
    '_artifactTag.Size = new Vector2(cardWidth - bandPx * 2, tagH);',
    ''
)
ap_c = ap_c.replace(
    'int tagFontSize = Mathf.Max(7, Mathf.RoundToInt(tagH * 0.60f));',
    ''
)
ap_c = ap_c.replace(
    '_artifactTag.AddThemeFontSizeOverride("font_size", tagFontSize);',
    ''
)

# Remove _chargeRailBg creation
old_rail_block = """            // ── Charge rail background ──
            _chargeRailBg = new ColorRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                Color = FrameStatRail
            };
            AddChild(_chargeRailBg);

"""
ap_c = ap_c.replace(old_rail_block, "")

# Remove _chargeRailBg from layout
ap_c = ap_c.replace(
    "        _chargeRailBg.Position = new Vector2(0, nameBandH);\n        _chargeRailBg.Size = new Vector2(cardWidth, railH);\n",
    ""
)
ap_c = ap_c.replace(
    '        _chargeRailBg.Position = new Vector2(0, cardHeight);',
    ''
)
ap_c = ap_c.replace(
    '        _chargeRailBg.Size = new Vector2(cardWidth, 0);',
    ''
)

# Move charge display into name band
ap_c = ap_c.replace(
    "        float pipY = nameBandH + (railH - pipH) / 2f;\n        _chargeDisplay.Position = new Vector2(bandPx + 4, pipY);\n        _chargeDisplay.Size = new Vector2(cardWidth - bandPx * 2 - 8, pipH);\n        int chargeFontSize = Mathf.Max(8, Mathf.RoundToInt(pipH * 0.7f));\n        _chargeDisplay.AddThemeFontSizeOverride(\"font_size\", chargeFontSize);",
    "        float pipH = 14f;\n        float pipY = bandY + (nameBandH - pipH) / 2f;\n        _chargeDisplay.Position = new Vector2(cardWidth - 60f - 12f, pipY);\n        _chargeDisplay.Size = new Vector2(60f, pipH);\n        int chargeFontSize = Mathf.Max(8, Mathf.RoundToInt(pipH * 0.7f));\n        _chargeDisplay.AddThemeFontSizeOverride(\"font_size\", chargeFontSize);"
)

# Remove separate rail fraction
ap_c = ap_c.replace(
    'private const float ChargeRailFraction = 0.12f;',
    'private const float ChargeRailFraction = 0f; // B2: removed, pips in name band'
)
ap_c = ap_c.replace(
    'float railH = cardHeight * ChargeRailFraction;',
    'float railH = 0f; // B2: removed'
)
ap_c = ap_c.replace(
    'float tagH = cardHeight * TagHeightFraction;',
    'float tagH = 0f; // B1: removed'
)

files[ap] = ap_c

# ═══ C1: RulesSlab height ──
rs_c = files[rs]
rs_c = rs_c.replace(
    "// C2: height fits content, clamped\n        float contentH = _vbox.GetCombinedMinimumSize().Y;\n        float minH = 260f * (viewportSize.Y / RefVh);\n        float actualH = Mathf.Clamp(ScalePx(InnerPadTop) + contentH + ScalePx(InnerPadBottom), minH, slabH);\n        Size = new Vector2(slabW, actualH);\n        CustomMinimumSize = new Vector2(slabW, actualH);",
    "// C1: height fits content, measured per-line\n        float H(Label l) => l.Visible ? l.GetLineCount() * l.GetLineHeight() : 0f;\n        float ruleH = _rulesLabel.Visible ? _rulesLabel.GetLineHeight() * 0.5f : 0f;\n        float kwGap = _keywordsLabel.Visible ? 4f : 0f;\n        float flavorRuleH = _flavorLabel.Visible ? _rulesLabel.GetLineHeight() * 0.5f : 0f;\n        float flavorGap = _flavorLabel.Visible ? 4f : 0f;\n        float contentH = H(_nameLabel) + ruleH + H(_rulesLabel) + kwGap + H(_keywordsLabel) + flavorRuleH + flavorGap + H(_flavorLabel);\n        float minH = 160f * (viewportSize.Y / RefVh);\n        float actualH = Mathf.Clamp(ScalePx(InnerPadTop) + contentH + ScalePx(InnerPadBottom), minH, slabH);\n        Size = new Vector2(slabW, actualH);\n        CustomMinimumSize = new Vector2(slabW, actualH);"
)
files[rs] = rs_c

# ═══ Write all files ──
for path, content in files.items():
    with open(path, 'w') as f:
        f.write(content)
    print(f"WRITTEN: {path} ({len(content)} chars)")

# ═══ Build ──
print("\n--- BUILD ---")
r = subprocess.run(
    "cd /home/fictive/runewake && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | tail -5",
    shell=True, capture_output=True, text=True, timeout=120
)
print(r.stdout[-500:])
if "0 Error(s)" in r.stdout:
    r2 = subprocess.run(
        "cd /home/fictive/runewake && git add -A && git commit -m 'TASK-PLAY-UNBLOCK-1 A1-A3 B1-B2 C1: selection, gold pulse, drag, relic, plaque' && git push origin main 2>&1 | tail -3",
        shell=True, capture_output=True, text=True, timeout=60
    )
    print(r2.stdout)
else:
    print("BUILD FAILED")
    # Print errors specifically
    r3 = subprocess.run(
        "cd /home/fictive/runewake && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | grep -i 'error CS'",
        shell=True, capture_output=True, text=True, timeout=120
    )
    print(r3.stdout[:2000])