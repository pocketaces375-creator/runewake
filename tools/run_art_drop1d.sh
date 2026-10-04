#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
python3 tools/art_screen.py batch --set class_drop_1 > ~/art_drop1d.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "Last new-card art finished: $(tail -6 ~/art_drop1d.log | tr "\n" " ") — ask Tcgbot for the sheet photo"