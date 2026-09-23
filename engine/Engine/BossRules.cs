using System;
using System.Collections.Generic;
using System.Globalization;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// FABLE-021: rules a boss (Tower Keeper, raid boss, world area boss) bends for the
/// whole fight. Authored as short strings on the encounter's "boss_rules" list so a
/// floor can be retuned from Supabase without an app update.
///
///   extra_draw            the boss draws one extra card at the start of each of its turns
///   regen:N               the boss heals N at the start of each of its turns (never above its max)
///   crumble:T:N           from turn T on, the challenger loses N Vigor at the start of each of their turns
///   challenger_vigor:N    the challenger starts the fight with N Vigor instead of the usual amount
///   boss_attunement:N     the boss gains N extra Attunement per turn (on top of the normal +1)
///
/// The owner is state.OpeningRuleOwner (the boss seat, 1 by default). Unknown rules are
/// ignored, so older app builds survive newer floor files.
/// </summary>
public static class BossRules
{
    public static readonly string[] Known = { "extra_draw", "regen", "crumble", "challenger_vigor", "boss_attunement" };

    public static (string id, int[] args) Parse(string rule)
    {
        var parts = rule.Split(':', StringSplitOptions.TrimEntries);
        var args = new int[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++)
            int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out args[i - 1]);
        return (parts[0].ToLowerInvariant(), args);
    }

    public static bool IsValid(string rule)
    {
        var (id, a) = Parse(rule);
        return id switch
        {
            "extra_draw" => a.Length == 0,
            "regen" or "challenger_vigor" or "boss_attunement" => a.Length == 1 && a[0] > 0,
            "crumble" => a.Length == 2 && a[0] > 0 && a[1] > 0,
            _ => false,
        };
    }

    /// <summary>What the player is told, e.g. on the Tower panel.</summary>
    public static string Describe(string rule)
    {
        var (id, a) = Parse(rule);
        int A(int i) => i < a.Length ? a[i] : 0;
        return id switch
        {
            "extra_draw" => "The boss draws an extra card every turn.",
            "regen" => $"The boss heals {A(0)} at the start of each of its turns.",
            "crumble" => $"From turn {A(0)}, the floor gives way: you lose {A(1)} Vigor at the start of each of your turns.",
            "challenger_vigor" => $"You start this fight with {A(0)} Vigor.",
            "boss_attunement" => $"The boss gains {A(0)} extra Attunement every turn.",
            _ => rule,
        };
    }

    /// <summary>Init-time rules. Called once from GameState.Initialize.</summary>
    public static void ApplyAtStart(GameState state)
    {
        int challenger = 1 - state.OpeningRuleOwner;
        foreach (var r in state.BossRules)
        {
            var (id, a) = Parse(r);
            if (id == "challenger_vigor" && a.Length == 1 && a[0] > 0)
            {
                state.Players[challenger].MaxVigor = a[0];
                state.Players[challenger].Vigor = a[0];
            }
        }
    }

    /// <summary>Turn-start rules for whoever's turn is beginning. `draw` is the engine's draw phase.</summary>
    public static void OnTurnStart(GameState state, PlayerState next, Action<PlayerState> draw)
    {
        int owner = state.OpeningRuleOwner;
        foreach (var r in state.BossRules)
        {
            if (state.IsGameOver) return;
            var (id, a) = Parse(r);
            if (next.Index == owner)
            {
                switch (id)
                {
                    case "extra_draw" when next.Deck.Count > 0:   // never into fatigue: an extra draw must not hurt the boss
                        draw(next);
                        break;
                    case "regen" when a.Length == 1 && a[0] > 0:
                        next.Vigor = Math.Min(next.MaxVigor, next.Vigor + a[0]);
                        break;
                    case "boss_attunement" when a.Length == 1 && a[0] > 0:
                        next.AttunementMax = Math.Min(10, next.AttunementMax + a[0]);
                        next.Attunement = next.AttunementMax;
                        break;
                }
            }
            else if (id == "crumble" && a.Length == 2 && state.TurnNumber >= a[0])
            {
                next.Vigor -= a[1];
                if (next.Vigor <= 0)
                {
                    state.IsGameOver = true;
                    state.WinnerIndex = owner;
                }
            }
        }
    }
}
