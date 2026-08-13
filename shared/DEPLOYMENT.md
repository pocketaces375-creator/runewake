# Deployment & Build

## Build Types

### Android APK
```bash
cd ~/runewake
./client/export_apk.sh               # Production
./client/export_apk.sh --debug        # Debug
```
Output: `client/exports/Runewake.apk`
Expected size: 90-120MB (healthy), ~75MB (stale assemblies — FAIL)

### Linux Export (headless smoke test)
```bash
cd ~/runewake/client
godot --headless --export-debug "Linux/X11" exports/Runewake.x86_64
```

### C# Tests
```bash
cd ~/runewake
dotnet test tests/    # 463/463 passing
```

### Python Tests
```bash
cd ~/runewake/pipeline
python3 -m pytest tests/   # 221/221 passing (3 pre-existing failures)
```

### Content Pipeline
```bash
cd ~/runewake/pipeline
./run_e2e.sh          # Sources env vars first
```

## App Store Listings
- App name: Runewake: The Buried Age
- Privacy policy: https://runewake.com/privacy
- Age rating: 12+ (fantasy violence, no real gambling)
- Platforms: iOS (App Store), Android (Google Play)
- Status: Not yet submitted. Needs human to upload builds.

## Critical Build Gotchas

1. **Android export MUST use `export_apk.sh`.** Direct godot CLI call
   silently swallows dotnet publish failures, producing a ~75MB APK that
   crashes on launch. The script detects stale assemblies.

2. **Exported builds crash on `GlobalizePath`.** Never pass GlobalizePath'd
   paths to `System.IO.File.*`. Use `Godot.FileAccess.GetFileAsString("res://")`.

3. **API keys don't survive in subprocesses.** Pipeline modules need
   `OPENROUTER_API_KEY`. Must use `run_e2e.sh` wrapper or source env first.

4. **Content packs must be versioned.** Increment content version on every
   pipeline run. Version embedded in every match record for replay safety.