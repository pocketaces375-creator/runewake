# The Ecosystem — one page

## Who's who
| Role | Who | Runs on | Job |
|---|---|---|---|
| **You** | Trikzos | Telegram | Talk in plain English. That's it. |
| **Front desk / boss** | Jett | Mini PC (DeepSeek) | Takes your requests, runs the machine, manages the other bots |
| **Specialists** | TcgBot (game), Vender (vending) | Mini PC (DeepSeek) | Do the actual work in their area |
| **Architect / second opinion** | Fable (Claude) | Mini PC via Claude Code, your subscription | Sits in every active chat: answers you, checks the bots, unblocks them |
| **Muscle** | GPU box (7900 XTX) | LAN only | Images, music (ACE-Step), LoRA training, later a free local model |
| **Bench** | The other 11 bots | Mini PC | Parked. Add one to `~/.hub/responder/active.txt` when you use it again |

## How a message flows
1. You write in Jett's / TcgBot's / Vender's chat.
2. The bot answers like it always has.
3. Fable reads your message **and** the bot's answer (waits up to 90 s), then posts `[Fable] …`: adds what was missed, corrects mistakes, or confirms and gives the next step.
4. When a bot posts something that matters (a question, BLOCKED, FAILED, "done", a decision), Fable answers the bot directly. Routine chatter is ignored for free.
5. Fable remembers each chat as its own ongoing conversation, so you never re-explain.

## When you're in the Claude app instead
The Hub connectors (Hub-Jett / Hub-tcgbot / Hub-vender) let Claude read every chat and post into it. Rule: Claude checks the chat **before** answering anything about the game, vending or a bot.

## The pieces on the mini PC
| Piece | What it is | Status |
|---|---|---|
| `hub-mcp` service + Tailscale Funnel | The private door to the chats for the Claude app | install_hub.sh |
| `fable-responder` service | Fable taking part inside Telegram | install_responder.sh |
| `hub-digest` repo + `NOW.md` | Written history + "what each bot is doing" board, every 5 min | digest_to_git.py |
| `STATUS:` lines | Each bot says what it's doing | BOT_RULES_HUB.md |
| Scout | Hourly suggestions watcher | **parked** (reminder set) |

## Money
- Bots: DeepSeek, cents a day.
- Fable: your Claude subscription. Default model Sonnet so it lasts all day; change `FABLE_MODEL=opus` (or `fable`) when you want deeper thinking. Hard cap 300 replies/day.
- GPU box: electricity only.

## Dials you can turn (all in `~/.config/systemd/user/fable-responder.service`)
`FABLE_MODEL` · `FABLE_WAIT_FOR_BOT` (90 s) · `FABLE_COOLDOWN` (20 s per chat) · `FABLE_DAILY_CAP` (300) · active chats in `~/.hub/responder/active.txt`.
