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