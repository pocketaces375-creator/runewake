using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// Applies a <see cref="RunePage"/> to a <see cref="GameState"/> at match start.
/// PASSIVE runes (unconditional) apply their effects immediately.
/// PASSIVE runes with conditions become ON_TURN_START triggers.
/// All other triggered runes register via RuneTokens on the trigger bus.
/// </summary>
public static class RuneInjector
{
    /// <summary>
    /// Inject all runes from the given page into the game state.
    /// Call after GameState.Initialize() but before the first turn begins.
    /// Only applies to player 0 (human). Player 1 (bot) gets no runes.
    /// </summary>
    public static void ApplyRunes(GameState state, RunePage page)
    {
        int playerIndex = 0;
        var player = state.Player(playerIndex);

        foreach (var rune in page.GetAllEquipped())
        {
            var ability = rune.Ability;

            if (ability.Trigger == Trigger.PASSIVE && ability.Condition == null)
            {
                // Unconditional PASSIVE — apply immediately
                ApplyPassiveEffect(state, player, ability, playerIndex);
            }
            else if (ability.Trigger == Trigger.PASSIVE && ability.Condition != null)
            {
                // Conditional PASSIVE — register as ON_TURN_START with the condition
                var modifiedAbility = new AbilityDef
                {
                    Trigger = Trigger.ON_TURN_START,
                    Condition = ability.Condition,
                    Effects = ability.Effects
                };
                CreateRuneToken(state, player, modifiedAbility, playerIndex);
            }
            else
            {
                // Triggered rune — register on the trigger bus via rune token
                CreateRuneToken(state, player, ability, playerIndex);
            }
        }
    }

    /// <summary>
    /// Apply the effects of a passive, unconditional ability directly.
    /// This simulates the effect as if it had fired once at match start.
    /// </summary>
    private static void ApplyPassiveEffect(GameState state, PlayerState player, AbilityDef ability, int controller)
    {
        var opponent = state.Player(state.OpponentIndex(controller));
        var source = CreateSentinelCard(state, playerIndex: controller);

        foreach (var effect in ability.Effects)
        {
            var targets = TargetResolver.Resolve(
                effect.Target ?? new TargetDef { Scope = Scope.NONE },
                source,
                player,
                opponent,
                state);
            EffectExecutor.Execute(effect, source, state, targets);
        }
    }

    /// <summary>
    /// Create a virtual rune token card and add it to the player's RuneTokens list.
    /// The token holds the rune's ability and is collected by the trigger bus.
    /// </summary>
    private static void CreateRuneToken(GameState state, PlayerState player, AbilityDef ability, int playerIndex)
    {
        var token = new CardInstance(state.NextInstanceId++, $"rune_token_{player.RuneTokens.Count}", playerIndex)
        {
            CardType = CardType.TOKEN,
            Zone = Zone.RemovedFromGame,
            LaneIndex = -1,
        };
        token.Abilities.Add(new AbilityDef
        {
            Trigger = ability.Trigger,
            Condition = ability.Condition,
            ActivationCost = ability.ActivationCost,
            Effects = ability.Effects.Select(e => new EffectDef
            {
                Op = e.Op,
                Target = e.Target != null ? new TargetDef
                {
                    Scope = e.Target.Scope,
                    Filter = e.Target.Filter,
                    Count = e.Target.Count
                } : null,
                Amount = e.Amount,
                Attack = e.Attack,
                Vigor = e.Vigor,
                Keyword = e.Keyword,
                TokenId = e.TokenId,
                Duration = e.Duration
            }).ToList()
        });
        player.RuneTokens.Add(token);
    }

    /// <summary>
    /// Create a sentinel card for passive ability effect execution.
    /// The sentinel lives off-board and is only used during initialization.
    /// </summary>
    private static CardInstance CreateSentinelCard(GameState state, int playerIndex)
    {
        return new CardInstance(state.NextInstanceId++, "rune_passive_sentinel", playerIndex)
        {
            CardType = CardType.TOKEN,
            Zone = Zone.RemovedFromGame,
            LaneIndex = -1,
        };
    }
}