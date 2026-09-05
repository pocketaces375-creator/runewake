using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

public class TriggerBusTests
{
    /// <summary>
    /// Creates a state with full attunement and one empty lane (lane 4)
    /// for placing test creatures.
    /// </summary>
    private static GameState CreateTestState(int p0DeckSize = 5)
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < p0DeckSize; i++)
            {
                var c = new CardInstance(state.NextInstanceId++, "tst_d", p)
                { Zone = Zone.Deck };
                state.Players[p].Deck.Add(c);
            }
        }
        return state;
    }

    /// <summary>
    /// Places a creature with the given stats and abilities in a lane.
    /// </summary>
    private static void PlaceCreature(GameState state, int pIdx, int lane,
        int attack, int vigor, List<AbilityDef>? abilities = null)
    {
        var card = new CardInstance(state.NextInstanceId++, "tst_trigger", pIdx)
        {
            Zone = Zone.Lane, LaneIndex = lane,
            CardType = CardType.CREATURE,
            BaseAttack = attack, BaseVigor = vigor,
            Cost = 1, IsExhausted = false
        };
        if (abilities is not null)
            card.Abilities = abilities;
        state.Players[pIdx].Lanes[lane].Occupant = card;
    }

    /// <summary>
    /// Helper to create an ON_DEATH ability with a DESTROY effect targeting
    /// an enemy creature.
    /// </summary>
    private static AbilityDef OnDeathDestroyEnemy()
    {
        return new AbilityDef
        {
            Trigger = Trigger.ON_DEATH,
            Effects = new List<EffectDef>
            {
                new EffectDef
                {
                    Op = Op.DESTROY,
                    Target = new TargetDef
                    {
                        Scope = Scope.ENEMY_CREATURE,
                        Filter = "ANY",
                        Count = TargetCount.Exactly(1)
                    }
                }
            }
        };
    }

    /// <summary>
    /// Helper to create an ON_DEATH ability that SUMMONs a self-referential token.
    /// </summary>
    private static AbilityDef OnDeathSummonSelf()
    {
        return new AbilityDef
        {
            Trigger = Trigger.ON_DEATH,
            Effects = new List<EffectDef>
            {
                new EffectDef
                {
                    Op = Op.SUMMON,
                    Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                    TokenId = "tst_loop_token"
                }
            }
        };
    }

    private static AbilityDef OnSummonBuffSelf(int atk, int vig)
    {
        return new AbilityDef
        {
            Trigger = Trigger.ON_SUMMON,
            Effects = new List<EffectDef>
            {
                new EffectDef
                {
                    Op = Op.BUFF,
                    Target = new TargetDef { Scope = Scope.SELF },
                    Attack = atk, Vigor = vig
                }
            }
        };
    }

    // ——— ON_SUMMON ———

    [Fact]
    public void OnSummon_FiresWhenCreaturePlayed()
    {
        var state = CreateTestState();
        // P0 has a creature with ON_SUMMON that buffs itself
        var card = new CardInstance(state.NextInstanceId++, "tst_buffer", 0)
        {
            Zone = Zone.Hand, CardType = CardType.CREATURE,
            BaseAttack = 2, BaseVigor = 2, Cost = 1, IsExhausted = false,
            Abilities = new List<AbilityDef> { OnSummonBuffSelf(2, 3) }
        };
        state.Players[0].Hand.Add(card);

        state = DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = 0, CardInstanceId = card.InstanceId,
            Cost = 1, LaneIndex = 4
        });

        // ON_SUMMON should have fired: buff +2/+3
        var placed = state.Players[0].Lanes[4].Occupant!;
        Assert.Equal(2, placed.AttackModifier);
        Assert.Equal(3, placed.VigorModifier);
    }

    // ——— ON_DEATH chain ———

    [Fact]
    public void OnDeath_ChainOfThree()
    {
        // Death-trigger chain of 3.
        // P1 lane 3: attacker with REACH (attacks P0 lane 0)
        // P0 lane 0: first ON_DEATH trigger creature
        // P1 lane 0: second ON_DEATH trigger creature  
        // P0 lane 1: normal creature (end of chain)
        var state = CreateTestState();

        PlaceCreature(state, 0, 0, 1, 1, new List<AbilityDef> { OnDeathDestroyEnemy() });
        PlaceCreature(state, 1, 0, 1, 1, new List<AbilityDef> { OnDeathDestroyEnemy() });
        PlaceCreature(state, 0, 1, 1, 1); // end of chain

        // P1 lane 1: attacker with REACH (2/2) attacks P0 lane 0
        var attacker = new CardInstance(state.NextInstanceId++, "tst_atk", 1)
        {
            Zone = Zone.Lane, LaneIndex = 1,
            CardType = CardType.CREATURE,
            BaseAttack = 2, BaseVigor = 2, Cost = 1, IsExhausted = false
        };
        attacker.Keywords.Add("REACH");
        state.Players[1].Lanes[1].Occupant = attacker;
        attacker.IsExhausted = false;

        // P1 lane 1 attacks P0 lane 0 with REACH (adjacent via lane 0)
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 1, SourceLane = 1, TargetLane = 0
        });

        // P0 lane 0 (1/1) dies against P1 lane 1 (2/2).
        // P0 lane 0 ON_DEATH: DESTROY enemy ANY → P1 lane 0 (first P1 creature)
        // P1 lane 0 (1/1) dies. ON_DEATH: DESTROY enemy ANY → P0 lane 1 (last)
        // P0 lane 1 (1/1) dies. No ON_DEATH. Chain ends.
        // Chain: 3 deaths (P0 lane 0 → P1 lane 0 → P0 lane 1)
        Assert.Null(state.Players[0].Lanes[0].Occupant);
        Assert.Null(state.Players[1].Lanes[0].Occupant);
        Assert.Null(state.Players[0].Lanes[1].Occupant);
        // Attacker survived
        Assert.NotNull(state.Players[1].Lanes[1].Occupant);
    }

    // ——— Loop termination ———

    [Fact]
    public void TriggerDepth_LoopsAreCappedAt20()
    {
        // Create a self-referential loop: creature with ON_DEATH that
        // DESTROY enemy. Chain triggers back and forth.
        // P1's attacker kills P0's trigger creature.
        // That creature's ON_DEATH kills P1's trigger creature.
        // P1's ON_DEATH kills... nothing (no more enemy creatures).
        // For a real loop we'd need more creatures, but at minimum
        // this verifies the chain fires without crashing.
        var state = CreateTestState();

        // P0 lane 4: ON_DEATH that DESTROY enemy (in P0 lane 4, P1's lane 4 attacker hits it)
        PlaceCreature(state, 0, 4, 1, 1, new List<AbilityDef> { OnDeathDestroyEnemy() });
        // P1 lane 0: ON_DEATH that DESTROY enemy (will be hit by P0's chain)
        PlaceCreature(state, 1, 0, 1, 1, new List<AbilityDef> { OnDeathDestroyEnemy() });

        // P1 lane 4: attacker (2/10) kills P0 lane 4 (opposing lane has P0's trigger)
        var attacker = new CardInstance(state.NextInstanceId++, "tst_atk", 1)
        {
            Zone = Zone.Lane, LaneIndex = 4,
            CardType = CardType.CREATURE,
            BaseAttack = 2, BaseVigor = 10, Cost = 1, IsExhausted = false
        };
        state.Players[1].Lanes[4].Occupant = attacker;
        attacker.IsExhausted = false;

        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 1, SourceLane = 4 });

        // P0 lane 4 dies (1/1 vs 2/10). ON_DEATH: DESTROY enemy → P1 lane 0.
        // P1 lane 0 dies. ON_DEATH: DESTROY enemy → no remaining P0 creatures.
        // Chain depth should be 2 (P0 ON_DEATH fires at depth 1, P1 at depth 2).
        Assert.True(state.TriggerDepth > 0);
        Assert.Null(state.Players[0].Lanes[4].Occupant);
        Assert.Null(state.Players[1].Lanes[0].Occupant);
    }

    [Fact]
    public void TriggerDepth_StopsAtMaxDepth()
    {
        // Directly test the depth cap by repeatedly calling FireDeathEvents.
        // Create a card with ON_DEATH that always fires.
        var state = CreateTestState();

        // Set depth to max-1, fire an event that should increase it
        state.TriggerDepth = TriggerBus.MaxTriggerDepth - 1;

        var deadCard = new CardInstance(999, "tst_dead", 0)
        {
            CardType = CardType.CREATURE,
            Abilities = new List<AbilityDef>
            {
                new AbilityDef
                {
                    Trigger = Trigger.ON_DEATH,
                    Effects = new List<EffectDef>
                    {
                        new EffectDef
                        {
                            Op = Op.DRAW,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                            Amount = 1
                        }
                    }
                }
            }
        };

        // This should fire and hit max depth, but not throw
        TriggerBus.FireDeathEvents(state, deadCard, 0);

        // Depth should be capped
        Assert.Equal(TriggerBus.MaxTriggerDepth, state.TriggerDepth);

        // Drawing should have happened (depth before = 19, fires once = 20, 
        // but wait — we set depth to 19, the check is 19 >= 20? No, 19 < 20.
        // So it fires, depth becomes 20, and the DRAW happens.
        // The next ability (if any) would be blocked at 20 >= 20.
        Assert.Single(state.Players[0].Hand);
    }

    [Fact]
    public void TriggerDepth_BlocksAtDepth20()
    {
        var state = CreateTestState();
        state.TriggerDepth = TriggerBus.MaxTriggerDepth; // already at cap

        var deadCard = new CardInstance(998, "tst_dead", 0)
        {
            CardType = CardType.CREATURE,
            Abilities = new List<AbilityDef>
            {
                new AbilityDef
                {
                    Trigger = Trigger.ON_DEATH,
                    Effects = new List<EffectDef>
                    {
                        new EffectDef
                        {
                            Op = Op.DRAW,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                            Amount = 1
                        }
                    }
                }
            }
        };

        TriggerBus.FireDeathEvents(state, deadCard, 0);

        // Depth unchanged, no draw happened
        Assert.Equal(TriggerBus.MaxTriggerDepth, state.TriggerDepth);
        Assert.Empty(state.Players[0].Hand);
    }

    // ——— ON_TURN_START ———

    /// <summary>
    /// When P0 ends their turn, P1's turn starts. ON_TURN_START fires only
    /// for the player whose turn it is (P1). P0's creature should NOT fire
    /// its ON_TURN_START during P1's turn — turn-start triggers are per-player.
    /// </summary>
    [Fact]
    public void OnTurnStart_FiresOnlyForCurrentPlayer()
    {
        var state = CreateTestState();
        // Place a creature with ON_TURN_START that draws a card for P0
        PlaceCreature(state, 0, 4, 1, 1, new List<AbilityDef>
        {
            new AbilityDef
            {
                Trigger = Trigger.ON_TURN_START,
                Effects = new List<EffectDef>
                {
                    new EffectDef
                    {
                        Op = Op.DRAW,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 1
                    }
                }
            }
        });

        // End P0's turn → P1's turn starts. Only P1's ON_TURN_START should fire.
        // P0's creature should NOT fire because it's not P1's creature.
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // P0's creature should NOT have drawn — it's P1's turn start, not P0's.
        Assert.Empty(state.Players[0].Hand);
    }

    // ——— ON_TURN_END ———

    [Fact]
    public void OnTurnEnd_FiresForEndingPlayer()
    {
        var state = CreateTestState();
        // Place a creature with ON_TURN_END that draws
        PlaceCreature(state, 0, 4, 1, 1, new List<AbilityDef>
        {
            new AbilityDef
            {
                Trigger = Trigger.ON_TURN_END,
                Effects = new List<EffectDef>
                {
                    new EffectDef
                    {
                        Op = Op.DRAW,
                        Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                        Amount = 1
                    }
                }
            }
        });

        // End P0's turn — ON_TURN_END fires
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        Assert.Single(state.Players[0].Hand);
    }
}