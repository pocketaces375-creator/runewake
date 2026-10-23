Jett — new infra job from Trikzos + Fable. Goal: let Claude read ALL our bots' Telegram chats and post into them directly, so Trikzos stops copy-pasting. No OpenAI/ChatGPT in this. Do NOT edit ~/.hermes/config.yaml.

1. git clone -b hub https://github.com/pocketaces375-creator/runewake.git ~/hub-src && cp -r ~/hub-src/hub ~/hub
2. Read ~/hub/README.md (2 min).
3. Run: bash ~/hub/install_hub.sh
   - It installs the mcp-serve plugin on EVERY profile (default, tcgbot, vender, compiler, forge, sentinel, vega, guardian, brandbot, gandalf, sammy, creditcious, friend). If a profile name is wrong, fix the HUB_PROFILES list at the top of the script to match `ls ~/.hermes/profiles`.
   - If it says Tailscale isn't logged in: the VendOps funnel on fictive-1 says it should be — check `tailscale status` and tell Trikzos.
   - It adds a NEW path on the existing funnel and must not touch the VendOps path. Confirm with `tailscale funnel status` afterwards.
4. DM Trikzos the contents of ~/.hub/URLS.txt (DM only, never the group — the URLs are the key).
5. Verify: curl -s http://127.0.0.1:8765/status  → should answer; `systemctl --user status hub-mcp` → active.
6. Append ~/hub/BOT_RULES_HUB.md to every profile's system prompt / AGENTS.md (all 14, including yourself). Then post `STATUS: idle` or your current task in each bot's home chat once so the board is populated.
7. Layer 2: create a private repo `hub-digest` (gh repo create pocketaces375-creator/hub-digest --private --clone ~/hub-digest), then
   python3 -m venv ~/.hub/venv && ~/.hub/venv/bin/pip install mcp
   Run it once by hand: HUB_DIGEST_REPO=~/hub-digest ~/.hub/venv/bin/python ~/hub/digest_to_git.py
   Then add the cron line from the top of ~/hub/digest_to_git.py (with HUB_DIGEST_REPO set). Send Trikzos ~/.hub/tools_schema_default.json so Fable can check argument names.
8. Report: what worked, what didn't, exact error text. Never retry a failed command unchanged.
