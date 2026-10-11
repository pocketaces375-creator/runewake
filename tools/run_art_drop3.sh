#!/usr/bin/env bash
# FABLE-DROP-3: paint the 100 class_drop_3 cards (about 3.5¢ a painting). Never --approve without Trikzos.
# FABLE-057: safe to run again at any time — the batch picks up only cards that still have no art, so a pass that
# dies on a network error is simply repeated (up to 4 passes, a minute apart). Out of credit stops it for good.
cd /home/fictive/runewake || exit 1
SEND=~/.local/bin/hermes
: > ~/art_drop3.log
rc=1
for pass in 1 2 3 4; do
  echo "=== pass $pass $(date '+%F %T') ===" >> ~/art_drop3.log
  python3 tools/art_screen.py batch --set class_drop_3 >> ~/art_drop3.log 2>&1
  rc=$?
  [ "$rc" = 0 ] && break
  [ "$rc" = 42 ] && break
  $SEND -p tcgbot send --to telegram:-5481648844 "Drop-3 art: pass $pass stopped (exit $rc) — trying again in a minute" >/dev/null 2>&1
  sleep 60
done
if [ "$rc" = 42 ]; then
  $SEND -p tcgbot send --to telegram:-5481648844 "STOP: Drop-3 art ran out of OpenRouter credit — add credit at openrouter.ai/credits, then run tools/run_art_drop3.sh again (it carries on where it stopped)"
elif [ "$rc" != 0 ]; then
  $SEND -p tcgbot send --to telegram:-5481648844 "STOP: Drop-3 art still failing after 4 passes (exit $rc) — last log lines: $(tail -n 6 ~/art_drop3.log | tr "\n" " ")"
else
  $SEND -p tcgbot send --to telegram:-5481648844 "Art for the 100 drop-3 cards finished: $(grep -hE 'cards have art|per painting|SHEET|already has art' ~/art_drop3.log | tail -n 4 | tr "\n" " ") — ask Tcgbot for the sheet photos"
fi
