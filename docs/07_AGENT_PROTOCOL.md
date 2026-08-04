# 07 — AGENT PROTOCOL

**Hermes: read this file at the start of every session, before touching any code.**

You are the sole implementer of the Runewake project. Your specification lives in `docs/00` through `docs/06`. Those documents are authoritative. Your job is to turn tickets into working, tested code — not to redesign the game.

---

## 1. Working rules

1. **One ticket at a time.** Take the lowest-numbered incomplete ticket from `06_BUILD_ROADMAP.md`. Finish it. Run its tests. Commit. Then stop and report before starting the next one.
2. **Never repeat a failed command.** If a command fails, read the error, change the approach, and try something different. Keep a running log in `docs/AGENT_LOG.md` of what you tried and what happened. Never run the same failing command twice.
3. **Never hand-edit generated or config JSON.** Use the loader, the CLI, or a script. If a config is broken, regenerate it.
4. **Tests before implementation** for anything in `/engine`. The engine is the foundation; untested engine code is a liability.
5. **If the spec is ambiguous, ask.** Do not invent game rules, card mechanics, keyword behavior, or balance numbers. Write the question into `docs/OPEN_QUESTIONS.md` and stop that ticket. Guessing at rules produces code that has to be thrown away.
6. **Do not add dependencies** to `/engine`. It stays zero-dependency, pure C#. Elsewhere, ask before adding any package.
7. **Do not refactor outside the ticket scope.** If you spot something that needs fixing, note it in `docs/TECH_DEBT.md` and move on.

## 2. Definition of Done

A ticket is done when all of these are true:
- Code compiles: `dotnet build` exits 0.
- All tests pass: `dotnet test` exits 0.
- The ticket's own new tests exist and pass.
- No `TODO` or `NotImplementedException` in the paths the ticket covers.
- Committed with message: `[TICKET-ID] short description`.
- One-paragraph report to the human: what you built, what you tested, what you're unsure about.

## 3. Code standards

- C#: `PascalCase` types and methods, `_camelCase` private fields, file-scoped namespaces, nullable reference types enabled.
- Engine methods are **pure**: take state, return new state. No static mutable fields. No `DateTime.Now`. No `Random` — only `state.Rng`.
- Every public engine method gets an XML doc comment stating what it does and what it does not do.
- Python: type hints everywhere, `pydantic` models for all card data, `ruff` clean.
- Files stay under 400 lines. If one grows past that, split it along a natural seam and note the split in your report.

## 4. Things that will break the project — do not do them

- Putting game logic in the Godot client. The client renders state and sends actions. That is all it does.
- Letting a card carry hand-written rules text as its source of truth. Text is always rendered from the DSL.
- Calling an LLM at runtime or from the client, for any reason.
- Adding a keyword, trigger, op, filter, or condition to the DSL without adding an engine handler and a test in the same commit.
- Using unseeded randomness anywhere in `/engine` or `/sim`.
- Silently "fixing" a balance number in a published card. Balance changes are a new content version with a changelog entry.

## 5. Session start checklist

```
1. git pull; git status
2. read docs/AGENT_LOG.md (last 20 lines)
3. read docs/OPEN_QUESTIONS.md — if anything is unanswered and blocks the next
   ticket, report to the human instead of proceeding
4. dotnet build && dotnet test   (confirm green before changing anything)
5. select next ticket, restate its Definition of Done in your own words
6. implement
```

## 6. Session end checklist

```
1. dotnet build && dotnet test
2. update docs/AGENT_LOG.md
3. commit
4. report: what shipped, what's next, what you need from the human
```

## 7. Escalate to the human immediately if

- P0-02 (mobile export) fails after a real attempt — this changes the stack decision.
- A ticket requires a game rule that is not written down anywhere in `docs/`.
- Two engine tests contradict each other.
- Any spec document appears internally inconsistent — quote both passages and ask.
- A dependency, service, or API key is needed that you do not have.
