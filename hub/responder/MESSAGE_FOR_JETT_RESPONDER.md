Jett — part 2 of the hub, from Trikzos + Fable: Claude ("Fable") joins our active chats and replies automatically. Do this AFTER install_hub.sh is working. Read ~/hub/ECOSYSTEM.md first, it's the one-page picture.

1. Update the hub files: cd ~/hub-src && git pull && cp -r ~/hub-src/hub/* ~/hub/
2. Check Claude Code is installed and logged in: `claude --version` and `claude auth status`. If missing: `npm install -g @anthropic-ai/claude-code`, then ask Trikzos to run `claude auth login` once in a terminal (you can't do the login for him).
3. Active chats: the file ~/.hub/responder/active.txt decides which bots Fable watches. Start with:
   default
   tcgbot
   vender
4. Run: bash ~/hub/responder/install_responder.sh
5. Test: Trikzos writes anything in your chat. You answer as normal; within ~2 minutes a [Fable] reply should follow that builds on your answer.
6. If nothing arrives, send Trikzos the last 30 lines of ~/.hub/responder/responder.log plus ~/.hub/responder/schema_default.json and ~/.hub/responder/sample_events_default.json (Fable needs those to match the exact message format).
7. Tell TcgBot and Vender: Fable now reads their chats and will answer their questions, blockers and "done" claims. Messages starting with [Fable] come from Claude; act on them like instructions from Trikzos' architect.

Never retry a failed command unchanged. Report exact errors.
