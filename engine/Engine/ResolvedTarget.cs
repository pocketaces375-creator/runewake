using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// A resolved target for an effect. Can be a creature (CardInstance in a lane)
/// or a player (PlayerState).
/// </summary>
public abstract record ResolvedTarget
{
    /// <summary>The player index that owns/controls this target.</summary>
    public abstract int OwnerIndex { get; }
}

/// <summary>A creature or relic in a lane.</summary>
public sealed record CreatureTarget(CardInstance Card, int PlayerIndex, int LaneIndex) : ResolvedTarget
{
    public override int OwnerIndex => PlayerIndex;
}

/// <summary>A player (face/vigor).</summary>
public sealed record PlayerTarget(PlayerState Player) : ResolvedTarget
{
    public override int OwnerIndex => Player.Index;
}