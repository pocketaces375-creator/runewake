# Jett memory fix — send in 2 parts

## PART 1 — send this alone, exactly as written (it's a slash command):

/compress here 10

(Keeps your last 10 exchanges word-for-word and summarizes everything older. Nothing is deleted: the older turns stay archived in state.db and searchable with session_search. If it still overflows, send `/compress here 4`.)

## PART 2 — send after Jett is responsive again:

Jett — Trikzos + Fable here. Your context overflowed because compression was switched off. Your history matters a lot, so we're making memory gentle AND permanent. Nothing gets deleted: compression archives old turns in state.db and session_search can always pull them back. Do these in order, and report each result:

1. CHECK HISTORY IS SAFE: run session_search for a topic from last week (e.g. "Runewake" or "VendOps") and confirm you can still read the old messages. Tell Trikzos yes/no.

2. TURN COMPRESSION BACK ON, FORGIVING SETTINGS. Trikzos approves this edit to ~/.hermes/config.yaml (it's shared by all profiles, so every bot gets it):
   compression:
     enabled: true
     threshold: 0.75          # wait until 75% full before compressing
     protect_last_n: 40       # always keep the last 40 messages word-for-word
     tail_mode: lean          # keeps IDs, paths, errors and Trikzos' own words verbatim
     in_place: true           # same session; old turns archived, never deleted
   auxiliary:
     compression:
       model: deepseek/deepseek-v4-flash   # cheap summarizer
       provider: openrouter
   Back up the file first (cp ~/.hermes/config.yaml ~/.hermes/config.yaml.bak-$(date +%F)). Change only these keys. Then restart the gateway.
   If a key name is rejected, don't guess. Report the exact error.

3. WRITE YOUR CORE MEMORY NOW: use your memory tool to save the facts you'd least want to lose (who the bots are, your boss role, active projects, standing rules). MEMORY.md is small (2,200 chars), so keep only the essentials. Then raise the limits in config.yaml under memory: memory_char_limit: 6000, user_char_limit: 3000.

4. INSTALL A LOCAL LONG-TERM MEMORY (free, stays on this machine):
   hermes plugins install holographic
   hermes config set memory.provider holographic
   This gives you fact_store / fact_feedback, which save facts to a local SQLite database that survives any compression or /new. Then store your 20 most important facts with fact_store.

5. Post `STATUS: memory fixed — compression on (75%/keep 40), holographic installed` and then continue with the hub install message Trikzos sends next.

Never retry a failed command unchanged. If anything here doesn't exist in your Hermes version, say so plainly.
