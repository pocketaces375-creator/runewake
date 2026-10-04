#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
python3 tools/art_screen.py batch --set class_drop_2 > ~/art_drop2.log 2>&1
python3 tools/art_screen.py batch --set class_drop_1 > ~/art_drop1e.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "Art for the 100 new cards finished: $(grep -hE 'cards have art|per painting|SHEET' ~/art_drop2.log ~/art_drop1e.log | tr "\n" " ") — ask Tcgbot for the sheet photos"