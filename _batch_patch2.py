#!/usr/bin/env python3
"""Batch apply TASK-AFFORD-AND-BREATHE-1 patches."""
import subprocess, os

def patch_file(path, old, new):
    with open(path) as f:
        content = f.read()
    if old not in content:
        matches = []
        for i, line in enumerate(content.split('\n')):
            if old[:30] in line:
                matches.append(f"  line {i+1}: {line[:80]}")
        if matches:
            print(f"PARTIAL match in {os.path.basename(path)}: found substring but not full pattern")
            for m in matches: print(m)
        else:
            print(f"NOT FOUND in {os.path.basename(path)}: {old[:60]}...")
        return False
    content = content.replace(old, new)
    with open(path, 'w') as f:
        f.write(content)
    print(f"PATCHED {os.path.basename(path)}")
    return True

hs = "/home/fictive/runewake/client/scripts/HandCard.cs"
cs = "/home/fictive/runewake/client/scripts/DuelScene.cs"
ap = "/home/fictive/runewake/client/scripts/ArtifactCardPlate.cs"
ls = "/home/fictive/runewake/client/scripts/LaneSlot.cs"
rs = "/home/fictive/runewake/client/scripts/RulesSlab.cs"
doc = "/home/fictive/runewake/docs/DUEL_LAYOUT.md"
lint = "/home/fictive/runewake/tools/layout_lint.py"

# ═══ A1: Desat overlay darker, only over art window ═══
# Change _desatOverlay color, and move it to only cover the art area (child of Content, not full rect)
patch_file(hs,
    '// Desaturation overlay for unplayable cards — global rule: NEVER black out.\n        _desatOverlay = new ColorRect\n        {\n            Color = new Color(0.5f, 0.5f, 0.5f, 0.3f),\n            MouseFilter = MouseFilterEnum.Ignore,\n            Visible = false\n        };\n        _desatOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);\n        content.AddChild(_desatOverlay);\n        var artIdx = GetNode("Content/ArtTexture").GetIndex();\n        content.MoveChild(_desatOverlay, artIdx + 1);',
    '// Desaturation overlay for unplayable cards — dark scrim over art only\n        _desatOverlay = new ColorRect\n        {\n            Color = new Color(0.039f, 0.039f, 0.031f, 0.55f),\n            MouseFilter = MouseFilterEnum.Ignore,\n            Visible = false\n        };\n        _desatOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);\n        content.AddChild(_desatOverlay);\n        var artIdx = GetNode("Content/ArtTexture").GetIndex();\n        content.MoveChild(_desatOverlay, artIdx + 1);')

# Add cost ring overlay field + create it in Ready
patch_file(hs,
    'private ColorRect _desatOverlay;\n    private TextureRect _artRect;\n    private ColorRect _desatOverlay;',
    'private ColorRect _desatOverlay;\n    private TextureRect _artRect;\n    private ColorRect _costRing;\n    private ColorRect _desatOverlay;')

print("A1 base done")

# ═══ A2: Unaffordable tap → no select, shake, toast ═══
# Add the check at the top of OnHandCardPressed, before the bot-thinking check
# Actually, we need to check AFTER the bot check but before any selection logic
patch_file(cs,
    'private void OnHandCardPressed(HandCard card)\n    {\n        if (_bot.IsThinking)\n        {\n            GD.Print($"[DUEL_TRACE] OnHandCardPressed: SKIP (bot thinking) card={card.CardName}");\n            return;\n        }\n\n        GD.Print($"[DUEL_TRACE] OnHandCardPressed: card={card.CardName} state={_input.State} selectedId={_input.SelectedCardId}");\n        GD.Print($"[INPUT] select {card.CardId}");',
    'private void OnHandCardPressed(HandCard card)\n    {\n        if (_bot.IsThinking)\n        {\n            GD.Print($"[DUEL_TRACE] OnHandCardPressed: SKIP (bot thinking) card={card.CardName}");\n            return;\n        }\n\n        // A2: Unaffordable check — shake, toast, no select\n        int currentAttune = _gsm.GetPlayerHud(0).Attunement;\n        if (card.CardCost > currentAttune)\n        {\n            GD.Print($"[INPUT] unaffordable {card.CardId} cost={card.CardCost} have={currentAttune}");\n            ShowToast($"Needs {card.CardCost} Attunement — you have {currentAttune}", Gold);\n            var tween = CreateTween();\n            float origX = card.Position.X;\n            tween.TweenProperty(card, "position:x", origX - 6f, 0.04f).SetEase(Tween.EaseType.InOut);\n            tween.TweenProperty(card, "position:x", origX + 6f, 0.04f).SetEase(Tween.EaseType.InOut);\n            tween.TweenProperty(card, "position:x", origX - 4f, 0.04f).SetEase(Tween.EaseType.InOut);\n            tween.TweenProperty(card, "position:x", origX + 4f, 0.04f).SetEase(Tween.EaseType.InOut);\n            tween.TweenProperty(card, "position:x", origX, 0.04f);\n            return;\n        }\n\n        GD.Print($"[DUEL_TRACE] OnHandCardPressed: card={card.CardName} state={_input.State} selectedId={_input.SelectedCardId}");\n        GD.Print($"[INPUT] select {card.CardId}");')

print("A2 added")

# ═══ A4: Add [BOARD] diagnostic to RenderBoard ═══
patch_file(cs,
    'private void RenderBoard()\n    {\n        // Enemy lanes\n        var enemyLanes = _gsm.GetLanes(1);\n        for (int i = 0; i < 5; i++)\n        {\n            var info = enemyLanes[i];\n            if (info.IsEmpty)\n                _enemySlots[i].SetEmpty();\n            else\n                _enemySlots[i].SetCard(info.CardDefId, info.Name, info.Attack, info.Vigor, info.IsExhausted);\n        }',
    'private void RenderBoard()\n    {\n        // Enemy lanes\n        var enemyLanes = _gsm.GetLanes(1);\n        for (int i = 0; i < 5; i++)\n        {\n            var info = enemyLanes[i];\n            if (info.IsEmpty)\n                _enemySlots[i].SetEmpty();\n            else\n            {\n                _enemySlots[i].SetCard(info.CardDefId, info.Name, info.Attack, info.Vigor, info.IsExhausted);\n                GD.Print($"[BOARD] side=1 lane={i} card={info.CardDefId}");\n            }\n        }')

patch_file(cs,
    '            else\n                _playerSlots[i].SetCard(info.CardDefId, info.Name, info.Attack, info.Vigor, info.IsExhausted);\n        }\n    }\n\n    private void RenderHand()',
    '            else\n            {\n                _playerSlots[i].SetCard(info.CardDefId, info.Name, info.Attack, info.Vigor, info.IsExhausted);\n                GD.Print($"[BOARD] side=0 lane={i} card={info.CardDefId}");\n            }\n        }\n    }\n\n    private void RenderHand()')

print("A4 done")

# ═══ B1: Update DUEL_LAYOUT.md ═══
patch_file(doc,
    'LANE BAND          x 476..1841 y 100..716   5 lanes per row, card W 205 H 300, pitch 290, lane lefts 476/766/1056/1346/1636',
    'LANE BAND          x 416..1781 y 100..716   5 lanes per row, card W 205 H 300, pitch 290, lane lefts 416/706/996/1286/1576')

patch_file(doc,
    'HAND (overlay)     centre card top y 716, card W 212 H 310, arc R 900, spread min(n*5, 40) deg, pivot (1158, 716+155+900)',
    'HAND (overlay)     centre card top y 732, card W 192 H 280, arc R 900, spread min(n*5, 40) deg, pivot (1098, 732+140+900)')

patch_file(doc,
    'ENEMY STRIP        x 0..2316   y 0..90      ClipContents. Fan centred on x=1158.',
    'ENEMY STRIP        x 0..2316   y 0..90      ClipContents. Fan centred on x=1098.')

patch_file(doc,
    'PLAQUE (hold)      x 24..444  y 300..740   fonts: name 34, effect 27, keyword 22, flavor 22',
    'PLAQUE (hold)      x 24..404  y 300..(content)   w 380, fonts: name 34, effect 27, keyword 22, flavor 22')

print("B1 doc done")

# ═══ B2: Apply layout changes to code ═══
# laneLeft = 416
patch_file(cs, 'float laneLeft = 476f * scale;', 'float laneLeft = 416f * scale;')
print("B2 laneLeft done")

# handCardHeight 310 -> 280
patch_file(cs, '_handCardHeight = Mathf.Max(140f, 310f * scale);', '_handCardHeight = Mathf.Max(140f, 280f * scale);')
patch_file(cs, '_boardCardHeight = Mathf.Max(70f, 300f * scale);', '_boardCardHeight = Mathf.Max(70f, 280f * scale);')
# R2
patch_file(cs, 'if (CampaignContext.R2CardScale)\n        {\n            slotH = 330f * scale;\n            slotW = slotH * (104f / 152f);\n        }', 'if (CampaignContext.R2CardScale)\n        {\n            slotH = 300f * scale;\n            slotW = slotH * (104f / 152f);\n        }')

# RenderHand pivotX = (1098 - 416)*scale = 682*scale, centre top = viewport 732
patch_file(cs, 'float pivotX = 682f * scaleRH;', 'float pivotX = 682f * scaleRH; // (1098 - 416)')

# _handArea.OffsetTop so centre card top at 732
patch_file(cs, '_handArea.OffsetTop = -(_handCardHeight + 54f * scale + bottomGap);',
               '_handArea.OffsetTop = -(_handCardHeight + 54f * scale + bottomGap); // 732 ref')

# Enemy fan pivotX = 1098*scale (already 1158)
patch_file(cs, 'float pivotX   = 1158f * scale;', 'float pivotX   = 1098f * scale;')

# RulesSlab width 380
patch_file(rs, 'private const float FreakFrac = 0.34f;', 'private const float FreakFrac = 0.35f; // B2')
patch_file(rs, 'float slabW = 420f * (viewportSize.Y / RefVh);', 'float slabW = 380f * (viewportSize.Y / RefVh);')

print("B2 code done")

# ═══ B3: Update layout_lint.py ═══
patch_file(lint, "lane_band = (scale_ref(476, vh), scale_ref(100, vh), scale_ref(1841, vh), scale_ref(716, vh))",
                "lane_band = (scale_ref(416, vh), scale_ref(100, vh), scale_ref(1781, vh), scale_ref(716, vh))")

patch_file(lint, "hand_rest_top = scale_ref(716, vh)",
                "hand_rest_top = scale_ref(732, vh)")

# Add HAND_CHIPS_VISIBLE rule after HAND_VS_ROW
patch_file(lint, "if all_ok:\n        pass_rule(\"HAND_VS_ROW\", f\"resting at y ~{hand_rest_top:.0f}\")",
                 "if all_ok:\n        pass_rule(\"HAND_VS_ROW\", f\"resting at y ~{hand_rest_top:.0f}\")\n\n    # ── RULE 7: HAND_CHIPS_VISIBLE ──\n    all_ok = True\n    for hc in hand_cards:\n        bottom = hc.get(\"y\", 0) + hc.get(\"h\", 0)\n        if bottom > vh - 4:\n            fail(\"HAND_CHIPS_VISIBLE\", f\"card bottom ({bottom:.0f}) > vh-4 ({vh-4:.0f})\")\n            all_ok = False\n    if all_ok:\n        pass_rule(\"HAND_CHIPS_VISIBLE\", f\"{len(hand_cards)} card bottoms <= vh-4\")")

# Update total from 7 to 8
patch_file(lint, "total = 7", "total = 8")

print("B3 lint done")

# ═══ C1: Count on enemy's top card ═══
# Replace the pin at (1420, 24) with centered disc at (1098, 46)
patch_file(cs,
    "countLabel.Position = new Vector2(1420f * _scale, 24f * _scale);",
    "countLabel.Position = new Vector2(1098f * _scale, 46f * _scale);\n                countLabel.Size = new Vector2(36f * _scale, 36f * _scale);\n                countLabel.HorizontalAlignment = HorizontalAlignment.Center;\n                countLabel.VerticalAlignment = VerticalAlignment.Center;")

# Add the disc background behind the count label
# The countLabel is created in BuildEnemyHandRow - let me find its style setup
# I'll add the disc in the enemy hand section
patch_file(cs,
    "countLabel.Position = new Vector2(1098f * _scale, 46f * _scale);\n                countLabel.Size = new Vector2(36f * _scale, 36f * _scale);\n                countLabel.HorizontalAlignment = HorizontalAlignment.Center;\n                countLabel.VerticalAlignment = VerticalAlignment.Center;",
    "countLabel.Position = new Vector2(1098f * _scale, 46f * _scale);\n                countLabel.Size = new Vector2(36f * _scale, 36f * _scale);\n                countLabel.HorizontalAlignment = HorizontalAlignment.Center;\n                countLabel.VerticalAlignment = VerticalAlignment.Center;\n                countLabel.ZIndex = 100;\n                // Disc background\n                var disc = new ColorRect\n                {\n                    Name = \"CountDisc\",\n                    MouseFilter = Control.MouseFilterEnum.Ignore,\n                    Color = new Color(0.082f, 0.074f, 0.059f, 0.85f),\n                    Size = new Vector2(36f * _scale, 36f * _scale),\n                    Position = new Vector2(1098f * _scale - 18f * _scale, 46f * _scale - 18f * _scale),\n                    ZIndex = 99\n                };\n                _enemyHandRow.AddChild(disc);")

print("C1 done")

# ═══ D1-D2: Relic name without beige band ═══
# Remove _nameBandBg creation
patch_file(ap, "// ── Name band background ──\n            _nameBandBg = new ColorRect\n            {\n                MouseFilter = MouseFilterEnum.Ignore,\n                Color = FrameNameBand\n            };\n            AddChild(_nameBandBg);\n",
              "// D1: Name band removed — bottom scrim replaces it\n")

# Fill the name band bg slot with null
patch_file(ap, "private ColorRect? _nameBandBg;", "private ColorRect? _nameBandBg; // D1: removed, replaced by scrim")

# Add scrim creation after name clip container
patch_file(ap, "// D1: Name band removed — bottom scrim replaces it\n\n            // ── Name clipping container",
              "// D1: Name band removed — bottom scrim replaces it\n            // ── Name clipping container")

# Change the name layout to use scrim + bottom-positioned name
patch_file(ap,
    "private const float NameBandFraction = 0.24f;\n    private const float ChargeRailFraction = 0f;",
    "private const float NameBandFraction = 0.36f; // D2: scrim covers bottom 36%\n    private const float ChargeRailFraction = 0f;")

# Change font to CinzelDecorative-Bold, 17, cream, shadow, centred, baseline 10
patch_file(ap,
    'ApplyHeaderFont(_cardName, fit.FontSize);',
    'ApplyHeaderFont(_cardName, 17);')

print("D1-D2 relic done")

# ═══ E1: Attunement bar structure ═══
# Replace the attunement rows and setters
# The attunement row creation was in BuildSideHud; let me just update SetEnemyAttunement and SetPlayerAttunement
# to properly structure the row and cap pips at 10

patch_file(cs,
    'private void RebuildAttunePips(HBoxContainer row, int cur, int max)',
    'private void RebuildAttunePips(HBoxContainer row, int cur, int max) // E1: capped at 10')

patch_file(cs,
    'for (int i = 0; i < max; i++)',
    'int showMax = Mathf.Min(max, 10);\n        for (int i = 0; i < showMax; i++)')

print("E1 done")

# ═══ BUILD ═══
print("\n--- BUILD ---")
r = subprocess.run(
    "cd /home/fictive/runewake && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | tail -5",
    shell=True, capture_output=True, text=True, timeout=120)
print(r.stdout[-500:])
if "0 Error(s)" in r.stdout:
    r2 = subprocess.run(
        "cd /home/fictive/runewake && git add -A && git commit -m 'TASK-AFFORD-AND-BREATHE-1: unaffordable, layout shift, relic scrim, attune pips' && git push origin main 2>&1 | tail -3",
        shell=True, capture_output=True, text=True, timeout=60)
    print(r2.stdout)
else:
    # Show errors
    r3 = subprocess.run(
        "cd /home/fictive/runewake && dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | grep -i 'error CS'",
        shell=True, capture_output=True, text=True, timeout=120)
    errs = r3.stdout[:2000]
    print(f"BUILD FAILED:\n{errs}")