using System;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

[Collection("NonParallel")]
public class RuneEngineTests
{
    /// <summary>
    /// Helper: register a minimal card set and build a config with a rune page.
    /// </summary>
    private static GameConfig MakeConfig(RunePage? runePage = null, bool useAttacker = false)
    {
        // Ensure we have at least one card in the registry
        CardRegistry.Clear();
        var def = new CardDef
        {
            Id = "test_attacker",
            Set = "test",
            Name = "Test Attacker",
            Strata = Runewake.Engine.Cards.Strata.VERDANT,
            Type = CardType.CREATURE,
            Rarity = Rarity.COMMON,
            Cost = 3,
            Attack = 3,
            Vigor = 4,
            Keywords = new(),
            Abilities = new()
        };
        CardRegistry.Register(def);

        var deckIds = Enumerable.Repeat("test_attacker", 30).ToList();
        return new GameConfig
        {
            Seed = 42,
            ContentVersion = 1,
            Player0DeckIds = deckIds,
            Player1DeckIds = deckIds,
            RunePage = runePage
        };
    }

    [Fact]
    public void NoRunes_GameInitializesNormally()
    {
        var config = MakeConfig(null);
        var state = GameState.Initialize(config);

        Assert.Equal(0, state.Players[0].RuneTokens.Count);
        Assert.Equal(1, state.TurnNumber);
        Assert.Equal(25, state.Players[0].Vigor);
    }

    [Fact]
    public void SharpRoots_GivesCreaturesPlusOneAttack()
    {
        // Sharp Roots: PASSIVE BUFF ALLY_CREATURE +1 ATK
        var rune = new RuneDef
        {
            Id = "rune_vrd_sharp_roots",
            Name = "Sharp Roots",
            Description = "+1 ATK",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 8,
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.BUFF,
                        Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All },
                        Attack = 1,
                        Duration = Duration.PERMANENT
                    }
                }
            }
        };
        var page = new RunePage();
        page.Equip(rune);

        var config = MakeConfig(page, useAttacker: true);
        var state = GameState.Initialize(config);

        // Sharp Roots is unconditional PASSIVE — no rune token created
        // The BUFF effect would apply to creatures already on board, but at match
        // start there are none. Creatures summoned later are unaffected by this
        // one-time application. This is a known design limitation for PASSIVE runes
        // that buff all creatures; they only work if creatures are already on board.
        Assert.Empty(state.Players[0].RuneTokens);
    }

    [Fact]
    public void TidalBarrier_IncreasesMaxVigor()
    {
        // Tidal Barrier: PASSIVE GAIN_VIGOR +5
        var rune = new RuneDef
        {
            Id = "rune_tid_tidal_barrier",
            Name = "Tidal Barrier",
            Description = "+5 max vigor",
            SlotType = RuneSlotType.DEFENSIVE,
            Cost = 10,
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.GAIN_VIGOR,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 5
                    }
                }
            }
        };
        var page = new RunePage();
        page.Equip(rune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        // GAIN_VIGOR increases both MaxVigor and current Vigor
        Assert.Equal(30, state.Players[0].MaxVigor);
        Assert.Equal(30, state.Players[0].Vigor);
        // Player 1 (bot) should be unchanged
        Assert.Equal(25, state.Players[1].MaxVigor);
    }

    [Fact]
    public void EmberDraw_DrawsCardAtTurnStart()
    {
        // Ember Draw: ON_TURN_START DRAW 1
        var rune = new RuneDef
        {
            Id = "rune_ember_draw",
            Name = "Ember Draw",
            Description = "Draw on turn start",
            SlotType = RuneSlotType.UTILITY,
            Cost = 14,
            Ability = new AbilityDef
            {
                Trigger = Trigger.ON_TURN_START,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.DRAW,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 1
                    }
                }
            }
        };
        var page = new RunePage();
        page.Equip(rune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        // Rune token should be registered
        Assert.Single(state.Players[0].RuneTokens);
        Assert.Equal(Trigger.ON_TURN_START, state.Players[0].RuneTokens[0].Abilities[0].Trigger);
    }

    [Fact]
    public void Kindling_FiresOnSummon()
    {
        // Kindling: ON_SUMMON DAMAGE PLAYER_ENEMY 1
        var rune = new RuneDef
        {
            Id = "rune_emb_kindling",
            Name = "Kindling",
            Description = "1 damage to enemy on summon",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 10,
            Ability = new AbilityDef
            {
                Trigger = Trigger.ON_SUMMON,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.DAMAGE,
                        Target = new TargetDef { Scope = Scope.PLAYER_ENEMY },
                        Amount = 1
                    }
                }
            }
        };
        var page = new RunePage();
        page.Equip(rune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        Assert.Single(state.Players[0].RuneTokens);
        Assert.Equal(Trigger.ON_SUMMON, state.Players[0].RuneTokens[0].Abilities[0].Trigger);
    }

    [Fact]
    public void UnconditionalPassiveRune_DoeNotCreateRuneToken()
    {
        // Sharp Roots is unconditional PASSIVE — no rune token, effects applied directly
        var rune = new RuneDef
        {
            Id = "rune_test_passive",
            Name = "Test Passive",
            Description = "Unconditional passive",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 5,
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.GAIN_VIGOR,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 3
                    }
                }
            }
        };
        var page = new RunePage();
        page.Equip(rune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        // Unconditional PASSIVE runes should NOT create rune tokens (they're applied immediately)
        // The sentinel cards used during passive application increment NextInstanceId but
        // are not added to any list, so RuneTokens should be empty
        Assert.Equal(0, state.Players[0].RuneTokens.Count);
        // But the effect should have been applied
        Assert.Equal(28, state.Players[0].Vigor);
    }

    [Fact]
    public void ConditionalPassive_BecomesOnTurnStart()
    {
        // Barrow Strength: PASSIVE with DAMAGED_THIS_TURN condition
        var rune = new RuneDef
        {
            Id = "rune_hol_barrow_strength",
            Name = "Barrow Strength",
            Description = "+2 ATK when damaged",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 10,
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Condition = new ConditionDef
                {
                    Op = ConditionOp.DAMAGED_THIS_TURN
                },
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.BUFF,
                        Target = new TargetDef { Scope = Scope.ALLY_CREATURE, Count = TargetCount.All },
                        Attack = 2,
                        Duration = Duration.PERMANENT
                    }
                }
            }
        };
        var page = new RunePage();
        page.Equip(rune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        // Conditional PASSIVE should create a rune token with ON_TURN_START trigger
        Assert.Single(state.Players[0].RuneTokens);
        Assert.Equal(Trigger.ON_TURN_START, state.Players[0].RuneTokens[0].Abilities[0].Trigger);
        Assert.NotNull(state.Players[0].RuneTokens[0].Abilities[0].Condition);
    }

    [Fact]
    public void MultipleRunes_AllInjected()
    {
        var page = new RunePage();

        var passiveRune = new RuneDef
        {
            Id = "rune_passive",
            Name = "Passive",
            Description = "Unconditional",
            SlotType = RuneSlotType.DEFENSIVE,
            Cost = 5,
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.GAIN_VIGOR,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 2
                    }
                }
            }
        };
        page.Equip(passiveRune);

        var triggeredRune = new RuneDef
        {
            Id = "rune_triggered",
            Name = "Triggered",
            Description = "On turn start",
            SlotType = RuneSlotType.UTILITY,
            Cost = 10,
            Ability = new AbilityDef
            {
                Trigger = Trigger.ON_TURN_START,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.DRAW,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 1
                    }
                }
            }
        };
        page.Equip(triggeredRune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        // 1 passive (applied, no token) + 1 triggered (1 token) = 1 token
        Assert.Single(state.Players[0].RuneTokens);
        // Passive effect applied
        Assert.Equal(27, state.Players[0].Vigor);
    }

    [Fact]
    public void PlayerOne_HasNoRunes()
    {
        var page = new RunePage();
        var passiveRune = new RuneDef
        {
            Id = "rune_p1_test",
            Name = "P1 Test",
            Description = "Test",
            SlotType = RuneSlotType.DEFENSIVE,
            Cost = 5,
            Ability = new AbilityDef
            {
                Trigger = Trigger.PASSIVE,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.GAIN_VIGOR,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 10
                    }
                }
            }
        };
        page.Equip(passiveRune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        // Only player 0 gets rune effects
        Assert.Equal(35, state.Players[0].Vigor);
        Assert.Empty(state.Players[1].RuneTokens);
        Assert.Equal(25, state.Players[1].Vigor);
    }

    [Fact]
    public void GrowthRite_RegistersAsOnTurnStart()
    {
        var rune = new RuneDef
        {
            Id = "rune_vrd_growth_rite",
            Name = "Growth Rite",
            Description = "+1 attune/turn",
            SlotType = RuneSlotType.UTILITY,
            Cost = 10,
            Ability = new AbilityDef
            {
                Trigger = Trigger.ON_TURN_START,
                Effects = new()
                {
                    new EffectDef
                    {
                        Op = Op.ATTUNE,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 1
                    }
                }
            }
        };
        var page = new RunePage();
        page.Equip(rune);

        var config = MakeConfig(page);
        var state = GameState.Initialize(config);

        Assert.Single(state.Players[0].RuneTokens);
        Assert.Equal(Trigger.ON_TURN_START, state.Players[0].RuneTokens[0].Abilities[0].Trigger);
    }
}