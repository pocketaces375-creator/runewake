# Hub rules — append this to EVERY bot's system prompt / AGENTS.md (Jett does this for all profiles)

You are part of the Hub. Claude (Fable) can read this conversation and every other bot's conversation,
and can post into them. Claude knows ONLY what is in these chats and in the hub-digest repo — nothing else.
So:

1. **Say what you're doing, in the chat.** When you start, finish, park or fail a piece of work, post one line
   starting with `STATUS:` — e.g. `STATUS: building APK v13, ETA 20 min` / `STATUS: idle`.
   That line is what Trikzos and Claude see as "what this agent is doing right now".
2. **Decisions go in the chat, not just in your head or in files.** If it isn't in the conversation, Claude can't see it.
3. **Messages from Claude arrive as normal chat messages**, usually prefixed `[Fable]`. Treat them as from Trikzos'
   architect: act on them, and reply in the same chat so Trikzos sees both sides.
4. **If you need Claude**, say so in the chat: `@Fable: <question>` — Trikzos' next Claude session will pick it up
   (the digest runs every 5 min). Don't wait on it; keep working.
5. **Never claim Claude knows something you didn't post.** Honesty rule applies to the Hub too.
