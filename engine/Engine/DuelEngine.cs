using Runewake.Engine.State;
using Runewake.Engine.Cards;

namespace Runewake.Engine.Engine;

/// <summary>
/// The pure deterministic duel engine.
/// P1: <c>Engine.Apply(GameState, GameAction) -> GameState</c>
/// Every action clones the state, applies the mutation, and returns the new state.
/// No I/O, no side effects, no static mutable state.
/// </summary>
public static partial class DuelEngine
{
    /// <summary>
    /// Applies a player action to the game state and returns the new state.
    /// The original state is never mutated.
    /// </summary>
    public static GameState Apply(GameState state, GameAction action)
    {
        state = state.Clone();
        state.ActionLog.Add(action);
        state.LastTrapSprung = null;
        // FABLE-DROP-1: the chain cap is per action. It was never reset, so after the 20th trigger of a
        // match NOTHING triggered again — no death effects, no artifact charges, for the rest of the game.
        state.TriggerDepth = 0;
        Auras.Recompute(state);

        var result = action switch
        {
            EndTurnAction e => ApplyEndTurn(state, e),
            PlayCardAction p => ApplyPlayCard(state, p),
            AttackAction a => ApplyAttack(state, a),
            TapArtifactAction t => ApplyTapArtifact(state, t),
            _ => throw new ArgumentException($"Unknown action type: {action.GetType()}")
        };
        result.AimLane = null;
        if (!result.IsGameOver)
            Auras.Recompute(result);
        return result;
    }

    /// <summary>
    /// FABLE-DROP-1: wear off timed modifiers. <paramref name="endOfTurnOf"/> set = the end of that player's
    /// turn (THIS_TURN mods, NEXT_TURN mods of that player); <paramref name="startOfTurnOf"/> set = the start
    /// of that player's turn (UNTIL_YOUR_NEXT_TURN and artifact WHILE_PRESENT mods from that player).
    /// </summary>
    internal static void ExpireTimed(GameState state, int? endOfTurnOf, int? startOfTurnOf)
    {
        foreach (var p in state.Players)
            for (int i = 0; i < 5; i++)
            {
                var c = p.Lanes[i].Occupant;
                if (c is null || c.TimedMods.Count == 0) continue;
                foreach (var m in c.TimedMods.ToList())
                {
                    bool gone = false;
                    if (endOfTurnOf is int e)
                    {
                        if (m.EndOfTurn) gone = true;
                        if (m.ExpiresAtEndOfPlayersTurn == e)
                        {
                            if (m.SkipFirstEnd) m.SkipFirstEnd = false;
                            else gone = true;
                        }
                    }
                    if (startOfTurnOf is int st && m.ExpiresAtStartOfPlayer == st) gone = true;
                    if (!gone) continue;
                    c.TimedMods.Remove(m);
                    c.AttackModifier -= m.Attack;
                    c.VigorModifier -= m.Vigor;
                    if (m.Keyword is string kw && !c.TimedMods.Any(o => o.Keyword == kw))
                        c.GrantedKeywords.Remove(kw);
                    // losing a buff's Vigor never kills — it leaves the creature on at least 1
                    if (c.CurrentVigor <= 0 && m.Vigor > 0) c.Damage = Math.Max(0, c.MaxVigorNow - 1);
                }
            }
        EffectExecutor.SweepDead(state);   // a debuff wearing off can't kill; a buff wearing off can't either — safety only
    }

    // ——— Action handlers ———

    private static GameState ApplyEndTurn(GameState state, EndTurnAction action)
    {
        var endingPlayer = state.Player(action.PlayerIndex);

        // THIS_TURN cost discounts expire when the owning player ends their turn
        // (a discount created during the enemy's turn — Aura — survives into the
        // owner's turn and is cleared when the owner ends it).
        endingPlayer.CostMods.RemoveAll(m => m.Duration == Duration.THIS_TURN);

        // 1. End phase — ON_TURN_END triggers, Fragile check, then hand size check
        TriggerBus.Fire(state, Trigger.ON_TURN_END, action.PlayerIndex);
        KeywordHandlers.ProcessFragile(endingPlayer, state);
        MechanicOps.OnTurnEnd(state, action.PlayerIndex);
        ExpireTimed(state, endOfTurnOf: action.PlayerIndex, startOfTurnOf: null);
        TruncateHand(endingPlayer);

        // Tick suppression on the ending player's Artifacts (counted in owner's turns)
        // But we also tick AFTER triggers so ON_ARTIFACT_UNSUPPRESS can fire correctly
        TickArtifactSuppression(endingPlayer);

        // 1.5 Deferred ON_CHARGE_FULL — fire any artifact triggers with timing END_OF_TURN
        // that had PendingChargeFull set during this turn (Censer, Grimoire per G8).
        FireDeferredChargeFull(state, endingPlayer);

        // 1.6 Auto-charge gain for artifacts with gain_on="on_turn_end"
        AutoGainCharges(endingPlayer, state, "on_turn_end");

        // 2. Switch to next player
        state.CurrentPlayerIndex = state.OpponentIndex(action.PlayerIndex);
        if (state.CurrentPlayerIndex == 0)
            state.TurnNumber++;

        // 2.5 Refresh phase — ready all of the next player's creatures
        // Per rules §5-6: creatures summoned on a previous turn are Ready
        // at the start of your turn. Clear exhaustion, attack flags,
        // and summoned-this-turn markers.
        var nextPlayer = state.CurrentPlayer;
        foreach (var lane in nextPlayer.Lanes)
        {
            if (lane.Occupant is CardInstance creature)
            {
                creature.IsExhausted = false;
                creature.HasAttackedThisTurn = false;
                creature.SummonedThisTurn = false;
            }
        }
        // FABLE-DROP-1: "until your next turn" ends now
        ExpireTimed(state, endOfTurnOf: null, startOfTurnOf: state.CurrentPlayerIndex);

        // 3. Attune phase — increase attunement and refill
        nextPlayer = state.CurrentPlayer;
        int newMax = Math.Min(
            nextPlayer.AttunementMax + nextPlayer.AttunementPerTurn,
            10);
        nextPlayer.AttunementMax = newMax;
        nextPlayer.Attunement = newMax;
        MechanicOps.OnTurnStartBeforeDraw(state, nextPlayer);

        // 3.5 Cadence phase — cadenced ON_TURN_START artifact passives.
        // Prey marking (order BEFORE_ALL_OTHER_TURN_START_EFFECTS) resolves
        // before all other turn-start effects (R15); Censer heal after;
        // then draw (R11, R15).
        FireCadencedPassives(state, nextPlayer);

        // 3.6 Auto-charge gain for artifacts with gain_on="on_turn_start"
        AutoGainCharges(nextPlayer, state, "on_turn_start");

        // 4. Draw phase
        // First player (P0) skips their very first draw phase.
        // Tracked with HasSkippedFirstDraw to fire exactly once.
        bool firstPlayerSkipsDraw =
            state.CurrentPlayerIndex == 0
            && !state.HasSkippedFirstDraw;

        if (!firstPlayerSkipsDraw)
            ExecuteDraw(nextPlayer, state);
        else
            state.HasSkippedFirstDraw = true;

        // 4.5 FABLE-021: boss rules (extra draws, regeneration, the floor crumbling)
        if (state.BossRules.Count > 0 && !state.IsGameOver)
            BossRules.OnTurnStart(state, nextPlayer, p => ExecuteDraw(p, state));

        // 5. Start triggers — Unearth processing + ON_TURN_START triggers + relic identification
        KeywordHandlers.ProcessUnearth(nextPlayer);
        TriggerBus.Fire(state, Trigger.ON_TURN_START, state.CurrentPlayerIndex);
        IdentifyRelics(state, nextPlayer);

        // 6. Per-turn tracking reset for the current player
        nextPlayer.AttackCountLastTurn = nextPlayer.AttackCountThisTurn;
        nextPlayer.AttackCountThisTurn = 0;
        nextPlayer.SpellCastCountThisTurn = 0;
        nextPlayer.HasAttackedThisTurn = false;
        nextPlayer.SpellCastThisTurn = false;
        nextPlayer.PreyAttackCountThisTurn = 0;
        nextPlayer.FirstAttackerLaneIndex = null;
        nextPlayer.SecondAttackerLaneIndex = null;
        nextPlayer.FirstAttackedLaneIndex = null;
        state.CreatureDiedThisTurnCount[0] = 0;
        state.CreatureDiedThisTurnCount[1] = 0;
        // Reset per-turn charge tracking for the next player's artifacts (max_per_turn, etc.)
        foreach (var slot in nextPlayer.ArtifactSlots)
        {
            slot.ResetChargeTracking();
        }
        // Reset damage-prevention shield usage counters (R5: resets at start of EVERY turn, both players).
        DamageInterceptor.ResetUsage(state);
        // Reset ANCESTRAL_SHIELD usage flags at the start of the current player's turn (R1).
        KeywordHandlers.ResetAncestralShields(nextPlayer);

        // 7. Apply Artifact passives for this turn (re-applied each turn, cleared if suppressed)
        // PASSIVE abilities with WHILE_PRESENT duration are refreshed each turn.
        // Suppressed Artifacts skip this — their buffs naturally expire.
        ApplyArtifactPassives(state, nextPlayer);

        return state;
    }

    private static GameState ApplyPlayCard(GameState state, PlayCardAction action)
    {
        var player = state.Player(action.PlayerIndex);
        var card = player.Hand.FirstOrDefault(c => c.InstanceId == action.CardInstanceId)
            ?? throw new ArgumentException($"Card instance {action.CardInstanceId} not found in hand.");

        if (card.Zone != Zone.Hand)
            throw new InvalidOperationException("Card is not in hand.");

        // COST_MOD discounts (the discount mechanic) reduce the effective cost
        // at play time — the engine charges the discounted amount (floor 0).
        int effectiveCost = CostInterceptor.GetEffectiveCost(state, card, action.PlayerIndex);

        if (player.Attunement < effectiveCost)
            throw new InvalidOperationException($"Not enough attunement: have {player.Attunement}, need {effectiveCost}.");

        player.Attunement -= effectiveCost;

        // Consume per-turn discount gates (FIRST_SPELL_EACH_TURN) after a successful play.
        CostInterceptor.ConsumePerTurnMods(state, card, action.PlayerIndex);

        // Track spell casting for Artifact conditions
        if (card.CardType == CardType.RITUAL)
        {
            player.SpellCastThisTurn = true;
            player.SpellCastCountThisTurn++;
        }

        player.Hand.Remove(card);
        card.Controller = action.PlayerIndex;

        if (card.CardType == CardType.CREATURE || card.CardType == CardType.RELIC)
        {
            if (action.LaneIndex is not int laneIdx || laneIdx < 0 || laneIdx > 4)
                throw new ArgumentException($"Invalid lane index: {action.LaneIndex}.");
            var lane = player.Lanes[laneIdx];
            // FABLE-DROP-1: Lock and Tribute join Occupied and Buried as lane rules
            if (MechanicOps.LaneProblem(player, card, laneIdx) is string problem)
                throw new InvalidOperationException(problem);
            if (card.Tribute > 0)
            {
                foreach (var victim in MechanicOps.TributeVictims(player, card, laneIdx)!)
                    EffectExecutor.KillCreature(victim, state);
                if (lane.Occupant is not null)   // an Unearth victim left, a death trigger summoned into it…
                    throw new InvalidOperationException($"Lane {laneIdx} is already occupied.");
            }
            lane.Occupant = card;
            card.Zone = Zone.Lane;
            card.LaneIndex = laneIdx;

            if (card.CardType == CardType.CREATURE)
            {
                // Apply keyword effects: Swift, Ward, SummonedThisTurn, etc.
                // FABLE-DROP-2: auras first, so a keyword an aura grants (Swift from a pack leader) counts on arrival
                Auras.Recompute(state);
                KeywordHandlers.OnPlay(card);

                // FABLE-DROP-1: THIS creature's ON_SUMMON (twice with Echo), then the artifacts and runes
                // that watch summons. It used to re-run every creature's ON_SUMMON on the board.
                Auras.Recompute(state);
                TriggerBus.FireCardEvent(state, Trigger.ON_SUMMON, card, action.PlayerIndex, listenersBothSides: true);
            }
            else if (card.CardType == CardType.RELIC)
            {
                // Relic enters as a 0/3 unidentified artifact
                card.BaseAttack = 0;
                card.BaseVigor = 3;
                card.IsIdentified = false;
                card.IsExhausted = true;
            }
        }
        else if (card.CardType == CardType.RITUAL)
        {
            // FABLE-DROP-1: rituals RESOLVE. This was still the placeholder "no-op until P1-05": all 19
            // rituals cost their Attunement and did nothing. The lane the ritual was dropped on is its aim
            // (the creature in that lane, yours or theirs, is the one a single-target effect picks).
            card.Zone = Zone.RemovedFromGame;   // on the stack: not in hand, not yet in the discard
            if (!MechanicOps.TryCounter(state, card, action.PlayerIndex))
            {
                state.AimLane = action.LaneIndex;
                foreach (var ability in card.Abilities.ToList())
                    if (ability.Trigger == Trigger.RESOLVE && !state.IsGameOver)
                        TriggerBus.RunAbility(state, ability, card, action.PlayerIndex);
                state.AimLane = null;
            }
            card.Zone = Zone.Discard;
            player.Discard.Add(card);
            if (!state.IsGameOver)
            {
                TriggerBus.FireSide(state, Trigger.ON_CAST_RITUAL, action.PlayerIndex);
                TriggerBus.FireSide(state, Trigger.ON_SPELL_CAST, action.PlayerIndex);
            }
        }

        return state;
    }

    /// <summary>
    /// Track attack counts for Artifact system conditions.
    /// </summary>
    private static GameState ApplyAttack(GameState state, AttackAction action)
    {
        var player = state.Player(action.PlayerIndex);
        var opponent = state.Player(state.OpponentIndex(action.PlayerIndex));

        var sourceLane = player.Lanes[action.SourceLane];
        var attacker = sourceLane.Occupant
            ?? throw new InvalidOperationException($"No creature in lane {action.SourceLane} to attack with.");

        // Validate Ready
        if (attacker.IsExhausted)
            throw new InvalidOperationException("Attacker is exhausted.");
        if (attacker.HasAttackedThisTurn)
            throw new InvalidOperationException("Attacker has already attacked this turn.");

        // Rooted cannot attack
        if (!KeywordHandlers.CanAttack(attacker))
            throw new InvalidOperationException(attacker.Stunned ? "Attacker is stunned." : "Attacker has Rooted and cannot attack.");

        // Resolve target lane (handles Reach targeting)
        int? resolvedTarget = KeywordHandlers.ResolveTargetLane(attacker, action.SourceLane, action.TargetLane);
        if (resolvedTarget is null)
            throw new InvalidOperationException("Invalid attack target.");

        // Track attack for Artifact conditions
        bool isFirstAttack = player.AttackCountThisTurn == 0;
        player.AttackCountThisTurn++;
        player.HasAttackedThisTurn = true;
        if (isFirstAttack)
            player.FirstAttackerLaneIndex = action.SourceLane;
        else if (player.AttackCountThisTurn == 2)
            player.SecondAttackerLaneIndex = action.SourceLane;

        // Fire ON_CREATURE_ATTACKS — set current attacker context for trigger target resolution
        player.CurrentAttackerLaneIndex = action.SourceLane;
        TriggerBus.Fire(state, Trigger.ON_CREATURE_ATTACKS, action.PlayerIndex);
        // FABLE-DROP-1: "when this attacks" — never fired before (two cards and the Dawnbreaker Maul relied on it)
        if (!state.IsGameOver && ReferenceEquals(sourceLane.Occupant, attacker))
            TriggerBus.FireCardEvent(state, Trigger.ON_ATTACK, attacker, action.PlayerIndex, listenersBothSides: false);
        player.CurrentAttackerLaneIndex = null;
        // FABLE-DROP-1: an AMBUSH Sigil strikes the attacker first
        if (!state.IsGameOver && ReferenceEquals(sourceLane.Occupant, attacker))
            MechanicOps.SpringAmbush(state, opponent, attacker);
        Auras.Recompute(state);
        // the attack trigger may have ended the game or killed the attacker: then there is no combat
        if (state.IsGameOver || !ReferenceEquals(sourceLane.Occupant, attacker))
            return state;

        int targetLaneIdx = resolvedTarget.Value;
        targetLaneIdx = MechanicOps.RedirectAttack(state, opponent, targetLaneIdx);

        // Determine final target: creature or face (with Guard redirect)
        var targetLane = opponent.Lanes[targetLaneIdx];
        int? actualTargetLaneIdx;

        if (targetLane.Occupant is not null)
        {
            // Occupied — fight the blocker
            actualTargetLaneIdx = targetLaneIdx;
        }
        else
        {
            // Empty opposing lane — check Guard
            var guardLane = FindGuardLane(opponent);
            if (guardLane is not null)
            {
                // Redirect to Guard lane
                actualTargetLaneIdx = guardLane;
            }
            else
            {
                // Face damage
                actualTargetLaneIdx = null;
            }
        }

        int attackPower = attacker.CurrentAttack;
        var damagedSurvivors = new List<CardInstance>();   // FABLE-DROP-2: for "when this takes damage"

        // TASK-FUN-SIM-1(c): ALTAR mode — lane 2 is War Altar (+1 atk)
        if (state.AltarMode && action.SourceLane == 2)
            attackPower += 1;

        if (actualTargetLaneIdx is int tgtIdx0)
        {
            // FABLE-DROP-1: "whenever one of your creatures is attacked" — ten artifacts listen for it and it
            // never fired. It goes before the blow lands; if the defender is gone after it, the attack fizzles.
            var defender0 = opponent.Lanes[tgtIdx0].Occupant!;
            opponent.LastAttackedLaneIndex = tgtIdx0;
            // FABLE-054: mark the first creature attacked BEFORE its listeners run. It used to be set after, so
            // "the first creature attacked each enemy turn" (Shield, Unbroken Bulwark) found no one on the first blow.
            if (opponent.FirstAttackedLaneIndex is null)
                opponent.FirstAttackedLaneIndex = tgtIdx0;
            TriggerBus.FireSide(state, Trigger.ON_ALLY_ATTACKED, opponent.Index);
            Auras.Recompute(state);
            if (state.IsGameOver || !ReferenceEquals(sourceLane.Occupant, attacker))
                return state;
            if (!ReferenceEquals(opponent.Lanes[tgtIdx0].Occupant, defender0))
            {
                attacker.HasAttackedThisTurn = true;
                attacker.IsExhausted = true;
                return state;
            }
            attackPower = attacker.CurrentAttack + (state.AltarMode && action.SourceLane == 2 ? 1 : 0);
            attackPower = MechanicOps.AttackPowerWithExalted(state, player, attacker, attackPower);
        }
        else
            attackPower = MechanicOps.AttackPowerWithExalted(state, player, attacker, attackPower);

        if (actualTargetLaneIdx is int tgtIdx)
        {
            var actualLane = opponent.Lanes[tgtIdx];
            var defender = actualLane.Occupant!;

            // Track first creature attacked on the defender's side (Bulwark FIRST_ATTACKED)
            if (opponent.FirstAttackedLaneIndex is null)
                opponent.FirstAttackedLaneIndex = tgtIdx;

            // Prey tracking: if the defender is this player's marked Prey, count the attack (Quiver R17)
            if (player.PreyTargetId == defender.InstanceId)
                player.PreyAttackCountThisTurn++;

            // Ward reduces attacker's damage to defender
            int damageToDefender = KeywordHandlers.ApplyWard(defender, attackPower);
            damageToDefender = MechanicOps.CombatDamageTo(state, defender, damageToDefender);

            // Simultaneous damage (defender hits back with full power unless attacker has STEALTH_STRIKE — R8)
            int atkDamage = defender.CurrentAttack;

            // TASK-FUN-SIM-1(c): ALTAR mode — lane 2 attacker takes double combat damage
            if (state.AltarMode && action.SourceLane == 2)
                atkDamage *= 2;

            // FABLE-DROP-1: the counter-blow goes through Ward too (it used to ignore the attacker's Ward)
            if (!attacker.EffectiveKeywords.Contains("STEALTH_STRIKE"))
                atkDamage = MechanicOps.CombatDamageTo(state, attacker, KeywordHandlers.ApplyWard(attacker, atkDamage));
            // Combat damage is intercepted by PREVENT_DAMAGE shields (source ATTACK).
            int dealtToDefender = MechanicOps.ReduceByArmor(defender,
                DamageInterceptor.Reduce(state, defender, damageToDefender, DamageInterceptor.SourceAttack));
            defender.Damage += dealtToDefender;
            int dealtToAttacker = 0;
            if (!attacker.EffectiveKeywords.Contains("STEALTH_STRIKE"))
            {
                dealtToAttacker = MechanicOps.ReduceByArmor(attacker,
                    DamageInterceptor.Reduce(state, attacker, atkDamage, DamageInterceptor.SourceAttack));
                attacker.Damage += dealtToAttacker;
            }
            MechanicOps.AfterCombatDamage(state, attacker, defender, dealtToDefender, dealtToAttacker);
            damageToDefender = dealtToDefender;

            // Venom marking
            KeywordHandlers.OnCombatDamageDealt(attacker, defender, damageToDefender);

            // Pierce: excess damage to defender carries to face
            // TASK-FUN-SIM-1(c): ALTAR mode — edge lanes 0 and 4 block Pierce
            bool hedgeBlockPierce = state.AltarMode && action.SourceLane is 0 or 4;
            bool defenderKilled = defender.CurrentVigor <= 0;
            if (defenderKilled && attacker.EffectiveKeywords.Contains("PIERCE") && !hedgeBlockPierce)
            {
                int neededToKill = defender.MaxVigorNow - (defender.Damage - dealtToDefender);
                int excessDamage = System.Math.Max(0, attackPower - neededToKill);
                excessDamage = DamageInterceptor.Reduce(state, opponent, excessDamage, DamageInterceptor.SourceAttack);
                opponent.Vigor -= excessDamage;
                CheckGameOver(state, opponent);
            }

            // Remove dead defender (FABLE-DROP-1: the one death path — Unearth, triggers, counters)
            if (defenderKilled)
                EffectExecutor.KillCreature(defender, state);
            else if (dealtToDefender > 0)
                damagedSurvivors.Add(defender);
            if (dealtToAttacker > 0)
                damagedSurvivors.Add(attacker);
        }
        else
        {
            // Face damage (intercepted by PREVENT_DAMAGE shields, source ATTACK).
            attackPower = DamageInterceptor.Reduce(state, opponent, attackPower, DamageInterceptor.SourceAttack);
            opponent.Vigor -= attackPower;
            CheckGameOver(state, opponent);
        }

        // Resolve Venom (destroy any creatures marked by Venom this combat)
        KeywordHandlers.ResolveVenom(state, action.PlayerIndex);

        // Remove dead attacker (FABLE-DROP-1: the one death path)
        if (attacker.Zone == Zone.Lane && attacker.CurrentVigor <= 0)
        {
            EffectExecutor.KillCreature(attacker, state);
        }
        else if (attacker.Zone == Zone.Lane)
        {
            // Mark attacker as used if it survived
            attacker.HasAttackedThisTurn = true;
            attacker.IsExhausted = true;
        }

        foreach (var hurt in damagedSurvivors)
            TriggerBus.FireDamaged(state, hurt);

        return state;
    }

    /// <summary>
    /// Find the first lane index (0–4) on the given player's board that holds
    /// a creature with the Guard keyword, or null if none exists.
    /// </summary>
    private static int? FindGuardLane(PlayerState player)
    {
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is not null && occ.EffectiveKeywords.Contains("GUARD"))
                return i;
        }
        return null;
    }

    /// <summary>
    /// Checks if the given player's Vigor has reached 0 or below and sets game-over state.
    /// </summary>
    private static void CheckGameOver(GameState state, PlayerState player)
    {
        if (player.Vigor <= 0)
        {
            state.IsGameOver = true;
            state.WinnerIndex = state.OpponentIndex(player.Index);
        }
    }

    /// <summary>
    /// Check all relics belonging to the given player. Any that have an identify condition
    /// that is now met get flipped (IsIdentified = true) and fire ON_RELIC_IDENTIFY.
    /// </summary>
    private static void IdentifyRelics(GameState state, PlayerState player)
    {
        for (int i = 0; i < 5; i++)
        {
            var occ = player.Lanes[i].Occupant;
            if (occ is null || occ.CardType != CardType.RELIC || occ.IsIdentified)
                continue;

            if (occ.IdentifyCondition is not null &&
                TriggerBus.EvaluateCondition(occ.IdentifyCondition, occ, player.Index, state))
            {
                occ.IsIdentified = true;
                // Fire ON_RELIC_IDENTIFY triggers (the relic's own abilities come online)
                TriggerBus.Fire(state, Trigger.ON_RELIC_IDENTIFY, player.Index);
            }
        }
    }

    /// <summary>
    /// TASK-FUN-SIM-1(b): Tap an artifact to fire its held charge-full effect.
    /// Only fires when InvokeMode is active and the slot has HasHeldChargeFull.
    /// TEST HARNESS ONLY — never shipped.
    /// </summary>
    private static GameState ApplyTapArtifact(GameState state, TapArtifactAction action)
    {
        var player = state.Player(action.PlayerIndex);
        if (action.SlotIndex < 0 || action.SlotIndex >= player.ArtifactSlots.Length)
            return state;

        var slot = player.ArtifactSlots[action.SlotIndex];
        if (!slot.HasHeldChargeFull || slot.Occupant is null || slot.IsSuppressed)
            return state;

        // Fire the held charge-full effect
        slot.HasHeldChargeFull = false;
        slot.PendingChargeFull = false;
        TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, player.Index, slot.Index);

        // Spend all charges after firing (same as auto-fire behavior)
        slot.SpendAllCharges();

        return state;
    }

    /// <summary>
    /// Check if any of the player's artifacts have a held charge-full (InvokeMode).
    /// </summary>
    private static bool HasHeldCharges(PlayerState player)
    {
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.HasHeldChargeFull)
                return true;
        }
        return false;
    }

    // ——— Phase helpers ———

    private static void ExecuteDraw(PlayerState player, State.GameState state)
    {
        if (player.Deck.Count > 0)
        {
            var drawn = player.Deck[0];
            player.Deck.RemoveAt(0);
            drawn.Zone = Zone.Hand;
            player.Hand.Add(drawn);
        }
        else
        {
            // Fatigue
            player.FatigueCounter++;
            player.Vigor -= player.FatigueCounter;
            if (player.Vigor <= 0)
            {
                state.IsGameOver = true;
                state.WinnerIndex = state.OpponentIndex(player.Index);
            }
        }
    }

    private static void TruncateHand(PlayerState player)
    {
        while (player.Hand.Count > player.MaxHandSize)
        {
            var discarded = player.Hand[^1];
            player.Hand.RemoveAt(player.Hand.Count - 1);
            discarded.Zone = Zone.Discard;
            player.Discard.Add(discarded);
        }
    }

    // ——— Artifact system helpers ———

    /// <summary>
    /// Fire cadenced ON_TURN_START artifact passives in explicit order.
    /// Cadence <see cref="EffectDef.CadenceOnTurnStart"/> means the passive
    /// resolves at the start of the owner's turn, BEFORE the draw phase
    /// (R11, R15). The <see cref="EffectDef.Order"/> key gives an explicit
    /// resolution order: <see cref="EffectDef.OrderBeforeAllOtherTurnStartEffects"/>
    /// (Bow Prey marking, R15) resolves before any other turn-start effect.
    /// Suppressed Artifacts don't contribute. Stable by slot order within
    /// the same order key.
    /// </summary>
    private static void FireCadencedPassives(GameState state, PlayerState player)
    {
        var opponent = state.Player(state.OpponentIndex(player.Index));

        var pending = new List<(ArtifactSlot slot, CardInstance artifact, AbilityDef ability, EffectDef effect)>();

        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.Occupant is null || slot.IsSuppressed)
                continue;

            foreach (var ability in slot.Occupant.Abilities)
            {
                if (ability.Trigger != Trigger.PASSIVE)
                    continue;

                foreach (var effect in ability.Effects)
                {
                    if (effect.Cadence == EffectDef.CadenceOnTurnStart)
                        pending.Add((slot, slot.Occupant, ability, effect));
                }
            }
        }

        // Explicit ordering key: BEFORE_ALL_OTHER_TURN_START_EFFECTS first,
        // then default order; stable by slot index within each group.
        var ordered = pending
            .OrderBy(p => p.effect.Order == EffectDef.OrderBeforeAllOtherTurnStartEffects ? 0 : 1)
            .ThenBy(p => p.slot.Index)
            .ToList();

        foreach (var (_, artifact, ability, effect) in ordered)
        {
            if (!TriggerBus.EvaluateCondition(ability.Condition, artifact, player.Index, state))
                continue;

            var targets = TargetResolver.Resolve(
                effect.Target ?? new TargetDef { Scope = Scope.NONE },
                artifact,
                player,
                opponent,
                state);
            EffectExecutor.Execute(effect, artifact, state, targets);
        }
    }

    /// <summary>
    /// Apply Artifact PASSIVE abilities for the given player.
    /// Called at the start of each turn. Suppressed Artifacts don't apply theirs.
    /// Passives with WHILE_PRESENT duration are re-applied each turn so
    /// suppression naturally suspends them.
    /// Cadenced passives (effects with a Cadence) fire in their cadence phase
    /// (e.g. <see cref="EffectDef.CadenceOnTurnStart"/>) and are skipped here.
    /// </summary>
    private static void ApplyArtifactPassives(GameState state, PlayerState player)
    {
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.Occupant is null || slot.IsSuppressed)
                continue;

            foreach (var ability in slot.Occupant.Abilities)
            {
                if (ability.Trigger != Trigger.PASSIVE)
                    continue;

                var opponent = state.Player(state.OpponentIndex(player.Index));

                // Check ANY condition on the passive ability
                if (!TriggerBus.EvaluateCondition(ability.Condition, slot.Occupant, player.Index, state))
                    continue;

                foreach (var effect in ability.Effects)
                {
                    // Cadenced passives fire in their cadence phase, not here.
                    if (!string.IsNullOrEmpty(effect.Cadence))
                        continue;

                    var targets = TargetResolver.Resolve(
                        effect.Target ?? new TargetDef { Scope = Scope.NONE },
                        slot.Occupant,
                        player,
                        opponent,
                        state);
                    EffectExecutor.Execute(effect, slot.Occupant, state, targets);
                }
            }
        }
    }

    /// <summary>
    /// Tick suppression counters on the given player's Artifacts.
    /// Called at the end of the player's turn (counted in that player's turns).
    /// When suppression expires, fires ON_ARTIFACT_UNSUPPRESS triggers.
    /// </summary>
    private static void TickArtifactSuppression(PlayerState player)
    {
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.IsSuppressed)
            {
                slot.TickSuppression();
            }
        }
    }

    /// <summary>
    /// Fire deferred ON_CHARGE_FULL triggers for any artifact slots with
    /// PendingChargeFull set. These are artifacts whose ON_CHARGE_FULL ability
    /// has timing "END_OF_TURN" (Censer, Grimoire per G8).
    /// Clears the PendingChargeFull flag after firing.
    /// </summary>
    private static void FireDeferredChargeFull(GameState state, PlayerState endingPlayer)
    {
        var opponent = state.Player(state.OpponentIndex(endingPlayer.Index));

        foreach (var slot in endingPlayer.ArtifactSlots)
        {
            if (!slot.PendingChargeFull || slot.Occupant is null || slot.IsSuppressed)
                continue;

            slot.PendingChargeFull = false;

            // Fire ON_CHARGE_FULL for THIS slot only (G6: the opponent's mirror
            // artifact must not fire when this player's charges filled).
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, endingPlayer.Index, slot.Index);
        }
    }

    /// <summary>
    /// Auto-gain 1 charge for all of the player's artifact slots whose
    /// AutoChargeGainOn matches the given trigger string (e.g. "on_turn_end").
    /// Skips suppressed artifacts. Triggers ON_CHARGE_FULL when charges fill.
    /// In InvokeMode, sets HasHeldChargeFull instead of auto-firing.
    /// </summary>
    private static void AutoGainCharges(PlayerState player, GameState state, string trigger)
    {
        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.Occupant is null || slot.IsSuppressed)
                continue;
            if (slot.MaxCharges <= 0 || slot.AutoChargeGainOn != trigger)
                continue;
            if (slot.Charges >= slot.MaxCharges)
                continue; // already full

            int before = slot.Charges;
            slot.AddCharges(1);
            int added = slot.Charges - before;
            if (added <= 0)
                continue;

            // Fire ON_CHARGE_GAINED for this slot
            TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_GAINED, player.Index, slot.Index);

            // Fire ON_CHARGE_FULL if charges just hit max
            bool justFilled = before < slot.MaxCharges && slot.Charges >= slot.MaxCharges;
            if (justFilled)
            {
                if (state.InvokeMode)
                {
                    // TASK-FUN-SIM-1(b): INVOKE — hold charge-full until tapped
                    slot.HasHeldChargeFull = true;
                }
                else if (slot.HasDeferredChargeFull)
                {
                    slot.PendingChargeFull = true;
                }
                else
                {
                    TriggerBus.FireArtifactSlot(state, Trigger.ON_CHARGE_FULL, player.Index, slot.Index);
                }
            }
        }
    }
}