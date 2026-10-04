#!/usr/bin/env bash
cd /home/fictive/runewake || exit 1
LAST_BATCH=$(ls -t ~/runewake_art_archive/batches/ 2>/dev/null | head -1)
if [ -n "$LAST_BATCH" ] && [ -f ~/runewake_art_archive/batches/"$LAST_BATCH" ]; then
  rm -f ~/runewake_art_archive/batches/"$LAST_BATCH"
fi
NEW=$(python3 -c "import json,glob;print(' '.join(sorted(c['id'] for f in glob.glob('content/cards/*.json') for c in json.load(open(f)) if c.get('set')=='class_drop_1' and c['type']!='TOKEN')))")
python3 tools/art_screen.py batch $NEW --max-paintings 70 > ~/art_drop1.log 2>&1
~/.local/bin/hermes -p tcgbot send --to telegram:-5481648844 "Art for the 50 new cards finished: $(tail -5 ~/art_drop1.log | tr "\n" " ") — ask Tcgbot for the preview sheet photo"