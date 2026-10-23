#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
bash tools/ship_apk.sh --release > ~/ship_055.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "FABLE-055 APK: $(grep -E "SHIPPED|NOT SHIPPING|https://" ~/ship_055.log | tail -4 | tr "\n" " ")"
git worktree remove --force ~/rw055 2>/dev/null; git worktree prune 2>/dev/null