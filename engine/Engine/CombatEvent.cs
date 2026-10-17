using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>What kind of thing happened (FABLE-064). The UI picks an animation per kind.</summary>
public enum FxKind
{
    /// <summary>A creature attacked (Amount = its power; target lane -1 = the face).</summary>
    Attack,
    /// <summary>Damage landed (Amount; Tag = ATTACK, CLASH, SPELL, PIERCE, BURN, AMBUSH, FATIGUE).</summary>
    Damage,
    /// <summary>Vigor restored (Amount actually healed).</summary>
    Heal,
    /// <summary>A creature left the board dead (Tag = COMBAT, DESTROY, VENOM, FRAGILE, TRIBUTE, BURN, WITHER).</summary>
    Death,
    /// <summary>A creature or token arrived (Tag = PLAY or TOKEN).</summary>
    Summon,
    /// <summary>A ritual was cast and resolved (TargetLane = its aim, or -1).</summary>
    Cast,
    /// <summary>A ritual was cast and a Sigil countered it (Tag = the trap's def id).</summary>
    Countered,
    /// <summary>A card's ability fired (Tag = the trigger name; SourceType tells relic / creature / artifact).</summary>
    Trigger,
    /// <summary>A keyword did something (Tag = GUARD, WARD, DODGE, ARMOR, VENOM, PIERCE, SWIFT, EXALTED, UNEARTH, SHIELD, REDIRECT).</summary>
    Keyword,
    /// <summary>Stats went up (Amount = attack, Amount2 = vigor).</summary>
    Buff,
    /// <summary>Stats went down (Amount = attack, Amount2 = vigor; both positive sizes).</summary>
    Debuff,
    /// <summary>Any other effect op on a target (Tag = the op name, Amount = its amount).</summary>
    Effect,
    /// <summary>A face-down Sigil sprang (Tag = COUNTER_RITUAL or AMBUSH).</summary>
    Sigil,
    /// <summary>FABLE-067: a player's turn begins (TargetSide = whose turn; Amount = turn number).</summary>
    TurnStart,
}

/// <summary>
/// FABLE-064: one thing that happened during an action, for the screen. The engine appends these to
/// <see cref="GameState.Events"/> while it works; they change nothing — not the rules, not the hash,
/// not replays. Sides and lanes are ENGINE indices (the client maps them to its own view).
/// </summary>
public sealed class CombatEvent
{
    /// <summary>Monotonic per game, so a screen can tell which events it has already shown.</summary>
    public int Seq { get; set; }
    public FxKind Kind { get; set; }

    /// <summary>Controller of whatever did this, or -1 (fatigue, burn ticks).</summary>
    public int Side { get; set; } = -1;
    public string SourceDefId { get; set; } = "";
    public int SourceInstanceId { get; set; } = -1;
    public CardType SourceType { get; set; } = CardType.CREATURE;
    /// <summary>Lane on <see cref="Side"/>'s board, or -1 when the source is off the board (ritual, artifact, rune).</summary>
    public int SourceLane { get; set; } = -1;
    /// <summary>Artifact slot index when the source is an artifact, else -1.</summary>
    public int SourceSlot { get; set; } = -1;

    /// <summary>The side that was hit / helped, or -1.</summary>
    public int TargetSide { get; set; } = -1;
    /// <summary>Lane on <see cref="TargetSide"/>'s board; -1 with a side set means the player's face.</summary>
    public int TargetLane { get; set; } = -1;
    public string TargetDefId { get; set; } = "";
    public int TargetInstanceId { get; set; } = -1;

    public int Amount { get; set; }
    public int Amount2 { get; set; }
    public string Tag { get; set; } = "";

    public bool TargetsFace => TargetSide >= 0 && TargetLane < 0;

    public override string ToString() =>
        $"#{Seq} {Kind} {Tag} src={SourceDefId}@{Side}/{SourceLane} tgt={TargetDefId}@{TargetSide}/{TargetLane} n={Amount},{Amount2}";
}

/// <summary>Builds and records <see cref="CombatEvent"/>s. Every method is a no-op while the state is muted.</summary>
public static class Fx
{
    /// <summary>Events kept per state; older ones fall off the front.</summary>
    public const int Keep = 240;

    public static CombatEvent New(FxKind kind, string tag = "") => new() { Kind = kind, Tag = tag };

    public static CombatEvent From(this CombatEvent e, CardInstance? src)
    {
        if (src is null) return e;
        e.Side = src.Controller;
        e.SourceDefId = src.CardDefId;
        e.SourceInstanceId = src.InstanceId;
        e.SourceType = src.CardType;
        e.SourceLane = src.Zone == Zone.Lane ? src.LaneIndex ?? -1 : -1;
        e.SourceSlot = src.CardType == CardType.ARTIFACT ? src.ArtifactSlotIndex : -1;
        return e;
    }

    public static CombatEvent To(this CombatEvent e, CardInstance? card)
    {
        if (card is null) return e;
        e.TargetSide = card.Controller;
        e.TargetLane = card.Zone == Zone.Lane ? card.LaneIndex ?? -1 : card.LaneIndex ?? -1;
        e.TargetDefId = card.CardDefId;
        e.TargetInstanceId = card.InstanceId;
        return e;
    }

    public static CombatEvent ToFace(this CombatEvent e, int side)
    {
        e.TargetSide = side;
        e.TargetLane = -1;
        return e;
    }

    public static CombatEvent To(this CombatEvent e, ResolvedTarget? t) => t switch
    {
        CreatureTarget ct => e.To(ct.Card),
        PlayerTarget pt => e.ToFace(pt.Player.Index),
        LaneTarget lt => e.ToLane(lt.PlayerIndex, lt.LaneIndex),
        _ => e
    };

    public static CombatEvent ToLane(this CombatEvent e, int side, int lane)
    {
        e.TargetSide = side;
        e.TargetLane = lane;
        return e;
    }

    public static CombatEvent N(this CombatEvent e, int amount, int amount2 = 0)
    {
        e.Amount = amount;
        e.Amount2 = amount2;
        return e;
    }

    /// <summary>Record it. Nothing is recorded while <see cref="GameState.FxMuted"/> is set.</summary>
    public static void Emit(GameState state, CombatEvent e)
    {
        if (state.FxMuted) return;
        e.Seq = ++state.EventSeq;
        state.Events.Add(e);
        if (state.Events.Count > Keep) state.Events.RemoveRange(0, state.Events.Count - Keep);
    }

    /// <summary>A keyword stopped or changed damage: WARD, DODGE, ARMOR (Amount = damage it took away), SHIELD.</summary>
    public static void Keyword(GameState state, string keyword, CardInstance? src, CardInstance target, int amount = 0) =>
        Emit(state, New(FxKind.Keyword, keyword).From(src).To(target).N(amount));
}
