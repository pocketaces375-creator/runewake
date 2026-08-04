using System.Collections.Generic;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// Mutable state for one player in a duel.
/// </summary>
public sealed class PlayerState
{
    /// <summary>Player index (0 or 1).</summary>
    public int Index { get; }

    /// <summary>Current Vigor (life total). Starts at 25.</summary>
    public int Vigor { get; set; }

    /// <summary>Maximum Vigor — baseline is 25, can be modified by effects.</summary>
    public int MaxVigor { get; set; }

    /// <summary>Current attunement available this turn.</summary>
    public int Attunement { get; set; }

    /// <summary>Maximum attunement cap (default 10). Raised temporarily by effects.</summary>
    public int AttunementMax { get; set; }

    /// <summary>Attunement increment per turn (default 1).</summary>
    public int AttunementPerTurn { get; set; }

    /// <summary>Cards currently in deck (instances, not definitions).</summary>
    public List<CardInstance> Deck { get; }

    /// <summary>Cards in hand.</summary>
    public List<CardInstance> Hand { get; }

    /// <summary>Discard pile.</summary>
    public List<CardInstance> Discard { get; }

    /// <summary>Barrow — face-down buried cards.</summary>
    public List<CardInstance> Barrow { get; }

    /// <summary>The player's five lanes.</summary>
    public LaneState[] Lanes { get; }

    /// <summary>
    /// Number of times this player has drawn from an empty deck this game.
    /// Used to calculate fatigue damage: 1, 2, 3, …
    /// </summary>
    public int FatigueCounter { get; set; }

    /// <summary>Maximum hand size (default 10).</summary>
    public int MaxHandSize { get; set; }

    /// <summary>IDs of curse instances attached to this player.</summary>
    public List<int> AttachedCurseIds { get; } = new();

    /// <summary>
    /// Cards pending Unearth return. At the start of this player's turn,
    /// they pay the cost and these return to hand.
    /// </summary>
    public List<CardInstance> UnearthQueue { get; } = new();

    /// <summary>
    /// Virtual token cards for rune abilities. Each holds one rune's AbilityDef.
    /// These sit off-board (LaneIndex = -1) and are collected by the trigger bus.
    /// </summary>
    public List<CardInstance> RuneTokens { get; } = new();

    public PlayerState(int index)
    {
        Index = index;
        MaxVigor = 25;
        Vigor = 25;
        AttunementMax = 0;
        Attunement = 0;
        AttunementPerTurn = 1;
        MaxHandSize = 10;
        Deck = new List<CardInstance>();
        Hand = new List<CardInstance>();
        Discard = new List<CardInstance>();
        Barrow = new List<CardInstance>();
        Lanes = new LaneState[5];
        for (int i = 0; i < 5; i++)
            Lanes[i] = new LaneState(i);
    }

    private PlayerState(PlayerState other)
    {
        Index = other.Index;
        Vigor = other.Vigor;
        MaxVigor = other.MaxVigor;
        Attunement = other.Attunement;
        AttunementMax = other.AttunementMax;
        AttunementPerTurn = other.AttunementPerTurn;
        FatigueCounter = other.FatigueCounter;
        MaxHandSize = other.MaxHandSize;

        Deck = other.Deck.ConvertAll(c => c.Clone());
        Hand = other.Hand.ConvertAll(c => c.Clone());
        Discard = other.Discard.ConvertAll(c => c.Clone());
        Barrow = other.Barrow.ConvertAll(c => c.Clone());
        Lanes = new LaneState[5];
        for (int i = 0; i < 5; i++)
            Lanes[i] = other.Lanes[i].Clone();

        AttachedCurseIds = new List<int>(other.AttachedCurseIds);
        UnearthQueue = other.UnearthQueue.ConvertAll(c => c.Clone());
        RuneTokens = other.RuneTokens.ConvertAll(c => c.Clone());
    }

    /// <summary>
    /// Returns a deep clone of this player state.
    /// </summary>
    public PlayerState Clone() => new(this);
}
