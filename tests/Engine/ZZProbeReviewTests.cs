using System.IO;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// FABLE-104 engine review probes (2026-10-24). Each test asserts the rule-correct behaviour the review found broken.
/// </summary>
[Collection("NonParallel")]
public class ZZProbeReviewTests
{
    private static GameState CreateState()
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 10; i++)
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

    private static ArtifactSlot AddArtifact(GameState state, int p, int slotIdx, string id, int maxCharges = 0, bool deferred = false, params AbilityDef[] abilities)
    {
        var player = state.Players[p];
        if (player.ArtifactSlots.Length < 2) player.ArtifactSlots = new[] { new ArtifactSlot(0), new ArtifactSlot(1) };
        var slot = player.ArtifactSlots[slotIdx];
        var art = new CardInstance(state.NextInstanceId++, id, p) { CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = slotIdx };
        art.Abilities.AddRange(abilities);
        slot.Occupant = art; slot.MaxCharges = maxCharges; slot.HasDeferredChargeFull = deferred;
        return slot;
    }

    // ───────── 1. UNEARTH_FROM_GRAVEYARD brings back a corpse with its death damage ─────────
    [Fact]
    public void Probe_UnearthFromGraveyard_ReturnsCreatureAtFullVigor()
    {
        var state = CreateState();
        var mine = PlaceCreature(state, 0, 0, attack: 3, vigor: 4);
        PlaceCreature(state, 1, 0, attack: 4, vigor: 9);          // kills mine in combat
        state = Attack(state, 0, 0);
        Assert.Single(state.Players[0].Discard);
        var corpse = state.Players[0].Discard[0];
        Assert.Equal(4, corpse.Damage);                           // died with 4 damage recorded

        var effect = new EffectDef { Op = Op.UNEARTH_FROM_GRAVEYARD, Target = new TargetDef { Scope = Scope.PLAYER_SELF } };
        var src = new CardInstance(0, "src", 0);
        EffectExecutor.Execute(effect, src, state, new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });
        var back = state.Players[0].Lanes[0].Occupant;
        Assert.NotNull(back);
        // a resurrected 3/4 should be a 3/4, not a 3/0 that Auras.Recompute heals to 3/1
        Assert.Equal(4, back!.CurrentVigor);
    }

    // ───────── 2. Deferred (END_OF_TURN) charge-full that fills on the OPPONENT's turn is lost (R20) ─────────
    [Fact]
    public void Probe_DeferredChargeFull_FilledOnEnemyTurn_StillFires()
    {
        var state = CreateState();
        var slot = AddArtifact(state, 0, 0, "artf_necromancer_skull", maxCharges: 3, deferred: true,
            new AbilityDef
            {
                Trigger = Trigger.ON_CHARGE_FULL, Timing = "END_OF_TURN",
                Effects = new List<EffectDef>
                {
                    new() { Op = Op.REVIVE_TOKEN, Keyword = "artf_revenant_token", Target = new TargetDef { Scope = Scope.PLAYER_SELF } },
                    new() { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.SELF_ARTIFACT } }
                }
            });
        state = EndTurn(state, 0);                                  // now P1's turn
        Assert.Equal(1, state.CurrentPlayerIndex);
        var art = state.Players[0].ArtifactSlots[0].Occupant!;
        EffectExecutor.Execute(new EffectDef { Op = Op.ADD_CHARGE, Amount = 3 }, art, state,
            new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) });
        Assert.Equal(3, state.Players[0].ArtifactSlots[0].Charges);
        Assert.True(state.Players[0].ArtifactSlots[0].PendingChargeFull);

        state = EndTurn(state, 1);                                  // end of the turn the 3rd charge landed
        int tokens = state.Players[0].Lanes.Count(l => l.Occupant is not null);
        var s = state.Players[0].ArtifactSlots[0];
        // R20: the summon resolves at the end of WHICHEVER turn the 3rd charge landed
        Assert.True(tokens == 1 || s.PendingChargeFull, $"tokens={tokens} pending={s.PendingChargeFull} charges={s.Charges}");
        Assert.Equal(0, s.Charges);
    }

    // ───────── 3. NO_ATTACKERS_LAST_TURN is stale during the enemy's turn (R3 Bulwark) ─────────
    [Fact]
    public void Probe_NoAttackersLastTurn_DuringEnemyTurn_ReadsMostRecentCompletedTurn()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 2, vigor: 9);
        PlaceCreature(state, 1, 0, attack: 1, vigor: 9);
        // P0 turn 1: no attack. P1 turn 1: nothing. P0 turn 2: attack. Then during P1's turn 2 the condition
        // "zero friendly creatures attacked during your most recent completed turn" must be FALSE for P0.
        state = EndTurn(state, 0);
        state = EndTurn(state, 1);
        state = Attack(state, 0, 0);
        state = EndTurn(state, 0);
        Assert.Equal(1, state.CurrentPlayerIndex);
        var cond = new ConditionDef { Op = ConditionOp.NO_ATTACKERS_LAST_TURN };
        bool met = TriggerBus.EvaluateCondition(cond, new CardInstance(9999, "x", 0), 0, state);
        Assert.False(met, "P0 attacked on its most recent completed turn - Bulwark must not fire");
    }

    // ───────── 4. SEALED is never enforced ─────────
    [Fact]
    public void Probe_Sealed_CannotBeTargetedByEnemyAbility()
    {
        var state = CreateState();
        var sealedOne = PlaceCreature(state, 1, 2, attack: 2, vigor: 5, "SEALED");
        var ritual = new CardInstance(state.NextInstanceId++, "tst_bolt", 0) { CardType = CardType.RITUAL, Zone = Zone.Hand, Cost = 1 };
        ritual.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.RESOLVE,
            Effects = new List<EffectDef> { new() { Op = Op.DAMAGE, Amount = 3, Target = new TargetDef { Scope = Scope.ENEMY_CREATURE, Count = TargetCount.Exactly(1) } } }
        });
        state.Players[0].Hand.Add(ritual);
        state = DuelEngine.Apply(state, new PlayCardAction { PlayerIndex = 0, CardInstanceId = ritual.InstanceId, LaneIndex = 2 });
        var target = state.Players[1].Lanes[2].Occupant!;
        Assert.Equal(0, target.Damage);
    }

    // ───────── 5. Pierce excess ignores Armor / shields on the blocker ─────────
    [Fact]
    public void Probe_Pierce_ExcessUsesDamageActuallyDealt()
    {
        var state = CreateState();
        PlaceCreature(state, 0, 0, attack: 5, vigor: 9, "PIERCE");
        PlaceCreature(state, 1, 0, attack: 0, vigor: 2, "ARMOR:1");
        int before = state.Players[1].Vigor;
        state = Attack(state, 0, 0);
        // 5 power, armor shaves 1 -> 4 dealt, 2 needed to kill -> 2 excess
        Assert.Equal(before - 2, state.Players[1].Vigor);
    }

    // ───────── 6. P0's artifact passives are not applied on turn 1 (spec §3: active from turn 1) ─────────
    [Fact]
    public void Probe_ArtifactPassives_ActiveOnTurnOne()
    {
        CardRegistry.Clear(); ArtifactRegistry.Clear();
        try
        {
            CardRegistry.Register(new CardDef { Id = "zz_probe_grunt", Name = "Grunt", Type = CardType.CREATURE, Cost = 1, Attack = 1, Vigor = 1 });
            ArtifactRegistry.Register(new ArtifactDef
            {
                Id = "artf_zz_probe_discount", Name = "Probe Discount", Class = "zz", SlotPool = "a",
                Passive = new EffectDef { Op = Op.COST_MOD, AppliesTo = "CREATURE", Amount = 1, Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            });
            ArtifactRegistry.Register(new ArtifactDef
            {
                Id = "artf_zz_probe_blank", Name = "Probe Blank", Class = "zz", SlotPool = "b",
                Passive = new EffectDef { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } }
            });
            var deck = Enumerable.Repeat("zz_probe_grunt", 30).ToList();
            var state = GameState.Initialize(new GameConfig
            {
                Seed = 1, Player0DeckIds = deck, Player1DeckIds = deck,
                Player0Class = "zz", Player0ArtifactIds = new[] { "artf_zz_probe_discount", "artf_zz_probe_blank" },
                Player1Class = "zz", Player1ArtifactIds = new[] { "artf_zz_probe_blank", "artf_zz_probe_blank" },
            });
            // turn 1, P0 to move: the discount is already standing
            var card = state.Players[0].Hand[0];
            Assert.Equal(0, CostInterceptor.GetEffectiveCost(state, card, 0));
        }
        finally { CardRegistry.Clear(); ArtifactRegistry.Clear(); }
    }

    // ───────── 7. Rune: unconditional PASSIVE buff only hits creatures already on the (empty) board ─────────
    [Fact]
    public void Probe_PassiveRune_BuffsCreaturesSummonedLater()
    {
        var state = CreateState();
        var page = new RunePage();
        page.Equip(new RuneDef
        {
            Id = "rune_vrd_sharp_roots", Name = "Sharp Roots", Strata = Strata.VERDANT,
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new List<EffectDef> { new() { Op = Op.BUFF, Attack = 1, Duration = Duration.PERMANENT, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "ALL", Count = TargetCount.All } } }
            }
        });
        RuneInjector.ApplyRunes(state, page);
        var card = new CardInstance(state.NextInstanceId++, "tst_c", 0) { CardType = CardType.CREATURE, Zone = Zone.Hand, Cost = 1, BaseAttack = 2, BaseVigor = 2 };
        state.Players[0].Hand.Add(card);
        state = DuelEngine.Apply(state, new PlayCardAction { PlayerIndex = 0, CardInstanceId = card.InstanceId, LaneIndex = 0 });
        Assert.Equal(3, state.Players[0].Lanes[0].Occupant!.CurrentAttack);
    }
}
