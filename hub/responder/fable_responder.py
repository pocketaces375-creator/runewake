#!/usr/bin/env python3
"""
fable_responder.py — makes Claude ("Fable") answer in the Telegram chats on its own.

How it works (plain English):
  * Watches every bot's chats through `hermes mcp-serve` (the same bridge the hub uses).
  * When a NEW message is addressed to Fable (contains "@Fable", "Fable," or "Claude,"),
    or Trikzos writes in a chat listed in ALWAYS_ANSWER, it runs Claude Code on this
    machine (`claude -p`, on Trikzos' Claude subscription) with the hub tools attached,
    so Claude can read the chat history itself before answering.
  * Claude's answer is posted back into the same chat with `hermes -p <profile> send`,
    prefixed "[Fable]". Messages starting with "[Fable]" are never answered (no loops).
  * Guardrails: max 1 reply per chat per COOLDOWN seconds, max DAILY_CAP replies/day.

Run as a systemd user service (see install_responder.sh).
"""
import asyncio, json, os, re, subprocess, time, datetime, pathlib

PROFILES = os.environ.get("HUB_PROFILES",
    "default tcgbot vender compiler forge sentinel vega guardian brandbot gandalf sammy creditcious friend").split()
TRIGGER = re.compile(os.environ.get("FABLE_TRIGGER", r"(@fable\b|^fable[,:]|^claude[,:]|@claude\b)"), re.I)
ALWAYS_ANSWER = set(filter(None, os.environ.get("FABLE_ALWAYS_ANSWER", "").split(",")))  # chat ids
COOLDOWN = int(os.environ.get("FABLE_COOLDOWN", "120"))
DAILY_CAP = int(os.environ.get("FABLE_DAILY_CAP", "60"))
POLL = int(os.environ.get("FABLE_POLL", "15"))
STATE = pathlib.Path(os.path.expanduser("~/.hub/responder")); STATE.mkdir(parents=True, exist_ok=True)
LOG = STATE / "responder.log"
HUB_DIR = pathlib.Path(os.path.expanduser("~/hub/responder"))

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def log(*a):
    line = f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S} " + " ".join(str(x) for x in a)
    print(line, flush=True)
    with LOG.open("a") as f:
        f.write(line + "\n")


def text_of(res):
    return "\n".join(c.text for c in (getattr(res, "content", []) or []) if getattr(c, "type", "") == "text")


def first(d, *keys):
    for k in keys:
        if isinstance(d, dict) and d.get(k) not in (None, ""):
            return d[k]
    return None


class Budget:
    def __init__(self):
        self.last = {}
        self.day = None
        self.count = 0

    def ok(self, chat):
        today = datetime.date.today()
        if today != self.day:
            self.day, self.count = today, 0
        if self.count >= DAILY_CAP:
            return False
        if time.time() - self.last.get(chat, 0) < COOLDOWN:
            return False
        return True

    def spend(self, chat):
        self.last[chat] = time.time()
        self.count += 1


BUDGET = Budget()


def ask_claude(profile, chat_label, target, msg_text):
    prompt = (
        f"New message in Telegram chat {chat_label} (bot profile: {profile}, send target {target}):\n\n"
        f"{msg_text}\n\n"
        "First read the recent history of this chat (and other bots' chats if relevant) with the hub tools, "
        "then write the reply to post in this chat. Output ONLY the reply text. "
        "If no reply is needed, output exactly: [SKIP]"
    )
    cmd = ["claude", "-p", prompt,
           "--append-system-prompt", (HUB_DIR / "FABLE_RESPONDER.md").read_text(),
           "--mcp-config", str(STATE / "mcp.json"),
           "--allowedTools", ",".join(
               ["mcp__hub"] + [f"mcp__hub_{p}" for p in PROFILES if p != "default"]),
           "--max-turns", "8"]
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
    if r.returncode != 0:
        log("claude failed:", r.stderr[-500:])
        return None
    return r.stdout.strip()


def send(profile, target, text):
    args = ["hermes"] + ([] if profile == "default" else ["-p", profile]) + ["send", "--to", target, f"[Fable] {text}"]
    r = subprocess.run(args, capture_output=True, text=True, timeout=120)
    log("sent" if r.returncode == 0 else f"send failed: {r.stderr[-300:]}", profile, target)


async def handle(profile, ev):
    if not isinstance(ev, dict):
        return
    text = str(first(ev, "text", "content", "message", "body") or "")
    if not text or text.startswith("[Fable]"):
        return
    chat = str(first(ev, "chat_id", "session_key", "conversation", "conversation_id") or "")
    platform = str(first(ev, "platform") or "telegram")
    target = f"{platform}:{first(ev, 'chat_id') or chat}"
    if not (TRIGGER.search(text) or chat in ALWAYS_ANSWER):
        return
    if not BUDGET.ok(chat):
        log("budget/cooldown skip", profile, chat)
        return
    BUDGET.spend(chat)
    log("answering", profile, chat, text[:80])
    reply = await asyncio.to_thread(ask_claude, profile, chat, target, text)
    if reply and reply != "[SKIP]":
        await asyncio.to_thread(send, profile, target, reply)


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
                    cursor = None
                    log("watching", profile, "cursor arg:", cur_arg)
                    while True:
                        call = {cur_arg: cursor} if (cur_arg and cursor is not None) else {}
                        raw = text_of(await s.call_tool("events_poll", call))
                        try:
                            data = json.loads(raw)
                        except Exception:
                            data = {}
                        events = data if isinstance(data, list) else (first(data, "events", "messages", "items") or [])
                        cursor = first(data, "cursor", "next_cursor", "next") if isinstance(data, dict) else cursor
                        for ev in events:
                            await handle(profile, ev)
                        await asyncio.sleep(POLL)
        except Exception as e:
            log("watch error", profile, repr(e)[:300], "— retry in 30s")
            await asyncio.sleep(30)


async def main():
    servers = {"hub": {"command": "hermes", "args": ["mcp-serve"]}}
    for p in PROFILES:
        if p != "default":
            servers[f"hub_{p}"] = {"command": "hermes", "args": ["-p", p, "mcp-serve"]}
    (STATE / "mcp.json").write_text(json.dumps({"mcpServers": servers}, indent=2))
    await asyncio.gather(*(watch(p) for p in PROFILES))


asyncio.run(main())
