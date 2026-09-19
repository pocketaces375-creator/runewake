#!/usr/bin/env python3
"""
tutorial_script_sim.py — Prove a guided tutorial script is PLAYABLE before Godot
ever runs it.

validate_tutorial_script.py checks shape (schema, card ids, lengths). This walks
the script with a small model of the duel rules and checks that every beat the
player is told to perform is actually legal at that moment, and that every
scripted opponent action is legal too:

  - Attunement: P0 starts turn 1 with 1, both players gain +1 max per own turn
    (cap 10) and refill. player_attunement_override respected.
  - Summon: needs empty own lane, cost <= attunement. Creatures are exhausted on
    arrival unless SWIFT. Creatures ready at the start of their owner's turn.
  - Attack: attacker must be ready and not have attacked; target lane = source
    lane (no REACH modelled); empty target lane => face damage unless a GUARD
    creature redirects; combat is simultaneous.
  - Beats: SUMMON_CREATURE / ATTACK_WITH_CREATURE / END_TURN / NO_ATTACK_END_TURN,
    restricted by restrict_actions_to (SUMMON_LANE_n / ATTACK_LANE_n / END_TURN).
    The sim performs exactly what the prompt's highlights ask (hand_card_i +
    lane_j / lane_j + enemy_lane_j), i.e. the path the player is guided down.
  - The prompt/note copy is checked for the Vigor numbers it quotes ("25 to 23",
    "23 → 21") against the simulated Vigor after the action.

Exit 0 = every beat and opponent action is legal and the quoted numbers match.
Exit 1 with a plain description otherwise.

Usage: python3 tools/tutorial_script_sim.py content/tutorial/scripts/first_duel.json
"""
import glob
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def load_cards():
    cards = {}
    for f in glob.glob(str(ROOT / "client" / "content" / "cards" / "*.json")):
        d = json.load(open(f))
        items = d if isinstance(d, list) else d.get("cards", [])
        for c in items:
            if "id" in c:
                cards[c["id"]] = c
    return cards


class Creature:
    def __init__(self, cid, card, owner):
        self.id = cid
        self.name = card.get("name", cid)
        self.attack = card.get("attack") or 0
        self.vigor = card.get("vigor") or 0
        self.keywords = set(card.get("keywords") or [])
        self.owner = owner
        self.damage = 0
        self.exhausted = "SWIFT" not in self.keywords
        self.attacked = False

    @property
    def alive(self):
        return self.damage < self.vigor

    def __repr__(self):
        return f"{self.name}({self.attack}/{self.vigor - self.damage})"


class Player:
    def __init__(self, idx):
        self.idx = idx
        self.vigor = 25
        self.attune = 0
        self.attune_max = 0
        self.lanes = [None] * 5
        self.hand = []  # list of card ids


class Sim:
    def __init__(self, cards):
        self.cards = cards
        self.p = [Player(0), Player(1)]
        self.turn = 1
        self.errors = []
        self.log = []
        # P0's first attune step
        self.p[0].attune_max = 1
        self.p[0].attune = 1

    def fail(self, msg):
        self.errors.append(msg)
        self.log.append("  !! " + msg)

    def start_turn(self, idx):
        pl = self.p[idx]
        for c in pl.lanes:
            if c:
                c.exhausted = False
                c.attacked = False
        # attune (P0 turn 1 already handled at init)
        if not (idx == 0 and self.turn == 1):
            pl.attune_max = min(pl.attune_max + 1, 10)
            pl.attune = pl.attune_max

    def summon(self, idx, cid, lane, who):
        pl = self.p[idx]
        card = self.cards.get(cid)
        if card is None:
            self.fail(f"{who}: unknown card {cid}"); return False
        if cid not in pl.hand:
            self.fail(f"{who}: {cid} is not in P{idx}'s hand {pl.hand}"); return False
        cost = card.get("cost", 0)
        if cost > pl.attune:
            self.fail(f"{who}: {card['name']} costs {cost}, P{idx} has {pl.attune} attunement"); return False
        if not (0 <= lane <= 4):
            self.fail(f"{who}: lane {lane} out of range"); return False
        if pl.lanes[lane] is not None:
            self.fail(f"{who}: P{idx} lane {lane} already holds {pl.lanes[lane]}"); return False
        pl.attune -= cost
        pl.hand.remove(cid)
        pl.lanes[lane] = Creature(cid, card, idx)
        self.log.append(f"  P{idx} summons {pl.lanes[lane]} into lane {lane} (attune left {pl.attune})")
        return True

    def attack(self, idx, src, tgt, who):
        pl, op = self.p[idx], self.p[1 - idx]
        a = pl.lanes[src] if 0 <= src <= 4 else None
        if a is None:
            self.fail(f"{who}: P{idx} has no creature in lane {src}"); return False
        if a.exhausted:
            self.fail(f"{who}: {a} in lane {src} is exhausted (summoned this turn, no SWIFT)"); return False
        if a.attacked:
            self.fail(f"{who}: {a} in lane {src} already attacked this turn"); return False
        if tgt != src:
            self.fail(f"{who}: target lane {tgt} != source lane {src} (no REACH)"); return False
        a.attacked = True
        d = op.lanes[tgt]
        if d is None:
            guard = next((i for i, c in enumerate(op.lanes) if c and "GUARD" in c.keywords), None)
            if guard is not None:
                d = op.lanes[guard]
                self.log.append(f"  P{idx} {a} attacks lane {tgt}: empty, redirected to GUARD {d} in lane {guard}")
                tgt = guard
        if d is None:
            op.vigor -= a.attack
            self.log.append(f"  P{idx} {a} hits face for {a.attack}: P{1-idx} vigor {op.vigor}")
        else:
            d.damage += a.attack
            a.damage += d.attack
            self.log.append(f"  P{idx} {a} fights {d}: defender {'dies' if not d.alive else 'survives'}, attacker {'dies' if not a.alive else 'survives'}")
            if not d.alive:
                op.lanes[tgt] = None
            if not a.alive:
                pl.lanes[src] = None
        return True

    def end_turn(self, idx):
        self.log.append(f"  P{idx} ends turn")
        nxt = 1 - idx
        if nxt == 0:
            self.turn += 1
        self.start_turn(nxt)


def parse_highlight(hl, prefix):
    for h in hl or []:
        if h.startswith(prefix):
            try:
                return int(h[len(prefix):])
            except ValueError:
                pass
    return None


# FABLE-011: artifact_1 / artifact_2 name the player's OWN two Artifacts, so the
# copy never has to assume a class. The old pattern was [a-z_]+, which does not
# match a digit — {artifact_1} slipped through unchecked and the sim would not
# have caught the runner leaving it on screen.
KNOWN_TOKENS = {"card", "cost", "attune", "attune_max", "opponent", "artifact_1", "artifact_2"}
TOKEN_RE = re.compile(r"\{([a-z0-9_]+)\}")


def render_tokens(text, ctx, beat_id, sim, where):
    """
    FABLE-004: coach copy carries {tokens} filled from the live duel. The sim knows the
    same numbers, so it renders the sentence the player will actually read — and fails on
    a token the runner cannot fill, or on {card}/{cost} with no hand card to resolve from.
    """
    if not text:
        return text or ""
    for m in TOKEN_RE.finditer(text):
        tok = m.group(1)
        if tok not in KNOWN_TOKENS:
            sim.fail(f"beat {beat_id}: {where} uses unknown token {{{tok}}} — the runner will leave it on screen")
        elif tok in ("card", "cost") and ctx.get("card") is None:
            sim.fail(f"beat {beat_id}: {where} uses {{{tok}}} but the beat highlights no hand_card_N to resolve it from")
    out = text
    for tok in KNOWN_TOKENS:
        v = ctx.get(tok)
        out = out.replace("{" + tok + "}", "?" if v is None else str(v))
    return out


def check_quoted_vigor(text, before, after, beat_id, sim):
    """Find 'A to B' / 'A → B' / 'A -> B' pairs and compare with the sim."""
    if not text:
        return
    for m in re.finditer(r"(\d+)\s*(?:to|→|->)\s*(\d+)", text):
        a, b = int(m.group(1)), int(m.group(2))
        if (a, b) != (before, after):
            sim.fail(f"beat {beat_id}: copy says Vigor '{a} to {b}' but the sim went {before} to {after}")


def run(script_path):
    cards = load_cards()
    script = json.load(open(script_path))
    sim = Sim(cards)
    p0, p1 = sim.p
    turns = script["turns"]

    for ti, turn in enumerate(turns):
        kind = turn["type"]
        idx = 0 if kind == "player" else 1
        pl = sim.p[idx]
        sim.log.append(f"— script turn {ti+1}: {kind} (duel turn {sim.turn}, P{idx} attune {pl.attune}/{pl.attune_max})")
        hand_key = "player_hand_override" if idx == 0 else "opponent_hand_override"
        if turn.get(hand_key):
            pl.hand = list(turn[hand_key])
            sim.log.append(f"  hand override P{idx}: {pl.hand}")
        if idx == 0 and turn.get("player_attunement_override") is not None:
            pl.attune = turn["player_attunement_override"]
            pl.attune_max = max(pl.attune_max, pl.attune)

        if kind == "player":
            ended = False
            for beat in turn.get("player_beats", []):
                bid = beat["id"]
                trig = beat["trigger_event"]
                hl = beat.get("highlight") or []
                restrict = beat.get("restrict_actions_to") or []
                if not beat.get("prompt"):
                    sim.fail(f"beat {bid}: no 'prompt' — the player would get no instruction before acting")

                # FABLE-004: render the prompt the way the runner will, from the state the
                # player is actually looking at, and log the finished sentence.
                hand_i0 = parse_highlight(hl, "hand_card_")
                hc = cards.get(pl.hand[hand_i0]) if (hand_i0 is not None and hand_i0 < len(pl.hand)) else None
                ctx = {
                    "card": hc.get("name") if hc else None,
                    "cost": hc.get("cost", 0) if hc else None,
                    "attune": pl.attune,
                    "attune_max": pl.attune_max,
                    "opponent": script.get("title") or "the opponent",
                }
                sim.log.append(f'  [{bid}] prompt: "{render_tokens(beat.get("prompt"), ctx, bid, sim, "prompt")}"')
                before_vigor = p1.vigor

                if trig == "SUMMON_CREATURE":
                    hand_i = parse_highlight(hl, "hand_card_")
                    lane = parse_highlight(hl, "lane_")
                    if hand_i is None or lane is None:
                        sim.fail(f"beat {bid}: SUMMON beat must highlight hand_card_i and lane_j"); continue
                    if hand_i >= len(pl.hand):
                        sim.fail(f"beat {bid}: hand_card_{hand_i} but hand has {len(pl.hand)} cards"); continue
                    allowed = {f"SUMMON_LANE_{lane}", "SUMMON_CREATURE", "SUMMON_LANE_ANY", "ANY"}
                    if restrict and not (set(restrict) & allowed):
                        sim.fail(f"beat {bid}: highlights lane {lane} but restrict_actions_to {restrict} forbids it")
                    cid = pl.hand[hand_i]
                    sim.summon(0, cid, lane, f"beat {bid}")
                elif trig == "ATTACK_WITH_CREATURE":
                    src = parse_highlight(hl, "lane_")
                    tgt = parse_highlight(hl, "enemy_lane_")
                    if src is None or tgt is None:
                        sim.fail(f"beat {bid}: ATTACK beat must highlight lane_j and enemy_lane_j"); continue
                    allowed = {f"ATTACK_LANE_{src}", "ATTACK_ANY", "ANY"}
                    if restrict and not (set(restrict) & allowed):
                        sim.fail(f"beat {bid}: highlights attacker lane {src} but restrict_actions_to {restrict} forbids it")
                    sim.attack(0, src, tgt, f"beat {bid}")
                elif trig in ("END_TURN", "NO_ATTACK_END_TURN"):
                    if restrict and "END_TURN" not in restrict and "ANY" not in restrict:
                        sim.fail(f"beat {bid}: END_TURN beat but restrict_actions_to {restrict} disables End Turn")
                    sim.end_turn(0)
                    ended = True
                else:
                    sim.fail(f"beat {bid}: trigger {trig} not modelled")

                # The note lands after the action, so its numbers come from the state the
                # action left behind — that is what the quoted-Vigor check compares against.
                if beat.get("popup"):
                    ctx_after = dict(ctx, attune=pl.attune, attune_max=pl.attune_max)
                    shown = render_tokens(beat["popup"], ctx_after, bid, sim, "popup")
                    sim.log.append(f'  [{bid}] note:   "{shown}"')
                    check_quoted_vigor(shown, before_vigor, p1.vigor, bid, sim)
            if not ended:
                sim.fail(f"script turn {ti+1}: player turn has no END_TURN beat — the runner would leave the player stranded")
        else:
            ended = False
            for ai, act in enumerate(turn.get("opponent_actions", [])):
                who = f"opp turn {ti+1} action {ai+1} ({act['action']})"
                if act["action"] == "SUMMON":
                    sim.summon(1, act["card_id"], act["lane"], who)
                elif act["action"] == "ATTACK":
                    sim.attack(1, act["lane"], act.get("target_lane", act["lane"]), who)
                elif act["action"] == "END_TURN":
                    sim.end_turn(1)
                    ended = True
                elif act["action"] == "NO_OP":
                    pass
                else:
                    sim.fail(f"{who}: not modelled")
            if not ended:
                sim.fail(f"script turn {ti+1}: opponent turn never ends — bot is suspended, the duel would hang")

    print("\n".join(sim.log))
    print()
    print(f"final: P0 vigor {p0.vigor}, P1 vigor {p1.vigor}, P0 lanes {p0.lanes}, P1 lanes {p1.lanes}")
    if sim.errors:
        print(f"\nTUTORIAL SIM FAILED ({len(sim.errors)} problem{'s' if len(sim.errors) != 1 else ''}):")
        for e in sim.errors:
            print("  -", e)
        return 1
    print("\nTUTORIAL SIM PASSED — every guided action is legal when the player is asked to do it")
    return 0


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else str(ROOT / "content" / "tutorial" / "scripts" / "first_duel.json")
    sys.exit(run(path))
