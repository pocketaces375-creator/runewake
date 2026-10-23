#!/usr/bin/env bash
cd /home/fictive/runewake
git pull -q --rebase --autostash origin main
bash tools/ship_apk.sh --release --note "Ferryman's Toll + class starting zones + epic spells + combat clarity" > ~/ship_now.log 2>&1
$HOME/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "APK ready: $(grep -E 'SHIPPED|https://(gofile|github|catbox)' ~/ship_now.log | head -5 | tr '\n' ' ')"