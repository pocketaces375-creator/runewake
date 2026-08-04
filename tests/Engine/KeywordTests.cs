using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

public class KeywordTests
{
    // ——— Helpers ———

    private static GameState CreateState(
        int p0HandSize = 1,
        int p1HandSize = 0)
    {
        var state = new GameState(seed: 42);
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].AttunementMax = 10;
            state.Players[p].Attunement = 10;
        }
        for (int p = 0; p < 2; p++)
        {
            var player = state.Players[p];
            for (int i = 0; i < 5; i++)
            {
                var card = new CardInstance(state.NextInstanceId++, "tst_filler", p)
                { Zone = Zone.Deck };
                player.Deck.Add(card);
            }
        }
        for (int i = 0; i < p0HandSize; i++)
        {
            var card = new CardInstance(state.NextInstanceId++, "tst_p0card", 0)
            {
                Zone = Zone.Hand, CardType = CardType.CREATURE,
                Cost = 2, BaseAttack = 3, BaseVigor = 4, IsExhausted = false
            };
            state.Players[0].Hand.Add(card);
        }
        for (int i = 0; i < p1HandSize; i++)
        {
            var card = new CardInstance(state.NextInstanceId++, "tst_p1card", 1)
            {
                Zone = Zone.Hand, CardType = CardType.CREATURE,
                Cost = 2, BaseAttack = 3, BaseVigor = 4, IsExhausted = false
            };
            state.Players[1].Hand.Add(card);
        }
        return state;
    }

    private static GameState PlayFirst(GameState state, int pIdx, int lane)
    {
        var card = state.Players[pIdx].Hand[0];
        return DuelEngine.Apply(state, new PlayCardAction
        {
            PlayerIndex = pIdx, CardInstanceId = card.InstanceId,
            Cost = card.Cost, LaneIndex = lane
        });
    }

    private static void Ready(GameState state, int pIdx, int lane)
    {
        state.Players[pIdx].Lanes[lane].Occupant!.IsExhausted = false;
    }

    // ——— 1. Guard ———

    [Fact]
    public void Keyword_Guard_RedirectsAttackFromEmptyLane()
    {
        // P0: 3/4 in lane 0. P1: Guard (1/2) in lane 3.
        var state = CreateState(1, 1);
        state.Players[1].Hand[0].BaseAttack = 1;
        state.Players[1].Hand[0].BaseVigor = 2;
        state.Players[1].Hand[0].Keywords = new List<string> { "GUARD" };
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 3);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 });

        Assert.Null(state.Players[1].Lanes[3].Occupant); // Guard dead
        Assert.Equal(25, state.Players[1].Vigor); // face untouched
    }

    // ——— 2. Swift ———

    [Fact]
    public void Keyword_Swift_NotExhaustedOnSummon()
    {
        var state = CreateState(1);
        state.Players[0].Hand[0].Keywords = new List<string> { "SWIFT" };
        state = PlayFirst(state, 0, 2);

        Assert.False(state.Players[0].Lanes[2].Occupant!.IsExhausted);
    }

    // ——— 3. Pierce ———

    [Fact]
    public void Keyword_Pierce_ExcessDamagesFace()
    {
        // P0: 5/5 PIERCE vs P1: 2/2 blocker
        var state = CreateState(1, 1);
        state.Players[0].Hand[0].BaseAttack = 5;
        state.Players[0].Hand[0].BaseVigor = 5;
        state.Players[0].Hand[0].Keywords = new List<string> { "PIERCE" };
        state.Players[0].Hand[0].Cost = 5;
        state.Players[1].Hand[0].BaseAttack = 2;
        state.Players[1].Hand[0].BaseVigor = 2;
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 });

        Assert.Null(state.Players[1].Lanes[0].Occupant);
        Assert.Equal(22, state.Players[1].Vigor); // 25 - 3 excess
    }

    // ——— 4. Ward ———

    [Fact]
    public void Keyword_Ward_PreventsOneDamageInstance()
    {
        // P0: 5/5 vs P1: 3/4 WARD in lane 0
        var state = CreateState(1, 1);
        state.Players[0].Hand[0].BaseAttack = 5;
        state.Players[0].Hand[0].BaseVigor = 5;
        state.Players[0].Hand[0].Cost = 5;
        state.Players[1].Hand[0].BaseAttack = 3;
        state.Players[1].Hand[0].BaseVigor = 4;
        state.Players[1].Hand[0].Keywords = new List<string> { "WARD" };
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 });

        // Attacker dealt 5 damage, Ward blocks it → 0 damage to defender
        Assert.Equal(0, state.Players[1].Lanes[0].Occupant!.Damage);
        Assert.Equal(0, state.Players[1].Lanes[0].Occupant.WardRemaining); // ward consumed
        // Defender still hit back for 3
        Assert.Equal(3, state.Players[0].Lanes[0].Occupant!.Damage);
    }

    [Fact]
    public void Keyword_Ward_ConsumedAfterOneBlock()
    {
        // P0: 3/4 attacks P1: 3/4 WARD in lane 0.
        // First attack: Ward blocks all damage. WardRemaining drops to 0.
        // The creature itself has no WARD keyword, just WardRemaining set manually
        // to simulate a surviving creature that already burned its ward.
        var state = CreateState(1, 1);
        // P0 creature plays to lane 0
        state = PlayFirst(state, 0, 0);
        // P1 creature plays to lane 0 with WARD
        state.Players[1].Hand[0].Keywords = new List<string> { "WARD" };
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        // Attack — Ward should block the 3 damage
        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 });

        var defender = state.Players[1].Lanes[0].Occupant!;
        Assert.Equal(0, defender.Damage);  // Ward blocked
        Assert.Equal(0, defender.WardRemaining);  // Ward consumed
        // Defender still hit back for 3
        Assert.Equal(3, state.Players[0].Lanes[0].Occupant!.Damage);
    }

    // ——— 5. Venom ———

    [Fact]
    public void Keyword_Venom_DestroysDamagedCreatureAfterCombat()
    {
        // P0: 2/5 VENOM vs P1: 3/4 blocker. Venom should destroy P1's creature after combat.
        var state = CreateState(1, 1);
        state.Players[0].Hand[0].BaseAttack = 2;
        state.Players[0].Hand[0].BaseVigor = 5;
        state.Players[0].Hand[0].Keywords = new List<string> { "VENOM" };
        state.Players[0].Hand[0].Cost = 4;
        state.Players[1].Hand[0].BaseAttack = 3;
        state.Players[1].Hand[0].BaseVigor = 4;
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 });

        // Venom destroys the defender after combat — only attacker remains
        Assert.Null(state.Players[1].Lanes[0].Occupant); // defender dead from Venom
        Assert.NotNull(state.Players[0].Lanes[0].Occupant); // attacker alive (5 - 3 = 2)
    }

    // ——— 6. Reach ———

    [Fact]
    public void Keyword_Reach_CanAttackAdjacentLane()
    {
        // P0: REACH creature in lane 2 attacks empty lane 3 (adjacent)
        var state = CreateState(1);
        state.Players[0].Hand[0].Keywords = new List<string> { "REACH" };
        state = PlayFirst(state, 0, 2);
        Ready(state, 0, 2);

        state = DuelEngine.Apply(state, new AttackAction
        {
            PlayerIndex = 0, SourceLane = 2, TargetLane = 3
        });

        // Face damage through lane 3
        Assert.Equal(22, state.Players[1].Vigor); // 25 - 3
    }

    [Fact]
    public void Keyword_Reach_CannotAttackNonAdjacentLane()
    {
        var state = CreateState(1);
        state.Players[0].Hand[0].Keywords = new List<string> { "REACH" };
        state = PlayFirst(state, 0, 0); // lane 0
        Ready(state, 0, 0);

        // Attempt to attack lane 3 (diff = 3 > 1) — should be invalid
        Assert.Throws<InvalidOperationException>(() =>
            DuelEngine.Apply(state, new AttackAction
            {
                PlayerIndex = 0, SourceLane = 0, TargetLane = 3
            }));
    }

    // ——— 7. Rooted ———

    [Fact]
    public void Keyword_Rooted_CannotAttack()
    {
        var state = CreateState(1);
        state.Players[0].Hand[0].Keywords = new List<string> { "ROOTED" };
        state = PlayFirst(state, 0, 0);
        Ready(state, 0, 0);

        Assert.Throws<InvalidOperationException>(() =>
            DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 }));
    }

    // ——— 8. Unearth ———

    [Fact]
    public void Keyword_Unearth_ReturnsToHandNextTurn()
    {
        // P0: 5/3 with UNEARTH:3 vs P1: 5/3 blocker — both die in trade
        var state = CreateState(1, 1);
        state.Players[0].Hand[0].BaseAttack = 5;
        state.Players[0].Hand[0].BaseVigor = 3;
        state.Players[0].Hand[0].Cost = 5;
        state.Players[0].Hand[0].UnearthCost = 3;
        state.Players[1].Hand[0].BaseAttack = 5;
        state.Players[1].Hand[0].BaseVigor = 3;
        int p0CardId = state.Players[0].Hand[0].InstanceId;
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        // Attack — both die (5 damage each, vigor=3)
        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 });

        // P0 creature died — check it's in UnearthQueue, not discard
        Assert.Null(state.Players[0].Lanes[0].Occupant);
        Assert.DoesNotContain(state.Players[0].Discard, c => c.InstanceId == p0CardId);
        Assert.Single(state.Players[0].UnearthQueue);
        Assert.Equal(Zone.RemovedFromGame, state.Players[0].UnearthQueue[0].Zone);

        // End P0's turn → P1's turn starts
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // P0's creature still in queue (P1's turn start processed)
        Assert.Single(state.Players[0].UnearthQueue);

        // End P1's turn → P0's turn starts → ProcessUnearth returns the card
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 });

        // P0 should have the card back in hand (paid 3 attunement)
        Assert.Empty(state.Players[0].UnearthQueue);
        Assert.Contains(state.Players[0].Hand, c => c.InstanceId == p0CardId);
    }

    [Fact]
    public void Keyword_Unearth_DiscardsIfCannotAfford()
    {
        // P0: creature with Unearth:15 and 5/3 vs a 5/3 — both die
        var state = CreateState(1, 1);
        state.Players[0].Hand[0].BaseAttack = 5;
        state.Players[0].Hand[0].BaseVigor = 3;
        state.Players[0].Hand[0].Cost = 5;
        state.Players[0].Hand[0].UnearthCost = 15;
        state.Players[1].Hand[0].BaseAttack = 5;
        state.Players[1].Hand[0].BaseVigor = 3;
        int p0CardId = state.Players[0].Hand[0].InstanceId;
        state = PlayFirst(state, 0, 0);
        state = PlayFirst(state, 1, 0);
        Ready(state, 0, 0);

        // Attack — both die
        state = DuelEngine.Apply(state, new AttackAction { PlayerIndex = 0, SourceLane = 0 });

        // Now P0's creature is in UnearthQueue
        Assert.Single(state.Players[0].UnearthQueue);

        // Play through to P0's next turn
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 }); // P1 turn
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 1 }); // P0 turn

        // Can't afford cost 15 → go to discard
        Assert.Empty(state.Players[0].UnearthQueue);
        Assert.Contains(state.Players[0].Discard, c => c.InstanceId == p0CardId);
    }

    // ——— 9. Echo ———

    [Fact]
    public void Keyword_Echo_SetsFlagForDoubleTrigger()
    {
        // Echo's actual double-triggering depends on P1-06 (Trigger bus).
        // For now, verify the keyword flag is properly recognized.
        var state = CreateState(1);
        state.Players[0].Hand[0].Keywords = new List<string> { "ECHO" };
        state = PlayFirst(state, 0, 1);

        Assert.True(state.Players[0].Lanes[1].Occupant!.EffectiveKeywords.Contains("ECHO"));
    }

    // ——— 10. Fragile ———

    [Fact]
    public void Keyword_Fragile_DestroyedAtEndOfTurn()
    {
        var state = CreateState(1);
        state.Players[0].Hand[0].BaseAttack = 2;
        state.Players[0].Hand[0].BaseVigor = 5;
        state.Players[0].Hand[0].Keywords = new List<string> { "FRAGILE" };
        state.Players[0].Hand[0].Cost = 4;
        int cardId = state.Players[0].Hand[0].InstanceId;
        state = PlayFirst(state, 0, 2);

        // End P0's turn — Fragile should destroy the creature
        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        Assert.Null(state.Players[0].Lanes[2].Occupant);
        Assert.Contains(state.Players[0].Discard, c => c.InstanceId == cardId);
    }

    [Fact]
    public void Keyword_Fragile_NonFragileSurvivesEndOfTurn()
    {
        var state = CreateState(1);
        state = PlayFirst(state, 0, 2);
        int instanceId = state.Players[0].Lanes[2].Occupant!.InstanceId;

        state = DuelEngine.Apply(state, new EndTurnAction { PlayerIndex = 0 });

        // Non-Fragile creature should still be in the lane
        Assert.NotNull(state.Players[0].Lanes[2].Occupant);
        Assert.Equal(instanceId, state.Players[0].Lanes[2].Occupant!.InstanceId);
    }

    // ——— 11. Sealed ———

    [Fact]
    public void Keyword_Sealed_RecognizedAsUntargetable()
    {
        var state = CreateState(1);
        state.Players[0].Hand[0].Keywords = new List<string> { "SEALED" };
        state = PlayFirst(state, 0, 4);

        Assert.True(KeywordHandlers.IsSealed(state.Players[0].Lanes[4].Occupant!));
    }
}