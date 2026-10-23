#!/usr/bin/env python3
"""
digest_to_git.py — Layer 2 of the hub: write the latest Hermes conversations to markdown
files in a git repo and push, so ANY Claude/ChatGPT session (even ones with no live
connector) can `git pull` and see what the bots have been saying.

Talks to `hermes mcp-serve` over stdio using the official MCP python SDK.
Run from cron every 5 minutes:
    */5 * * * * /home/fictive/.hub/venv/bin/python /home/fictive/hub/digest_to_git.py >> /home/fictive/.hub/digest.log 2>&1

Setup (once):
    python3 -m venv ~/.hub/venv && ~/.hub/venv/bin/pip install mcp
    export HUB_DIGEST_REPO=/home/fictive/hub-digest   # a git clone with push rights

Robust by design: on the first run it dumps every tool's input schema to
~/.hub/tools_schema.json so the argument names can be checked; if a call fails,
the error is written into the digest file instead of crashing.
"""
import asyncio, json, os, subprocess, sys, datetime, re, pathlib

REPO = pathlib.Path(os.environ.get("HUB_DIGEST_REPO", os.path.expanduser("~/hub-digest")))
PROFILES = [p for p in os.environ.get("HUB_PROFILES", "default,tcgbot,vender").split(",") if p]
LIMIT = int(os.environ.get("HUB_MSG_LIMIT", "40"))
STATE = pathlib.Path(os.path.expanduser("~/.hub"))
STATE.mkdir(exist_ok=True)

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def pick(schema_props, *patterns):
    """Return the first property name matching any regex pattern (case-insensitive)."""
    for pat in patterns:
        for name in schema_props:
            if re.search(pat, name, re.I):
                return name
    return None


def text_of(result):
    parts = []
    for c in getattr(result, "content", []) or []:
        if getattr(c, "type", "") == "text":
            parts.append(c.text)
    return "\n".join(parts)


async def dump_profile(profile: str) -> str:
    args = ([] if profile == "default" else ["-p", profile]) + ["mcp-serve"]
    params = StdioServerParameters(command="hermes", args=args)
    out = [f"# Hermes profile: {profile}", f"_updated {datetime.datetime.utcnow():%Y-%m-%d %H:%M}Z_", ""]
    async with stdio_client(params) as (r, w):
        async with ClientSession(r, w) as s:
            await s.initialize()
            tools = await s.list_tools()
            schemas = {t.name: (t.inputSchema or {}) for t in tools.tools}
            (STATE / f"tools_schema_{profile}.json").write_text(json.dumps(schemas, indent=2))
            if "conversations_list" not in schemas:
                return "\n".join(out + ["(mcp-serve did not expose conversations_list — is the plugin installed for this profile?)"])
            convs_raw = text_of(await s.call_tool("conversations_list", {}))
            out += ["## conversations_list (raw)", "```json", convs_raw[:6000], "```", ""]
            try:
                convs = json.loads(convs_raw)
            except Exception:
                convs = []
            if isinstance(convs, dict):
                convs = convs.get("conversations") or convs.get("items") or []
            props = schemas.get("messages_read", {}).get("properties", {})
            key_arg = pick(props, r"session_key", r"conversation", r"session", r"chat", r"key", r"id")
            lim_arg = pick(props, r"limit", r"count", r"max")
            for c in convs[:12]:
                if not isinstance(c, dict):
                    continue
                key = c.get("session_key") or c.get("key") or c.get("id") or c.get("conversation_id")
                title = c.get("title") or c.get("name") or c.get("chat_id") or key
                out.append(f"## {title}  (`{key}`)")
                if not key or not key_arg:
                    out.append("(could not determine how to call messages_read — see tools_schema json)")
                    continue
                call = {key_arg: key}
                if lim_arg:
                    call[lim_arg] = LIMIT
                try:
                    out += ["```json", text_of(await s.call_tool("messages_read", call))[:12000], "```", ""]
                except Exception as e:  # keep going; record the failure
                    out += [f"(messages_read failed: {e})", ""]
    return "\n".join(out)


def git(*a):
    return subprocess.run(["git", "-C", str(REPO), *a], capture_output=True, text=True)


async def main():
    if not (REPO / ".git").exists():
        print(f"HUB_DIGEST_REPO {REPO} is not a git clone", file=sys.stderr)
        sys.exit(1)
    (REPO / "digest").mkdir(exist_ok=True)
    index = ["# Hub digest — latest Hermes conversations", ""]
    for p in PROFILES:
        try:
            md = await dump_profile(p)
        except Exception as e:
            md = f"# Hermes profile: {p}\n\n(dump failed: {e})\n"
        (REPO / "digest" / f"{p}.md").write_text(md)
        index.append(f"- [{p}](digest/{p}.md)")
    (REPO / "DIGEST.md").write_text("\n".join(index) + "\n")
    git("add", "-A")
    if git("diff", "--cached", "--quiet").returncode != 0:
        git("commit", "-m", f"digest {datetime.datetime.utcnow():%Y-%m-%d %H:%M}Z")
        r = git("push")
        print(r.stdout, r.stderr)


asyncio.run(main())
