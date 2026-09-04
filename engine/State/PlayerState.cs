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

    /// <summary>True after this player has exercised or declined their one mulligan.</summary>
    public bool HasMulliganed { get; set; }

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

    // ——— Artifact system ———

    /// <summary>
    /// The player's Artifact slots (array length = number of Artifacts this class carries).
    /// For launch: length 2 (all classes have 2 Artifact slots).
    /// Framework supports 1-3 slots per class (§8).
    /// </summary>
    public ArtifactSlot[] ArtifactSlots { get; set; } = Array.Empty<ArtifactSlot>();

    /// <summary>
    /// Instance ID of the creature currently marked as Prey for this player (Ranger mechanic).
    /// Null when no Prey is marked. Cleared when the marked creature leaves play
    /// or replaced by a new mark.
    /// </summary>
    public int? PreyTargetId { get; set; }

    /// <summary>
    /// Current class name for this player (e.g. "warrior", "rogue", "battlemage").
    /// Empty string if not yet assigned.
    /// </summary>
    public string ArtifactClass { get; set; } = string.Empty;

    /// <summary>
    /// IDs of the Artifact definitions assigned to this player's ArtifactSlots.
    /// Matches the slot count (2 for launch classes).
    /// </summary>
    public string[] ArtifactDefIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Number of creatures that attacked this turn (for condition tracking).
    /// Reset at start of each turn.
    /// </summary>
    public int AttackCountThisTurn { get; set; }

    /// <summary>
    /// Number of spells cast this turn (for condition tracking).
    /// Reset at start of each turn.
    /// </summary>
    public int SpellCastCountThisTurn { get; set; }

    /// <summary>
    /// Whether this player attacked at all this turn (for Anvil/ON_NO_ATTACK_TURN).
    /// </summary>
    public bool HasAttackedThisTurn { get; set; }

    /// <summary>
    /// Whether the player has cast at least one spell this turn (for Artifact triggers).
    /// </summary>
    public bool SpellCastThisTurn { get; set; }

    /// <summary>
    /// Number of creatures that attacked last turn (for NO_ATTACKERS_LAST_TURN).
    /// Set at end of turn before AttackCountThisTurn is reset.
    /// </summary>
    public int AttackCountLastTurn { get; set; }

    /// <summary>
    /// Number of friendly creatures that attacked Prey this turn (for NTH_ATTACKER_ON_PREY).
    /// Reset at start of turn.
    /// </summary>
    public int PreyAttackCountThisTurn { get; set; }

    /// <summary>
    /// Lane index of the first friendly creature that attacked this turn (for FIRST_ATTACKER filter).
    /// Null if no creature has attacked yet this turn.
    /// </summary>
    public int? FirstAttackerLaneIndex { get; set; }

    /// <summary>
    /// Lane index of the first friendly creature that was attacked by an enemy this turn
    /// (for FIRST_ATTACKED filter). Null if no creature has been attacked yet this turn.
    /// </summary>
    public int? FirstAttackedLaneIndex { get; set; }

    /// <summary>
    /// Active damage-prevention shields (PREVENT_DAMAGE) protecting this player.
    /// Intercepted at damage-application time by the engine.
    /// </summary>
    public List<DamageShield> DamageShields { get; } = new();

    /// <summary>
    /// Active cost discounts (COST_MOD) for this player.
    /// Applied at card-play time by CostInterceptor — matching mods reduce the
    /// effective play cost (never below 0).
    /// </summary>
    public List<CostMod> CostMods { get; } = new();

    public PlayerState(int index, int startingVigor = 25)
    {
        Index = index;
        MaxVigor = startingVigor;
        Vigor = startingVigor;
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

        // Artifact system
        ArtifactSlots = other.ArtifactSlots.Select(s => s.Clone()).ToArray();
        PreyTargetId = other.PreyTargetId;
        ArtifactClass = other.ArtifactClass;
        ArtifactDefIds = (string[])other.ArtifactDefIds.Clone();
        AttackCountThisTurn = other.AttackCountThisTurn;
        SpellCastCountThisTurn = other.SpellCastCountThisTurn;
        HasAttackedThisTurn = other.HasAttackedThisTurn;
        SpellCastThisTurn = other.SpellCastThisTurn;
        AttackCountLastTurn = other.AttackCountLastTurn;
        PreyAttackCountThisTurn = other.PreyAttackCountThisTurn;
        FirstAttackerLaneIndex = other.FirstAttackerLaneIndex;
        FirstAttackedLaneIndex = other.FirstAttackedLaneIndex;
        DamageShields = other.DamageShields.ConvertAll(s => s.Clone());
        CostMods = other.CostMods.ConvertAll(m => m.Clone());
    }

    /// <summary>
    /// Returns a deep clone of this player state.
    /// </summary>
    public PlayerState Clone() => new(this);
}
