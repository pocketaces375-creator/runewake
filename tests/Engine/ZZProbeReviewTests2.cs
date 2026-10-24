using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>FABLE-104 engine review probes, round 2 (artifact PASSIVE wiring, unearth through Apply, rune stacking, aura Ward).</summary>
[Collection("NonParallel")]
public class ZZProbeReviewTests2
{
    private static GameState CreateState()
    {
        var state = new GameState(seed: 7);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 20; i++)
                state.Players[p].Deck.Add(new CardInstance(state.NextInstanceId++, "tst_d", p) { Zone = Zone.Deck });
        }
        return state;
    }

    private static CardInstance PlaceCreature(GameState state, int pIdx, int lane, int attack = 2, int vigor = 5, params string[] keywords)
    {
        var c = new CardInstance(state.NextInstanceId++, $"tst_cr_p{pIdx}_l{lane}", pIdx)
        {
            Zone = Zone.Lane, LaneIndex = lane, CardType = CardType.CREATURE,
            BaseAttack = attack, BaseVigor = vigor, Cost = 1, IsExhausted = false
        };
        c.Keywords.AddRange(keywords);
        state.Players[pIdx].Lanes[lane].Occupant = c;
        return c;
    }

    private static GameState EndTurn(GameState s, int p) => DuelEngine.Apply(s, new EndTurnAction { PlayerIndex = p });
    private static GameState Attack(GameState s, int p, int lane) => DuelEngine.Apply(s, new AttackAction { PlayerIndex = p, SourceLane = lane, TargetLane = lane });

    private static ArtifactSlot AddArtifact(GameState state, int p, int slotIdx, string id, params AbilityDef[] abilities)
    {
        var player = state.Players[p];
        if (player.ArtifactSlots.Length < 2) player.ArtifactSlots = new[] { new ArtifactSlot(0), new ArtifactSlot(1) };
        var slot = player.ArtifactSlots[slotIdx];
        var art = new CardInstance(state.NextInstanceId++, id, p) { CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = slotIdx };
        art.Abilities.AddRange(abilities);
        slot.Occupant = art;
        return slot;
    }

    private static AbilityDef Passive(EffectDef e) => new() { Trigger = Trigger.PASSIVE, Effects = new List<EffectDef> { e } };

    // ───────── A. Battlemage Wand passive (ATTACK_LTE:1, WHILE_ATTACKING) stacks permanently every turn ─────────
    [Fact]
    public void Probe_WandPassive_DoesNotStackAcrossTurns()
    {
        var state = CreateState();
        AddArtifact(state, 0, 0, "artf_battlemage_wand", Passive(new EffectDef
        {
            Op = Op.BUFF, Attack = 1, Vigor = 0, Duration = Duration.WHILE_ATTACKING,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ATTACK_LTE:1", Count = TargetCount.All }
        }));
        var c = PlaceCreature(state, 0, 0, attack: 1, vigor: 3);
        for (int i = 0; i < 3; i++) { state = EndTurn(state, 0); state = EndTurn(state, 1); }
        var after = state.Players[0].Lanes[0].Occupant!;
        // a 1-attack creature under "+1 attack while attacking" should never exceed 2
        Assert.True(after.CurrentAttack <= 2, $"CurrentAttack={after.CurrentAttack} AttackModifier={after.AttackModifier}");
    }

    // ───────── B. Duskfang passive (FIRST_ATTACKER -> STEALTH_STRIKE) never finds its target ─────────
    [Fact(Skip = "FABLE-104 left open: artifact PASSIVEs are a turn-start snapshot, so FIRST_ATTACKER / HAS_NOT_ATTACKED passives need the aura redesign (review item #4)")]
    public void Probe_FirstAttackerPassive_AppliesToTheFirstAttacker()
    {
        var state = CreateState();
        AddArtifact(state, 0, 0, "artf_rogue_dagger_dusk", Passive(new EffectDef
        {
            Op = Op.GRANT_KEY, Keyword = "STEALTH_STRIKE",
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "FIRST_ATTACKER", Count = TargetCount.Exactly(1) }
        }));
        state = EndTurn(state, 0); state = EndTurn(state, 1);          // passives applied at P0's turn start
        var me = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 3, vigor: 9);
        state = Attack(state, 0, 0);
        var mine = state.Players[0].Lanes[0].Occupant!;
        // stealth strike: the first attacker takes no counter-damage
        Assert.Equal(0, mine.Damage);
    }

    // ───────── C. Bulwark passive (HAS_NOT_ATTACKED, "applied at end of turn") hits every creature at turn start ─────────
    [Fact(Skip = "FABLE-104 left open: artifact PASSIVEs are a turn-start snapshot, so FIRST_ATTACKER / HAS_NOT_ATTACKED passives need the aura redesign (review item #4)")]
    public void Probe_HasNotAttackedPassive_DoesNotBuffTheAttacker()
    {
        var state = CreateState();
        AddArtifact(state, 0, 0, "artf_warrior_shield", Passive(new EffectDef
        {
            Op = Op.BUFF, Attack = 0, Vigor = 1, Duration = Duration.UNTIL_YOUR_NEXT_TURN,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "HAS_NOT_ATTACKED", Count = TargetCount.All }
        }));
        PlaceCreature(state, 0, 0, attack: 2, vigor: 3);
        PlaceCreature(state, 0, 1, attack: 2, vigor: 3);
        PlaceCreature(state, 1, 0, attack: 0, vigor: 9);
        state = EndTurn(state, 0); state = EndTurn(state, 1);          // P0 turn 2: passive applied before anyone attacked
        state = Attack(state, 0, 0);
        state = EndTurn(state, 0);                                     // during P1's turn: attacker must NOT carry the +0/+1
        var attacker = state.Players[0].Lanes[0].Occupant!;
        var holder = state.Players[0].Lanes[1].Occupant!;
        Assert.Equal(3, attacker.MaxVigorNow);
        Assert.Equal(4, holder.MaxVigorNow);
    }

    // ───────── D. UNEARTH_FROM_GRAVEYARD through a real Apply: corpse comes back at 1 Vigor ─────────
    [Fact]
    public void Probe_UnearthFromGraveyard_ViaRitual_FullVigor()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 3, vigor: 4);
        PlaceCreature(state, 1, 0, attack: 4, vigor: 9);
        state = Attack(state, 0, 0);                                   // mine dies with Damage=4
        Assert.Null(state.Players[0].Lanes[0].Occupant);
        var ritual = new CardInstance(state.NextInstanceId++, "tst_raise", 0) { CardType = CardType.RITUAL, Zone = Zone.Hand, Cost = 1 };
        ritual.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.RESOLVE,
            Effects = new List<EffectDef> { new() { Op = Op.UNEARTH_FROM_GRAVEYARD, Target = new TargetDef { Scope = Scope.PLAYER_SELF } } }
        });
        state.Players[0].Hand.Add(ritual);
        state = DuelEngine.Apply(state, new PlayCardAction { PlayerIndex = 0, CardInstanceId = ritual.InstanceId, LaneIndex = 1 });
        var back = state.Players[0].Lanes[0].Occupant;
        Assert.NotNull(back);
        Assert.Equal(4, back!.CurrentVigor);
    }

    // ───────── E. Conditional PASSIVE rune (Barrow Strength) -> permanent +2 to ONE creature every turn ─────────
    [Fact]
    public void Probe_ConditionalPassiveRune_DoesNotStack()
    {
        var state = CreateState();
        var page = new RunePage();
        page.Equip(new RuneDef
        {
            Id = "rune_hol_barrow_strength", Name = "Barrow Strength",
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE, Condition = new ConditionDef { Op = ConditionOp.DAMAGED_THIS_TURN },
                Effects = new List<EffectDef> { new() { Op = Op.BUFF, Attack = 2, Duration = Duration.PERMANENT, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ALL", Count = TargetCount.Exactly(0) } } }
            }
        });
        RuneInjector.ApplyRunes(state, page);
        PlaceCreature(state, 0, 0, attack: 2, vigor: 5);
        PlaceCreature(state, 0, 1, attack: 2, vigor: 5);
        PlaceCreature(state, 1, 2, attack: 3, vigor: 9);                // hits P0's face every P1 turn
        for (int i = 0; i < 3; i++) { state = EndTurn(state, 0); state = Attack(state, 1, 2); state = EndTurn(state, 1); }
        var a = state.Players[0].Lanes[0].Occupant!; var b = state.Players[0].Lanes[1].Occupant!;
        // "Your creatures have +2 attack while damaged": a conditional aura — both at 4 (P0 took damage this turn), never more
        Assert.Equal(4, a.CurrentAttack);
        Assert.Equal(4, b.CurrentAttack);
        Assert.Equal(0, a.AttackModifier);                             // nothing permanent was written on the card
        state = EndTurn(state, 0); state = EndTurn(state, 1);           // a round with no damage: the aura is off
        Assert.Equal(2, state.Players[0].Lanes[0].Occupant!.CurrentAttack);
    }

    // ───────── F. Aura-granted Ward (Dawn Relic / cinder cloak) has no shield behind it ─────────
    [Fact]
    public void Probe_AuraWard_BlocksAHit()
    {
        var state = CreateState();
        var relic = new CardInstance(state.NextInstanceId++, "dwn_x_dawn_relic", 0) { CardType = CardType.RELIC, Zone = Zone.Lane, LaneIndex = 4, BaseVigor = 3, IsIdentified = true };
        relic.Abilities.Add(Passive(new EffectDef { Op = Op.GRANT_KEY, Keyword = "WARD", Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ANY", Count = TargetCount.All } }));
        state.Players[0].Lanes[4].Occupant = relic;
        PlaceCreature(state, 0, 0, attack: 1, vigor: 5);
        PlaceCreature(state, 1, 0, attack: 3, vigor: 9);
        state = EndTurn(state, 0);
        Assert.Contains("WARD", state.Players[0].Lanes[0].Occupant!.EffectiveKeywords);
        state = Attack(state, 1, 0);
        Assert.Equal(0, state.Players[0].Lanes[0].Occupant!.Damage);
    }

    // ───────── G. Sword/Hammer trigger: GRANT_KEY ANCESTRAL_SHIELD aimed at PLAYER_SELF is a no-op ─────────
    [Fact]
    public void Probe_AncestralShield_ArmsAfterThreeAttackers()
    {
        var state = CreateState();
        AddArtifact(state, 0, 0, "artf_warrior_sword", new AbilityDef
        {
            Trigger = Trigger.ON_CREATURE_ATTACKS,
            Condition = new ConditionDef { Op = ConditionOp.ATTACKERS_THIS_TURN_GTE, Value = System.Text.Json.JsonSerializer.SerializeToElement(3) },
            Effects = new List<EffectDef> { new() { Op = Op.GRANT_KEY, Keyword = "ANCESTRAL_SHIELD", Duration = Duration.UNTIL_YOUR_NEXT_TURN, Target = new TargetDef { Scope = Scope.PLAYER_SELF } } }
        });
        for (int i = 0; i < 3; i++) PlaceCreature(state, 0, i, attack: 1, vigor: 5);
        for (int i = 0; i < 3; i++) state = Attack(state, 0, i);
        bool armed = state.Players[0].Lanes.Any(l => l.Occupant?.EffectiveKeywords.Contains("ANCESTRAL_SHIELD") == true);
        Assert.True(armed, "three attackers this turn - the Blade must arm");
    }

    // ───────── H. Soul Harvest: DESTROY filter EXHAUSTED is not a resolver filter -> destroys any ally ─────────
    [Fact]
    public void Probe_UnknownFilter_Exhausted_IsHonoured()
    {
        var state = CreateState();
        var ready = PlaceCreature(state, 0, 0, attack: 2, vigor: 5);      // not exhausted
        var tired = PlaceCreature(state, 0, 1, attack: 2, vigor: 5); tired.IsExhausted = true;
        var ritual = new CardInstance(state.NextInstanceId++, "hol_u_soul_harvest", 0) { CardType = CardType.RITUAL, Zone = Zone.Hand, Cost = 1 };
        ritual.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.RESOLVE,
            Effects = new List<EffectDef> { new() { Op = Op.DESTROY, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "EXHAUSTED", Count = TargetCount.Exactly(1) } } }
        });
        state.Players[0].Hand.Add(ritual);
        state = DuelEngine.Apply(state, new PlayCardAction { PlayerIndex = 0, CardInstanceId = ritual.InstanceId, LaneIndex = 0 });
        Assert.NotNull(state.Players[0].Lanes[0].Occupant);   // the ready one must survive
        Assert.Null(state.Players[0].Lanes[1].Occupant);
    }
}
