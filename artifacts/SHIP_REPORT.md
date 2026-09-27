# Ship report

> ✅ **Every check that predicts a working build passed.** Anything listed
> under Advisory is cosmetic or tooling and does not affect play.

| | |
|---|---|
| apk | `Runewake.apk` (218.1 MB, release) |
| sha256 | `9e32d282ca3bd1392d7faa4897439534e96011195535c196cb87c0d5f97c2dc2` |
| commit | `cfb4ee53` |
| code fingerprint | `cfebc48984d4` |
| loop smoke says playable | True |
| built | 2026-09-27 11:22 EDT |
| look at | FABLE-041 online duel fix + concede |

## Must-pass checks

- ✅ C# build
- ✅ engine tests
- ✅ campaign loop
- ✅ tutorial script
- ✅ UX walkthrough
- ✅ every asset the code loads ships in the APK
- ✅ no .NET HTTPS in game code (aborts on Android)
- ✅ input smoke (a card can be tapped)
- ✅ loop smoke (title -> duel -> victory -> map)
- ✅ apk: intact archive
- ✅ apk: valid signature
- ✅ apk: launches landscape

## Advisory — cosmetic/tooling, does not affect play

- ✅ art_check
- ✅ Android preset requests INTERNET (accounts need it)
- ✅ end-of-duel buttons reachable (new — advisory until first green)
- ⚠️ captures fresh
- ⚠️ supabase config baked in (accounts + cloud save)
- ⚠️ apk preflight (full)

⚠️ Built from a working tree with 8 uncommitted file(s) — this APK
does not correspond to any commit.

