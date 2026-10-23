Jett — one-time setup from Trikzos + Fable. After this, Fable talks to you directly and Trikzos stops relaying. Do NOT edit ~/.hermes/config.yaml by hand.

1. Get the files:
   [ -d ~/hub-src ] && (cd ~/hub-src && git pull) || git clone -b hub https://github.com/pocketaces375-creator/runewake.git ~/hub-src
   cp -r ~/hub-src/hub/* ~/hub/ 2>/dev/null || cp -r ~/hub-src/hub ~/hub
   Read ~/hub/ECOSYSTEM.md (1 page).
2. Hub door: bash ~/hub/install_hub.sh
   It adds a new private path on the existing Tailscale funnel. It must NOT touch the VendOps path; confirm with `tailscale funnel status`.
3. Fable in the chats: put these 3 lines in ~/.hub/responder/active.txt → default, tcgbot, vender. Then check `claude auth status`. If Claude isn't logged in, ask Trikzos to run `claude auth login` in a terminal, wait for him, and then run:
   bash ~/hub/responder/install_responder.sh
4. Append ~/hub/BOT_RULES_HUB.md to your own and TcgBot's and Vender's system prompt / AGENTS.md. Post a STATUS: line in each of those chats.
5. DM Trikzos ONLY the "Hub-Jett" URL from ~/.hub/URLS.txt (never in a group).
6. Reply with a ✅/❌ per step plus exact error text for any ❌. Only mark ✅ after checking the result (service running, file contains the text). Never retry a failed command unchanged.
