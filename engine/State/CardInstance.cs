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
    /// FABLE-DROP-1: Attack/Vigor granted by PASSIVE auras (creatures and identified relics on the
    /// board). Recomputed from scratch after every action by <c>Auras.Recompute</c> — never edited by hand.
    /// </summary>
    public int AuraAttack { get; set; }

    /// <summary>FABLE-DROP-1: Vigor granted by PASSIVE auras (see <see cref="AuraAttack"/>).</summary>
    public int AuraVigor { get; set; }

    /// <summary>FABLE-DROP-1: keywords granted by PASSIVE auras (see <see cref="AuraAttack"/>).</summary>
    public HashSet<string> AuraKeywords { get; private set; } = new();

    /// <summary>
    /// FABLE-DROP-1: buffs, debuffs and keywords that wear off (THIS_TURN, UNTIL_YOUR_NEXT_TURN,
    /// WHILE_PRESENT artifact passives). Before this, every "this turn" buff was permanent.
    /// </summary>
    public List<TimedMod> TimedMods { get; private set; } = new();

    /// <summary>FABLE-DROP-1: an Unearth creature returns once; the copy that comes back has used it.</summary>
    public bool UnearthUsed { get; set; }

    // ——— FABLE-DROP-1: class-mechanic statuses ———

    /// <summary>Creature types (tribal), from the definition.</summary>
    public List<string> Types { get; set; } = new();

    /// <summary>Tribute N from the definition (0 = none).</summary>
    public int Tribute { get; set; }

    /// <summary>Stunned: can't attack. Wears off at the end of its controller's next turn.</summary>
    public bool Stunned { get; set; }

    /// <summary>Stunned during its controller's own turn: survive the first end-of-turn.</summary>
    public bool StunSkipFirstEnd { get; set; }

    /// <summary>Burn N: at the start of its controller's turn it takes N damage, then Burn drops by 1.</summary>
    public int Burn { get; set; }

    /// <summary>Redirect: the next enemy attack against this side hits this creature instead.</summary>
    public int RedirectCharges { get; set; }

    /// <summary>Taken by STEAL until end of turn: who gets it back (-1 = not stolen / stolen for good).</summary>
    public int StolenFrom { get; set; } = -1;

    /// <summary>The lane it was taken from (returned there if it is empty).</summary>
    public int StolenFromLane { get; set; } = -1;

    /// <summary>
    /// Current effective Attack: base + modifier + aura (never below 0).
    /// </summary>
    public int CurrentAttack => Math.Max(0, BaseAttack + AttackModifier + AuraAttack);

    /// <summary>Maximum Vigor right now: base + modifier + aura (before damage).</summary>
    public int MaxVigorNow => BaseVigor + VigorModifier + AuraVigor;

    /// <summary>
    /// Current effective Vigor: base + modifier + aura - damage (never below 0).
    /// </summary>
    public int CurrentVigor => Math.Max(0, BaseVigor + VigorModifier + AuraVigor - Damage);

    /// <summary>True if this card has attacked this turn.</summary>
    public bool HasAttackedThisTurn { get; set; }

    /// <summary>True if this card is Exhausted (cannot attack or activate).</summary>
    public bool IsExhausted { get; set; }

    /// <summary>True if this card was summoned during the current turn (for Fragile).</summary>
    public bool SummonedThisTurn { get; set; }

    // ——— Keyword state ———

    /// <summary>Remaining Ward charges. Each prevents one instance of damage.</summary>
    public int WardRemaining { get; set; }

    /// <summary>
    /// ANCESTRAL_SHIELD: true after the one-use-per-turn clamp has been consumed.
    /// Reset at the start of this creature's controller's next turn.
    /// </summary>
    public bool AncestralShieldUsedThisTurn { get; set; }

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

    /// <summary>
    /// Active damage-prevention shields (PREVENT_DAMAGE) protecting this creature.
    /// Intercepted at damage-application time by the engine.
    /// </summary>
    public List<DamageShield> DamageShields { get; } = new();

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
            effective.UnionWith(AuraKeywords);
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
        AncestralShieldUsedThisTurn = other.AncestralShieldUsedThisTurn;
        IsVenomed = other.IsVenomed;
        UnearthCost = other.UnearthCost;
        IsIdentified = other.IsIdentified;
        AuraAttack = other.AuraAttack;
        AuraVigor = other.AuraVigor;
        AuraKeywords = new HashSet<string>(other.AuraKeywords);
        TimedMods = other.TimedMods.ConvertAll(m => m.Clone());
        UnearthUsed = other.UnearthUsed;
        Types = new List<string>(other.Types);
        Tribute = other.Tribute;
        Stunned = other.Stunned;
        StunSkipFirstEnd = other.StunSkipFirstEnd;
        Burn = other.Burn;
        RedirectCharges = other.RedirectCharges;
        StolenFrom = other.StolenFrom;
        StolenFromLane = other.StolenFromLane;
        Keywords = new List<string>(other.Keywords);
        GrantedKeywords = new HashSet<string>(other.GrantedKeywords);
        RemovedKeywords = new HashSet<string>(other.RemovedKeywords);
        AttachedCurseIds = new List<int>(other.AttachedCurseIds);
        Abilities = other.Abilities.ConvertAll(a => new AbilityDef
        {
            Trigger = a.Trigger,
            Condition = a.Condition,
            ActivationCost = a.ActivationCost,
            Timing = a.Timing,
            Effects = a.Effects.ConvertAll(e => new EffectDef
            {
                Op = e.Op, Target = e.Target, Amount = e.Amount,
                Attack = e.Attack, Vigor = e.Vigor, Keyword = e.Keyword,
                TokenId = e.TokenId, Duration = e.Duration,
                Source = e.Source, Frequency = e.Frequency, Filter = e.Filter,
                Condition = e.Condition,
                AppliesTo = e.AppliesTo, Value = e.Value, Stacks = e.Stacks,
                Cadence = e.Cadence, Order = e.Order,
                SpendFrom = e.SpendFrom, Spend = e.Spend, PerCharge = e.PerCharge
            })
        });
        IdentifyCondition = other.IdentifyCondition is not null ? CopyCondition(other.IdentifyCondition) : null;
        DamageShields = other.DamageShields.ConvertAll(s => s.Clone());
    }

    private static ConditionDef CopyCondition(ConditionDef c)
    {
        return new ConditionDef
        {
            Op = c.Op,
            Value = c.Value,
            Side = c.Side,
            Tribe = c.Tribe,
            All = c.All?.ConvertAll(s => CopyCondition(s)),
            Any = c.Any?.ConvertAll(s => CopyCondition(s))
        };
    }

    /// <summary>
    /// Returns a deep clone of this card instance.
    /// </summary>
    public CardInstance Clone() => new(this);

    /// <summary>
    /// FABLE-DROP-1: the ONE way to make a playable instance from a card definition — every field the
    /// engine reads (types, tribute, every effect field). Copies made by hand in three places had each
    /// dropped different fields (the tutorial's cards had no abilities at all).
    /// </summary>
    public static CardInstance FromDef(CardDef def, int instanceId, int controller, Zone zone = Zone.Deck)
    {
        var c = new CardInstance(instanceId, def.Id, controller)
        {
            CardType = def.Type,
            Cost = def.Cost,
            Strata = def.Strata,
            BaseAttack = def.Attack ?? 0,
            BaseVigor = def.Vigor ?? 0,
            Zone = zone,
            Tribute = def.Tribute ?? 0,
        };
        c.Keywords.AddRange(def.Keywords.Select(k => k.ToUpperInvariant()));
        c.Types.AddRange(def.Types.Select(t => t.ToUpperInvariant()));
        c.Abilities.AddRange(def.Abilities.Select(CloneAbility));
        c.IdentifyCondition = def.IdentifyCondition;
        return c;
    }

    public static AbilityDef CloneAbility(AbilityDef a) => new()
    {
        Trigger = a.Trigger, Condition = a.Condition, ActivationCost = a.ActivationCost, Timing = a.Timing,
        Effects = a.Effects.Select(CloneEffect).ToList()
    };

    public static EffectDef CloneEffect(EffectDef e) => new()
    {
        Op = e.Op, Target = e.Target, Amount = e.Amount,
        Attack = e.Attack, Vigor = e.Vigor, Keyword = e.Keyword,
        TokenId = e.TokenId, Duration = e.Duration,
        Source = e.Source, Frequency = e.Frequency, Filter = e.Filter,
        Condition = e.Condition,
        AppliesTo = e.AppliesTo, Value = e.Value, Stacks = e.Stacks,
        Cadence = e.Cadence, Order = e.Order,
        SpendFrom = e.SpendFrom, Spend = e.Spend, PerCharge = e.PerCharge
    };
}

/// <summary>
/// FABLE-DROP-1: a modifier that wears off. <see cref="EndOfTurn"/> = gone when the current turn ends;
/// <see cref="ExpiresAtStartOfPlayer"/> = gone when that player's next turn starts (-1 = not used).
/// </summary>
public sealed class TimedMod
{
    public int Attack { get; set; }
    public int Vigor { get; set; }
    public string? Keyword { get; set; }
    public bool EndOfTurn { get; set; }
    public int ExpiresAtStartOfPlayer { get; set; } = -1;
    /// <summary>NEXT_TURN: gone at the end of this player's turn (-1 = not used)…</summary>
    public int ExpiresAtEndOfPlayersTurn { get; set; } = -1;
    /// <summary>…but not the end of the turn it was made in, when that is already their turn.</summary>
    public bool SkipFirstEnd { get; set; }
    public TimedMod Clone() => (TimedMod)MemberwiseClone();
}
