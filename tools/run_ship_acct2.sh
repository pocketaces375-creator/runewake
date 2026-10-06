#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
bash tools/ship_apk.sh --release > ~/ship_accounts2.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "Accounts fix APK: $(grep -E "SHIPPED|NOT SHIPPING|https://" ~/ship_accounts2.log | tail -4 | tr "\n" " ")"