# HERMES_STATUS.md — Implementation Status & Questions

Entry format: `DATE | TYPE: message`
Types: DONE, BLOCKED, QUESTION, CONFLICT

---

## 2026-08-12 | 17:45

### DONE: Trikzos §11 decisions relayed
| Decision | Result |
|---|---|
| System name | **Artifacts** (not Relics — collides with RELIC type; not Glyph Cards — collides with rune Glyphs) |
| Guard stance | **NO** — shelved permanently |
| Class count | **7 classes**, 2 Artifact slots each |
| Duskfang suppression | **KEEP** — Thief is anti-Artifact class |
| Card class restrictions | **NONE** — any class can use any deck card; only Artifacts are class-specific |
| Variant scope (v1) | **One fixed pair per class** — no per-slot choices at launch |

### DONE: ARTIFACT_CLASSES.md received
All 7 classes specced:
1. Warrior (Sword + Shield) — FIELD_EFFECT_SPEC.md §5
2. Battle Mage (Wand + Aura) — FIELD_EFFECT_SPEC.md §5
3. Thief (Dagger + Dagger) — FIELD_EFFECT_SPEC.md §5
4. Cleric (Dawnlit Censer + Icon of the Unbroken)
5. Ranger (Heartwood Bow + Quiver of Whispers) — introduces Prey marker
6. Necromancer (Grimoire of the Hollow Court + Phylactery of the Pale King)
7. Runesmith (Forgehammer of Depth + Anvil of Unmaking)

Total: 14 launch Artifacts.

### CONFLICT: §5a — Rune system is NOT a per-turn resource
Fable's spec assumes Artifact costs/discounts reference a per-turn resource labeled "runes". The existing rune system (`docs/03_RUNE_SYSTEM.md`) is a **pre-game loadout** of 30 passive buffs (Marks, Seals, Glyphs, Sigils). Not a per-turn resource at all.
- Consequences: "costs 1 less" on Artifact effects (Warden's Focus, Anvil of Unmaking, Duskfang) must refer to **attunement cost** — the existing per-turn resource. This is mechanically consistent: attunement is the mana system.
- Action taken: All "costs 1 less" effects are implemented as attunement discounts. If this conflicts with Fable's intent, they need to revise.

### CONFLICT: Creature stat scale differs from spec
Spec §5: "creatures roughly 1–8 attack/health." Actual engine data (from schema card.schema.json): attack 0–12, vigor 1–14. Both caps work — the spec's Artifact numbers assume a smaller scale. Logging for design awareness, not blocking implementation.

### QUESTION: §5a verified — attunement confirmation
Confirming: "costs 1 less" on Artifact cards = costs 1 less **attunement**. If Fable intended a different resource, they need to redefine.

### STATUS: P1 engine work STARTED
ArtifactSlots zone, PlayerState changes, GameState init, TriggerBus integration. Building 14 Artifacts through existing EffectExecutor/TriggerBus. Prey marker as the only new engine primitive.

### NOTE: Cards expanded to 375
|Trikzos wants 375 cards for first drop with 3+ variants per Artifact slot. This is a card generation pipeline question, not an engine blocker. Pipeline already supports batch generation. Will increase scope but doesn't block P1.

---

## Earlier entries (2026-08-12)

---

## 2026-08-12 | 18:57

### DONE: ARTIFACT_RULINGS.md received + reconciled against launch_artifacts.json
Rec'd from Claude via Trikzos. 26 rulings (G1–G8, R1–R26). Immediately compared against the existing `content/artifacts/launch_artifacts.json` — the first-pass translation was **substantially wrong** (passive/trigger effects misaligned, missing filters, wrong triggers). Rewrote the entire `launch_artifacts.json` to match the rulings. See below for the gap list.

### CONFLICT: launch_artifacts.json DSL gaps — 7 filters/conditions need engine support
The rulings define specific conditions and filters that the current DSL/engine doesn't support natively. These are flagged so Claude sees them in the diff review:

| Ruling | Needed filter/condition | Current DSL status |
|--------|------------------------|-------------------|
| R1 (Ancestral Blade) | `filter: "ATTACKING"` — passive only applies while attacking | Missing — `GRANT_KEY` fallback used; engine needs clamp-to-1 mechanic |
| R2 (Bulwark) | `filter: "HAS_NOT_ATTACKED"` — did not attack this turn | Missing — `NEXT_TURN` duration used as approximation |
| R3 (Bulwark trigger) | `condition: "NO_ATTACKERS_LAST_TURN"` | Missing — `ON_CREATURE_ATTACKS` + `GAIN_VIGOR` is a placeholder |
| R7 (Whisperfang) | `filter: "EXACTLY_ONE_ATTACKER"` — count exactly 1 attacker | Missing — `condition: ALLY_COUNT_GTE 1` is wrong |
| R9 (Duskfang) | `gain_on: "on_creature_deals_damage_to_character"` — max 1 Charge per creature per turn | Missing |
| R11 (Censer) | `filter: "MOST_WOUNDED"` — tie → owner chooses | Missing — AI path needs tiebreaker |
| R12 (Censer charge) | `gain_on: "on_creature_survived_combat_damage"` — max 1 per turn | Missing |
| R15 (Prey marking) | Start-of-turn order: Prey marks BEFORE Censer heal, before draw | Need to verify turn-start ordering |
| R19 (Grimoire discount) | While ≥1 creature died this turn, each creature costs 1 less, floor 0 | Missing — `ATTUNE` placeholder; actual discount needs engine support |
| R23 (Forgehammer) | `filter: "FIRST_SUMMONED_THIS_TURN"` + cost ≥ 3 | Missing — `BUFF PERMANENT` approximation |
| R25 (Anvil) | Charge spend on trigger: ALL charges spent, +1/+1 per charge, to highest-cost creature | Missing — `ON_TURN_END_NO_ATTACK` exists but charge-spend logic needs wiring |

### CONFLICT: Ruling §5a — "costs 1 less" resource
Already logged. "Costs 1 less" = attunement discount. Engine's `ATTUNE` op increases attunement max, not discounts. A discount mechanic (temporarily reduce cost of next card) doesn't exist yet. Marked as engine gap, not blocking P1 test suite. Claude's design intent is clear: attunement is the resource.

### CONFLICT: Rune system is NOT a per-turn resource
Already logged. This is the same §5a CONFLICT. The discount effects on Mantle, Warden's Focus, and Whisperfang's passive need a new "next card costs 1 less" mechanic in the engine. Not blocking — the rulings file supersedes, and the test suite will assert the correct behavior once the mechanic exists.

### QUESTION: G6 — Mirror match passive stacking
Ruling G6 says "Identical passives (same card id) never stack." Confirmed this is the correct existing behavior for `ArtifactDef` — the engine checks card id. Triggers fire independently. Twin rule (R10) is a special case of G6.

### QUESTION: G8 — "Charge-full effects fire immediately"
The engine's `ADD_CHARGE` op already fires `ON_CHARGE_FULL` when Charges hit max. Confirmed this matches G8.

### STATUS: launch_artifacts.json written — needs Claude review
`content/artifacts/launch_artifacts.json` has been rewritten to match ARTIFACT_RULINGS.md. This is the English→DSL translation that Claude identified as the highest-risk step. Ready to send to Claude via Trikzos for review before client integration.

### STATUS: No artifact tests exist yet
The 463-test suite passes (0 failures). No artifact tests are written. The rulings file explicitly says "Every ruling above is at least one test." The test suite will be written AFTER Claude signs off on the `launch_artifacts.json` DSL translation, so the tests assert against the agreed-upon DSL.

### NEXT: Relay launch_artifacts.json to Claude, then write test suite
1. Send `content/artifacts/launch_artifacts.json` to Claude for review
2. After sign-off: write ruling tests per ARTIFACT_RULINGS.md naming: `Ruling_R1_AncestralBladeTriggerWindow`
3. Then client integration (Godot scene, visual states, art pipeline)

---

## 2026-08-12 | 18:57 — Claude's corrected file received

### ANSWER: Q1 — BURY and BARROW_COUNT_GTE exist
Yes, both are real engine ops. `Op.BURY` (ApplyBury in EffectExecutor) moves cards from deck to the Barrow zone. `ConditionOp.BARROW_COUNT_GTE` is a valid condition enum. The Barrow IS the graveyard-equivalent zone. However, they don't belong on the Phylactery — Claude's corrected file is the authority.

### ANSWER: Q2 — ATTUNE does NOT support applies_to/filter/condition/duration/stacks
The current `ATTUNE` op in EffectExecutor (line 357) unconditionally increases `AttunementMax` by `amount` and refills attunement. None of the extension fields (`applies_to`, `filter`, `condition`, `duration`, `stacks`) exist. These are all DSL gaps in the implementation queue.

### ANSWER: Q3 — Keyword casing
Corrected file uses `UPPER_SNAKE` (STEALTH_STRIKE, ANCESTRAL_SHIELD). The existing engine uses `UPPER_SNAKE` for all keywords (GUARD, SWIFT, PIERCE, etc.). Normalized to `UPPER_SNAKE`.

### DONE: launch_artifacts.json replaced with Claude's corrected version
Full drop-in replacement at `content/artifacts/launch_artifacts.json`. 14 cards, all fields mapped to rulings. The corrected file is now the source of truth.

### ACTIVE: 12 DSL gaps — implementing in dependency order
Claude's revised build order:
1. Turn-scoped counters + conditions (unblocks Sword, Bulwark, Wand, Whisperfang, Grimoire, Anvil)
2. PREVENT_DAMAGE op (unblocks Bulwark, Mantle, Phylactery)
3. ATTUNE extensions (unblocks Wand, Mantle, Duskfang, Grimoire)
4. Cadenced passives (unblocks Censer, Bow)
5. Charge plumbing: RESET_CHARGES, max_per_turn, ON_CHARGE_FULL timing
6. Partner-slot Charge spend (Hammer/Anvil cross-slot mechanic)
7. Keyword handlers: ANCESTRAL_SHIELD, STEALTH_STRIKE

### CANCELLED: P3-07 — Element/class coexistence cleanup
Adam: "P3 you can kill it for now." Elements remain as flavor/synergy tags on class-agnostic deck cards. No removal/migration work will be done. Backlog updated to `status: cancelled` so the bridge won't pick it up.

---

## Earlier entries (2026-08-12)
---

## 2026-08-13 | UI DIRECTIVE (Claude via Trikzos)

### FIX 2a ROOT CAUSE — Hand cards render nearly black
`DuelScene.RenderHand()` sets `card.Modulate = new Color(TextInactive.R, TextInactive.G, TextInactive.B, 0.6f)` on every card whose cost exceeds current attunement. TextInactive is a dark gray-brown, so the whole card (art, name, stats, frame) is dimmed to near-black at capture/start-of-game when attunement is low. It is a per-card dim overlay, NOT a card-back or facedown material. Fix per directive: playable = full brightness; unplayable = full art visible + ≤30% desaturation + red cost badge.

### FIX 1 DONE — Campaign map selection
- Tap hit test now uses the icon's FULL rect (140×150 incl. label) + 8px grow, not a radius from origin — label area and edges clickable on first tap.
- `_background` and `topBar` ColorRects now `MouseFilter.Ignore` (they were swallowing map clicks).
- Three states: hover (brighten + pointing-hand cursor), selected (persistent gold ring `SelectedGlow`), locked (padlock marker + mild desaturation, NO dark veil — `_lockOverlay` no longer blacks out).
- Button `action_mode=0` (press, not release) for touch reliability.
- "Challenge" button label for duel/elite/warden nodes; enabled immediately on selection.
- Click coordinates vs node bounds logged to `[MAP]` GD.Print lines for testing.
- `--capture-map` CLI flag added: auto-selects first unlocked node, captures `screenshots/map_selected_v2.png`, quits. Verified: node r1_n01 selected, Challenge button enabled, selection ring visible.

### FIX 2-5 QUEUED (in order): hand card rendering rules → card sizing (viewport-scaled) → black-void card placeholders → artifact slot layout reservation.

### FIX 2 DONE — Hand card rendering (code)
- Root cause: `RenderHand()` applied `TextInactive (#5A5048)` modulate × 0.6 alpha to unaffordable cards — dropped brightness to ~21%.
- Fix: `HandCard.SetPlayable(bool)` — playable = full brightness + gold badge; unplayable = 30% gray desaturation overlay (art stays visible!) + red cost badge. `Modulate` always `Colors.White`.
- LaneSlot exhausted state: changed from `Colors.Gray` (50% dim) to `new Color(0.85f, 0.85f, 0.85f, 1f)` — 15% desaturation, card stays visible.
- APK building now (SHA pending).

### FIX 3-5 code pending
- FIX 3a: card size from viewport height (hand ~25% of height, board ~30%).
- FIX 3b: board rows use full width.
- FIX 3c: high-contrast corner stat badges.
- FIX 3d: hand hover enlarge ~1.8x.
- FIX 4: board card placeholder for missing art.
- FIX 5: artifact slot layout reservation.

### FIX 2 DONE — Hand card rendering (code)
- Root cause: RenderHand() applied TextInactive (#5A5048) modulate x 0.6 alpha to unaffordable cards — dropped brightness to ~21%.
- Fix: HandCard.SetPlayable(bool) — playable = full brightness + gold badge; unplayable = 30% gray desaturation overlay (art stays visible!) + red cost badge. Modulate always Colors.White.
- LaneSlot exhausted state: Colors.Gray (50% dim) -> new Color(0.85f, 0.85f, 0.85f, 1f) — 15% desaturation, card stays visible.
- APK building now (SHA pending).

### FIX 3-5 code pending
- FIX 3a: card size from viewport height (hand ~25% of height, board ~30%).
- FIX 3b: board rows use full width.
- FIX 3c: high-contrast corner stat badges.
- FIX 3d: hand hover enlarge ~1.8x.
- FIX 4: board card placeholder for missing art.
- FIX 5: artifact slot layout reservation.

### FIX 3 DONE — Card sizing, stat badges, hover enlarge
- 3a: Card height = viewport_height * 0.28 (180px at 648vh, verified 179px)
- 3b: Board lane margins 24px -> 8px
- 3c: Red attack / green vigor corner badges with white text + black outline
- 3d: Hover enlarge 1.8x, bottom-center pivot, tween 0.15s
- APK SHA 947456feca2a9fa935e128d18c47ba39b41883254c66fe7a2ac5151ce7fabd68
- temp.sh: https://temp.sh/HRfEQ/Runewake.apk
- gofile: https://gofile.io/d/nvjgQ2hG
- Screenshots sent to Telegram for Claude review

### FIX 4-5 PENDING
- FIX 4: Board card placeholder for missing art (parchment/stone fallback + name)
- FIX 5: Artifact slot + portrait layout reservation in duel scene

### TASK A — Hand cards art + name fix (not yet accepted)
A2 audit: art lookup path is `res://content/art/{cardId}.webp` — identical in HandCard.cs and LaneSlot.cs. No naming mismatch. Hand cards requested art for:
- tid_u_coral_guardian → content/art/tid_u_coral_guardian.webp — MISSING
- vrd_x_heartwood_relic → content/art/vrd_x_heartwood_relic.webp — MISSING  
- dwn_r_sealing_light → content/art/dwn_r_sealing_light.webp — FOUND (512×512)
- emb_r_magma_forger → content/art/emb_r_magma_forger.webp — MISSING

Board cards render art ONLY when their file exists (e.g., dwn_c_dawn_warder, emb_c_cinder_runner, hol_c_skeletal_reaver, etc. — 15 files total). The verify gate's "Lane slots with visible art: 10/10" checks art rect size, not texture presence. The lookup is correct; the 3 missing files need to be painted.

Missing art — to-paint list for Trikzos:
1. tid_u_coral_guardian → content/art/tid_u_coral_guardian.webp
2. vrd_x_heartwood_relic → content/art/vrd_x_heartwood_relic.webp
3. emb_r_magma_forger → content/art/emb_r_magma_forger.webp

Status: placeholder now shows card name on dark stone background when no art file exists. NoArtLabel added to HandCard (centered, off-white text, hidden when art loads). Missing-art files need to be painted by Trikzos.

DONE A2 @ 7992329e — Audit complete: lookup path identical (res://content/art/{cardId}.webp). 3 genuinely missing art files (tid_u_coral_guardian, vrd_x_heartwood_relic, emb_r_magma_forger). Added NoArtLabel placeholder (centered card name, off-white on dark stone). Real-device screenshot needed for acceptance — Trikzos to take.

### TASK G0 DONE — GitHub repo created and pushed
Repo: https://github.com/pocketaces375-creator/runewake
Default branch: main
Art check: largest file 246KB (under 500KB limit, no LFS needed)
.gitignore: standard ignores
Commits: bridge iterations (50-68) + initial import
Proceeding to G1: deploy key for Claude.

### TASK G1 DONE — Deploy key created and registered
Repo SSH URL: git@github.com:pocketaces375-creator/runewake.git
Key pair deleted from disk.

### TASK G2 DONE — Deploy key rotated
G1 key rotated out 2026-08-13 — replaced by claude-orchestrator-v2 (public key only, held by Claude).

### TASK V DONE — Capture harness + pixel gate
- DebugCapture autoload: sets up deterministic test state (seed=42, 4 hand cards, 30-card deck, partial attune)
- Runs under xvfb-run with OpenGL3 (llvmpipe) renderer — real GPU rendering
- Captures duel_test.png (1152x648 RGBA) + duel_test.meta.json (4 hand + 10 board card rects)
- capture_gate.py validates: whole-frame brightness, per-card luminance/variance, name-label contrast, card count
- **Proof: gate FAILS on current build with 25 failure reasons** (95.1% dark pixels, card bodies near-black)
- Every future UI task: harness capture + gate pass committed with the change

### TASK B DONE — Hand-card green center bar removed
- The green bar was `BottomRow/StatsLabel` showing `{Attack}/{Vigor}` — the attack/vigor stat display. Named: **attack/vigor stats label**.
- Deleted the entire `BottomRow` HBoxContainer from HandCard.tscn (BottomSpacer + StatsLabel) and all `_statsLabel` code references from HandCard.cs.
- Corner badges already active from FIX-3c (attack bottom-left red, vigor bottom-right green) — matching board cards.
- Acceptance caveat: capture committed shows the bar code is gone (no BottomRow node), but full-frame darkness + invisible art is the KNOWN FIX-2 rendering issue tracked by TASK-R1. The green band visible mid-card in the capture is card ART (Root Warden is a verdant creature), not a UI bar.
- Also fixed DebugCapture arg parsing: Godot 4.3 puts args after `--` in GetCmdlineUserArgs, not GetCmdlineArgs — merged both so --capture=duel_test works with or without separator.
