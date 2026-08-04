namespace Runewake.Engine.State;

/// <summary>
/// Where a card can be during a game.
/// </summary>
public enum Zone
{
    Deck,
    Hand,
    Lane,
    Discard,
    Barrow,
    RemovedFromGame
}

/// <summary>
/// Runtime instance of a card during a duel.
/// References its definition via <see cref="CardDefId"/>; all mutable
/// per-instance state lives here.
/// </summary>
public sealed class CardInstance
{
    /// <summary>Unique identifier for this instance within the game.</summary>
    public int InstanceId { get; }

    /// <summary>ID of the card definition this is an instance of.</summary>
    public string CardDefId { get; }

    /// <summary>Index of the player who controls this card (0 or 1).</summary>
    public int Controller { get; set; }

    /// <summary>Current zone this card occupies.</summary>
    public Zone Zone { get; set; }

    /// <summary>Lane index (0-4) when <see cref="Zone"/> is <see cref="Zone.Lane"/>; null otherwise.</summary>
    public int? LaneIndex { get; set; }

    // ——— Combat & State ———

    /// <summary>Total damage dealt to this card this game.</summary>
    public int Damage { get; set; }

    /// <summary>Bonus or penalty to Attack, applied at combat time.</summary>
    public int AttackModifier { get; set; }

    /// <summary>Bonus or penalty to base Vigor, applied at creation time or on buff.</summary>
    public int VigorModifier { get; set; }

    /// <summary>True if this card has attacked this turn.</summary>
    public bool HasAttackedThisTurn { get; set; }

    /// <summary>True if this card is Exhausted (cannot attack or activate).</summary>
    public bool IsExhausted { get; set; }

    // ——— Relic-specific ———

    /// <summary>True if a Relic card's identity condition has been met and the card is face-up.</summary>
    public bool IsIdentified { get; set; }

    // ——— Keywords at runtime ———

    /// <summary>Keywords granted at runtime (e.g. by abilities).</summary>
    public HashSet<string> GrantedKeywords { get; } = new();

    /// <summary>Keywords suppressed at runtime (e.g. by Silencing effects).</summary>
    public HashSet<string> RemovedKeywords { get; } = new();

    // ——— Curses ———

    /// <summary>List of curse instances attached to this card (by their InstanceId).</summary>
    public List<int> AttachedCurseIds { get; } = new();

    // ——— Construction ———

    public CardInstance(int instanceId, string cardDefId, int controller)
    {
        InstanceId = instanceId;
        CardDefId = cardDefId;
        Controller = controller;
        Zone = Zone.Deck;
    }

    private CardInstance(CardInstance other)
    {
        InstanceId = other.InstanceId;
        CardDefId = other.CardDefId;
        Controller = other.Controller;
        Zone = other.Zone;
        LaneIndex = other.LaneIndex;
        Damage = other.Damage;
        AttackModifier = other.AttackModifier;
        VigorModifier = other.VigorModifier;
        HasAttackedThisTurn = other.HasAttackedThisTurn;
        IsExhausted = other.IsExhausted;
        IsIdentified = other.IsIdentified;
        GrantedKeywords = new HashSet<string>(other.GrantedKeywords);
        RemovedKeywords = new HashSet<string>(other.RemovedKeywords);
        AttachedCurseIds = new List<int>(other.AttachedCurseIds);
    }

    /// <summary>
    /// Returns a deep clone of this card instance.
    /// </summary>
    public CardInstance Clone() => new(this);
}
