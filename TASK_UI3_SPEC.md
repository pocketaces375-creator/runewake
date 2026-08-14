# TASK-UI3 — The War Altar conversion (client layout, spec vs locked mockup v7)

Status: ready for queue insert AFTER the DSL series (or earlier if Trikzos wants the face before the heart). Target render: board_c7.png (Claude's mockup, provisionally locked by Trikzos 2026-08-14 — the "love the just recent rendition" + centered-name + art + atmosphere version). All geometry below in 1280×720 design units; scale from viewport height as established in FIX-3a.

## Sessions (foreman-sized; each commits + gates)

UI3a — Enemy HUD bar. Replace the enemy's board-side arsenal group + portrait with a 74px top bar: LEFT cluster = portrait chip (52×56) + stat chips (vigor red-tinted, attune, deck, barrow — 50×50 rounded, label under value); CENTER = enemy name (small-caps ~23px) over subtitle/title line; RIGHT cluster = two Artifact mini-cards (92×56: glyph + one-word name + charge pips bottom-right). All values live-bound. Remove enemy elements from the play area entirely.
Acceptance: capture shows populated top bar, NO enemy arsenal on the field, gate green (update meta.json rects: enemy group rect moves to the bar).

UI3b — The altar battlefield. Replace the two straight rows with facing arcs inside an altar ellipse: ellipse ~1240×418 centered under the bar (border #57492c, inner dashed ring, radial glow, inset shadow); 5 slots per side at 206×176, arc offsets — outer slots +34px vertical with ±4° rotation, second slots +8px ±2°, center flat; enemy arc top, player arc bottom, ~60px vertical gap at center. Empty slots keep their inset frames; faint rune glyphs (6, unicode runic block) spaced around the ellipse edge at low opacity.
Acceptance: capture matches mockup geometry within tolerance; gate green with occupied-slot checks passing on the arcs.

UI3c — Player shrine. Bottom-left group (anchored 12px left, 40px up from bottom bar): two Artifact cards at 86×120 (glyph + one-word name + 3 charge pips, gold-glow border #8a763c) + compact column: portrait 46×58, deck 42×50 + barrow chip 42×50 side by side, vigor number under. Hand recentered beside it (cards 104×152, hover-raise retained); auto-shrink hand name text so full names fit (fix the "dal Schol…" truncation — minimum 8px, then ellipsize).
Acceptance: capture shows shrine + full hand, zero overlaps (add an overlap assertion to the gate: no two meta rects from different groups may intersect).

UI3d — Atmosphere pass. Layered lighting: warm ember radial glow lower-left, cool moon glow upper-right, mist band across mid-field, vignette; 6–8 dust motes (1–3px, slow drift if cheap, static if not); card shadows deepened; contested/playable slot glow retained. All values in a theme resource so art can retune without code.
Acceptance: capture visibly matches board_c7 mood; gate luminance thresholds re-verified (atmosphere must not push occupied-slot checks under limits — if it does, adjust atmosphere, not thresholds).

## Rules
One session per sub-task. Never edit game logic — this is pure presentation. Meta.json and the gate evolve WITH each sub-task in the same commit. The mockup is the authority for geometry; HERMES_STATUS conflicts if the engine makes something impossible.
