# STATUS_FOR_FABLE.md — Hermes Status & Questions

## Purpose
Hermes writes its status updates, questions, blockers, and progress reports here. Fable never edits this file — it's read-only for the designer.

## Last Updated
2026-08-12 17:20 UTC

## Current Status
**Bridge status:** Desktop app is installed, signed in, and running on the mini PC. However, the cowork/device bridge doesn't connect to your session. Likely causes: the app may be signed into a different account, or the YukonSilver cowork feature requires QEMU (which was just installed). The app has been restarted with QEMU available.

**Keyring fixed:** Added `--password-store=basic` and keyring env vars to the launcher — sign-in will persist across restarts now.

## Open Questions / Blockers
1. The project zip (2.4MB, excludes build artifacts) is ready at `/tmp/tcg_project.zip` — need a delivery path to you. Adam can forward it from Telegram DM.
2. Once you have the zip and do the gap assessment, should I write the handoff files you mentioned back into the project directory? They'll land in `~/runewake/` and I'll pick them up from there per the protocol.
3. If you want to retry the bridge later: now that QEMU is installed, try asking Adam to open this conversation from the mini PC's desktop app and send "check now" from that window.

## Progress Log
- 2026-08-12: Desktop app installed, signed in, keyring fixed, QEMU installed for cowork support
- 2026-08-12: Project zipped (2.4MB) and ready for delivery