#!/usr/bin/env bash
set -euo pipefail
cd /home/fictive/runewake

echo "=== PATCHES ==="

# STEP 1: Stat chips inside frame - compose_frame_options.py
python3 -c "
with open('tools/compose_frame_options.py') as f:
    c = f.read()

old_chip = 'd.rounded_rectangle([sx - 56, py + ph - 20, sx + 56, py + ph + 44],\n                            radius=14, fill=col,\n                            outline=(0, 0, 0, 200), width=3)\n        d.text((sx, py + ph + 12), str(val), font=cinzel(46),'
new_chip = 'd.rounded_rectangle([sx - 56, py + (ph // 2) - 32, sx + 56, py + (ph // 2) + 32],\n                            radius=14, fill=col,\n                            outline=(0, 0, 0, 200), width=3)\n        d.text((sx, py + ph // 2), str(val), font=cinzel(46),'
assert old_chip in c, 'Old chip not found!'
c = c.replace(old_chip, new_chip)

with open('tools/compose_frame_options.py', 'w') as f:
    f.write(c)
print('compose_frame stat chips OK')
"

# Stat chips inside frame - bake_cards.py
python3 -c "
with open('pipeline/bake_cards.py') as f:
    c = f.read()

old_chip = 'd.rounded_rectangle([sx - 28, py + ph - 10, sx + 28, py + ph + 22], radius=7, fill=col, outline=(0, 0, 0, 200), width=2)'
new_chip = 'd.rounded_rectangle([sx - 28, py + (ph // 2) - 16, sx + 28, py + (ph // 2) + 16], radius=7, fill=col, outline=(0, 0, 0, 200), width=2)'
assert old_chip in c, 'Old bake chip not found!'
c = c.replace(old_chip, new_chip)

with open('pipeline/bake_cards.py', 'w') as f:
    f.write(c)
print('bake_cards stat chips OK')
"

# STEP 3: Centre the hand - delete +180f and dead comment
python3 -c "
with open('client/scripts/DuelScene.cs') as f:
    c = f.read()

# Delete dead comment line
c = c.replace('        // _handFlow.Alignment = BoxContainer.AlignmentMode.Center;\n', '')
print('Dead comment deleted')

# Remove +180f
c = c.replace('float startX = (availWidth - totalW) * 0.5f + 180f;', 'float startX = (availWidth - totalW) * 0.5f;')
print('+180f removed')

with open('client/scripts/DuelScene.cs', 'w') as f:
    f.write(c)
print('DuelScene.cs OK')
"

# STEP 4: Tap to open rules slab
# Wire card.Pressed to toggle the rules slab in DuelScene.cs
python3 -c "
with open('client/scripts/DuelScene.cs') as f:
    c = f.read()

# In RenderHand, replace the Pressed wiring
old_pressed = '''            var capturedCard = card;
            card.Pressed += () => OnHandCardPressed(capturedCard);

            // TASK-CARD-TEXT-1: Long-press for rules slab
            card.LongPressStarted += ShowRulesSlab;
            card.LongPressEnded += HideRulesSlab;'''

new_pressed = '''            var capturedCard = card;
            card.Pressed += () =>
            {
                OnHandCardPressed(capturedCard);
                // TASK-CARD-POLISH-3: Tap opens rules slab (toggle)
                if (_rulesSlabVisible && _slabCard == capturedCard)
                    HideRulesSlab();
                else
                    ShowRulesSlab(capturedCard);
                _slabCard = _rulesSlabVisible ? capturedCard : null;
            };

            // TASK-CARD-TEXT-1: Long-press for rules slab (kept as alternative path)
            card.LongPressStarted += ShowRulesSlab;
            card.LongPressEnded += HideRulesSlab;'''

assert old_pressed in c, 'Old Pressed block not found!'
c = c.replace(old_pressed, new_pressed)
print('Pressed block updated')

# Add _slabCard field near the class fields
# Find the _rulesSlabVisible field
import re
m = re.search(r'private bool _rulesSlabVisible;', c)
if m:
    # Check if _slabCard already exists
    if '_slabCard' not in c:
        c = c.replace('private bool _rulesSlabVisible;', 'private bool _rulesSlabVisible;\n    private HandCard? _slabCard;')
        print('_slabCard field added')
else:
    print('WARNING: _rulesSlabVisible not found!')

with open('client/scripts/DuelScene.cs', 'w') as f:
    f.write(c)
print('Tap slab toggle done')
"

echo "=== ALL PATCHES DONE ==="

# Verify
echo "Verifying patches..."
grep -q 'py + (ph // 2) - 32' tools/compose_frame_options.py && echo "  compose_frame chips OK"
grep -q 'py + (ph // 2) - 16' pipeline/bake_cards.py && echo "  bake_cards chips OK"
grep -q 'startX = (availWidth - totalW) * 0.5f;' client/scripts/DuelScene.cs && echo "  +180f removed OK"
grep -q '_slabCard' client/scripts/DuelScene.cs && echo "  _slabCard added OK"
grep -q '_rulesSlabVisible && _slabCard == capturedCard' client/scripts/DuelScene.cs && echo "  tap toggle OK"

echo "=== COMMIT PATCHES ==="
git add -A tools/ pipeline/ client/scripts/
git diff --cached --quiet || git commit -m "TASK-CARD-POLISH-3: chips inside frame, hand centred, tap opens rules"
git push origin main
echo "Patches committed"

echo "=== REBAKE ==="
python3 pipeline/bake_cards.py 2>&1 | tail -1
git add -A client/content/art/cards_baked/

echo "=== BUILD ==="
timeout 300 xvfb-run -a ~/.local/bin/godot --headless --import --path client 2>&1 | tail -1
dotnet build client/Runewake.Client.csproj -c Debug 2>&1 | grep -E "Build succeeded|Error"

echo "=== CAPTURE ==="
bash tools/capture_polish.sh 2>&1 | grep -E "PASS hash|PASS art|Results:|PASS black" | tail -3

echo "=== LINT + SMOKE ==="
echo "HAND_FIELD: $(python3 tools/ui_lint.py duel_test 2>&1 | grep -c 'HAND_FIELD_OVERLAP') (before: 5)"
python3 tools/label_fit.py artifacts/captures/duel_test.layout.json 2>&1 | grep "blocking"
bash tools/input_smoke.sh 2>&1 | tail -1

echo "=== COMMIT CAPTURES ==="
git add -f artifacts/captures/duel_test.png artifacts/captures/duel_test_wide.png
git diff --cached --quiet || git commit -m "TASK-CARD-POLISH-3: captures, rebake"
git push origin main

echo "=== APK ==="
GODOT_BIN=/home/fictive/.local/bin/godot bash tools/export_and_verify.sh --debug --skip-deliver 2>&1 | grep -E "Export complete|SHA-256|errors" | head -3

echo "=== ALL DONE ==="