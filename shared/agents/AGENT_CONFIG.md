# Agent Configuration

## All Bots & Profiles

| Agent | Profile | Display | Telegram | Model |
|---|---|---|---|---|
| Jett (assistant) | default | — | @JettTheBot | deepseek/deepseek-v4-flash |
| Tcgbot (builder) | tcgbot | :100 (5918) | @TcgBot | deepseek/deepseek-v4-flash |
| Compiler (builds) | compiler | :101 (5920) | — | deepseek/deepseek-v4-flash |
| Forge (code review) | forge | :102 (5922) | — | deepseek/deepseek-v4-flash |
| Sentinel (monitor) | sentinel | :103 (5924) | — | deepseek/deepseek-v4-flash |
| Vega (research) | vega | :104 (5926) | — | deepseek/deepseek-v4-flash |
| The Wolf (trading) | — | :105 (5928) | — | — |
| Vender (vending) | vender | :106 (5930) | — | deepseek/deepseek-v4-flash |
| Guardian (backup) | guardian | :107 (5932) | — | deepseek/deepseek-v4-flash |
| Brandbot (brand) | brandbot | :108 (5934) | @Brandbot | deepseek/deepseek-v4-flash |
| Gandalf (botmaster) | gandalf | :99 (5929) | — | deepseek/deepseek-v4-flash |
| Sammy (sister's bot) | sammy | :110 (5938) | @Sammythebestsbot | deepseek/deepseek-v4-flash |
| Creditcious (credit) | creditcious | :111 (5912) | @CreditciousBot | deepseek/deepseek-v4-flash |
| Friend (test) | friend | :109 (5936) | — | deepseek/deepseek-v4-flash |

## Display Registry
File: `~/.hermes/display-registry.json`
Next free display: :112 (5913)

## How Bots Connect
- All bots route through Hermes Gateway on their profile
- Each profile has isolated `skills/`, `plugins/`, `cron/`, `memories/`
- Gateway listens on Telegram bot token from profile's `.env`
- `TELEGRAM_HOME_CHANNEL` env var must match the chat_id for onboarding

## Bridge-Specific Setup
Bridge reads Tcgbot's stream at `~/bridge/streams/tcgbot.jsonl`.
Writes instructions via `send_to_rw_group.sh` → Telegram group chat.
Tcgbot reads from group and responds there.