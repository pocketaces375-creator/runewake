# The Hub — one place where Hermes and Claude meet (ChatGPT dropped for now)

Owner: Trikzos. Host: the mini PC (`/home/fictive`, already on Tailscale as `fictive-1.tailb48725.ts.net`).

## The idea in one sentence
Jett is the front desk. Everything else — Claude, the GPU box, the other 13 bots — plugs into
**one door on the mini PC**, so you talk in Telegram and never copy-paste again.

```
                 ┌──────────────────────────────────────────────────────┐
  You (Telegram) │  MINI PC  — "the hub" (always on, cheap)             │
   ───────────►  │                                                      │
                 │  Jett + 13 bots (Hermes, DeepSeek V4 Flash)          │
                 │        │ state.db (every chat, every bot)            │
                 │        ▼                                             │
                 │  hermes mcp-serve (x14 bots) ─► mcp-proxy :8765 ─► Funnel    │
                 │  (read chats / send / wait for new / approve)        │
                 │                                                      │
                 │  Hermes can ALSO call out as an MCP client to:       │
                 │   • GPU box (ACE-Step, image gen, LoRA jobs)         │
                 │   • VendOps API, n8n, Home control                   │
                 └───────────────┬───────────────────────▲──────────────┘
                                 │ https://fictive-1…ts.net/<secret>/sse
          ┌──────────────────────┼───────────────────────┼──────────┐
          ▼                      ▼                       │          │
   Claude (web/phone/desktop)                           GPU box (7900 XTX 24GB)
   custom connector "Hub"      connector "Hub")         LAN-only, direct cable
   = reads every bot chat,     = same tools,            image/music/LoRA/local LLM
     sends to Telegram,          cheap second brain     served to Hermes as an
     approves bot requests                              MCP/HTTP tool
```

## Layers (do them in this order)

### Layer 1 — the live door (today, ~20 min, $0)
`install_hub.sh` does it. Result: two URLs you paste once.
- **Hermes `mcp-serve`** (official plugin): exposes `conversations_list`, `messages_read`, `events_wait`,
  `messages_send`, `channels_list`, `permissions_respond`, `attachments_fetch`.
- **mcp-proxy** turns that into a local web endpoint on port 8765.
- **Tailscale Funnel** (already on this box) publishes it at `https://fictive-1.tailb48725.ts.net/<32-char-secret>/sse`.
- **Every bot is served**: Jett at `/<secret>/sse`, each other profile at `/<secret>/servers/<profile>/sse`.
- **Claude**: Customize → Connectors → Add custom connector → paste URL, auth "No sign-in". One connector per bot you want
  Claude to see (start with Hub-Jett, Hub-tcgbot, Hub-vender). Works on web, phone and desktop, and in Cowork sessions.

Private, one line: nothing answers at the bare hostname; only the 32-character secret path works, only you have it, and `rm ~/.hub/secret_path` + re-run kills the old one instantly.

### Layer 2 — the paper trail (same day, $0)
`digest_to_git.py` on a 5-minute cron writes every bot's latest messages to a private repo (`hub-digest`).
Why bother if Layer 1 exists: scheduled/unattended Claude shifts and anything without a live connector can still
`git pull` and know what happened; it's also a free searchable log of every bot forever.
It also writes **`NOW.md`** — one line per bot: what it's doing right now (from its last `STATUS:` post). Add the repo to Claude's authorized sources.

### Layer 3 — Hermes reaches out (when the GPU box is finalized)
Hermes is already an MCP *client* (`~/.hermes/config.yaml` → `mcp_servers`). Give Jett these tools:
- **GPU box** — run one small MCP/HTTP server there exposing `generate_music(prompt, lora)`, `generate_image(prompt)`,
  `train_lora(dataset)`, `llm_chat(prompt)` (local model for cheap/private turns). LAN-only over the direct cable; never Funnel'd.
- **VendOps** — its FastAPI already exists; add a read-only MCP wrapper so Jett/Vender can answer "what sold today" without you.
- **Claude as a *tool* for Jett** — optional later: an `ask_claude` tool (API key, pay-per-use) for the things DeepSeek can't do.

### Layer 4 — the single conversation
With 1–3 in place, you only ever talk to Jett. Jett can: read what Claude posted, ask Claude a question,
hand a music job to the GPU box, and report back — all in the same Telegram thread. Claude, when you open it,
already sees everything via the connector; no pasting.

## Where each brain does what (cost-sorted)
| Job | Runs on | Cost |
|---|---|---|
| Bot turns, routine agent work | DeepSeek V4 Flash via OpenRouter | ~$0.09/M in |
| Image gen, ACE-Step music, LoRA training, stem separation | GPU box, local | electricity |
| Private/cheap chat turns, summarizing chat logs for the digest | GPU box local LLM (Ollama/llama.cpp, 24 GB fits ~30B models at 4-bit) | $0 |
| Architecture, hard code, Runewake conducting | Claude (your subscription) via the Hub connector | subscription |

## The "check before you answer" rule (Claude side)
Saved in Claude's memory for Trikzos: **before answering anything about Runewake/the TCG, VendOps, or any bot's work, Claude
first reads the latest messages in that bot's chat through the Hub connector (or `hub-digest/NOW.md` + `digest/` when no
connector is live), then answers with: what was last said, what the agent is currently doing, and an offer to relay a message.**
Bot side: every bot posts `STATUS:` lines (see `BOT_RULES_HUB.md`) so that answer is accurate.

## Files
- `install_hub.sh` — Layer 1, run on the mini PC as `fictive`.
- `digest_to_git.py` — Layer 2 cron script.
- `BOT_RULES_HUB.md` — the paragraph Jett appends to every bot's system prompt.
- `MESSAGE_FOR_JETT.md` — paste this into Jett's Telegram to have him do the install.
