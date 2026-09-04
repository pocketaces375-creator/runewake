using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// TASK-CLASS-IDENTITY-1B — Four items get their own feel: Ritual Fetish,
/// Banner of Sunspire, Book of Familiar, Elemental Bond.
/// Tests that each effect exists as DSL/engine with a unit test that fires it.
/// </summary>
[Collection("NonParallel")]
public class HeroIdentityTests
{
    // ——— Shared helpers ———

    private static GameState CreateState()
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 10; i++)
            {
                var c = new CardInstance(state.NextInstanceId++, "tst_d", p) { Zone = Zone.Deck };
                state.Players[p].Deck.Add(c);
            }
        }
        return state;
    }

    private static CardInstance PlaceCreature(GameState state, int pIdx, int lane,
        int attack = 2, int vigor = 5, string? keyword = null)
    {
        var c = new CardInstance(state.NextInstanceId++, $"tst_cr_p{pIdx}_l{lane}", pIdx)
        {
            Zone = Zone.Lane,
            LaneIndex = lane,
            CardType = CardType.CREATURE,
            BaseAttack = attack,
            BaseVigor = vigor,
            Cost = 1,
            IsExhausted = false
        };
        if (keyword != null)
            c.Keywords.Add(keyword);
        state.Players[pIdx].Lanes[lane].Occupant = c;
        return c;
    }

    private static void SetupDualArtifactSlots(GameState state, string artClass)
    {
        var player = state.Players[0];
        player.ArtifactClass = artClass;
        player.ArtifactDefIds = new[] { "tst_art_a", "tst_art_b" };
        player.ArtifactSlots = new ArtifactSlot[2];
        player.ArtifactSlots[0] = new ArtifactSlot(0);
        player.ArtifactSlots[1] = new ArtifactSlot(1);
    }

    private static CardInstance MakeMinimalSlot(GameState state, int slotIdx)
    {
        var inst = new CardInstance(state.NextInstanceId++, "tst_art_min", 0)
        {
            CardType = CardType.ARTIFACT,
            Zone = Zone.ArtifactSlot,
            ArtifactSlotIndex = slotIdx
        };
        inst.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        return inst;
    }

    // ================================================================
    // RITUAL FETISH (Necromancer)
    // ================================================================

    [Fact]
    public void RitualFetish_GainsChargeOnFriendlyDeath()
    {
        // ON_CREATURE_DIES condition FRIENDLY → ADD_CHARGE
        var state = CreateState();
        SetupDualArtifactSlots(state, "necromancer");

        var fetishSlot = state.Players[0].ArtifactSlots[0];
        var fetish = new CardInstance(state.NextInstanceId++, "artf_necromancer_ritual_piece", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        // ON_CREATURE_DIES (FRIENDLY) → ADD_CHARGE
        fetish.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        fetish.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CREATURE_DIES,
            Condition = new ConditionDef { Op = ConditionOp.FRIENDLY },
            Effects = new List<EffectDef>
            {
                new() { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 1 }
            }
        });
        fetishSlot.MaxCharges = 3;
        fetishSlot.Charges = 0;
        fetishSlot.Occupant = fetish;
        state.Players[0].ArtifactSlots[0] = fetishSlot;

        // Slot 1 minimal
        state.Players[0].ArtifactSlots[1].Occupant = MakeMinimalSlot(state, 1);

        // Kill a friendly creature via EffectExecutor.Destroy
        var friendly = PlaceCreature(state, 0, 0, attack: 1, vigor: 1);
        friendly.Damage = 1;

        var killEffect = new EffectDef { Op = Op.DESTROY };
        var targets = new List<ResolvedTarget> { new CreatureTarget(friendly, 0, 0) };
        EffectExecutor.Execute(killEffect, friendly, state, targets);

        // Friendly death fired ON_CREATURE_DIES → ADD_CHARGE
        Assert.Equal(1, state.Players[0].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void RitualFetish_DoesNotGainChargeOnEnemyDeath()
    {
        var state = CreateState();
        SetupDualArtifactSlots(state, "necromancer");

        var fetishSlot = state.Players[0].ArtifactSlots[0];
        var fetish = new CardInstance(state.NextInstanceId++, "artf_necromancer_ritual_piece", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        fetish.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        fetish.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CREATURE_DIES,
            Condition = new ConditionDef { Op = ConditionOp.FRIENDLY },
            Effects = new List<EffectDef>
            {
                new() { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 1 }
            }
        });
        fetishSlot.MaxCharges = 3;
        fetishSlot.Charges = 0;
        fetishSlot.Occupant = fetish;
        state.Players[0].ArtifactSlots[0] = fetishSlot;
        state.Players[0].ArtifactSlots[1].Occupant = MakeMinimalSlot(state, 1);

        // Kill an enemy creature (should not trigger FRIENDLY condition)
        var enemy = PlaceCreature(state, 1, 0, attack: 1, vigor: 1);
        enemy.Damage = 1;
        var killEffect = new EffectDef { Op = Op.DESTROY };
        var targets = new List<ResolvedTarget> { new CreatureTarget(enemy, 1, 0) };
        EffectExecutor.Execute(killEffect, enemy, state, targets);

        Assert.Equal(0, state.Players[0].ArtifactSlots[0].Charges);
    }

    [Fact]
    public void RitualFetish_UnearthsHighestAttackCreatureFromGraveyard()
    {
        // UNEARTH_FROM_GRAVEYARD: find highest-attack creature in discard → empty lane
        var state = CreateState();
        var player = state.Players[0];

        // Put creatures in discard (graveyard) — higher attack one added second
        player.Discard.Add(new CardInstance(state.NextInstanceId++, "tst_corpse_low", 0)
        {
            Zone = Zone.Discard, CardType = CardType.CREATURE,
            BaseAttack = 2, BaseVigor = 3, Cost = 2
        });
        player.Discard.Add(new CardInstance(state.NextInstanceId++, "tst_corpse_high", 0)
        {
            Zone = Zone.Discard, CardType = CardType.CREATURE,
            BaseAttack = 5, BaseVigor = 3, Cost = 4
        });

        // Execute UNEARTH_FROM_GRAVEYARD
        var effect = new EffectDef { Op = Op.UNEARTH_FROM_GRAVEYARD };
        var source = new CardInstance(0, "src", 0);
        var targets = new List<ResolvedTarget> { new PlayerTarget(player) };
        EffectExecutor.Execute(effect, source, state, targets);

        // Highest-attack creature (5) should be in lane 0
        Assert.NotNull(player.Lanes[0].Occupant);
        Assert.Equal(5, player.Lanes[0].Occupant!.CurrentAttack);
        Assert.True(player.Lanes[0].Occupant!.IsExhausted);
        // Lower attack creature remains in discard
        Assert.Single(player.Discard, c => c.CardDefId == "tst_corpse_low");
    }

    [Fact]
    public void RitualFetish_Unearth_SkipsNonCreaturesInGraveyard()
    {
        var state = CreateState();
        var player = state.Players[0];

        // Put a ritual (non-creature) in discard — no valid creature
        player.Discard.Add(new CardInstance(state.NextInstanceId++, "tst_ritual", 0)
        {
            Zone = Zone.Discard, CardType = CardType.RITUAL, Cost = 1
        });

        var effect = new EffectDef { Op = Op.UNEARTH_FROM_GRAVEYARD };
        var source = new CardInstance(0, "src", 0);
        var targets = new List<ResolvedTarget> { new PlayerTarget(player) };
        EffectExecutor.Execute(effect, source, state, targets);

        // No lane filled (no creature in discard)
        Assert.Null(player.Lanes[0].Occupant);
    }

    [Fact]
    public void RitualFetish_FullCycle_ChargeFullFiresUnearthAndReset()
    {
        // Fill charges → ON_CHARGE_FULL fires UNEARTH + RESET_CHARGES
        var state = CreateState();
        SetupDualArtifactSlots(state, "necromancer");

        var fetishSlot = state.Players[0].ArtifactSlots[0];
        var fetish = new CardInstance(state.NextInstanceId++, "artf_necromancer_ritual_piece", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        fetish.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        fetish.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CREATURE_DIES,
            Condition = new ConditionDef { Op = ConditionOp.FRIENDLY },
            Effects = new List<EffectDef>
            {
                new() { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 1 }
            }
        });
        // ON_CHARGE_FULL: UNEARTH + RESET_CHARGES
        fetish.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.ON_CHARGE_FULL,
            Effects = new List<EffectDef>
            {
                new() { Op = Op.UNEARTH_FROM_GRAVEYARD, Target = new TargetDef { Scope = Scope.PLAYER_SELF } },
                new() { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.PLAYER_SELF } }
            }
        });
        fetishSlot.MaxCharges = 3;
        fetishSlot.Charges = 0;
        fetishSlot.Occupant = fetish;
        state.Players[0].ArtifactSlots[0] = fetishSlot;
        state.Players[0].ArtifactSlots[1].Occupant = MakeMinimalSlot(state, 1);

        var player = state.Players[0];
        // Add a dead creature to graveyard
        player.Discard.Add(new CardInstance(state.NextInstanceId++, "tst_corpse", 0)
        {
            Zone = Zone.Discard, CardType = CardType.CREATURE,
            BaseAttack = 4, BaseVigor = 3, Cost = 3
        });

        // Add charges through EffectExecutor (which fires ON_CHARGE_FULL)
        var chargeEffect = new EffectDef { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 3 };
        var chargeTargets = new List<ResolvedTarget> { new PlayerTarget(player) };
        EffectExecutor.Execute(chargeEffect, fetish, state, chargeTargets);

        // ON_CHARGE_FULL should have fired: unearthed the corpse and reset charges
        Assert.Equal(0, fetishSlot.Charges);
        Assert.NotNull(player.Lanes[0].Occupant);
        Assert.Equal(4, player.Lanes[0].Occupant!.CurrentAttack);
    }

    // ================================================================
    // BANNER OF SUNSPIRE (Paladin)
    // ================================================================

    [Fact]
    public void BannerOfSunspire_GrantsPlusZeroOneToCreatures()
    {
        // BUFF +0/+1 WHILE_PRESENT is a valid DSL operation
        var state = CreateState();
        var player = state.Players[0];
        var cr = PlaceCreature(state, 0, 0, attack: 2, vigor: 4);

        var buffEffect = new EffectDef
        {
            Op = Op.BUFF,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All },
            Attack = 0, Vigor = 1,
            Duration = Duration.WHILE_PRESENT
        };
        var source = new CardInstance(0, "banner", 0);
        var targets = TargetResolver.Resolve(buffEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(buffEffect, source, state, targets);

        // Creature gained +0/+1
        Assert.Equal(1, cr.VigorModifier);
    }

    [Fact]
    public void BannerOfSunspire_AddChargeOpWorks()
    {
        // ADD_CHARGE is a valid DSL operation
        var state = CreateState();
        SetupDualArtifactSlots(state, "paladin");

        var bannerSlot = state.Players[0].ArtifactSlots[0];
        bannerSlot.MaxCharges = 3;
        bannerSlot.Charges = 0;
        var banner = new CardInstance(state.NextInstanceId++, "artf_paladin_banner", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        banner.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        bannerSlot.Occupant = banner;
        state.Players[0].ArtifactSlots[0] = bannerSlot;
        state.Players[0].ArtifactSlots[1].Occupant = MakeMinimalSlot(state, 1);

        var addEffect = new EffectDef { Op = Op.ADD_CHARGE, Target = new TargetDef { Scope = Scope.PLAYER_SELF }, Amount = 1 };
        var targets = new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) };
        EffectExecutor.Execute(addEffect, banner, state, targets);

        Assert.Equal(1, bannerSlot.Charges);
    }

    [Fact]
    public void BannerOfSunspire_FullEffect_HealsAndGrantsWard()
    {
        // HEAL 2 all + GRANT_KEY WARD — test each effect independently
        var state = CreateState();
        var player = state.Players[0];

        var cr1 = PlaceCreature(state, 0, 0, attack: 2, vigor: 4);
        var cr2 = PlaceCreature(state, 0, 1, attack: 1, vigor: 3);
        cr1.Damage = 3; // 4-3 = 1 vigor remaining
        cr2.Damage = 2; // 3-2 = 1 vigor remaining

        // HEAL 2 to all friendly creatures
        var healEffect = new EffectDef { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All }, Amount = 2 };
        var source = new CardInstance(0, "banner", 0);
        var targets = TargetResolver.Resolve(healEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(healEffect, source, state, targets);

        Assert.Equal(1, cr1.Damage); // 3 - 2 = 1
        Assert.Equal(0, cr2.Damage); // 2 - 2 = 0

        // GRANT_KEY WARD to all friendly creatures
        var wardEffect = new EffectDef { Op = Op.GRANT_KEY, Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All }, Keyword = "WARD" };
        targets = TargetResolver.Resolve(wardEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(wardEffect, source, state, targets);

        Assert.Contains("WARD", cr1.EffectiveKeywords);
        Assert.Contains("WARD", cr2.EffectiveKeywords);
    }

    [Fact]
    public void BannerOfSunspire_ChargeReset_Works()
    {
        // RESET_CHARGES works on a slot with charges
        var state = CreateState();
        SetupDualArtifactSlots(state, "paladin");

        var bannerSlot = state.Players[0].ArtifactSlots[0];
        bannerSlot.MaxCharges = 3;
        bannerSlot.Charges = 2;
        var banner = new CardInstance(state.NextInstanceId++, "artf_paladin_banner", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        banner.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        bannerSlot.Occupant = banner;
        state.Players[0].ArtifactSlots[0] = bannerSlot;
        state.Players[0].ArtifactSlots[1].Occupant = MakeMinimalSlot(state, 1);

        var resetEffect = new EffectDef { Op = Op.RESET_CHARGES, Target = new TargetDef { Scope = Scope.PLAYER_SELF } };
        var targets = new List<ResolvedTarget> { new PlayerTarget(state.Players[0]) };
        EffectExecutor.Execute(resetEffect, banner, state, targets);

        Assert.Equal(0, bannerSlot.Charges);
    }

    // ================================================================
    // BOOK OF FAMILIAR (Druid)
    // ================================================================

    [Fact]
    public void BookOfFamiliar_SummonFamiliar_OpWorks()
    {
        // SUMMON with token_id, attack=1, vigor=1, keyword=ROOTED
        var state = CreateState();
        var player = state.Players[0];

        var summonEffect = new EffectDef
        {
            Op = Op.SUMMON,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF },
            TokenId = "tok_familiar", Attack = 1, Vigor = 1, Keyword = "ROOTED"
        };
        var source = new CardInstance(0, "book", 0);
        var targets = new List<ResolvedTarget> { new PlayerTarget(player) };
        EffectExecutor.Execute(summonEffect, source, state, targets);

        // Familiar token should be in lane 0
        var lane0 = player.Lanes[0];
        Assert.NotNull(lane0.Occupant);
        Assert.Equal("tok_familiar", lane0.Occupant!.CardDefId);
        Assert.Equal(1, lane0.Occupant!.CurrentAttack);
        Assert.Equal(1, lane0.Occupant!.CurrentVigor);
        Assert.Contains("ROOTED", lane0.Occupant!.EffectiveKeywords);
        Assert.True(lane0.Occupant!.IsExhausted);
    }

    [Fact]
    public void BookOfFamiliar_SummonUsesEmptyLane()
    {
        // SUMMON places token in first empty lane
        var state = CreateState();
        var player = state.Players[0];

        // Fill lane 0 with a creature
        PlaceCreature(state, 0, 0, attack: 3, vigor: 3);

        var summonEffect = new EffectDef
        {
            Op = Op.SUMMON,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF },
            TokenId = "tok_familiar", Attack = 1, Vigor = 1, Keyword = "ROOTED"
        };
        var source = new CardInstance(0, "book", 0);
        var targets = new List<ResolvedTarget> { new PlayerTarget(player) };
        EffectExecutor.Execute(summonEffect, source, state, targets);

        // Lane 0 still occupied by our creature
        Assert.NotNull(player.Lanes[0].Occupant);
        Assert.Equal("tst_cr_p0_l0", player.Lanes[0].Occupant!.CardDefId);
        // Familiar should be in lane 1 (first empty lane)
        Assert.NotNull(player.Lanes[1].Occupant);
        Assert.Equal("tok_familiar", player.Lanes[1].Occupant!.CardDefId);
    }

    [Fact]
    public void BookOfFamiliar_NoSummonOnFullBoard()
    {
        // SUMMON fails silently when no empty lane
        var state = CreateState();
        var player = state.Players[0];

        // Fill all lanes
        for (int i = 0; i < 5; i++)
            PlaceCreature(state, 0, i, attack: 1, vigor: 2);

        var summonEffect = new EffectDef
        {
            Op = Op.SUMMON,
            Target = new TargetDef { Scope = Scope.PLAYER_SELF },
            TokenId = "tok_familiar", Attack = 1, Vigor = 1, Keyword = "ROOTED"
        };
        var source = new CardInstance(0, "book", 0);
        var targets = new List<ResolvedTarget> { new PlayerTarget(player) };
        EffectExecutor.Execute(summonEffect, source, state, targets);

        // No familiar summoned — all lanes still have their original occupants
        for (int i = 0; i < 5; i++)
            Assert.NotNull(player.Lanes[i].Occupant);
    }

    [Fact]
    public void BookOfFamiliar_FullEffect_BuffsFamiliarsToTwoTwoWithGuard()
    {
        // SET_STAT 2/2 + GRANT_KEY GUARD to token creatures
        var state = CreateState();
        var player = state.Players[0];

        // Create two Familiar tokens
        var fam1 = new CardInstance(state.NextInstanceId++, "tok_familiar", 0)
        {
            Zone = Zone.Lane, LaneIndex = 0, CardType = CardType.TOKEN,
            BaseAttack = 1, BaseVigor = 1, Cost = 0, IsExhausted = true
        };
        var fam2 = new CardInstance(state.NextInstanceId++, "tok_familiar", 0)
        {
            Zone = Zone.Lane, LaneIndex = 1, CardType = CardType.TOKEN,
            BaseAttack = 1, BaseVigor = 1, Cost = 0, IsExhausted = true
        };
        player.Lanes[0].Occupant = fam1;
        player.Lanes[1].Occupant = fam2;

        // SET_STAT 2/2 to all tokens
        var setStatEffect = new EffectDef
        {
            Op = Op.SET_STAT,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "TYPE:TOKEN", Count = TargetCount.All },
            Attack = 2, Vigor = 2
        };
        var source = new CardInstance(0, "book", 0);
        var targets = TargetResolver.Resolve(setStatEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(setStatEffect, source, state, targets);

        // GRANT_KEY GUARD to all tokens
        var guardEffect = new EffectDef
        {
            Op = Op.GRANT_KEY,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "TYPE:TOKEN", Count = TargetCount.All },
            Keyword = "GUARD"
        };
        targets = TargetResolver.Resolve(guardEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(guardEffect, source, state, targets);

        Assert.Equal(2, fam1.BaseAttack);
        Assert.Equal(2, fam1.BaseVigor);
        Assert.Contains("GUARD", fam1.EffectiveKeywords);

        Assert.Equal(2, fam2.BaseAttack);
        Assert.Equal(2, fam2.BaseVigor);
        Assert.Contains("GUARD", fam2.EffectiveKeywords);
    }

    // ================================================================
    // ELEMENTAL BOND (Druid)
    // ================================================================

    [Fact]
    public void ElementalBond_GrantsBuffAndRooted()
    {
        // BUFF +0/+2 PERMANENT + GRANT_KEY ROOTED
        var state = CreateState();
        var player = state.Players[0];
        var cr = PlaceCreature(state, 0, 0, attack: 3, vigor: 5);

        var source = new CardInstance(0, "bond", 0);

        // BUFF +0/+2 PERMANENT
        var buffEffect = new EffectDef
        {
            Op = Op.BUFF,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "MOST_WOUNDED", Count = TargetCount.Exactly(1) },
            Attack = 0, Vigor = 2, Duration = Duration.PERMANENT
        };
        var targets = TargetResolver.Resolve(buffEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(buffEffect, source, state, targets);

        Assert.Equal(2, cr.VigorModifier);

        // GRANT_KEY ROOTED
        var rootedEffect = new EffectDef
        {
            Op = Op.GRANT_KEY,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "MOST_WOUNDED", Count = TargetCount.Exactly(1) },
            Keyword = "ROOTED"
        };
        targets = TargetResolver.Resolve(rootedEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(rootedEffect, source, state, targets);

        Assert.Contains("ROOTED", cr.EffectiveKeywords);
    }

    [Fact]
    public void ElementalBond_FullEffect_GrantsBonusStatsAndReach()
    {
        // BUFF +2/+2 PERMANENT + GRANT_KEY REACH to bonded (ROOTED) creature
        var state = CreateState();
        var player = state.Players[0];
        // Create a creature with ROOTED (simulating bonded creature)
        var cr = PlaceCreature(state, 0, 0, attack: 3, vigor: 4);
        cr.Keywords.Add("ROOTED");

        var source = new CardInstance(0, "bond", 0);

        // BUFF +2/+2 PERMANENT to ROOTED creature
        var buffEffect = new EffectDef
        {
            Op = Op.BUFF,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:ROOTED", Count = TargetCount.Exactly(1), Tiebreak = "OLDEST_IN_PLAY" },
            Attack = 2, Vigor = 2, Duration = Duration.PERMANENT
        };
        var targets = TargetResolver.Resolve(buffEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(buffEffect, source, state, targets);

        Assert.Equal(2, cr.AttackModifier);
        Assert.Equal(2, cr.VigorModifier);

        // GRANT_KEY REACH
        var reachEffect = new EffectDef
        {
            Op = Op.GRANT_KEY,
            Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Filter = "KEYWORD:ROOTED", Count = TargetCount.Exactly(1), Tiebreak = "OLDEST_IN_PLAY" },
            Keyword = "REACH"
        };
        targets = TargetResolver.Resolve(reachEffect.Target!, source, player, state.Players[1], state);
        EffectExecutor.Execute(reachEffect, source, state, targets);

        Assert.Contains("REACH", cr.EffectiveKeywords);
    }

    [Fact]
    public void ElementalBond_AddChargeViaTurnStart_Works()
    {
        // ADD_CHARGE each turn via on_turn_start auto-charge
        // This tests the auto-charge system: slot with AutoChargeGainOn="on_turn_start"
        // gains 1 charge at the start of the owner's turn.
        var state = CreateState();
        SetupDualArtifactSlots(state, "druid");

        var bondSlot = state.Players[0].ArtifactSlots[0];
        var bond = new CardInstance(state.NextInstanceId++, "artf_druid_elemental_bond", 0)
        {
            CardType = CardType.ARTIFACT, Zone = Zone.ArtifactSlot, ArtifactSlotIndex = 0
        };
        bond.Abilities.Add(new AbilityDef
        {
            Trigger = Trigger.PASSIVE,
            Effects = new List<EffectDef> { new() { Op = Op.HEAL, Target = new TargetDef { Scope = Scope.NONE } } }
        });
        bondSlot.MaxCharges = 3;
        bondSlot.Charges = 0;
        bondSlot.AutoChargeGainOn = "on_turn_start";
        bondSlot.Occupant = bond;
        state.Players[0].ArtifactSlots[0] = bondSlot;
        state.Players[0].ArtifactSlots[1].Occupant = MakeMinimalSlot(state, 1);

        // Simulate the start of P0's turn by calling AutoGainCharges directly
        DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 }); // P0 ends → P1's turn
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 }); // P1 ends → P0's turn → auto-charge fires

        Assert.Equal(1, state.Players[0].ArtifactSlots[0].Charges);
    }
}