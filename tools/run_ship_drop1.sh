#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
bash tools/ship_apk.sh --release --note "FABLE-DROP-1: 50 new class cards + engine fixes (new cards art-less until approved)" > ~/ship.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "APK build finished: $(tail -8 ~/ship.log | tr "\n" " ")"