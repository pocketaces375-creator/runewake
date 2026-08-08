using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

public class CombatTests
{
    /// <summary>
    /// Creates a fresh game state with full attunement and optional hand cards.
    /// All hand cards are 3/4 CREATUREs by default.
    /// </summary>
    private static GameState CreateCombatState(
        int p0HandSize = 2,
        int p1HandSize = 0,
        string cardId = "tst_combatant")
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
        }

        // Small decks so we don't care about draws
        for (int p = 0; p < 2; p++)
        {
            var player = state.Players[p];
            for (int i = 0; i < 5; i++)
            {
                var card = new CardInstance(
                    state.NextInstanceId++, "tst_deck_filler", p)
                { Zone = Zone.Deck };
                player.Deck.Add(card);
            }
        }

        // Deal p0's hand
        for (int i = 0; i < p0HandSize; i++)
        {
            var card = new CardInstance(
                state.NextInstanceId++, cardId, 0)
            {
                Zone = Zone.Hand,
                CardType = CardType.CREATURE,
                Cost = 2,
                BaseAttack = 3,
                BaseVigor = 4,
                IsExhausted = false
            };
            state.Players[0].Hand.Add(card);
        }

        // Deal p1's hand
        for (int i = 0; i < p1HandSize; i++)
        {
            var card = new CardInstance(
                state.NextInstanceId++, "tst_defender", 1)
            {
                Zone = Zone.Hand,
                CardType = CardType.CREATURE,
                Cost = 2,
                BaseAttack = 3,
                BaseVigor = 4,
                IsExhausted = false
            };
            state.Players[1].Hand.Add(card);
        }

        return state;
    }

    /// <summary>
    /// Plays the first card in a player's hand to the given lane.
    /// After playing, the hand shrinks so the next card to play is
    /// always at index 0.
    /// </summary>
    private static GameState PlayFirst(GameState state, int playerIndex, int laneIndex)
    {
        var card = state.Players[playerIndex].Hand[0];
        return DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = playerIndex,
            CardInstanceId = card.InstanceId,
            Cost = card.Cost,
            LaneIndex = laneIndex
        });
    }

    /// <summary>
    /// Makes a creature in a player's lane Ready to attack
    /// (clears IsExhausted).
    /// </summary>
    private static void Ready(GameState state, int playerIndex, int laneIndex)
    {
        state.Players[playerIndex].Lanes[laneIndex].Occupant!.IsExhausted = false;
    }

    // ——— PlayCard ———

    [Fact]
    public void PlayCreature_PlacesInLaneAndExhausts()
    {
        var state = PlayFirst(CreateCombatState(p0HandSize: 1), 0, 2);

        var occupant = state.Players[0].Lanes[2].Occupant;
        Assert.NotNull(occupant);
        Assert.Equal("tst_combatant", occupant.CardDefId);
        Assert.Equal(Zone.Lane, occupant.Zone);
        Assert.Equal(2, occupant.LaneIndex);
        Assert.True(occupant.IsExhausted); // no Swift
        Assert.Empty(state.Players[0].Hand);
    }

    [Fact]
    public void PlayCreature_DeductsAttunementCost()
    {
        var state = PlayFirst(CreateCombatState(p0HandSize: 1), 0, 0);
        Assert.Equal(8, state.Players[0].Attunement);
    }

    [Fact]
    public void PlayCreature_FailsInOccupiedLane()
    {
        var state = CreateCombatState(p0HandSize: 2);
        state = PlayFirst(state, 0, 2);
        Assert.Throws<InvalidOperationException>(() => PlayFirst(state, 0, 2));
    }

    // ——— Combat basics ———

    [Fact]
    public void TradeKill_BothCreaturesDie()
    {
        // P0: 3/4 vs P1: 3/4 — trade should kill both
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 1);
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        // 3 attack each. 3 damage on a 4-vigor creature leaves CurrentVigor = 1.
        Assert.Equal(3, state.Players[0].Lanes[0].Occupant!.Damage);
        Assert.Equal(3, state.Players[1].Lanes[0].Occupant!.Damage);
        Assert.Equal(1, state.Players[0].Lanes[0].Occupant.CurrentVigor);
        Assert.Equal(1, state.Players[1].Lanes[0].Occupant.CurrentVigor);
        Assert.True(state.Players[0].Lanes[0].Occupant.HasAttackedThisTurn);
    }

    [Fact]
    public void OneSidedKill_DefenderDiesAttackerSurvives()
    {
        // P0: 5/5 vs P1: 2/2 in same lane
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 1);
        state.Players[0].Hand[0].BaseAttack = 5;
        state.Players[0].Hand[0].BaseVigor = 5;
        state.Players[0].Hand[0].Cost = 5;
        state.Players[1].Hand[0].BaseAttack = 2;
        state.Players[1].Hand[0].BaseVigor = 2;
        state = PlayFirst(state, 0, 2);
        state = PlayFirst(state, 1, 2);
        Ready(state, 0, 2);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 2
        });

        // Defender (2/2) takes 5 → CurrentVigor = 2 - 5 = -3 → dead
        Assert.Null(state.Players[1].Lanes[2].Occupant);
        // Attacker (5/5) takes 2 → CurrentVigor = 5 - 2 = 3 → alive
        Assert.NotNull(state.Players[0].Lanes[2].Occupant);
        Assert.Equal(2, state.Players[0].Lanes[2].Occupant!.Damage);
        Assert.Equal(3, state.Players[0].Lanes[2].Occupant.CurrentVigor);
        // Attacker marked
        Assert.True(state.Players[0].Lanes[2].Occupant.HasAttackedThisTurn);
    }

    [Fact]
    public void BothDieInTrade_DefenderInDiscard()
    {
        // Both 5/3 creatures — both die after trading
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 1);
        state.Players[0].Hand[0].BaseAttack = 5;
        state.Players[0].Hand[0].BaseVigor = 3;
        state.Players[0].Hand[0].Cost = 5;
        state.Players[1].Hand[0].BaseAttack = 5;
        state.Players[1].Hand[0].BaseVigor = 3;
        state = PlayFirst(state, 0, 3);
        state = PlayFirst(state, 1, 3);
        Ready(state, 0, 3);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 3
        });

        // Both take 5 → CurrentVigor = 3 - 5 = -2 → both dead
        Assert.Null(state.Players[0].Lanes[3].Occupant);
        Assert.Null(state.Players[1].Lanes[3].Occupant);
        Assert.Contains(state.Players[1].Discard, c => c.CardDefId == "tst_defender");
        Assert.Contains(state.Players[0].Discard, c => c.CardDefId == "tst_combatant");
    }

    // ——— Face damage ———

    [Fact]
    public void AttackEmptyLane_DamagesFace()
    {
        // P0: 3/4 in lane 1, P1: empty board
        var state = CreateCombatState(p0HandSize: 1);
        state = PlayFirst(state, 0, 1);
        Ready(state, 0, 1);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 1
        });

        Assert.Equal(22, state.Players[1].Vigor); // 25 - 3
        Assert.NotNull(state.Players[0].Lanes[1].Occupant);
        Assert.True(state.Players[0].Lanes[1].Occupant!.HasAttackedThisTurn);
    }

    [Fact]
    public void FaceDamageCanWinGame()
    {
        var state = CreateCombatState(p0HandSize: 1);
        state.Players[0].Hand[0].BaseAttack = 30;
        state.Players[0].Hand[0].Cost = 10;
        state = PlayFirst(state, 0, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        Assert.True(state.IsGameOver);
        Assert.Equal(0, state.WinnerIndex);
    }

    // ——— Guard ———

    [Fact]
    public void GuardRedirectsFaceAttack()
    {
        // P0: 3/4 in lane 0. P1: Guard (1/2) in lane 3, empty lane 0.
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 1);
        state.Players[1].Hand[0].BaseAttack = 1;
        state.Players[1].Hand[0].BaseVigor = 2;
        state.Players[1].Hand[0].Keywords = new List<string> { "GUARD" };
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 3); // Guard in lane 3
        Ready(state, 0, 0);

        // P0 attacks lane 0 (empty opposing) — redirects to Guard (lane 3)
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        // Guard took 3 damage → CurrentVigor = 2 - 3 = -1 → dead
        Assert.Null(state.Players[1].Lanes[3].Occupant);
        // P0's attacker took 1 damage
        Assert.Equal(1, state.Players[0].Lanes[0].Occupant!.Damage);
        Assert.Equal(3, state.Players[0].Lanes[0].Occupant.CurrentVigor);
        // P1 face untouched
        Assert.Equal(25, state.Players[1].Vigor);
    }

    [Fact]
    public void GuardDoesNotRedirectWhenOpposingLaneOccupied()
    {
        // P0: 3/4 in lane 0. P1: Guard (1/2 in lane 2) AND blocker (2/4 in lane 0).
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 2);
        state.Players[1].Hand[0].BaseAttack = 1;
        state.Players[1].Hand[0].BaseVigor = 2;
        state.Players[1].Hand[0].Keywords = new List<string> { "GUARD" };
        state.Players[1].Hand[1].BaseAttack = 2;
        state.Players[1].Hand[1].BaseVigor = 4;
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 2); // Guard in lane 2
        state = PlayFirst(state, 1, 0); // blocker in lane 0 (hand[1] is now hand[0] after first play)
        Ready(state, 0, 0);

        // Attack lane 0 (occupied) — fights blocker, NOT redirected
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        // Blocker (2/4) and attacker (3/4) trade — both survive
        Assert.NotNull(state.Players[0].Lanes[0].Occupant); // 4 - 2 = 2 → alive
        Assert.NotNull(state.Players[1].Lanes[0].Occupant); // 4 - 3 = 1 → alive
        Assert.NotNull(state.Players[1].Lanes[2].Occupant); // Guard untouched
        Assert.Equal(25, state.Players[1].Vigor);
    }

    [Fact]
    public void GuardRedirectToFirstLane()
    {
        // P0 attacks empty opposing lane. P1 has Guard in lane 1 and lane 3.
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 2);
        state.Players[1].Hand[0].BaseAttack = 1;
        state.Players[1].Hand[0].BaseVigor = 3;
        state.Players[1].Hand[0].Keywords = new List<string> { "GUARD" };
        state.Players[1].Hand[1].BaseAttack = 1;
        state.Players[1].Hand[1].BaseVigor = 3;
        state.Players[1].Hand[1].Keywords = new List<string> { "GUARD" };
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 1); // Guard in lane 1
        state = PlayFirst(state, 1, 3); // Guard in lane 3 (second card becomes hand[0])
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        // First Guard (lane 1) took the hit
        Assert.Null(state.Players[1].Lanes[1].Occupant); // dead
        Assert.NotNull(state.Players[1].Lanes[3].Occupant); // alive
    }

    // ——— Pierce ———

    [Fact]
    public void PierceExcessCarriesToFace()
    {
        // P0: 5/5 PIERCE vs P1: 2/2 in lane 0
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 1);
        state.Players[0].Hand[0].BaseAttack = 5;
        state.Players[0].Hand[0].BaseVigor = 5;
        state.Players[0].Hand[0].Keywords = new List<string> { "PIERCE" };
        state.Players[0].Hand[0].Cost = 5;
        state.Players[1].Hand[0].BaseAttack = 2;
        state.Players[1].Hand[0].BaseVigor = 2;
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        // 5 damage to 2/2 → dead. Excess 5-2=3 to face.
        Assert.Null(state.Players[1].Lanes[0].Occupant);
        Assert.Equal(22, state.Players[1].Vigor); // 25 - 3
        // Attacker took 2 back
        Assert.Equal(3, state.Players[0].Lanes[0].Occupant!.CurrentVigor); // 5 - 2
    }

    [Fact]
    public void PierceExactKill_NoCarry()
    {
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 1);
        state.Players[0].Hand[0].BaseAttack = 2;
        state.Players[0].Hand[0].BaseVigor = 5;
        state.Players[0].Hand[0].Keywords = new List<string> { "PIERCE" };
        state.Players[0].Hand[0].Cost = 4;
        state.Players[1].Hand[0].BaseAttack = 2;
        state.Players[1].Hand[0].BaseVigor = 2;
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        // Exact kill: 2 damage = 2 vigor. No excess.
        Assert.Equal(25, state.Players[1].Vigor);
    }

    // ——— Edge cases ———

    [Fact]
    public void AttackerCannotAttackIfExhausted()
    {
        var state = CreateCombatState(p0HandSize: 1);
        state = PlayFirst(state, 0, 0);

        Assert.Throws<InvalidOperationException>(() =>
            DuelEngine.Apply(state, new AttackAction
            {
                PlayerIndex = 0,
                SourceLane = 0
            }));
    }

    [Fact]
    public void AttackerCannotAttackTwice()
    {
        var state = CreateCombatState(p0HandSize: 1);
        state = PlayFirst(state, 0, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });

        Assert.Throws<InvalidOperationException>(() =>
            DuelEngine.Apply(state, new AttackAction
            {
                PlayerIndex = 0,
                SourceLane = 0
            }));
    }

    [Fact]
    public void CannotAttackFromEmptyLane()
    {
        var state = CreateCombatState();
        Assert.Throws<InvalidOperationException>(() =>
            DuelEngine.Apply(state, new AttackAction
            {
                PlayerIndex = 0,
                SourceLane = 0
            }));
    }

    [Fact]
    public void SwiftCreature_NotExhaustedOnPlay()
    {
        var state = CreateCombatState(p0HandSize: 1);
        state.Players[0].Hand[0].Keywords = new List<string> { "SWIFT" };
        state = PlayFirst(state, 0, 4);

        Assert.False(state.Players[0].Lanes[4].Occupant!.IsExhausted);
    }

    [Fact]
    public void Creature_RefreshesAfterTurnPasses()
    {
        // Per rules §5-6: a creature summoned on a previous turn should be
        // Ready at the start of your next turn (not exhausted, can attack).
        // This test verifies the turn-start refresh in ApplyEndTurn.
        var state = CreateCombatState(p0HandSize: 1, p1HandSize: 1);

        // P0 plays a creature to lane 0
        state = PlayFirst(state, 0, 0);
        var creature = state.Players[0].Lanes[0].Occupant!;
        Assert.True(creature.IsExhausted);  // summoned this turn = exhausted
        Assert.False(creature.HasAttackedThisTurn);

        // P0 ends turn → P1's turn begins (refresh happens for P1, not P0 yet)
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });
        Assert.Equal(1, state.CurrentPlayerIndex); // P1's turn
        // Re-grab creature reference after clone
        creature = state.Players[0].Lanes[0].Occupant!;
        // P1's turn — P0's creature still exhausted (only current player gets refreshed)
        Assert.True(creature.IsExhausted);

        // P1 plays a creature to a different lane so P0's lane 0 faces empty
        state = PlayFirst(state, 1, 2);
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        // Re-grab creature reference after clone
        creature = state.Players[0].Lanes[0].Occupant!;

        // Now P0's turn again — creature should be refreshed
        Assert.Equal(0, state.CurrentPlayerIndex);
        Assert.Equal(2, state.TurnNumber);
        Assert.False(creature.IsExhausted, "Creature should be Ready at start of owner's next turn");
        Assert.False(creature.HasAttackedThisTurn, "Attack flag should reset at start of owner's next turn");

        // Creature can now attack
        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0,
            SourceLane = 0
        });
        // Re-grab creature reference after clone
        creature = state.Players[0].Lanes[0].Occupant!;
        // P1 face took 3 damage (empty opposing lane)
        Assert.Equal(22, state.Players[1].Vigor); // 25 - 3
        Assert.True(creature.HasAttackedThisTurn);
    }
}