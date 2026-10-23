Jett — new infra job from Trikzos + Fable. Goal: let Claude and ChatGPT read our Telegram chats and send into them directly, so Trikzos stops copy-pasting. Do NOT edit ~/.hermes/config.yaml.

1. git clone -b hub https://github.com/pocketaces375-creator/runewake.git ~/hub-src && cp -r ~/hub-src/hub ~/hub
2. Read ~/hub/README.md (2 min).
3. Run: bash ~/hub/install_hub.sh
   - If it says Tailscale isn't logged in, it isn't — the VendOps funnel on fictive-1 says it should be; check `tailscale status` and tell Trikzos.
   - It will add a NEW path on the existing funnel; it must not touch the VendOps path. Confirm with `tailscale funnel status` afterwards.
4. Post the two URLs it prints to Trikzos (DM, not the group). That URL is the key — only to Trikzos.
5. Verify from the box: curl -s http://127.0.0.1:8765/status  → should answer.
6. Layer 2 (after Layer 1 works): create a private repo `hub-digest` (gh repo create pocketaces375-creator/hub-digest --private), clone it to ~/hub-digest, then
   python3 -m venv ~/.hub/venv && ~/.hub/venv/bin/pip install mcp
   and add the cron line from the top of ~/hub/digest_to_git.py. Run it once by hand first and send Trikzos the first ~/.hub/tools_schema_default.json so Fable can check the argument names.
7. Report: what worked, what didn't, exact error text. Never retry a failed command unchanged.
