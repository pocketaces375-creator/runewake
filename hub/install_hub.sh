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
# Every bot gets the feature. "default" = Jett. Served at /<secret>/servers/<profile>/sse ; Jett also at /<secret>/sse
HUB_PROFILES="${HUB_PROFILES:-default tcgbot vender compiler forge sentinel vega guardian brandbot gandalf sammy creditcious friend}"
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
HERMES_BIN="$(command -v hermes)"
for p in $HUB_PROFILES; do
  if [[ "$p" == default ]]; then hermes plugins install mcp-serve || log "plugin install failed for default profile";
  else hermes -p "$p" plugins install mcp-serve || log "plugin install failed for profile $p (does it exist?)"; fi
done
# one named server per profile (mcp-proxy JSON format: top-level mcpServers{name:{command,args}})
python3 - "$HERMES_BIN" $HUB_PROFILES > "$STATE_DIR/servers.json" <<'PY'
import json,sys
hermes=sys.argv[1]; servers={}
for p in sys.argv[2:]:
    servers[p]={"command":hermes,"args":(["mcp-serve"] if p=="default" else ["-p",p,"mcp-serve"])}
print(json.dumps({"mcpServers":servers},indent=2))
PY

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
cat > "$HOME/.config/systemd/user/hub-mcp.service" <<EOF
[Unit]
Description=Hub: Hermes conversation bridge over HTTP (mcp-proxy -> hermes mcp-serve, all profiles)
After=network-online.target

[Service]
Environment=PATH=$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin
# root (/sse) = Jett; every bot also at /servers/<profile>/sse
ExecStart=$MCP_PROXY --host=127.0.0.1 --port=$HUB_PORT --stateless --named-server-config $STATE_DIR/servers.json -- $HERMES_BIN mcp-serve
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
{
echo "================  HUB IS LIVE  ================"
echo "Add in Claude: Customize > Connectors > Add custom connector, auth = No sign-in. One connector per bot you want Claude to see:"
echo "    Hub-Jett      $BASE/sse"
for p in $HUB_PROFILES; do [[ $p == default ]] && continue; echo "    Hub-$p   $BASE/servers/$p/sse"; done
echo "Status:   systemctl --user status hub-mcp ; tailscale funnel status ; curl -s http://127.0.0.1:$HUB_PORT/status"
echo "Private: nothing is served at the bare host path — only under the 32-char secret. Rotate: rm ~/.hub/secret_path && re-run."
echo "==============================================="
} | tee "$STATE_DIR/URLS.txt"
