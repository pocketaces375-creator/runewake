using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Cards;
using Xunit;
using Xunit.Abstractions;

namespace Runewake.Tests.Engine;

[Collection("NonParallel")]
public class ReplayDeterminismTests
{
    private readonly ITestOutputHelper _output;

    public ReplayDeterminismTests(ITestOutputHelper output)
    {
        _output = output;

        // Register a minimal set of card definitions for the fuzz game
        ReplayDeterminismTests.RegisterTestCards();
    }

    /// <summary>
    /// Register the test card definitions into CardRegistry.
    /// Static so individual test methods can re-register after other tests clobber the registry.
    /// </summary>
    private static void RegisterTestCards()
    {
        CardRegistry.Clear();
        CardRegistry.RegisterRange(new[]
        {
            new CardDef
            {
                Id = "tst_soldier", Name = "Test Soldier", Set = "test",
                Type = CardType.CREATURE, Cost = 2, Attack = 2, Vigor = 2,
                Strata = Strata.VERDANT, Rarity = Rarity.COMMON,
                Keywords = new List<string>(),
                Abilities = new List<AbilityDef>(),
            },
            new CardDef
            {
                Id = "tst_scout", Name = "Test Scout", Set = "test",
                Type = CardType.CREATURE, Cost = 1, Attack = 1, Vigor = 1,
                Strata = Strata.VERDANT, Rarity = Rarity.COMMON,
                Keywords = new List<string>(),
                Abilities = new List<AbilityDef>(),
            },
            new CardDef
            {
                Id = "tst_tank", Name = "Test Tank", Set = "test",
                Type = CardType.CREATURE, Cost = 4, Attack = 3, Vigor = 5,
                Strata = Strata.VERDANT, Rarity = Rarity.UNCOMMON,
                Keywords = new List<string>(),
                Abilities = new List<AbilityDef>(),
            },
        });
    }

    /// <summary>
    /// Fuzz test: plays 200 random legal games, records the action log,
    /// replays each from scratch, and asserts the final state hashes are equal.
    /// </summary>
    [Fact]
    public void Fuzz200_ReplayedGames_ProduceIdenticalFinalState()
    {
        // Ensure no leftover card data from other test classes
        CardRegistry.Clear();
        RegisterTestCards();

        var seeds = new ulong[]
        {
            42, 12345, 99999, 7777777, 314159265,
            8675309, 0, 1, ulong.MaxValue, 1337,
            2026, 9001, 5551212, 11223344, 99887766,
            42424242, 1000000000, 123456789, 987654321, 111111111,
        };

        int totalGames = 0;
        int replayVerified = 0;
        int gameCount = 0;

        foreach (var baseSeed in seeds)
        {
            for (int variant = 0; variant < 10; variant++)
            {
                ulong seed = (ulong)((long)baseSeed + variant * 100003);
                gameCount++;

                // Build deck lists: 20 copies of a mix of cards, same for both players
                var deckIds = new List<string>();
                for (int i = 0; i < 10; i++)
                    deckIds.Add("tst_soldier");
                for (int i = 0; i < 6; i++)
                    deckIds.Add("tst_scout");
                for (int i = 0; i < 4; i++)
                    deckIds.Add("tst_tank");

                var config = new GameConfig
                {
                    Seed = seed,
                    ContentVersion = 1,
                    Player0DeckIds = new List<string>(deckIds),
                    Player1DeckIds = new List<string>(deckIds),
                };

                // Play the game with a random bot
                var (finalState, actions) = PlayRandomGame(config);

                // Serialize to JSON and back
                var replayLog = new ReplayLog
                {
                    Config = config,
                    Actions = actions,
                };
                string json = replayLog.ToJson();
                var deserialized = ReplayLog.FromJson(json);

                // Replay
                var replayedState = ReplayRunner.Replay(deserialized);

                // Compare state hashes
                ulong originalHash = finalState.ComputeStateHash();
                ulong replayedHash = replayedState.ComputeStateHash();

                if (originalHash != replayedHash)
                {
                    _output.WriteLine(
                        $"MISMATCH seed={seed} game={gameCount}: " +
                        $"original={originalHash:x16} replayed={replayedHash:x16}");
                }

                Assert.Equal(originalHash, replayedHash);
                totalGames++;
                replayVerified++;
            }
        }

        _output.WriteLine($"Fuzz complete: {totalGames} games played, {replayVerified} replayed and verified.");
    }

    /// <summary>
    /// Plays a random game using a simple bot, returns the final state and action log.
    /// </summary>
    private static (GameState finalState, List<GameAction> actions) PlayRandomGame(GameConfig config)
    {
        var state = GameState.Initialize(config);
        var actions = new List<GameAction>();
        int maxTurns = 100; // safety limit to prevent infinite loops

        for (int turn = 0; turn < maxTurns && !state.IsGameOver; turn++)
        {
            var player = state.CurrentPlayer;

            // Collect valid actions
            var validActions = new List<GameAction>();

            // 1. Play card actions — cards in hand with cost <= available attunement
            foreach (var card in player.Hand)
            {
                if (card.Cost <= player.Attunement)
                {
                    if (card.CardType == CardType.CREATURE || card.CardType == CardType.RELIC)
                    {
                        // Find empty lanes
                        for (int l = 0; l < 5; l++)
                        {
                            if (player.Lanes[l].Occupant is null)
                            {
                                validActions.Add(new PlayCardAction
                                {
                                    PlayerIndex = state.CurrentPlayerIndex,
                                    CardInstanceId = card.InstanceId,
                                    Cost = card.Cost,
                                    LaneIndex = l,
                                });
                            }
                        }
                    }
                    else
                    {
                        // RITUAL — no lane target needed
                        validActions.Add(new PlayCardAction
                        {
                            PlayerIndex = state.CurrentPlayerIndex,
                            CardInstanceId = card.InstanceId,
                            Cost = card.Cost,
                            LaneIndex = null,
                        });
                    }
                }
            }

            // 2. Attack actions — ready creatures
            for (int l = 0; l < 5; l++)
            {
                var occ = player.Lanes[l].Occupant;
                if (occ is not null && !occ.IsExhausted && !occ.HasAttackedThisTurn && occ.CurrentAttack > 0)
                {
                    var opponent = state.Player(state.OpponentIndex(state.CurrentPlayerIndex));
                    for (int tl = 0; tl < 5; tl++)
                    {
                        validActions.Add(new AttackAction
                        {
                            PlayerIndex = state.CurrentPlayerIndex,
                            SourceLane = l,
                            TargetLane = tl,
                        });
                    }
                }
            }

            // 3. End turn — always valid (unless game is already over)
            if (!state.IsGameOver)
            {
                validActions.Add(new EndTurnAction
                {
                    PlayerIndex = state.CurrentPlayerIndex,
                });
            }

            if (validActions.Count == 0)
            {
                // No valid actions — force end turn
                validActions.Add(new EndTurnAction
                {
                    PlayerIndex = state.CurrentPlayerIndex,
                });
            }

            // Pick a random valid action using the seeded RNG
            var action = validActions[state.Rng.NextInt(validActions.Count)];
            actions.Add(action);
            state = DuelEngine.Apply(state, action);
        }

        return (state, actions);
    }
}