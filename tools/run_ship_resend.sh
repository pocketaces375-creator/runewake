#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
bash tools/ship_apk.sh --release --note "DROP-3: 100 new cards, 98 with art" > ~/ship_resend.log 2>&1
$HOME/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "APK ready: $(tail -5 ~/ship_resend.log | tr "\n" " ")"