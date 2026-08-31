# 05 — Art Style Guide, Factions, Lore, Naming

Canonical: `docs/ART_STYLE_SPEC.md` (v3.0, LOCKED by Trikzos 2026-08-17 — do not relitigate), `docs/ART_WAVES.md`, `docs/04_WORLD_AND_MAP.md`. This file summarizes; the spec is verbatim law for prompts.

## Style lock: "Storybook Brushwork" v3.0

- **Anchors (reference by name in every prompt):** PRIMARY *Bloomweaver* (`vrd_r_bloomweaver`) — painterly brushwork, lush storybook quality, Van Gogh-adjacent stroke energy, classical-Renaissance composition and light. SECONDARY *Thornbark Defender* — thick impasto, heavy brushwork, earthy warmth, figure embedded in environment. TERTIARY *Verdant Sproutling* — quieter register, painted forest depth.
- **Hard rules:** hand-painted quality on every card (nothing flat, airbrushed, photoreal); high fantasy always, NO sci-fi ever; visible stroke texture and painted light; NO text/lettering/signatures rendered in art (check all four corners — probes shipped fake painted signatures; regenerate once, else flag in caption, never ship silently); never put a bare stratum name (EMBER etc.) in a prompt — the model paints the literal word.
- **Prompt spine (v3.0 FINAL, blend of probes A+B+C):** oil painting / classical storybook illustration / chiaroscuro / swirling brushwork reserved for skies-smoke-magic / single grounded focal subject, Renaissance tableau staging with breathing room / atmospheric depth / restrained palette with selective vivid accents / thick impasto, canvas texture, painterly edges. Trikzos: "touch more painterly than Wave 1."
- **Generator:** FLUX.2 Pro (`black-forest-labs/flux.2-pro` via OpenRouter), locked 2026-08-16. Known issue: ~30% moderation rejection on HOLLOW prompts (see 08-open-questions Q1).
- **The wave gate:** every batch = 6-card sample wave → numbered Telegram post to Trikzos → per-card veto/approve → approved samples become style refs for the next wave. Production STOPS at every wave end; never self-releases. The old 38-piece Ember/gemini batch is retired and never referenced.

## Stratum palettes (use plain color language, never the stratum word)

| Stratum | Palette language | Identity | Mechanical lean |
|---|---|---|---|
| VERDANT | deep forest greens, earthy moss browns, golden highlights | overgrown ruins, root and beast | big bodies, growth, adjacency |
| EMBER | charcoal greys, cool slate shadows, molten orange/gold flame | forge-holds, ash and iron | burn, Swift, sacrifice-tempo |
| TIDE | abyssal blue-teal, pale foam edges, sea-green light | sunken cities, drowned archives | draw, bounce, Excavate, delay |
| HOLLOW | bone-white, murky violet, sickly green, shadow-heavy | catacombs, rot, the unburied | death triggers, Unearth, drain |
| DAWN | warm cream, pale gold, soft amber, dawn-sky | temple wards, order, preservation | Guard, Ward, healing, taxing |

## The world (lore spine)

Something ended the world; survivors **buried** it under five strata and set Wardens to keep the seals. The seals fail; buried things wake as creatures; Delvers dig them up, bind them to rune-cards, and duel Wardens. The arc reveal: Wardens are the buriers' descendants, and the burial was probably right — the player's progression IS the transgression. This lore powers systems: Wardens guard seals → bosses drop rares; travelers trade what they carry → foes drop their own cards.

**Regions (v1):** 1 The Fallow Reach (VERDANT/DAWN — farmland swallowed by an overnight forest; boss Warden Aelin, the Last Steward — built, hand-authored). 2 Cinderhold (EMBER/HOLLOW — forge city that mined downward into a seal; Warden Bruk, Who Struck First). 3 The Drowned Archive (TIDE/HOLLOW — library-city flooded on purpose by its own librarians). Future design space: The Glass Waste, The Hanging Tombs, Sundermoor, The Ninth Seal. Regions are 6–10 wielders each with legible archetypes (Root-Binder wide-adjacency, Ashkeeper burn, Silt-Reader Excavate control) — legibility beats variety.

**Classes & towns** (content/classes.json; 7-class roster, 4 entries pending CLASS-7): warrior/EMBER/**Emberhold**, necromancer/HOLLOW/**Palewatch**, druid/VERDANT/**Mossgrave**; plus tidecaller/TIDE, dawnward/DAWN, ranger/VERDANT (cross-strata), occultist/HOLLOW (cross-strata) — portraits exist for all 7 at `client/content/art/classes/`.

## Naming conventions

- World naming is **Tolkien-inspired, original names only** — no real Tolkien names.
- Card ids: `{strata}_{rarity}_{snake_name}` — e.g. `vrd_r_bloomweaver`, `hol_c_gravewrit_thrall` (strata: vrd/emb/tid/hol/dwn; rarity: c/u/r/x where x = artifact/relic-tier).
- Compound English coinages in card names: Gravewrit, Bloomweaver, Sunblade, Silt Reader, Thornbark. Flavor text is one line, wry-mythic register ("The grove keeps its own ledgers, and it does not forgive debts.").
- Artifact display names are ONE WORD (Sword, Shield, Wand, Aura, Whisperfang, Duskfang, Skull, Shard, Hammer, Anvil, Censer, Icon, Bow, Quiver); full names live in ids/aliases.
- In-game vocabulary: life = **Vigor**, mana = **Attunement**, discard-adjacent zone = **Barrow**, graveyard verb = **Bury**, class weapons = **Artifacts**, players = **Delvers**, bosses = **Wardens**.

## Client visual language (current locked pieces)

- **Card border:** Root-Bound carved stone (option 5 of 7 generated candidates), 9-slice assets at `client/content/art/border/rootbound_*.png` + spec JSON; band = round(card_width × 0.07); cost rune top-right; name auto-fit per the safe-zone rules (04-decisions-log #3). Full duel-screen reference mock: `artifacts/mockups/duel_target_final.png`.
- **Board:** moss granite painting (`client/content/art/board/default.png`) under the existing glow/glyph polish; BoardSkin registry in ThemeTokens for future zone skins.
- **Title:** Flooded Temple seal art; intro splash "Before the maps had edges…" (canon copy, first launch only, tap to skip).
- **Fonts:** Cinzel for card/UI display text (`client/assets/fonts/Cinzel.ttf`), Inter for body.
- **Artifact visual states (hard requirement):** Ready (idle glow) / Triggered (≤0.8s non-blocking flash) / Charged (pips scale with count) / Suppressed (chained-frosted-dimmed full-art change + turn pip) — readable from across the room, no tooltip-only states; weapons read teal vs creature gold.
