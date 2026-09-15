# Duel Layout Table

Every number is at 1080 reference. Multiply by `scale = vh / 1080`.  
x is measured from the LEFT edge of the viewport unless stated as "from right".

**Any change to duel geometry edits THIS file first, then the code, then `tools/layout_lint.py` must pass.**

---

| Region | x | y | Notes |
|---|---|---|---|
| **VIEWPORT** | 0..2316 | 0..1080 | Reference resolution |
| **ENEMY STRIP** | 0..2316 | 0..90 | `ClipContents`. Fan centred on x=1158. |
| **LANE BAND** | 476..1841 | 100..716 | 5 lanes per row, card W 205 H 300, pitch 290 |

### Lane row coordinates

| Row | y | |
|---|---|---|
| Enemy | 100..400 | |
| Player | 416..716 | |

### Lane slot x centres (476..1841 band)

| Slot | 0 | 1 | 2 | 3 | 4 |
|---|---|---|---|---|---|
| left | 476 | 766 | 1056 | 1346 | 1636 |

### Hand (overlay)

| Property | Value |
|---|---|
| Centre card top y | 716 |
| Card W × H | 212 × 310 |
| Arc R | 900 |
| Spread | `min(n * 5, 40)` degrees |
| Pivot (_handFlow-local) | (682, 716 + 155 + 900) |

### Right column

| Property | Value |
|---|---|
| Column x | 1972..2294 |
| Column width | 322 (= two relics + 10 gap) |

| Node | y | Details |
|---|---|---|
| Enemy relics | 16..254 | Two plates 156×238, x 1972 and 2138 |
| Enemy pill | 270..304 | |
| Enemy deck/barrow | 312..342 | |
| Turn label | 462 | `"— TURN N —"`, Cinzel 22, gold, centred in the column |
| Player pill | 600..634 | |
| Player deck/barrow | 642..672 | |
| Player relics | 688..926 | x 1972 and 2138 |
| YOUR TURN tag | 930 | |
| End Turn | 954..1068 | x 1972..2294 |

### Plaque (hold-to-peek)

| Property | Value |
|---|---|
| Position | (24, 300) |
| Size | (420, 440) |
| Name font | 34 |
| Effect font | 27 |
| Keyword font | 22 |
| Flavor font | 22 |

### Gutter

| Region | x | Notes |
|---|---|---|
| Left gutter | 0..476 | Reserved — nothing lives here except the plaque while held |
| Right gutter | 2294..2316 | Unused spill |