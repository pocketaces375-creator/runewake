#!/usr/bin/env python3
"""
fable_responder.py — Claude ("Fable") takes part in Trikzos' active Telegram chats.

RULES (plain English)
  1. Only the ACTIVE chats are watched (~/.hub/responder/active.txt; default: Jett, TcgBot, Vender).
  2. Every message Trikzos writes there gets a Claude reply. Claude waits for the bot's own
     answer first (up to WAIT_FOR_BOT seconds) so it adds to it instead of repeating it.
  3. Bot messages get a Claude reply only when they matter: a question, a blocker, a failure,
     a "done" claim, or a decision. Checked by a free word filter, so ordinary chatter costs nothing.
  4. Claude keeps ONE ongoing conversation per chat (claude --session-id / --resume), so it
     remembers that chat between messages.
  5. Replies are posted by the bot with a "[Fable]" prefix. "[Fable]" messages are never answered.
  6. Guardrails: COOLDOWN seconds per chat, DAILY_CAP replies per day, then it goes quiet and logs.
"""
import asyncio, json, os, re, subprocess, time, datetime, pathlib, uuid

ACTIVE_FILE = pathlib.Path(os.path.expanduser("~/.hub/responder/active.txt"))
TRIKZOS_ID = os.environ.get("TRIKZOS_ID", "7007731907")
MODEL = os.environ.get("FABLE_MODEL", "sonnet")          # "opus" / "fable" for heavier thinking
WAIT_FOR_BOT = int(os.environ.get("FABLE_WAIT_FOR_BOT", "90"))
COOLDOWN = int(os.environ.get("FABLE_COOLDOWN", "20"))
DAILY_CAP = int(os.environ.get("FABLE_DAILY_CAP", "300"))
POLL = int(os.environ.get("FABLE_POLL", "5"))
BOT_WAKE = re.compile(os.environ.get("FABLE_BOT_WAKE",
    r"\?\s*$|\b(blocked|failed|error|exception|parked|halt(ed)?|done|finished|shipped|"
    r"should i|which (one|option)|need (your|a) (ok|approval|decision)|waiting (on|for)|@fable|fable)\b"),
    re.I | re.M)

STATE = pathlib.Path(os.path.expanduser("~/.hub/responder")); STATE.mkdir(parents=True, exist_ok=True)
LOG = STATE / "responder.log"
SESSIONS = STATE / "sessions.json"
HUB_DIR = pathlib.Path(os.path.expanduser("~/hub/responder"))
SERVER_NAMES = []

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def log(*a):
    line = f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S} " + " ".join(str(x) for x in a)
    print(line, flush=True)
    with LOG.open("a") as f:
        f.write(line + "\n")


def active_profiles():
    if not ACTIVE_FILE.exists():
        ACTIVE_FILE.write_text("default\ntcgbot\nvender\n")
    return [l.strip() for l in ACTIVE_FILE.read_text().splitlines() if l.strip() and not l.startswith("#")]


def text_of(res):
    return "\n".join(c.text for c in (getattr(res, "content", []) or []) if getattr(c, "type", "") == "text")


def first(d, *keys):
    for k in keys:
        if isinstance(d, dict) and d.get(k) not in (None, ""):
            return d[k]
    return None


def is_from_trikzos(ev):
    role = str(first(ev, "role", "direction", "type") or "").lower()
    who = str(first(ev, "sender_id", "user_id", "from_id", "author_id", "sender", "from", "author", "user_name") or "").lower()
    if role in ("user", "human", "inbound", "incoming"):
        return True
    return TRIKZOS_ID in who or "trikzos" in who or "fictive" in who


# ---------- guardrails ----------
class Budget:
    def __init__(self):
        self.last, self.day, self.count = {}, None, 0

    def ok(self, chat):
        if datetime.date.today() != self.day:
            self.day, self.count = datetime.date.today(), 0
        return self.count < DAILY_CAP and time.time() - self.last.get(chat, 0) >= COOLDOWN

    def spend(self, chat):
        self.last[chat] = time.time()
        self.count += 1


BUDGET = Budget()


# ---------- Claude, one remembered conversation per chat ----------
def session_for(chat):
    data = json.loads(SESSIONS.read_text()) if SESSIONS.exists() else {}
    sid = str(uuid.uuid5(uuid.NAMESPACE_URL, f"fable-hub/{chat}"))
    started = data.get(chat, False)
    return sid, started


def mark_started(chat):
    data = json.loads(SESSIONS.read_text()) if SESSIONS.exists() else {}
    data[chat] = True
    SESSIONS.write_text(json.dumps(data, indent=2))


def ask_claude(profile, chat, prompt):
    sid, started = session_for(chat)
    cmd = ["claude", "-p", prompt, "--model", MODEL,
           "--append-system-prompt-file", str(HUB_DIR / "FABLE_RESPONDER.md"),
           "--mcp-config", str(STATE / "mcp.json"), "--strict-mcp-config",
           "--allowedTools", ",".join(f"mcp__{n}" for n in SERVER_NAMES),
           "--permission-prompts", "none",
           "--max-turns", "10"]
    cmd += ["--resume", sid] if started else ["--session-id", sid]
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=600, cwd=str(STATE))
    if r.returncode != 0:
        log("claude failed:", (r.stderr or r.stdout)[-500:])
        if started and "session" in (r.stderr or "").lower():   # lost session → start fresh next time
            data = json.loads(SESSIONS.read_text()); data.pop(chat, None); SESSIONS.write_text(json.dumps(data))
        return None
    if not started:
        mark_started(chat)
    return r.stdout.strip()


def send(profile, target, text):
    args = ["hermes"] + ([] if profile == "default" else ["-p", profile]) + ["send", "--to", target, f"[Fable] {text}"]
    r = subprocess.run(args, capture_output=True, text=True, timeout=120)
    log("sent" if r.returncode == 0 else f"send failed: {r.stderr[-300:]}", profile, target)


async def reply(profile, chat, target, prompt, allow_skip):
    if not BUDGET.ok(chat):
        log("cooldown/daily cap — skipped", profile, chat)
        return
    BUDGET.spend(chat)
    out = await asyncio.to_thread(ask_claude, profile, chat, prompt)
    if out and not (allow_skip and out.strip() == "[SKIP]"):
        await asyncio.to_thread(send, profile, target, out)


# ---------- watching ----------
class ChatWatch:
    """Holds Trikzos' message until the bot answers (or WAIT_FOR_BOT passes), then Claude speaks."""
    def __init__(self):
        self.pending = {}   # chat -> dict(deadline, profile, target, texts[], bot_reply)

    def trikzos_said(self, profile, chat, target, text):
        p = self.pending.setdefault(chat, {"profile": profile, "target": target, "texts": [], "bot": ""})
        p["texts"].append(text)
        p["deadline"] = time.time() + WAIT_FOR_BOT

    def bot_said(self, chat, text):
        if chat in self.pending:
            self.pending[chat]["bot"] += text + "\n"
            self.pending[chat]["deadline"] = min(self.pending[chat]["deadline"], time.time() + 8)  # let it finish
            return True
        return False

    def due(self):
        now = time.time()
        ready = [c for c, p in self.pending.items() if p["deadline"] <= now]
        return [(c, self.pending.pop(c)) for c in ready]


WATCH = ChatWatch()


async def handle(profile, ev):
    if not isinstance(ev, dict):
        return
    text = str(first(ev, "text", "content", "message", "body") or "").strip()
    if not text or text.startswith("[Fable]"):
        return
    chat = str(first(ev, "chat_id", "session_key", "conversation_id", "conversation") or "")
    target = f"{first(ev, 'platform') or 'telegram'}:{first(ev, 'chat_id') or chat}"
    if is_from_trikzos(ev):
        WATCH.trikzos_said(profile, chat, target, text)
    elif not WATCH.bot_said(chat, text) and BOT_WAKE.search(text):
        prompt = (f"The {profile} bot just posted in chat {chat}:\n\n{text}\n\n"
                  "Review it. If it asks a question, is blocked, failed, claims something is done, or needs a decision, "
                  "reply to the bot with what to do (check done-claims against the evidence in the chat). "
                  "If nothing useful to add, output exactly [SKIP].")
        log("bot message worth a look", profile, chat, text[:80])
        await reply(profile, chat, target, prompt, allow_skip=True)


async def flush_pending():
    while True:
        for chat, p in WATCH.due():
            said = "\n".join(p["texts"])
            bot = p["bot"].strip() or "(the bot has not answered yet)"
            prompt = (f"Trikzos wrote in chat {chat} (bot: {p['profile']}):\n\n{said}\n\n"
                      f"The bot answered:\n{bot}\n\n"
                      "Reply to Trikzos. Add what the bot missed, correct it if it's wrong, or confirm briefly and "
                      "give the next step. If the bot hasn't answered, answer him yourself.")
            log("answering Trikzos", p["profile"], chat, said[:80])
            await reply(p["profile"], chat, p["target"], prompt, allow_skip=False)
        await asyncio.sleep(2)


async def watch(profile):
    args = ([] if profile == "default" else ["-p", profile]) + ["mcp-serve"]
    while True:
        try:
            async with stdio_client(StdioServerParameters(command="hermes", args=args)) as (r, w):
                async with ClientSession(r, w) as s:
                    await s.initialize()
                    tools = {t.name: (t.inputSchema or {}) for t in (await s.list_tools()).tools}
                    (STATE / f"schema_{profile}.json").write_text(json.dumps(tools, indent=2))
                    props = tools.get("events_poll", {}).get("properties", {})
                    cur_arg = next((k for k in props if re.search(r"cursor|since|after", k, re.I)), None)
                    cursor, first_poll = None, True
                    log("watching", profile, "| cursor arg:", cur_arg)
                    while True:
                        call = {cur_arg: cursor} if (cur_arg and cursor is not None) else {}
                        raw = text_of(await s.call_tool("events_poll", call))
                        try:
                            data = json.loads(raw)
                        except Exception:
                            data = {}
                        if first_poll:
                            (STATE / f"sample_events_{profile}.json").write_text(raw[:20000])
                            first_poll = False
                        events = data if isinstance(data, list) else (first(data, "events", "messages", "items") or [])
                        if isinstance(data, dict):
                            cursor = first(data, "cursor", "next_cursor", "next") or cursor
                        for ev in events:
                            await handle(profile, ev)
                        await asyncio.sleep(POLL)
        except Exception as e:
            log("watch error", profile, repr(e)[:300], "— retry in 30s")
            await asyncio.sleep(30)


async def main():
    profiles = active_profiles()
    # Claude's own read access: one hub server per ACTIVE bot (names hub, hub_tcgbot, ...)
    servers = {}
    for p in profiles:
        servers["hub" if p == "default" else f"hub_{p}"] = {
            "command": "hermes", "args": (["mcp-serve"] if p == "default" else ["-p", p, "mcp-serve"])}
    SERVER_NAMES[:] = list(servers)
    (STATE / "mcp.json").write_text(json.dumps({"mcpServers": servers}, indent=2))
    log("Fable responder up — active:", ", ".join(profiles), "| model:", MODEL)
    await asyncio.gather(flush_pending(), *(watch(p) for p in profiles))


asyncio.run(main())
