# Agent log

_Older entries in docs/archive/AGENT_LOG_ARCHIVE.md._

**Crash-safety design note:**
`Save()` runs inside a single SQLite transaction (`BeginTransaction` → writes → `Commit`, with `Rollback` on exception). SQLite's atomicity guarantees that a process killed between `BeginTransaction` and `Commit` rolls back to the last committed state on next open. The tests simulate exactly that kill and assert the rollback.

**Evidence:**
- 387/387 engine tests green (+6 from P4-03 baseline: 4 version tests, 2 crash-safety tests, plus roundtrip/twice-save/empty-db)
- Client builds clean (0 errors)
- Exported Linux build (`--headless --export-debug`): ran under Xvfb with MapScene as main, confirmed `runewake_save.db` created at `~/.local/share/godot/app_userdata/Runewake/` (the `user://` target) with all 7 tables and `integrity_check = ok`. Path is `user://`, not `res://`, not an absolute path.
- Android APK export: `Runewake.Persistence.dll`, `Microsoft.Data.Sqlite.dll`, and native `libe_sqlite3.so` (arm64) all confirmed bundled — SQLite stack works on device.
- project.godot `[display]` section restored after editor export (stripped on exit).

**What's next:** P4-05 — Deck builder screen with collection filtering.

---

## Session 28 — 2026-08-10

### Art direction — duel screen rendering fix + project settings guard

**BoardWrap removed (device invisible-layout fix):**
- `BoardWrap (PanelContainer)` removed from DuelScene.tscn — prime suspect for board+hand rendering black-screen on device
- BoardBg and Board (VBoxContainer) promoted to direct children of DuelScene with `anchors_preset=15` + `offset_top=40` / `offset_bottom=-160`
- Removed the `StyleBoxFlat` stone-slab styling in `_Ready()`
- All `GetNode("BoardWrap/...")` paths updated to `GetNode("Board/...")`
- APK verified: board and hand render correctly on device

**Critical project settings restored (regression catch):**
- `window/size/viewport_width=1152`, `window/size/viewport_height=648`, `window/handheld/orientation=0`, `window/stretch/aspect="expand"` were missing from project.godot since commit `51b6296` — restored from git history
- Added `AssertProjectSettings()` in Main.cs — startup assertion checks all 5 critical settings and logs `GD.PrintErr` on any mismatch
- TECH_DEBT.md updated: anchor/offset confusion (2 instances) and project settings silent drop documented with fix patterns

