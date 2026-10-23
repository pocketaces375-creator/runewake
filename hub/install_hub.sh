#!/usr/bin/env bash
# install_hub.sh — publish Jett's conversation bridge so Claude and ChatGPT can read/send
# Telegram chats directly. Run on the mini PC as the user that runs Hermes (fictive).
#
# What it does (plain English):
#   1. Installs Hermes' official "mcp-serve" plugin (the conversation bridge).
#   2. Installs mcp-proxy, which turns that bridge into a local web endpoint (port 8765).
#   3. Runs the proxy as a systemd user service so it survives reboots.
#   4. Publishes it over HTTPS with Tailscale Funnel under a secret path.
#   5. Prints the two URLs to paste into Claude (custom connector) and ChatGPT (developer mode).
#
# Idempotent: safe to re-run. Nothing here touches ~/.hermes/config.yaml.
set -euo pipefail

HUB_PORT="${HUB_PORT:-8765}"
HUB_PROFILE="${HUB_PROFILE:-}"           # empty = default profile (Jett). e.g. HUB_PROFILE=tcgbot
STATE_DIR="$HOME/.hub"
mkdir -p "$STATE_DIR"

log(){ printf '\n[hub] %s\n' "$*"; }

# ---------- 0. secret path (acts as the password in the URL) ----------
if [[ ! -s "$STATE_DIR/secret_path" ]]; then
  head -c 24 /dev/urandom | base64 | tr -dc 'a-zA-Z0-9' | head -c 32 > "$STATE_DIR/secret_path"
fi
SECRET="$(cat "$STATE_DIR/secret_path")"

# ---------- 1. Hermes mcp-serve plugin ----------
log "Installing Hermes mcp-serve plugin"
command -v hermes >/dev/null || { echo "hermes not on PATH for this user"; exit 1; }
if [[ -n "$HUB_PROFILE" ]]; then
  hermes -p "$HUB_PROFILE" plugins install mcp-serve || true
else
  hermes plugins install mcp-serve || true
fi
# smoke test: the server must start and answer stdio (handshake is enough)
if [[ -n "$HUB_PROFILE" ]]; then HERMES_CMD=(hermes -p "$HUB_PROFILE" mcp-serve); else HERMES_CMD=(hermes mcp-serve); fi
timeout 20 "${HERMES_CMD[@]}" --help >/dev/null 2>&1 || log "note: 'hermes mcp-serve --help' did not exit 0 (plugin may still work; continuing)"

# ---------- 2. mcp-proxy ----------
log "Installing mcp-proxy (stdio -> HTTP)"
if ! command -v uv >/dev/null; then
  curl -LsSf https://astral.sh/uv/install.sh | sh
  export PATH="$HOME/.local/bin:$PATH"
fi
uv tool install --upgrade mcp-proxy >/dev/null
MCP_PROXY="$(command -v mcp-proxy || echo "$HOME/.local/bin/mcp-proxy")"

# ---------- 3. systemd user service ----------
log "Writing systemd user service hub-mcp"
mkdir -p "$HOME/.config/systemd/user"
HERMES_BIN="$(command -v hermes)"
PROFILE_ARGS=""; [[ -n "$HUB_PROFILE" ]] && PROFILE_ARGS="-p $HUB_PROFILE"
cat > "$HOME/.config/systemd/user/hub-mcp.service" <<EOF
[Unit]
Description=Hub: Hermes conversation bridge over HTTP (mcp-proxy -> hermes mcp-serve)
After=network-online.target

[Service]
Environment=PATH=$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin
ExecStart=$MCP_PROXY --host=127.0.0.1 --port=$HUB_PORT --stateless --allow-origin='*' -- $HERMES_BIN $PROFILE_ARGS mcp-serve
Restart=always
RestartSec=5

[Install]
WantedBy=default.target
EOF
systemctl --user daemon-reload
systemctl --user enable --now hub-mcp.service
loginctl enable-linger "$USER" >/dev/null 2>&1 || true
sleep 3
if curl -fs "http://127.0.0.1:$HUB_PORT/status" >/dev/null 2>&1; then
  log "local endpoint up: http://127.0.0.1:$HUB_PORT (SSE at /sse)"
else
  log "WARNING: proxy not answering on /status yet. Check: journalctl --user -u hub-mcp -n 50"
fi

# ---------- 4. Tailscale Funnel ----------
log "Tailscale Funnel"
if ! command -v tailscale >/dev/null; then
  curl -fsSL https://tailscale.com/install.sh | sh
fi
if ! tailscale status >/dev/null 2>&1; then
  log "Tailscale is not logged in. Run:  sudo tailscale up   (open the printed link, sign in — free account), then re-run this script."
  exit 0
fi
# publish 127.0.0.1:$HUB_PORT at https://<host>.<tailnet>.ts.net/<secret>/
sudo tailscale funnel --bg --https=443 --set-path="/$SECRET" "http://127.0.0.1:$HUB_PORT" \
  || sudo tailscale funnel --bg --https=443 --set-path="/$SECRET" "127.0.0.1:$HUB_PORT"
HOST="$(tailscale status --json | python3 -c 'import sys,json;print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')"
BASE="https://$HOST/$SECRET"
printf '%s\n' "$BASE" > "$STATE_DIR/public_base_url"

# ---------- 5. hand-off ----------
cat <<EOF

================  HUB IS LIVE  ================
Paste into Claude  (Customize > Connectors > Add custom connector, auth = No sign-in):
    $BASE/sse
Paste into ChatGPT (Settings > Connectors > Advanced > Developer mode > Create, auth = No authentication):
    $BASE/sse
Status:   systemctl --user status hub-mcp ; tailscale funnel status
Rotate the secret:  rm ~/.hub/secret_path && re-run this script (old URL dies instantly).
Treat the URL like a password — it IS the password.
===============================================
EOF
