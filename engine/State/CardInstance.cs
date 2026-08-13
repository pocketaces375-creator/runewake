using Runewake.Engine.Cards;

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
    RemovedFromGame,
    ArtifactSlot  /// Permanent field-effect slot (Artifact). Never changes zone.
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

    /// <summary>The card type from the definition (CREATURE, RITUAL, RELIC, etc.).</summary>
    public CardType CardType { get; set; }

    /// <summary>Attunement cost to play this card.</summary>
    public int Cost { get; set; }

    /// <summary>Stratum (color/region) for filter matching (STRATA:VERDANT, etc.).</summary>
    public Strata Strata { get; set; }

    /// <summary>Index of the player who controls this card (0 or 1).</summary>
    public int Controller { get; set; }

    /// <summary>Current zone this card occupies.</summary>
    public Zone Zone { get; set; }

    /// <summary>Lane index (0-4) when <see cref="Zone"/> is <see cref="Zone.Lane"/>; null otherwise.</summary>
    public int? LaneIndex { get; set; }

    // ——— Combat & State ———

    /// <summary>Base Attack value from the card definition.</summary>
    public int BaseAttack { get; set; }

    /// <summary>Base Vigor value from the card definition.</summary>
    public int BaseVigor { get; set; }

    /// <summary>Total damage dealt to this card this game.</summary>
    public int Damage { get; set; }

    /// <summary>Bonus or penalty to Attack, applied at combat time.</summary>
    public int AttackModifier { get; set; }

    /// <summary>Bonus or penalty to base Vigor, applied at creation time or on buff.</summary>
    public int VigorModifier { get; set; }

    /// <summary>
    /// Current effective Attack: base + modifier (never below 0).
    /// </summary>
    public int CurrentAttack => Math.Max(0, BaseAttack + AttackModifier);

    /// <summary>
    /// Current effective Vigor: base + modifier - damage (never below 0).
    /// </summary>
    public int CurrentVigor => Math.Max(0, BaseVigor + VigorModifier - Damage);

    /// <summary>True if this card has attacked this turn.</summary>
    public bool HasAttackedThisTurn { get; set; }

    /// <summary>True if this card is Exhausted (cannot attack or activate).</summary>
    public bool IsExhausted { get; set; }

    /// <summary>True if this card was summoned during the current turn (for Fragile).</summary>
    public bool SummonedThisTurn { get; set; }

    // ——— Keyword state ———

    /// <summary>Remaining Ward charges. Each prevents one instance of damage.</summary>
    public int WardRemaining { get; set; }

    /// <summary>True if marked by Venom for destruction at end of combat.</summary>
    public bool IsVenomed { get; set; }

    /// <summary>Cost to return this card via Unearth. 0 if not Unearth.</summary>
    public int UnearthCost { get; set; }

    // ——— Relic-specific ———

    /// <summary>True if a Relic card's identity condition has been met and the card is face-up.</summary>
    public bool IsIdentified { get; set; }

    // ——— Artifact-specific ———

    /// <summary>The class this Artifact belongs to (e.g. "warrior", "mage"). Empty string for non-artifact cards.</summary>
    public string ArtifactClass { get; set; } = string.Empty;

    /// <summary>The slot pool this Artifact draws from (e.g. "sword", "shield", "dagger").</summary>
    public string SlotPool { get; set; } = string.Empty;

    /// <summary>Index of the ArtifactSlot this card occupies (-1 if not in an Artifact slot).</summary>
    public int ArtifactSlotIndex { get; set; } = -1;

    /// <summary>Whether this card is an Artifact (kind: "artifact").</summary>
    public bool IsArtifact => CardType == CardType.ARTIFACT;

    // ——— Keywords at runtime ———

    /// <summary>Keywords this card naturally has (from its definition).</summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>Keywords granted at runtime (e.g. by abilities).</summary>
    public HashSet<string> GrantedKeywords { get; } = new();

    /// <summary>Keywords suppressed at runtime (e.g. by Silencing effects).</summary>
    public HashSet<string> RemovedKeywords { get; } = new();

    /// <summary>
    /// Resolved keywords: definition keywords + granted - removed.
    /// </summary>
    public HashSet<string> EffectiveKeywords
    {
        get
        {
            var effective = new HashSet<string>(Keywords);
            effective.UnionWith(GrantedKeywords);
            effective.ExceptWith(RemovedKeywords);
            return effective;
        }
    }

    // ——— Curses ———

    /// <summary>List of curse instances attached to this card (by their InstanceId).</summary>
    public List<int> AttachedCurseIds { get; } = new();

    // ——— Abilities ———

    /// <summary>Ability definitions from the card (for trigger matching and resolution).</summary>
    public List<AbilityDef> Abilities { get; set; } = new();

    /// <summary>
    /// Condition that must be met for this RELIC to identify (flip).
    /// Only applies to RELIC-type cards. Null for non-relics.
    /// </summary>
    public ConditionDef? IdentifyCondition { get; set; }

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
        CardType = other.CardType;
        Cost = other.Cost;
        Strata = other.Strata;
        Controller = other.Controller;
        Zone = other.Zone;
        LaneIndex = other.LaneIndex;
        BaseAttack = other.BaseAttack;
        BaseVigor = other.BaseVigor;
        Damage = other.Damage;
        AttackModifier = other.AttackModifier;
        VigorModifier = other.VigorModifier;
        HasAttackedThisTurn = other.HasAttackedThisTurn;
        IsExhausted = other.IsExhausted;
        SummonedThisTurn = other.SummonedThisTurn;
        WardRemaining = other.WardRemaining;
        IsVenomed = other.IsVenomed;
        UnearthCost = other.UnearthCost;
        IsIdentified = other.IsIdentified;
        Keywords = new List<string>(other.Keywords);
        GrantedKeywords = new HashSet<string>(other.GrantedKeywords);
        RemovedKeywords = new HashSet<string>(other.RemovedKeywords);
        AttachedCurseIds = new List<int>(other.AttachedCurseIds);
        Abilities = other.Abilities.ConvertAll(a => new AbilityDef
        {
            Trigger = a.Trigger,
            Condition = a.Condition,
            ActivationCost = a.ActivationCost,
            Effects = a.Effects.ConvertAll(e => new EffectDef
            {
                Op = e.Op, Target = e.Target, Amount = e.Amount,
                Attack = e.Attack, Vigor = e.Vigor, Keyword = e.Keyword,
                TokenId = e.TokenId, Duration = e.Duration
            })
        });
        IdentifyCondition = other.IdentifyCondition is not null ? CopyCondition(other.IdentifyCondition) : null;
    }

    private static ConditionDef CopyCondition(ConditionDef c)
    {
        return new ConditionDef
        {
            Op = c.Op,
            Value = c.Value,
            All = c.All?.ConvertAll(s => CopyCondition(s)),
            Any = c.Any?.ConvertAll(s => CopyCondition(s))
        };
    }

    /// <summary>
    /// Returns a deep clone of this card instance.
    /// </summary>
    public CardInstance Clone() => new(this);
}
