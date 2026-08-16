# Capture Recipe — Runewake

## Proven Invocation (DEBUG build only)

```bash
GODOT_BIN="$HOME/.local/bin/godot"

# Build C# project first (Release DLLs cause Sqlite mismatch — must use Debug)
dotnet build client/Runewake.Client.csproj -c Debug

# Run capture via xvfb-run with real OpenGL/Vulkan renderer (NOT --headless)
# The --path client flag is REQUIRED — project.godot lives in client/
xvfb-run -a "$GODOT_BIN" --path client -- --capture=duel_test
```

## Capture modes

| Flag | Description |
|------|-------------|
| `--capture=duel_test` | Full duel scene with hand + board + shrine + art |
| `--capture=deck_test` | Deck builder Ancient Tome with 31-card seeded deck |
| `--capture=title_deck` | Title screen → navigate to deck builder |

Output: `artifacts/captures/<mode>.png` + `artifacts/captures/<mode>.meta.json`

## Gate

```bash
python3 tools/capture_gate.py
```

Reads all three captures from `artifacts/captures/` and validates:
- Hand/board card counts
- Pixel luminance thresholds
- Text contrast ratios
- Pairwise overlap checks (AABB per group)
- Hand cards clear of player slots (min 12px gap)

## Known Failure Modes

1. **Stale build-solutions lock:** If a previous `--build-solutions` or editor instance crashed, it may hold a lock. Kill all godot processes older than 10 minutes before retrying:
   ```bash
   ps -eo pid,etimes,args | grep godot | awk '$2 > 600 {print $1}' | xargs kill -9
   ```

2. **Release vs Debug DLL gap:** The Sqlite NuGet package resolves differently in Release vs Debug. Godot C# requires Debug configuration. If captures hang or crash, verify the build was `-c Debug`.

3. **`--headless` dummy renderer:** Godot's `--headless` flag uses a dummy OpenGL driver that produces all-black/zero-pixel renders. Always use `xvfb-run` with no `--headless` flag for real pixel captures.

4. **Missing `--path client`:** The project file `project.godot` lives in the `client/` subdirectory. Running Godot from the repo root without `--path client` gives "Can't run project: no main scene defined".

5. **First-run C# compilation:** Godot needs to compile C# assemblies on first launch. This can take 30-60s. Subsequent runs are faster. If it hangs > 5 minutes, kill and ensure the C# project has been built with `dotnet build -c Debug` first.