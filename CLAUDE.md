# Runewake — Project Context

Runewake is a high-fantasy Trading Card Game with an Artifact system and a War Altar battlefield board. NOT an OSRS-inspired project.

## PARTNERSHIP
Hermes is a full partner with capabilities Claude lacks (FLUX image gen, Gemini 2.5 Pro, builds, captures, Telegram, push). Claude delegates whenever Hermes has the better tool. Claude designs and reviews; Hermes executes and paints. Neither reconstructs docs from memory — flag, don't fabricate.

## Handoff Protocol
- Fable (designer) writes intent to NOTES_FOR_HERMES.md
- Hermes (this agent) reads NOTES_FOR_HERMES.md as instruction source
- Hermes writes status/questions to STATUS_FOR_FABLE.md
- Clean lanes: no file clobbering between Fable and Hermes

## Trusted Directories
The following directories have been allowed for tool access:
- /home/fictive/runewake/ (full project root)

## Workflow
1. Fable updates NOTES_FOR_HERMES.md with design intent and tasks
2. Hermes reads NOTES_FOR_HERMES.md, implements tasks
3. Hermes writes progress and questions to STATUS_FOR_FABLE.md
4. Repeat

## Standing Lessons (from BORDER-FIX-2)
1. **A gate is a floor, not a proof.** Before marking any visual task DONE, look at the capture as an image and describe what a player would see. If the description does not match the task's goal, it is not done.
2. **Re-verify from zero when you change rendering mid-task.** When you change from one approach (TextureRects → StyleBox → NinePatchRect), your earlier measurements no longer apply to the new approach. Every approach change requires a full re-verification, not just delta-checking.
3. **A task is done when tools/finish_task.sh exits 0.** Chat-session work must end by running it too — a report in the group without a finish_task.sh run is not done.
4. **Every patch ends with an APK that is worth opening.** Run `bash tools/ship_apk.sh --note "<what to look at>"` after every patch and post the link together with `artifacts/SHIP_REPORT.md`. The script sorts checks into two tiers and that distinction is the whole point. BLOCKING — the build, engine tests, campaign loop, tutorial script, UX walkthrough, input smoke, loop smoke, and intact-zip / valid-signature / landscape — predict a build that cannot be played; if one is red, report what broke INSTEAD of delivering, because a dud costs Trikzos more than no build does. ADVISORY — label_fit, capture freshness, the visual gate, art file sizes — is cosmetic or tooling and never withholds a playable build; it goes on the label instead. `--anyway` forces an override build and stamps it as one. Two opposite failures to avoid repeating: between 2026-09-18 and 2026-09-20 he got no build at all, because `export_and_verify.sh` exits on the first red check and every red check was cosmetic — a Continue-button bug then survived four rounds untested because nobody could press the button. Shipping regardless is the mirror-image mistake: it moves the wasted trip onto the one person on this project who cannot be replaced. Shipping and marking done are separate decisions — `finish_task.sh` stays strict about "done", this decides "is it worth his thumb". A blocking check that was already red before the work, and is not what stands between him and a playable build, goes in `tools/ship_known_failures.json` with a reason, an owning task and an expiry date; it then reports loudly on every run instead of withholding. The build, the engine tests, input smoke, loop smoke and the three install-critical APK facts are unwaivable and `ship_apk.sh` refuses to run if that file tries — those checks ARE the promise, and waiving one would make the promise a lie.5. **A check that simulates the outcome of a control has not tested the control.** Soak mode navigates the end-of-duel screen by calling `ChangeSceneToFile` on a timer, so `loop_smoke` has only ever proved that the scene *after* Continue loads. It has never once pressed Continue. Three separate button bugs shipped green underneath it: covered by an opaque node (FABLE-005), freed between touch-down and touch-up so `BaseButton` never paired the press (FABLE-012), and throwing inside the handler where Godot swallows the exception (FABLE-011). When a check stands in for a player action, write down what it does NOT cover, and treat "the test drives the code the handler calls" as untested until something drives the handler.
6. **Diagnose which build the report came from before diagnosing the report.** On 2026-09-20 Trikzos reported three faults — dead defeat buttons, a freeze, a still-static title screen — all of which had already been fixed in the repo. His APK was from 2026-09-18 and predated every fix; the build pipeline had been withholding since. Before debugging a device report, establish the APK's commit. `artifacts/SHIP_REPORT.md` now carries it, which is most of why `ship_apk.sh` exists.
7. **A capture proves the code runs on the build machine — not that it runs on the phone.** Three rounds of title captures showed a turning vortex while Trikzos's phone showed a still picture: both layer PNGs sat in `content/art/title/layers/`, which `export_presets.cfg` excluded from the APK. The first accounts build passed 21/21 smoke checks from a desktop while every request on the phone failed: the Android preset had `permissions/internet=false`. Anything that differs between the project folder and the APK — the export filter, the manifest, permissions, signing — is invisible to every capture and every desktop test. `tools/export_exclusion_check.py` (blocking) and `tools/android_manifest_check.py` (advisory) now check the two that bit us; when a device report contradicts a capture, suspect the export before the code.
