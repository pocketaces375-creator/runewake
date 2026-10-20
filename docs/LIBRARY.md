# The Runewake media library

Every picture, painting, mock-up and piece of music made for Runewake — used or still waiting for a place —
lives in one library (Trikzos, 2026-10-20: "save all current media that we've used and are using somewhere,
cause we'll always find a place for everything").

- **Where:** the private GitHub repo `pocketaces375-creator/runewake-library` (start at its `CATALOG.md`), built
  on the mini PC in `~/runewake_library`.
- **What:** `in_game/` (everything in `client/content/art` and `client/content/audio`), `candidates/` (everything
  under `~/runewake_art_archive`: generator output, mock-ups, music takes, map and button candidates), `meta/`
  (art ledger, spend log). Each item is marked *in game*, *kept (not in the APK)* or *candidate*.
- **Rule:** every generator saves its output under `~/runewake_art_archive`; after anything new is made or
  installed, run `python3 tools/media_library.py build && python3 tools/media_library.py publish`.
