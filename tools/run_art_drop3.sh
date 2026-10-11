#!/usr/bin/env bash
# FABLE-DROP-3: paint the 100 class_drop_3 cards (about 3.5¢ a painting). Never --approve without Trikzos.
cd /home/fictive/runewake || exit 1
python3 tools/art_screen.py batch --set class_drop_3 > ~/art_drop3.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "Art for the 100 drop-3 cards finished: $(grep -hE 'cards have art|per painting|SHEET' ~/art_drop3.log | tr "\n" " ") — ask Tcgbot for the sheet photos"
