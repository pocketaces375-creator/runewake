# Crash Reporting (P7-06)

## Sentry Setup
Crash reporting integrated via Godot Sentry SDK (`godot-sentry`).
Config in `client/scripts/CrashReporter.cs`.

- DSN: Configured in `project.godot` under `sentry/dsn`
- Environment: `production` or `debug`
- Release: set to git commit hash at build time
- Events captured: unhandled exceptions, GD.PrintErr calls, engine crashes
- User context: attached when Supabase auth is available (player_id)

## Testing
```bash
# Trigger a test crash in debug builds
# Tap settings icon 5 times → "Crash Test" button appears
```

## Known Issues
- Offline crashes are queued and sent on next online session
- Stack traces from exported builds are less detailed (IL2CPP stripping)
- Symbol uploads not yet configured for deobfuscation