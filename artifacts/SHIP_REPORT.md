# Ship report

> ✅ **Every check that predicts a working build passed.** Anything listed
> under Advisory is cosmetic or tooling and does not affect play.

| | |
|---|---|
| apk | `Runewake.apk` (216.1 MB, release) |
| sha256 | `c0b75963d141674c126e6347096be93c67bc4de826e23b953e2529fd6f1d29af` |
| commit | `5fc0261a` |
| code fingerprint | `9a35ac020b02` |
| loop smoke says playable | True |
| built | 2026-09-25 12:26 EDT |
| look at | FABLE-033 one-screen victory; drops live; versionCode 33 |

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

