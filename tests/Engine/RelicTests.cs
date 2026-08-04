using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

public class RelicTests
{
    /// <summary>
    /// Create a test state with full attunement and deck cards.
    /// </summary>
    private static GameState CreateState(int extraP0HandCards = 0)
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
            for (int i = 0; i < 5; i++)
            {
                var c = new CardInstance(state.NextInstanceId++, "tst_d", p)
                { Zone = Zone.Deck };
                state.Players[p].Deck.Add(c);
            }
        }
        // Add extra hand cards for P0 if requested
        for (int i = 0; i < extraP0HandCards; i++)
        {
            var hc = new CardInstance(state.NextInstanceId++, "tst_hand", 0)
            { Zone = Zone.Hand };
            state.Players[0].Hand.Add(hc);
        }
        return state;
    }

    /// <summary>
    /// Helper: places a card in a player's hand ready to play.
    /// </summary>
    private static CardInstance MakeRelic(GameState state, ConditionDef? identifyCondition = null,
        List<AbilityDef>? abilities = null)
    {
        var card = new CardInstance(state.NextInstanceId++, "tst_relic", 0)
        {
            Zone = Zone.Hand,
            CardType = CardType.RELIC,
            Cost = 3,
            BaseAttack = 5,
            BaseVigor = 7,
            IsExhausted = false
        };
        if (identifyCondition is not null)
            card.IdentifyCondition = identifyCondition;
        if (abilities is not null)
            card.Abilities = abilities;
        state.Players[0].Hand.Add(card);
        return card;
    }

    /// <summary>
    /// Play a card from hand to a lane.
    /// </summary>
    private static GameState PlayCard(GameState state, CardInstance card, int lane)
    {
        return DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = 0,
            CardInstanceId = card.InstanceId,
            Cost = card.Cost,
            LaneIndex = lane
        });
    }

    // ——— Relic enters as 0/3 unidentified ———

    [Fact]
    public void Relic_EntersAsUnidentified()
    {
        var state = CreateState();
        var relic = MakeRelic(state);
        state = PlayCard(state, relic, 2);

        var placed = state.Players[0].Lanes[2].Occupant!;
        Assert.Equal(CardType.RELIC, placed.CardType);
        Assert.False(placed.IsIdentified);
        Assert.Equal(0, placed.BaseAttack);
        Assert.Equal(3, placed.BaseVigor);
        Assert.True(placed.IsExhausted);
    }

    [Fact]
    public void Relic_PreservesOriginalStatsAfterIdentify()
    {
        // After identify, BaseAttack and BaseVigor remain at 0 and 3
        // (the card's real stats come from its abilities/effects)
        var state = CreateState();
        var relic = MakeRelic(state);
        relic.IsIdentified = true; // manually identify
        state = PlayCard(state, relic, 2);

        // Even if "identified" before play, we override to unidentified + 0/3 on play
        var placed = state.Players[0].Lanes[2].Occupant!;
        Assert.False(placed.IsIdentified);
        Assert.Equal(0, placed.BaseAttack);
    }

    // ——— Identify condition check at turn start ———

    [Fact]
    public void Relic_IdentifiesWhenConditionMetAtTurnStart()
    {
        // Relic with BARROW_COUNT_GTE: 3. P0 has 3 cards in barrow → condition met.
        var state = CreateState();
        // Add 3 cards to barrow
        for (int i = 0; i < 3; i++)
        {
            var bc = new CardInstance(state.NextInstanceId++, "tst_buried", 0)
            { Zone = Zone.Barrow };
            state.Players[0].Barrow.Add(bc);
        }

        var relic = MakeRelic(state, new ConditionDef
        {
            Op = ConditionOp.BARROW_COUNT_GTE,
            Value = System.Text.Json.JsonDocument.Parse("3").RootElement
        });
        state = PlayCard(state, relic, 0);
        Assert.False(state.Players[0].Lanes[0].Occupant!.IsIdentified);

        // End P0's turn → P1's turn starts (relic check is for next player, not P0)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // Relic still unidentified (it's P1's turn, relic is controlled by P0 — but
        // IdentifyRelics checks the next player's relics. Wait — the next player is P1,
        // but the relic belongs to P0. So the condition check is for the next player.
        // Hmm, that's wrong. The spec says "At the start of YOUR turn."
        // But in ApplyEndTurn, the next player is the one whose turn is starting.
        // The relic belongs to P0. P0's turn isn't starting — P1's turn is starting.
        // So the relic check should check BOTH players' relics? Or the player whose
        // turn just ended?

        // Actually: "At the start of your turn" means at the START of the controller's turn.
        // The controller is P0. P0's turn hasn't started yet (we just ended P0's turn
        // and started P1's). So the relic should check at P0's NEXT turn start.

        // Let me end P1's turn too → P0's turn starts
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        // Now P0's turn started. P0's relic with BARROW_COUNT_GTE=3 should identify.
        Assert.True(state.Players[0].Lanes[0].Occupant!.IsIdentified);
    }

    [Fact]
    public void Relic_StaysUnidentifiedWhenConditionNotMet()
    {
        var state = CreateState();
        // Barrow has 0 cards, condition requires 5
        var relic = MakeRelic(state, new ConditionDef
        {
            Op = ConditionOp.BARROW_COUNT_GTE,
            Value = System.Text.Json.JsonDocument.Parse("5").RootElement
        });
        state = PlayCard(state, relic, 0);

        // Two full turn cycles
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        Assert.False(state.Players[0].Lanes[0].Occupant!.IsIdentified);
    }

    // ——— ON_RELIC_IDENTIFY fires ———

    [Fact]
    public void Relic_ON_RELIC_IDENTIFY_FiresAbilities()
    {
        var state = CreateState();
        // Add 3 barrow cards
        for (int i = 0; i < 3; i++)
        {
            var bc = new CardInstance(state.NextInstanceId++, "tst_buried", 0)
            { Zone = Zone.Barrow };
            state.Players[0].Barrow.Add(bc);
        }

        // Relic with BARROW_COUNT_GTE condition and ON_RELIC_IDENTIFY ability that draws
        var relic = MakeRelic(state,
            identifyCondition: new ConditionDef
            {
                Op = ConditionOp.BARROW_COUNT_GTE,
                Value = System.Text.Json.JsonDocument.Parse("3").RootElement
            },
            abilities: new List<AbilityDef>
            {
                new AbilityDef
                {
                    Trigger = Trigger.ON_RELIC_IDENTIFY,
                    Effects = new List<EffectDef>
                    {
                        new EffectDef
                        {
                            Op = Op.DRAW,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                            Amount = 2
                        }
                    }
                }
            });
        state = PlayCard(state, relic, 0);

        // Two full turns to get back to P0's turn
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        // After the natural draw (1 card) + ON_RELIC_IDENTIFY draw (2 cards) = 3 total
        Assert.True(state.Players[0].Lanes[0].Occupant!.IsIdentified);
        Assert.Equal(3, state.Players[0].Hand.Count);
    }

    // ——— Excavate in engine flow ———

    [Fact]
    public void Excavate_MovesCardsToHandAndBury()
    {
        var state = CreateState();
        // Place a creature with ON_SUMMON: EXCAVATE 3
        var card = new CardInstance(state.NextInstanceId++, "tst_excavator", 0)
        {
            Zone = Zone.Hand, CardType = CardType.CREATURE,
            BaseAttack = 2, BaseVigor = 2, Cost = 1, IsExhausted = false,
            Abilities = new List<AbilityDef>
            {
                new AbilityDef
                {
                    Trigger = Trigger.ON_SUMMON,
                    Effects = new List<EffectDef>
                    {
                        new EffectDef
                        {
                            Op = Op.EXCAVATE,
                            Target = new TargetDef { Scope = Scope.PLAYER_SELF },
                            Amount = 3
                        }
                    }
                }
            }
        };
        state.Players[0].Hand.Add(card);

        state = DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = 0, CardInstanceId = card.InstanceId,
            Cost = 1, LaneIndex = 4
        });

        // Excavate 3: 1 to hand, 2 buried
        // Deck started with 5, hand started with 0
        // After excavate: hand should have 1 (from excavate) + 0 (the card was played)
        // Actually the card was in hand, then played (removed from hand), then excavate adds 1
        Assert.Single(state.Players[0].Hand);
        Assert.Equal(2, state.Players[0].Deck.Count); // 5 - 3 excavated
        Assert.Equal(2, state.Players[0].Barrow.Count); // 0 + 2 buried
    }
}