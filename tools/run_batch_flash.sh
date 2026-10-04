#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
python3 tools/art_screen.py batch > ~/art_batch_flash.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "Flash 3.1 batch done: $(tail -6 ~/art_batch_flash.log | tr "\n" " ")"