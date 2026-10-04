#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
NEW=$(python3 -c "
import json,glob
ids = sorted(c['id'] for f in glob.glob('content/cards/*.json') for c in json.load(open(f)) if c.get('set')=='class_drop_1' and c['type']!='TOKEN')
print(' '.join(ids))
")
python3 tools/art_screen.py batch $NEW --max-paintings 70 > ~/art_drop1.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "Art for the 50 new cards finished: $(tail -5 ~/art_drop1.log | tr "\n" " ") — ask Tcgbot for the preview sheet photo"