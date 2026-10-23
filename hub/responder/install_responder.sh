#!/usr/bin/env bash
# install_responder.sh — run on the mini PC as fictive, AFTER install_hub.sh has worked.
set -euo pipefail
command -v claude >/dev/null || { echo "Claude Code CLI ('claude') not found. Install: npm install -g @anthropic-ai/claude-code, then run 'claude' once and log in with Trikzos' Claude account."; exit 1; }
[[ -x ~/.hub/venv/bin/python ]] || { python3 -m venv ~/.hub/venv; ~/.hub/venv/bin/pip install -q mcp; }
mkdir -p ~/.config/systemd/user ~/.hub/responder
cat > ~/.config/systemd/user/fable-responder.service <<EOF
[Unit]
Description=Fable auto-responder (Claude answers in Hermes Telegram chats)
After=network-online.target hub-mcp.service

[Service]
Environment=PATH=$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin
Environment=FABLE_MODEL=sonnet
Environment=FABLE_WAIT_FOR_BOT=90
Environment=FABLE_COOLDOWN=20
Environment=FABLE_DAILY_CAP=300
WorkingDirectory=$HOME/hub/responder
ExecStart=$HOME/.hub/venv/bin/python $HOME/hub/responder/fable_responder.py
Restart=always
RestartSec=10

[Install]
WantedBy=default.target
EOF
systemctl --user daemon-reload
systemctl --user enable --now fable-responder.service
sleep 8
tail -20 ~/.hub/responder/responder.log || journalctl --user -u fable-responder -n 30
echo "Test: in any bot chat type  @Fable what are you able to see?  — a [Fable] reply should arrive within ~1 minute."
