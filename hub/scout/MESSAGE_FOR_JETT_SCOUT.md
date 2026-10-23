Jett — new bot from Trikzos + Fable: "Scout", a quiet watcher that pings Trikzos hourly with suggestions, but ONLY when something actually changed. Do this after the memory fix and hub install are done.

1. Create the profile: hermes profile create scout   (if that command doesn't exist in our version, copy the tcgbot profile layout and say what you did). Give it its own Telegram bot token from @BotFather named "Scout", home channel = Trikzos' DM (7007731907). Model: deepseek/deepseek-v4-flash, reasoning low.

2. Install the eyes:
   cp ~/hub/scout/scout_check.sh ~/.hermes/profiles/scout/scripts/ 2>/dev/null || cp ~/hub/scout/scout_check.sh ~/.hermes/scripts/
   chmod +x on it. Run it once by hand and show Trikzos the output.

3. Create the hourly job on the scout profile (script runs first; if it prints {"wakeAgent": false} the AI never wakes = $0):
   hermes -p scout cron create "every 1h" "<PROMPT BELOW>" --script scout_check.sh
   Deliver to telegram:7007731907. Quiet hours: none unless Trikzos says so.

PROMPT:
You are Scout, Trikzos' second pair of eyes. The text above is what changed in the last hour across his projects (Runewake game, VendOps, the bots, the GPU box). Write AT MOST 3 short suggestions, each a yes/no question he can answer with one word, e.g. "New build alpha-14 finished — run loop_smoke + a duel capture to check it's not buggy?" or "Lane 2 parked TASK-X twice — want TcgBot to re-scope it?". Prefer checks, follow-ups and next steps he'd likely do himself. Use his habits list: suggest more of what he says yes to, stop suggesting what he keeps ignoring. Nothing worth asking → reply exactly [SILENT]. Never invent a change that isn't in the text. Number the questions.

4. When Trikzos answers (e.g. "1 yes, 2 no"):
   - do the yes items or hand them to the right bot (game → TcgBot, vending → Vender, system → Jett) and post STATUS: lines;
   - append one line per answer to ~/.hub/scout/habits.md: "<date> <suggestion type> → yes/no". That file is how Scout learns his habits.

5. Add Scout to the hub: append "scout" to HUB_PROFILES in ~/hub/install_hub.sh and ~/hub/digest_to_git.py, re-run install_hub.sh, DM Trikzos the new Scout URL.

6. Report what worked and the exact text of anything that didn't. Never retry a failed command unchanged.
