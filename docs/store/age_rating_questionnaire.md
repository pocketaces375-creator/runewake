# Age Rating Questionnaire — App Store & Google Play

---

## 1. Apple App Store — Age Rating (9+)

Apple's rating system asks a series of yes/no questions about content categories. Below are the correct answers for Runewake: The Buried Age at v1.0.

| Category | Answer | Justification |
|----------|--------|---------------|
| **Cartoon or Fantasy Violence** | **Frequent/Mild** | Combat between fantasy creatures (elementals, wardens, relics). Damage is shown as numeric values and fade-out effects. No blood, gore, or dismemberment. The player's creatures attack enemy creatures and the opponent directly, but all combat is abstracted through lane slots and stat numbers. This is the sole content category that applies. |
| **Realistic Violence** | **None** | No realistic depictions of violence. No firearms, no melee weapons resembling real-world equivalents, no human-on-human combat. |
| **Sexual Content or Nudity** | **None** | No sexual content, nudity, or suggestive material. |
| **Profanity or Crude Humour** | **None** | No profanity in card text, dialogue, or UI. Fantasy curses ("By the seal") exist but are not real-world profanity. |
| **Medical or Treatment Information** | **None** | No medical content. |
| **Drug, Tobacco, or Alcohol Use** | **None** | No references to drugs, alcohol, or tobacco. |
| **Horror or Fear Themes** | **None** | The game has dark fantasy themes (undead, barrows, crypts) but no jump scares, horror imagery, or terror-focused content. The HOLLOW strata uses skeletal imagery but it is presented as fantasy combat, not horror. |
| **Gambling, Contests, or Sweepstakes** | **None** | No gambling mechanics. Dig site tile reveals are deterministic (strike-based, not random). Card acquisition is through gameplay, not loot boxes or randomised purchases. No sweepstakes. |

**Result: 9+** — The only applicable category is Mild Cartoon/Fantasy Violence, which maps to the 9+ rating.

---

## 2. Google Play — IARC Content Rating (Everyone 10+)

Google Play uses the IARC (International Age Rating Coalition) questionnaire. The relevant questions and answers are:

| Question | Answer | Justification |
|----------|--------|---------------|
| **Does the app contain violence?** | **Yes — Mild, fantasy** | Combat between fantasy creatures shown with numeric damage and fade effects. No blood or gore. |
| **Does the app contain sexual content?** | **No** | No sexual content of any kind. |
| **Does the app contain gambling?** | **No** | No simulated gambling, no loot boxes, no real-money gambling. |
| **Does the app contain controlled substances?** | **No** | No references to drugs, alcohol, or tobacco. |
| **Does the app contain hate speech?** | **No** | No hate speech or discriminatory content. |
| **Does the app contain user-generated content?** | **No** | All content is authored. No chat, forums, or user-created cards shared between players. |
| **Does the app share location?** | **No** | No location data collected or shared. |
| **Does the app share personal data with third parties?** | **No** | The only third-party service is Supabase (database). No ad networks, no analytics SDKs. |

**Result: Everyone 10+** — Google's IARC maps Mild Fantasy Violence to Everyone 10+. This matches Apple's 9+.

---

## 3. Notes for Future Content

The following categories may change if future regions introduce new content:

| Future Risk | Region / Feature | Would Change To |
|-------------|------------------|-----------------|
| **Horror/Fear** | The Drowned Archive (TIDE / HOLLOW) uses undead imagery — skeletal creatures, crypts, barrows. At v1 this is fantasy combat, not horror. If a future update adds atmospheric horror elements (ambient screams, jump scares, body horror in card art), the rating may need to increase to 12+. | 9+ → 12+ (Apple) / E10+ → T (Google) |
| **Realistic Violence** | A future region with humanoid factions using weapons could push into realistic violence territory if the art style becomes more literal. Current art is stylised fantasy. If card art begins depicting realistic wounds or blood, re-rate. | 9+ → 12+ (Apple) / E10+ → T (Google) |
| **Gambling** | If a future system introduces randomised card acquisition (packs, boosters, gacha), this must be disclosed and the rating will increase. The v1 design explicitly avoids this. | 9+ → 12+ or higher depending on implementation |
| **User-Generated Content** | If PvP chat or a card-sharing gallery is added, the rating questionnaire must be re-filed to account for UGC moderation requirements. | No change to age band, but questionnaire must be re-filed |

---

## 4. Recommended Rating Displays

| Store | Rating | Badge |
|-------|--------|-------|
| Apple App Store | 9+ | 9+ (blue circle) |
| Google Play | Everyone 10+ | E10+ (green circle) |

These ratings should be displayed on the store listing, in the app's settings screen, and on any marketing website. No age gate is required at download time for these ratings. No parental gate is required for in-app purchases at these ratings (Apple's 9+ and Google's E10+ both allow purchases without parental approval by default, but Apple requires a parental gate for IAP if the rating is 17+).