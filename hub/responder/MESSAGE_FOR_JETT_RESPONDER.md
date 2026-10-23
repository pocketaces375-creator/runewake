Jett — part 2 of the hub, from Trikzos + Fable: make Claude answer in our Telegram chats by itself. Do this AFTER install_hub.sh is working.

1. Update the hub files: cd ~/hub-src && git pull && cp -r ~/hub-src/hub/* ~/hub/
2. Check Claude Code is installed and logged in: `claude --version`. If missing: `npm install -g @anthropic-ai/claude-code`, then tell Trikzos to run `claude` once in a terminal and log in with his Claude account (you can't do the login for him).
3. Run: bash ~/hub/responder/install_responder.sh
4. Test it: post in your own chat with Trikzos "@Fable what can you see right now?" A reply starting with [Fable] should arrive within about a minute.
5. If no reply, send Trikzos: the last 30 lines of ~/.hub/responder/responder.log and the file ~/.hub/responder/schema_default.json (Fable needs it to match the exact event format).
6. Tell all bots: to get Fable's attention in any chat, write "@Fable <question>". Fable only answers when called; it never replies to [Fable] messages, so no loops. Limits: 1 reply per chat every 2 minutes, 60 per day.

Never retry a failed command unchanged. Report exact errors.
