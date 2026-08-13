using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Executes card effects on resolved targets.
/// Every method mutates the (already cloned) GameState in place.
/// See <c>docs/02_CARD_DSL.md §2</c> for the full OP reference.
/// </summary>
public static class EffectExecutor
{
    /// <summary>
    /// Execute an effect definition against the given resolved targets.
    /// </summary>
    public static void Execute(
        EffectDef effect,
        CardInstance source,
        GameState state,
        List<ResolvedTarget> targets)
    {
        var player = state.Player(source.Controller);
        var opponent = state.Player(state.OpponentIndex(source.Controller));

        foreach (var target in targets)
        {
            switch (effect.Op)
            {
                case Op.DAMAGE:
                    ApplyDamage(target, effect.Amount ?? 0, state);
                    break;
                case Op.HEAL:
                    ApplyHeal(target, effect.Amount ?? 0);
                    break;
                case Op.BUFF:
                    ApplyBuff(target, effect.Attack ?? 0, effect.Vigor ?? 0, effect.Duration);
                    break;
                case Op.DEBUFF:
                    ApplyBuff(target, -(effect.Attack ?? 0), -(effect.Vigor ?? 0), effect.Duration);
                    break;
                case Op.DESTROY:
                    ApplyDestroy(target, state);
                    break;
                case Op.DRAW:
                    ApplyDraw(target, effect.Amount ?? 1, state);
                    break;
                case Op.DISCARD:
                    ApplyDiscard(target, effect.Amount ?? 1, state);
                    break;
                case Op.EXCAVATE:
                    ApplyExcavate(target, effect.Amount ?? 1, state);
                    break;
                case Op.BURY:
                    ApplyBury(target, effect.Amount ?? 1, state);
                    break;
                case Op.UNBURY:
                    ApplyUnbury(target, effect.Amount ?? 1, state);
                    break;
                case Op.SUMMON:
                    ApplySummon(target, effect.TokenId ?? "tst_token", source, state);
                    break;
                case Op.GRANT_KEY:
                    ApplyGrantKey(target, effect.Keyword ?? "");
                    break;
                case Op.REMOVE_KEY:
                    ApplyRemoveKey(target, effect.Keyword ?? "");
                    break;
                case Op.SILENCE:
                    ApplySilence(target);
                    break;
                case Op.BOUNCE:
                    ApplyBounce(target, state);
                    break;
                case Op.ATTUNE:
                    ApplyAttune(target, effect.Amount ?? 1);
                    break;
                case Op.MOVE_LANE:
                    ApplyMoveLane(target, source, state);
                    break;
                case Op.IDENTIFY:
                    ApplyIdentify(target);
                    break;
                case Op.GAIN_VIGOR:
                    ApplyGainVigor(target, effect.Amount ?? 0);
                    break;
                case Op.LOSE_VIGOR:
                    ApplyGainVigor(target, -(effect.Amount ?? 0));
                    break;
                case Op.COPY:
                    ApplyCopy(target, source, state);
                    break;
                case Op.SET_STAT:
                    ApplySetStat(target, effect.Attack, effect.Vigor);
                    break;
                case Op.REFRESH:
                    ApplyRefresh(target);
                    break;
                case Op.SUPPRESS:
                    ApplySuppress(target, effect.Amount ?? 1, source, state);
                    break;
                case Op.ADD_CHARGE:
                    ApplyAddCharge(target, effect.Amount ?? 1, state);
                    break;
                case Op.SET_PREY:
                    ApplySetPrey(target, source, state);
                    break;
                case Op.REVIVE_TOKEN:
                    ApplyReviveToken(target, effect.Keyword ?? "artf_skeleton", source, state);
                    break;
            }
        }
    }

    // ——— Effect implementations ———

    private static void ApplyDamage(ResolvedTarget target, int amount, GameState state)
    {
        if (target is CreatureTarget ct)
        {
            ct.Card.Damage += amount;
            if (ct.Card.CurrentVigor <= 0)
                KillCreature(ct.Card, state);
        }
        else if (target is PlayerTarget pt)
        {
            pt.Player.Vigor -= amount;
            if (pt.Player.Vigor <= 0)
            {
                state.IsGameOver = true;
                state.WinnerIndex = state.OpponentIndex(pt.Player.Index);
            }
        }
    }

    private static void ApplyHeal(ResolvedTarget target, int amount)
    {
        if (target is CreatureTarget ct)
        {
            // Reduce damage, but not below 0
            ct.Card.Damage = Math.Max(0, ct.Card.Damage - amount);
        }
        else if (target is PlayerTarget pt)
        {
            pt.Player.Vigor = Math.Min(pt.Player.MaxVigor, pt.Player.Vigor + amount);
        }
    }

    private static void ApplyBuff(ResolvedTarget target, int attack, int vigor, Duration? duration)
    {
        if (target is CreatureTarget ct)
        {
            ct.Card.AttackModifier += attack;
            ct.Card.VigorModifier += vigor;
        }
        // Duration tracking (PERMANENT/THIS_TURN) would be handled by a future buff system
    }

    private static void ApplyDestroy(ResolvedTarget target, GameState state)
    {
        if (target is CreatureTarget ct)
        {
            KillCreature(ct.Card, state);
        }
    }

    private static void ApplyDraw(ResolvedTarget target, int amount, GameState state)
    {
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null) return;

        for (int i = 0; i < amount; i++)
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
    }

    private static void ApplyDiscard(ResolvedTarget target, int amount, GameState state)
    {
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null) return;

        for (int i = 0; i < amount && player.Hand.Count > 0; i++)
        {
            var discarded = player.Hand[^1];
            player.Hand.RemoveAt(player.Hand.Count - 1);
            discarded.Zone = Zone.Discard;
            player.Discard.Add(discarded);
        }
    }

    private static void ApplyExcavate(ResolvedTarget target, int amount, GameState state)
    {
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null || amount <= 0) return;

        // Look at top N cards, put 1 in hand, bury the rest
        int canSee = Math.Min(amount, player.Deck.Count);
        if (canSee < 1) return;

        // Pick the top card (deterministic: always pick the first)
        var chosen = player.Deck[0];
        player.Deck.RemoveAt(0);
        chosen.Zone = Zone.Hand;
        player.Hand.Add(chosen);

        // Bury the rest
        int toBury = Math.Min(canSee - 1, player.Deck.Count);
        for (int i = 0; i < toBury && player.Deck.Count > 0; i++)
        {
            var buried = player.Deck[0];
            player.Deck.RemoveAt(0);
            buried.Zone = Zone.Barrow;
            player.Barrow.Add(buried);
        }
    }

    private static void ApplyBury(ResolvedTarget target, int amount, GameState state)
    {
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null) return;

        for (int i = 0; i < amount && player.Deck.Count > 0; i++)
        {
            var buried = player.Deck[0];
            player.Deck.RemoveAt(0);
            buried.Zone = Zone.Barrow;
            player.Barrow.Add(buried);
        }
    }

    private static void ApplyUnbury(ResolvedTarget target, int amount, GameState state)
    {
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null) return;

        for (int i = 0; i < amount && player.Barrow.Count > 0; i++)
        {
            var unb = player.Barrow[^1];
            player.Barrow.RemoveAt(player.Barrow.Count - 1);
            unb.Zone = Zone.Hand;
            player.Hand.Add(unb);
        }
    }

    private static void ApplySummon(ResolvedTarget target, string tokenId, CardInstance source, GameState state)
    {
        // Target should be a player or creature indicating whose board to summon on
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null) return;

        // Find an empty lane
        for (int i = 0; i < 5; i++)
        {
            if (player.Lanes[i].Occupant is null)
            {
                var token = new CardInstance(
                    state.NextInstanceId++,
                    tokenId,
                    player.Index)
                {
                    Zone = Zone.Lane,
                    LaneIndex = i,
                    CardType = CardType.TOKEN,
                    BaseAttack = 0,
                    BaseVigor = 1,
                    Cost = 0,
                    IsExhausted = true
                };
                player.Lanes[i].Occupant = token;
                return;
            }
        }
        // No empty lane — summon fails silently
    }

    private static void ApplyGrantKey(ResolvedTarget target, string keyword)
    {
        if (target is CreatureTarget ct && !string.IsNullOrEmpty(keyword))
            ct.Card.GrantedKeywords.Add(keyword);
    }

    private static void ApplyRemoveKey(ResolvedTarget target, string keyword)
    {
        if (target is CreatureTarget ct && !string.IsNullOrEmpty(keyword))
            ct.Card.RemovedKeywords.Add(keyword);
    }

    private static void ApplySilence(ResolvedTarget target)
    {
        if (target is CreatureTarget ct)
        {
            ct.Card.GrantedKeywords.Clear();
            foreach (var kw in ct.Card.Keywords)
                ct.Card.RemovedKeywords.Add(kw);
        }
    }

    private static void ApplyBounce(ResolvedTarget target, GameState state)
    {
        if (target is CreatureTarget ct && ct.Card.Zone == Zone.Lane)
        {
            var lane = state.Player(ct.PlayerIndex).Lanes[ct.LaneIndex];
            lane.Occupant = null;
            ct.Card.Zone = Zone.Hand;
            ct.Card.LaneIndex = null;
            state.Player(ct.PlayerIndex).Hand.Add(ct.Card);
        }
    }

    private static void ApplyAttune(ResolvedTarget target, int amount)
    {
        if (target is PlayerTarget pt)
        {
            int newMax = Math.Min(10, pt.Player.AttunementMax + amount);
            pt.Player.AttunementMax = newMax;
            pt.Player.Attunement = newMax;
        }
    }

    private static void ApplyMoveLane(ResolvedTarget target, CardInstance source, GameState state)
    {
        // Moves the source card to the first empty lane on its controller's board
        if (source.Zone != Zone.Lane || source.LaneIndex is not int srcIdx)
            return;

        var sourcePlayer = state.Player(source.Controller);
        var srcLane = sourcePlayer.Lanes[srcIdx];

        for (int i = 0; i < 5; i++)
        {
            if (i == srcIdx) continue;
            if (sourcePlayer.Lanes[i].Occupant is null)
            {
                srcLane.Occupant = null;
                sourcePlayer.Lanes[i].Occupant = source;
                source.LaneIndex = i;
                return;
            }
        }
    }

    private static void ApplyIdentify(ResolvedTarget target)
    {
        if (target is CreatureTarget ct)
            ct.Card.IsIdentified = true;
    }

    private static void ApplyGainVigor(ResolvedTarget target, int amount)
    {
        if (target is PlayerTarget pt)
        {
            pt.Player.MaxVigor = Math.Max(1, pt.Player.MaxVigor + amount);
            pt.Player.Vigor = Math.Max(0, pt.Player.Vigor + amount);
        }
    }

    private static void ApplyCopy(ResolvedTarget target, CardInstance source, GameState state)
    {
        if (target is CreatureTarget ct)
        {
            // Create a copy of the source card's attributes at the target's position
            var copy = new CardInstance(
                state.NextInstanceId++,
                source.CardDefId,
                ct.PlayerIndex)
            {
                Zone = Zone.Lane,
                LaneIndex = ct.LaneIndex,
                CardType = source.CardType,
                Cost = source.Cost,
                Strata = source.Strata,
                BaseAttack = source.BaseAttack,
                BaseVigor = source.BaseVigor,
                Damage = 0,
                AttackModifier = source.AttackModifier,
                VigorModifier = source.VigorModifier,
                IsExhausted = source.IsExhausted,
                HasAttackedThisTurn = source.HasAttackedThisTurn,
                Keywords = new List<string>(source.Keywords)
            };
            state.Player(ct.PlayerIndex).Lanes[ct.LaneIndex].Occupant = copy;
        }
    }

    private static void ApplySetStat(ResolvedTarget target, int? attack, int? vigor)
    {
        if (target is CreatureTarget ct)
        {
            if (attack.HasValue)
                ct.Card.BaseAttack = attack.Value;
            if (vigor.HasValue)
                ct.Card.BaseVigor = vigor.Value;
        }
    }

    private static void ApplyRefresh(ResolvedTarget target)
    {
        if (target is CreatureTarget ct)
        {
            ct.Card.IsExhausted = false;
            ct.Card.HasAttackedThisTurn = false;
        }
    }

    // ——— Artifact-specific ops ———

    /// <summary>
    /// SUPPRESS: Suppress the enemy player's Artifacts for N turns.
    /// Target should be PLAYER_ENEMY or scope that resolves to the opponent.
    /// </summary>
    private static void ApplySuppress(ResolvedTarget target, int turns, CardInstance source, GameState state)
    {
        PlayerState? targetPlayer = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (targetPlayer is null || turns <= 0) return;

        foreach (var slot in targetPlayer.ArtifactSlots)
        {
            if (slot.Occupant is not null)
            {
                slot.ApplySuppression(turns, $"artf_effect_{source.InstanceId}");
                TriggerBus.Fire(state, Trigger.ON_ARTIFACT_SUPPRESS, targetPlayer.Index);
            }
        }
    }

    /// <summary>
    /// ADD_CHARGE: Add N Charges to an Artifact slot or to the player's active Artifact.
    /// Target can be PLAYER_SELF (adds to all slots) or ALLY_CREATURE + filter for a specific slot.
    /// </summary>
    private static void ApplyAddCharge(ResolvedTarget target, int amount, GameState state)
    {
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null || amount <= 0) return;

        foreach (var slot in player.ArtifactSlots)
        {
            if (slot.MaxCharges > 0 && slot.Occupant is not null && !slot.IsSuppressed)
            {
                int before = slot.Charges;
                slot.AddCharges(amount);
                TriggerBus.Fire(state, Trigger.ON_CHARGE_GAINED, player.Index);

                // Fire ON_CHARGE_FULL if charges just hit max
                if (slot.Charges == slot.MaxCharges && before < slot.MaxCharges)
                {
                    TriggerBus.Fire(state, Trigger.ON_CHARGE_FULL, player.Index);
                }
            }
        }
    }

    /// <summary>
    /// SET_PREY: Mark an enemy creature as Prey for the given player (Ranger mechanic).
    /// Target should be a creature target.
    /// </summary>
    private static void ApplySetPrey(ResolvedTarget target, CardInstance source, GameState state)
    {
        if (target is CreatureTarget ct)
        {
            var player = state.Player(source.Controller);
            player.PreyTargetId = ct.Card.InstanceId;
            TriggerBus.Fire(state, Trigger.ON_PREY_MARKED, player.Index);
        }
    }

    /// <summary>
    /// REVIVE_TOKEN: Revive the most recently deceased creature as a token.
    /// Creates a 1/1 token in the first empty lane.
    /// </summary>
    private static void ApplyReviveToken(ResolvedTarget target, string tokenId, CardInstance source, GameState state)
    {
        PlayerState? player = target switch
        {
            PlayerTarget pt => pt.Player,
            CreatureTarget ct => state.Player(ct.PlayerIndex),
            _ => null
        };
        if (player is null) return;

        // Find first empty lane
        for (int i = 0; i < 5; i++)
        {
            if (player.Lanes[i].Occupant is null)
            {
                var token = new CardInstance(
                    state.NextInstanceId++,
                    tokenId,
                    player.Index)
                {
                    Zone = Zone.Lane,
                    LaneIndex = i,
                    CardType = CardType.TOKEN,
                    BaseAttack = 1,
                    BaseVigor = 1,
                    Cost = 0,
                    IsExhausted = true
                };
                player.Lanes[i].Occupant = token;
                return;
            }
        }
        // No empty lane — revive fails silently
    }

    // ——— Helpers ———

    private static void KillCreature(CardInstance card, GameState state)
    {
        if (card.Zone != Zone.Lane) return;
        var owner = state.Player(card.Controller);
        var lane = owner.Lanes[card.LaneIndex ?? 0];

        // Increment death counter
        state.CreatureDiedThisTurn++;
        state.LastDeathPlayerIndex = card.Controller;

        // Check Unearth first
        if (!KeywordHandlers.OnDeath(card, owner))
        {
            lane.Occupant = null;
            card.Zone = Zone.Discard;
            owner.Discard.Add(card);
        }
        else
        {
            lane.Occupant = null;
        }
        TriggerBus.FireDeathEvents(state, card, owner.Index);
        // Fire global ON_CREATURE_DIES for all abilities (Artifact triggers, etc.)
        TriggerBus.Fire(state, Trigger.ON_CREATURE_DIES, owner.Index);
    }
}