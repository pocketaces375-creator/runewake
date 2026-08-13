# System Architecture

## Directory Structure

```
~/runewake/
├── client/          # Godot 4.3 .NET project
│   ├── scripts/     # C# game logic (DuelScene, Main, CampaignContext, etc.)
│   ├── scenes/      # Godot .tscn files
│   ├── exports/     # Built APK / Linux exports
│   └── export_apk.sh  # APK builder with stale-DLL detection
├── engine/          # C# .NET 8 class library (Runewake.Engine)
├── tests/           # C# test project
│   ├── State/       # GameState initialization tests (doc-section-cited)
│   └── TurnLoop/    # Turn loop tests
├── pipeline/        # Python content generation pipeline
│   ├── modules/     # generate.py, validate.py, score.py, simulate.py, art.py
│   └── run_e2e.sh   # End-to-end runner with env sourcing
├── content/         # Generated card packs (JSON)
├── persistence/     # SQLite schema + save manager
├── schema/          # card.schema.json
├── sim/             # Headless simulation runner
├── tools/           # Utility scripts
├── docs/            # Game design specs (source of truth)
├── shared/          # This folder — comprehensive reference
├── backlog.json     # 51-item ticket backlog
├── PROJECT_STATE.md # Current phase state
└── Runewake.sln     # .NET solution
```

## Key Integration Points

### Engine → Client
Engine compiles to a .NET DLL. Client references it.
`(GameState, Action) -> GameState` is the contract.

### Content Pipeline → Client
Pipeline produces JSON card packs in `content/`. Client loads via
Godot `FileAccess.GetFileAsString("res://content/...")`.

### Backlog → Bridge
Bridge reads `backlog.json` and `PROJECT_STATE.md` to decide what to work on.
Director (Claude) selects open items, bridge dispatches to agent.

### Bridge → Agent
Bridge reads stream at `~/bridge/streams/tcgbot.jsonl`, writes instructions
via `send_to_rw_group.sh` to Telegram group chat. Agent sees them there.

## Known Gotchas

- **Android export:** Must use `export_apk.sh` not raw godot CLI — detects
  stale assemblies. Fresh DLL = 90-120MB APK. Stale = ~75MB, crashes.
- **Filesystem I/O in exports:** Never pass `GlobalizePath()` results to
  `System.IO.File.*`. Always use `Godot.FileAccess.GetFileAsString("res://...")`.
- **Anchor vs offset:** In .tscn files, pixel values go in `offset_*`, never
  `anchor_*`. Use `SetAnchorsPreset()` in code.
- **Project settings silently drop:** `project.godot` settings can vanish on
  commit. `Main.AssertProjectSettings()` catches this at launch.