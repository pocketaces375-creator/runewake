using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;

namespace Runewake.Engine.State;

/// <summary>
/// The complete deterministic state of a Runewake duel.
/// P1: (GameState, Action) -> GameState.
/// Every field is included in Clone() so replay and what-if simulation
/// each operate on independent copies.
/// </summary>
public sealed class GameState
{
    /// <summary>Both players, indexed 0 (first player) and 1 (second player).</summary>
    public PlayerState[] Players { get; }

    /// <summary>Index of the player whose turn it is (0 or 1).</summary>
    public int CurrentPlayerIndex { get; set; }

    /// <summary>Current turn number. Starts at 1 and increments after the End step.</summary>
    public int TurnNumber { get; set; }

    /// <summary>The seeded deterministic RNG used for all randomness.</summary>
    public SeededRng Rng { get; set; }

    /// <summary>Content version identifier for replay validation.</summary>
    public int ContentVersion { get; set; }

    /// <summary>
    /// Next available instance ID for a newly created card token or copy.
    /// Monotonically increasing within the game.
    /// </summary>
    public int NextInstanceId { get; set; }

    /// <summary>
    /// Current trigger chain depth. Hard cap at 20 to prevent infinite loops.
    /// </summary>
    public int TriggerDepth { get; set; }

    /// <summary>
    /// True when the game has ended (a player reached 0 Vigor).
    /// </summary>
    public bool IsGameOver { get; set; }

    /// <summary>
    /// Index of the winning player, or null if the game is not yet over.
    /// </summary>
    public int? WinnerIndex { get; set; }

    /// <summary>
    /// The action log for replay generation: every action applied this game.
    /// Not cloned for performance — replays build their own.
    /// </summary>
    public List<GameAction> ActionLog { get; } = new();

    public GameState(ulong seed, int contentVersion = 1)
    {
        Players = new PlayerState[2];
        Players[0] = new PlayerState(0);
        Players[1] = new PlayerState(1);
        CurrentPlayerIndex = 0;
        TurnNumber = 1;
        Rng = new SeededRng(seed);
        ContentVersion = contentVersion;
        NextInstanceId = 1;
    }

    /// <summary>
    /// Factory: creates a fully initialized game state from a <see cref="GameConfig"/>.
    /// Resolves card definitions via <see cref="Cards.CardRegistry"/>, shuffles each
    /// deck using the seeded RNG, and deals starting hands.
    /// </summary>
    public static GameState Initialize(GameConfig config)
    {
        var state = new GameState(config.Seed, config.ContentVersion);

        for (int p = 0; p < 2; p++)
        {
            var deckIds = p == 0 ? config.Player0DeckIds : config.Player1DeckIds;
            var player = state.Players[p];

            foreach (var cardId in deckIds)
            {
                var def = Cards.CardRegistry.Get(cardId)
                    ?? throw new InvalidOperationException($"Card definition '{cardId}' not found in registry.");

                var instance = new CardInstance(state.NextInstanceId++, cardId, p)
                {
                    CardType = def.Type,
                    Cost = def.Cost,
                    Strata = def.Strata,
                    BaseAttack = def.Attack ?? 0,
                    BaseVigor = def.Vigor ?? 0,
                    Zone = Zone.Deck,
                };
                instance.Keywords.AddRange(def.Keywords);
                instance.Abilities.AddRange(def.Abilities.Select(a => new Cards.AbilityDef
                {
                    Trigger = a.Trigger, Condition = a.Condition, ActivationCost = a.ActivationCost,
                    Effects = a.Effects.Select(e => new Cards.EffectDef
                    {
                        Op = e.Op, Target = e.Target, Amount = e.Amount,
                        Attack = e.Attack, Vigor = e.Vigor, Keyword = e.Keyword,
                        TokenId = e.TokenId, Duration = e.Duration
                    }).ToList()
                }));
            }

            // Shuffle deck using seeded RNG (Fisher-Yates)
            Shuffle(player.Deck, state.Rng, player.Deck.Count);

            // Deal starting hands: P0 gets 4, P1 gets 5
            int handSize = p == 0 ? 4 : 5;
            for (int i = 0; i < handSize && player.Deck.Count > 0; i++)
            {
                var card = player.Deck[0];
                player.Deck.RemoveAt(0);
                card.Zone = Zone.Hand;
                player.Hand.Add(card);
            }

            // P1 starts with +1 Attunement (Second Delver compensation)
            if (p == 1)
            {
                player.AttunementMax = 1;
                player.Attunement = 1;
            }
        }

        return state;
    }

    private static void Shuffle(List<CardInstance> list, SeededRng rng, int count)
    {
        // Fisher-Yates shuffle using the seeded RNG
        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private GameState(GameState other)
    {
        Players = new PlayerState[2];
        Players[0] = other.Players[0].Clone();
        Players[1] = other.Players[1].Clone();
        CurrentPlayerIndex = other.CurrentPlayerIndex;
        TurnNumber = other.TurnNumber;
        Rng = other.Rng.Clone();
        ContentVersion = other.ContentVersion;
        NextInstanceId = other.NextInstanceId;
        TriggerDepth = other.TriggerDepth;
        IsGameOver = other.IsGameOver;
        WinnerIndex = other.WinnerIndex;
    }

    /// <summary>
    /// Returns a deep clone of the entire game state.
    /// The ActionLog is not cloned (it's append-only and replay-constructed).
    /// </summary>
    public GameState Clone() => new(this);

    /// <summary>
    /// Shortcut for the current player.
    /// </summary>
    public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];

    /// <summary>
    /// Shortcut for the opponent of the current player.
    /// </summary>
    public PlayerState Opponent => Players[1 - CurrentPlayerIndex];

    /// <summary>
    /// Returns a player state by index (0 or 1).
    /// </summary>
    public PlayerState Player(int index) => Players[index];

    /// <summary>
    /// Returns the opposing player index.
    /// </summary>
    public int OpponentIndex(int playerIndex) => 1 - playerIndex;

    /// <summary>
    /// Computes a deterministic 64-bit hash of the entire game state.
    /// Two states with the same hash have the same observable game state
    /// (ignoring RNG position and trigger depth, which are transient).
    /// Uses FNV-1a for deterministic, portable hashing.
    /// </summary>
    public ulong ComputeStateHash()
    {
        const ulong fnvOffset = 14695981039346656037;
        const ulong fnvPrime = 1099511628211;

        ulong Hash(ulong current, ulong value)
        {
            unchecked
            {
                for (int i = 0; i < 8; i++)
                {
                    current ^= (value >> (i * 8)) & 0xFF;
                    current *= fnvPrime;
                }
            }
            return current;
        }

        ulong HashInt(ulong current, int value) => Hash(current, (ulong)value);
        ulong HashBool(ulong current, bool value) => Hash(current, value ? 1UL : 0UL);

        ulong h = fnvOffset;

        // Game-level fields
        h = HashInt(h, CurrentPlayerIndex);
        h = HashInt(h, TurnNumber);
        h = HashInt(h, ContentVersion);
        h = HashBool(h, IsGameOver);
        h = HashInt(h, WinnerIndex ?? -1);
        h = HashInt(h, NextInstanceId);

        // Players
        for (int p = 0; p < 2; p++)
        {
            var pl = Players[p];
            h = HashInt(h, pl.Index);
            h = HashInt(h, pl.Vigor);
            h = HashInt(h, pl.MaxVigor);
            h = HashInt(h, pl.Attunement);
            h = HashInt(h, pl.AttunementMax);
            h = HashInt(h, pl.AttunementPerTurn);
            h = HashInt(h, pl.FatigueCounter);
            h = HashInt(h, pl.MaxHandSize);

            // Decks (remaining card IDs)
            h = HashInt(h, pl.Deck.Count);
            foreach (var c in pl.Deck)
                h = HashInt(h, c.InstanceId);

            // Hand
            h = HashInt(h, pl.Hand.Count);
            foreach (var c in pl.Hand)
                h = HashInt(h, c.InstanceId);

            // Discard
            h = HashInt(h, pl.Discard.Count);
            foreach (var c in pl.Discard)
                h = HashInt(h, c.InstanceId);

            // Barrow
            h = HashInt(h, pl.Barrow.Count);
            foreach (var c in pl.Barrow)
                h = HashInt(h, c.InstanceId);

            // Unearth queue
            h = HashInt(h, pl.UnearthQueue.Count);
            foreach (var c in pl.UnearthQueue)
                h = HashInt(h, c.InstanceId);

            // Lanes
            for (int l = 0; l < 5; l++)
            {
                var lane = pl.Lanes[l];
                if (lane.Occupant is CardInstance occ)
                {
                    h = HashInt(h, occ.InstanceId);
                    h = HashInt(h, occ.Controller);
                    h = HashInt(h, occ.BaseAttack);
                    h = HashInt(h, occ.BaseVigor);
                    h = HashInt(h, occ.Damage);
                    h = HashInt(h, occ.AttackModifier);
                    h = HashInt(h, occ.VigorModifier);
                    h = HashInt(h, (int)occ.Zone);
                    h = HashInt(h, occ.LaneIndex ?? -1);
                    h = HashBool(h, occ.HasAttackedThisTurn);
                    h = HashBool(h, occ.IsExhausted);
                    h = HashBool(h, occ.SummonedThisTurn);
                    h = HashInt(h, occ.WardRemaining);
                    h = HashBool(h, occ.IsVenomed);
                    h = HashInt(h, occ.UnearthCost);
                    h = HashBool(h, occ.IsIdentified);
                    // Hash effective keywords
                    var keywords = occ.EffectiveKeywords.OrderBy(k => k).ToList();
                    h = HashInt(h, keywords.Count);
                    foreach (var kw in keywords)
                        foreach (var ch in kw)
                            h = HashInt(h, (int)ch);
                }
                else
                {
                    h = HashInt(h, -1); // empty lane marker
                }
            }
        }

        return h;
    }
}
