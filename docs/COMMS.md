# Communications Protocol

## Delivery Rule

All Runewake artifacts, reports, captures, APK links, and scout digests **post to the Runewake group** (`telegram:Runewake`). The TCGBot DM is never a delivery destination.

## Enforcement

- `foreman.sh` uses `FOREMAN_TELEGRAM_TARGET` env var (default: `telegram:Runewake`).
- All Telegram sends route through `telegram_text()` / `telegram_photo()` in foreman.sh.
- No script in `tools/` or `pipeline/` hardcodes a DM chat ID.
- APK deliver scripts (`apk_deliver.sh`, `serve_apk.py`, `serve_apk_v6.py`) do not send Telegram messages — they upload files and print to stdout; the foreman or agent delivers the announcement.

## Exceptions

- `daily_scout.sh` logs to `docs/SCOUT_LOG.md` and prints to stdout; its cron job delivers the digest via the configured cron delivery target.
- If a brick arrives via DM, the agent responds in DM with a one-line pointer ("posted in Runewake") and delivers all artifacts to the group.