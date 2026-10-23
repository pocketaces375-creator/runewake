#!/usr/bin/env bash
say(){ ~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "$1"; }
P=~/fable-drop-3-hundred-cards.patch
[ -f "$P" ] || { say "STOP: $P is missing"; exit 1; }
cd /home/fictive/runewake && git fetch -q origin || { say "STOP: git fetch failed"; exit 1; }
git worktree remove --force ~/rw057 2>/dev/null; git worktree prune
git worktree add -q -B fable-drop-3 ~/rw057 origin/main && cd ~/rw057 || { say "STOP: worktree failed"; exit 1; }
grep -q class_drop_3 content/cards/ember.json && { say "DROP-3 is already on main — skipping to art"; cd /home/fictive/runewake && bash tools/run_art_drop3.sh; exit 0; }
git apply --index "$P" || { say "STOP: DROP-3 patch did not apply on $(git rev-parse --short HEAD)"; exit 1; }
n=$(git diff --cached --name-only | wc -l); [ "$n" = 124 ] || { say "STOP: DROP-3 staged $n files, expected 124"; exit 1; }
git commit -qm "FABLE-DROP-3: 100 cards on the Forge engine (when you summon another creature, cards that charge artifacts), Null Sigil/Saboteur cap, clearer text, baked faces" || { say "STOP: commit failed"; exit 1; }
dotnet test tests/Runewake.Tests.csproj -c Debug > ~/rw057_tests.log 2>&1
grep -qE "^Passed!" ~/rw057_tests.log || { say "STOP: tests red (nothing pushed): $(grep -E "^\s*Failed |^Failed!|error CS" ~/rw057_tests.log | head -6 | tr "\n" " ")"; exit 1; }
git push -q origin HEAD:main || { say "STOP: push failed"; exit 1; }
C=$(git rev-parse HEAD)
cd /home/fictive/runewake && git pull -q --ff-only origin main && git merge-base --is-ancestor $C HEAD || { say "STOP: pushed ${C:0:8} but the main repo could not pull it"; exit 1; }
tools/ship_apk.sh --release --note "DROP-3: 100 new cards (dark art until painted) + the save fixes" > ~/rw057_ship.log 2>&1
say "DROP-3 landed ${C:0:8} — ship: $(tail -4 ~/rw057_ship.log | tr "\n" " ")"
bash tools/run_art_drop3.sh