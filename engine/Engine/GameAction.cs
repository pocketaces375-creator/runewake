namespace Runewake.Engine.Engine;

/// <summary>
/// Base type for all actions a player can take in a duel.
/// Every action produces a new <see cref="State.GameState"/> via <c>Engine.Apply</c>.
/// </summary>
public abstract record GameAction
{
    /// <summary>Index of the player taking this action.</summary>
    public int PlayerIndex { get; init; }
}

/// <summary>
/// Ends the current player's turn. Advances through Attune → Draw → StartTriggers → End.
/// </summary>
public sealed record EndTurnAction : GameAction;

/// <summary>
/// Plays a card from the player's hand.
/// </summary>
public sealed record PlayCardAction : GameAction
{
    /// <summary>The instance ID of the card in hand to play.</summary>
    public int CardInstanceId { get; init; }

    /// <summary>Attunement cost to pay.</summary>
    public int Cost { get; init; }

    /// <summary>Target lane index (0–4) for creatures and relics.</summary>
    public int? LaneIndex { get; init; }
}

/// <summary>
/// Attacks with a creature from the specified lane.
/// </summary>
public sealed record AttackAction : GameAction
{
    /// <summary>Lane of the attacking creature (0–4).</summary>
    public int SourceLane { get; init; }
}
