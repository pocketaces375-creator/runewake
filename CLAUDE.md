# Runewake — Project Context

This is an Old School RuneScape-inspired game project.

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