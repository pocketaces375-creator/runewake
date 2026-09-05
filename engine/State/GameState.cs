using System.Linq;
using System.Text.Json;
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

    /// <summary>
    /// Number of creatures that died this turn, per player index (side-aware).
    /// [0] = creatures controlled by player 0 that died this turn, [1] = player 1's.
    /// Incremented in KillCreature / attack resolution. Reset at start of each turn.
    /// Used for CREATURE_DIED_THIS_TURN (Grimoire discount R19, G7).
    /// </summary>
    public int[] CreatureDiedThisTurnCount { get; set; } = new int[2];

    /// <summary>
    /// Per-player count of total creatures died this entire game.
    /// Used to evaluate opening rule lift conditions
    /// (e.g. root_choked: lifts when Warden's first creature dies).
    /// </summary>
    public int[] TotalCreatureDiedCount { get; set; } = new int[2];

    /// <summary>
    /// Player index of the most recently deceased creature.
    /// Set before firing ON_CREATURE_DIES so conditions (FRIENDLY/ENEMY) can evaluate.
    /// </summary>
    public int LastDeathPlayerIndex { get; set; }

    /// <summary>
    /// The opening rule active in this game (e.g. "root_choked"), or null if none.
    /// Set at Initialize from GameConfig.OpeningRule.
    /// </summary>
    public string? OpeningRule { get; set; }

    /// <summary>
    /// Player index (0 or 1) that owns the opening rule (the Warden).
    /// Set at Initialize from GameConfig.OpeningRuleOwner.
    /// Rules are resolved relative to this owner.
    /// </summary>
    public int OpeningRuleOwner { get; set; }

    /// <summary>
    /// Tracks whether each player's opening rule has been lifted.
    /// [0] = P0's rule lifted, [1] = P1's rule lifted.
    /// </summary>
    public bool[] OpeningRuleLifted { get; set; } = new bool[2];

    /// <summary>
    /// True once the first player (P0) has had their turn-one draw skipped.
    /// Ensures the skip fires exactly once, for P0 only.
    /// </summary>
    public bool HasSkippedFirstDraw { get; set; }

    // ——— TASK-FUN-SIM-1: Sim variant mode flags ———
    // TEST HARNESS ONLY — never shipped. Default false = existing behavior.

    /// <summary>TASK-FUN-SIM-1(a): Override starting vigor to 20.</summary>
    public bool StartingVigor20 { get; set; }

    /// <summary>TASK-FUN-SIM-1(b): Artifact charges held until tapped.</summary>
    public bool InvokeMode { get; set; }

    /// <summary>TASK-FUN-SIM-1(c): Altar/heedge lane rules active.</summary>
    public bool AltarMode { get; set; }

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
        int startingVigor = 25;

        var state = new GameState(config.Seed, config.ContentVersion);

        // Apply sim variant flags from MatchConfig
        if (config.MatchConfig is not null)
        {
            if (config.MatchConfig.StartingVigor20)
            {
                startingVigor = 20;
                state.StartingVigor20 = true;
            }
            if (config.MatchConfig.InvokeMode)
                state.InvokeMode = true;
            if (config.MatchConfig.AltarMode)
                state.AltarMode = true;
        }

        // Apply starting vigor from MatchConfig before any deck processing
        for (int p = 0; p < 2; p++)
        {
            state.Players[p].MaxVigor = startingVigor;
            state.Players[p].Vigor = startingVigor;
        }

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
                        TokenId = e.TokenId, Duration = e.Duration,
                        Source = e.Source, Frequency = e.Frequency, Filter = e.Filter,
                        Condition = e.Condition,
                        AppliesTo = e.AppliesTo, Value = e.Value, Stacks = e.Stacks
                    }).ToList()
                }));

                player.Deck.Add(instance);
            }

            // Shuffle deck using seeded RNG (Fisher-Yates)
            Shuffle(player.Deck, state.Rng, player.Deck.Count);

            // Deal starting hands: P0 gets 4, P1 gets 6 (first-player compensation)
            int handSize = p == 0 ? 4 : 6;
            for (int i = 0; i < handSize && player.Deck.Count > 0; i++)
            {
                var card = player.Deck[0];
                player.Deck.RemoveAt(0);
                card.Zone = Zone.Hand;
                player.Hand.Add(card);
            }
        }

        // Run the first player's Attune step (Turn 1, current player = P0).
        // The Attune phase in ApplyEndTurn only runs for the *next* player, so
        // P0's first turn never gets one unless we do it here.
        int p0Max = Math.Min(state.Players[0].AttunementMax + state.Players[0].AttunementPerTurn, 10);
        state.Players[0].AttunementMax = p0Max;
        state.Players[0].Attunement = p0Max;

        // P1 gets no compensation at Initialize — the normal Attune step on
        // their first turn (when P0's EndTurn switches to P1) gives them +1.

        // Inject runes for player 0 if a rune page is configured
        if (config.RunePage != null)
        {
            RuneInjector.ApplyRunes(state, config.RunePage);
        }

        // ——— Initialize Artifacts ———
        for (int p = 0; p < 2; p++)
        {
            var player = state.Players[p];
            var artifactIds = p == 0 ? config.Player0ArtifactIds : config.Player1ArtifactIds;
            var className = p == 0 ? config.Player0Class : config.Player1Class;

            if (artifactIds.Length == 0) continue;

            player.ArtifactClass = className;
            player.ArtifactDefIds = artifactIds;
            player.ArtifactSlots = new ArtifactSlot[artifactIds.Length];
            player.AttackCountThisTurn = 0;
            player.SpellCastCountThisTurn = 0;
            player.HasAttackedThisTurn = false;

            for (int slotIdx = 0; slotIdx < artifactIds.Length; slotIdx++)
            {
                var slot = new ArtifactSlot(slotIdx);
                var artDef = Cards.ArtifactRegistry.Get(artifactIds[slotIdx])
                    ?? throw new InvalidOperationException($"Artifact definition '{artifactIds[slotIdx]}' not found.");

                var instance = new CardInstance(state.NextInstanceId++, artifactIds[slotIdx], p)
                {
                    CardType = CardType.ARTIFACT,
                    Zone = Zone.ArtifactSlot,
                    ArtifactSlotIndex = slotIdx,
                    ArtifactClass = artDef.Class,
                    SlotPool = artDef.SlotPool,
                    Cost = 0,
                    BaseAttack = 0,
                    BaseVigor = 0,
                };

                // Build the passive effect into an ability
                var passiveAbility = new AbilityDef
                {
                    Trigger = Trigger.PASSIVE,
                    Effects = new List<EffectDef> { artDef.Passive }
                };

                // Build the trigger ability
                var triggerAbility = artDef.Trigger;

                instance.Abilities.Add(passiveAbility);
                if (triggerAbility is not null)
                    instance.Abilities.Add(triggerAbility);

                // If the artifact has a dedicated full_charge effects list,
                // add an ON_CHARGE_FULL ability for them. This lets the trigger
                // handle charge-gain events while the full-charge effect is separate.
                if (artDef.FullCharge is { Count: > 0 })
                {
                    instance.Abilities.Add(new AbilityDef
                    {
                        Trigger = Trigger.ON_CHARGE_FULL,
                        Effects = artDef.FullCharge
                    });
                }

                // Initialize Charges if configured
                if (artDef.Charges is { } chargeCfg)
                {
                    slot.MaxCharges = chargeCfg.Max;
                    slot.Charges = 0;
                    slot.ChargeConfigMaxPerTurn = chargeCfg.MaxPerTurn;
                    slot.ChargeConfigMaxPerCreaturePerTurn = chargeCfg.MaxPerCreaturePerTurn;

                    // Store auto-charge gain trigger from ChargeConfig
                    string? gainOn = chargeCfg.GainOn;
                    if (!string.IsNullOrEmpty(gainOn) && (gainOn == "on_turn_start" || gainOn == "on_turn_end"))
                        slot.AutoChargeGainOn = gainOn;
                }

                // Determine if this artifact's ON_CHARGE_FULL trigger has timing END_OF_TURN
                slot.HasDeferredChargeFull = artDef.Trigger is not null
                    && artDef.Trigger.Trigger == Trigger.ON_CHARGE_FULL
                    && artDef.Trigger.Timing == "END_OF_TURN";

                slot.Occupant = instance;
                player.ArtifactSlots[slotIdx] = slot;
            }
        }

        // Fire ON_ARTIFACT_REVEAL triggers for all Artifacts (open info, before mulligans)
        for (int p = 0; p < 2; p++)
        {
            Engine.TriggerBus.Fire(state, Trigger.ON_ARTIFACT_REVEAL, p);
        }

        // ——— Apply opening rule from encounter ———
        if (!string.IsNullOrEmpty(config.OpeningRule))
        {
            state.OpeningRule = config.OpeningRule;
            state.OpeningRuleOwner = config.OpeningRuleOwner;
            Engine.OpeningRuleHandler.ApplyRule(state, config.OpeningRule);
        }

        return state;
    }

    /// <summary>
    /// Fisher-Yates shuffle using the given seeded RNG.
    /// </summary>
    public static void Shuffle(List<CardInstance> list, SeededRng rng, int count)
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
        CreatureDiedThisTurnCount = (int[])other.CreatureDiedThisTurnCount.Clone();
        LastDeathPlayerIndex = other.LastDeathPlayerIndex;
        TotalCreatureDiedCount = (int[])other.TotalCreatureDiedCount.Clone();
        OpeningRule = other.OpeningRule;
        OpeningRuleOwner = other.OpeningRuleOwner;
        OpeningRuleLifted = (bool[])other.OpeningRuleLifted.Clone();
        HasSkippedFirstDraw = other.HasSkippedFirstDraw;
        StartingVigor20 = other.StartingVigor20;
        InvokeMode = other.InvokeMode;
        AltarMode = other.AltarMode;
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

        // Hash a single damage-prevention shield into the state hash.
        ulong HashShield(ulong current, DamageShield s)
        {
            current = HashInt(current, s.Amount);
            if (s.Source is not null)
                foreach (var ch in s.Source)
                    current = HashInt(current, (int)ch);
            if (s.Frequency is not null)
                foreach (var ch in s.Frequency)
                    current = HashInt(current, (int)ch);
            if (s.SourceArtifactDefId is not null)
                foreach (var ch in s.SourceArtifactDefId)
                    current = HashInt(current, (int)ch);
            current = HashInt(current, s.SourceArtifactInstanceId);
            current = HashInt(current, s.SourceController);
            current = HashInt(current, s.UsedThisTurn);
            return current;
        }

        // Hash a single cost discount (COST_MOD) into the state hash.
        ulong HashCostMod(ulong current, CostMod m)
        {
            current = HashInt(current, m.Amount);
            if (m.AppliesTo is not null)
                foreach (var ch in m.AppliesTo)
                    current = HashInt(current, (int)ch);
            if (m.Filter is not null)
                foreach (var ch in m.Filter)
                    current = HashInt(current, (int)ch);
            current = HashInt(current, m.Value ?? -1);
            if (m.Condition?.Op is ConditionOp cop)
                current = HashInt(current, (int)cop);
            if (m.Condition?.Value is JsonElement cv && cv.ValueKind == JsonValueKind.Number && cv.TryGetInt32(out var condVal))
                current = HashInt(current, condVal);
            if (m.Condition?.Side is not null)
                foreach (var ch in m.Condition.Side)
                    current = HashInt(current, (int)ch);
            current = HashInt(current, m.Duration is { } dur ? (int)dur : -1);
            current = HashBool(current, m.Stacks);
            current = HashInt(current, m.UsedThisTurn);
            if (m.SourceArtifactDefId is not null)
                foreach (var ch in m.SourceArtifactDefId)
                    current = HashInt(current, (int)ch);
            current = HashInt(current, m.SourceArtifactInstanceId);
            current = HashInt(current, m.SourceController);
            return current;
        }

        ulong h = fnvOffset;

        // Game-level fields
        h = HashInt(h, CurrentPlayerIndex);
        h = HashInt(h, TurnNumber);
        h = HashInt(h, ContentVersion);
        h = HashBool(h, IsGameOver);
        h = HashInt(h, WinnerIndex ?? -1);
        h = HashInt(h, NextInstanceId);
        h = HashInt(h, CreatureDiedThisTurnCount[0]);
        h = HashInt(h, CreatureDiedThisTurnCount[1]);
        h = HashInt(h, LastDeathPlayerIndex);
        h = HashInt(h, TotalCreatureDiedCount[0]);
        h = HashInt(h, TotalCreatureDiedCount[1]);
        h = HashBool(h, OpeningRuleLifted[0]);
        h = HashBool(h, OpeningRuleLifted[1]);
        h = HashBool(h, HasSkippedFirstDraw);
        h = HashInt(h, OpeningRuleOwner);

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
            // Turn-scoped counters (G5) — read by conditions/filters, part of observable state
            h = HashInt(h, pl.AttackCountThisTurn);
            h = HashInt(h, pl.SpellCastCountThisTurn);
            h = HashBool(h, pl.HasAttackedThisTurn);
            h = HashBool(h, pl.SpellCastThisTurn);
            h = HashInt(h, pl.AttackCountLastTurn);
            h = HashInt(h, pl.PreyAttackCountThisTurn);
            h = HashInt(h, pl.FirstAttackerLaneIndex ?? -1);
            h = HashInt(h, pl.FirstAttackedLaneIndex ?? -1);

            // Damage-prevention shields (player)
            h = HashInt(h, pl.DamageShields.Count);
            foreach (var s in pl.DamageShields)
                h = HashShield(h, s);

            // Cost discounts (player) — COST_MOD
            h = HashInt(h, pl.CostMods.Count);
            foreach (var m in pl.CostMods)
                h = HashCostMod(h, m);

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
                    // Damage-prevention shields (creature)
                    h = HashInt(h, occ.DamageShields.Count);
                    foreach (var s in occ.DamageShields)
                        h = HashShield(h, s);
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

            // Artifact slots (charges, suppression, tracking per G3, G5, G8)
            for (int a = 0; a < pl.ArtifactSlots.Length; a++)
            {
                var slot = pl.ArtifactSlots[a];
                h = HashInt(h, slot.Charges);
                h = HashInt(h, slot.MaxCharges);
                h = HashBool(h, slot.IsSuppressed);
                h = HashInt(h, slot.SuppressionRemaining);
                h = HashBool(h, slot.PassiveAppliedThisTurn);
                h = HashBool(h, slot.HasTriggeredThisTurn);
                h = HashInt(h, slot.ChargesGainedThisTurn);
                h = HashBool(h, slot.PendingChargeFull);
                h = HashBool(h, slot.HasDeferredChargeFull);
                h = HashInt(h, slot.ChargeConfigMaxPerTurn);
                h = HashInt(h, slot.ChargeConfigMaxPerCreaturePerTurn);
                // Per-creature tracking — hash each creature entry
                h = HashInt(h, slot.ChargesGainedThisTurnByCreature.Count);
                foreach (var kvp in slot.ChargesGainedThisTurnByCreature.OrderBy(kvp => kvp.Key))
                {
                    h = HashInt(h, kvp.Key);
                    h = HashInt(h, kvp.Value);
                }
                // Hash occupant instance id for replay determinism
                h = HashInt(h, slot.Occupant?.InstanceId ?? -1);
            }
        }

        return h;
    }
}
