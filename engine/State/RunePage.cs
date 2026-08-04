using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;

namespace Runewake.Engine.State;

/// <summary>
/// A rune page configuration — the set of runes a player brings into a duel.
/// Layout: 9 offensive, 9 defensive, 9 utility, 3 mythic slots.
/// Total RP cost must not exceed the budget cap.
/// </summary>
public sealed class RunePage
{
    /// <summary>Maximum total RP allowed on one page.</summary>
    public const int MaxBudget = 100;

    /// <summary>Offensive rune slots (9).</summary>
    public RuneDef?[] OffensiveSlots { get; } = new RuneDef?[9];

    /// <summary>Defensive rune slots (9).</summary>
    public RuneDef?[] DefensiveSlots { get; } = new RuneDef?[9];

    /// <summary>Utility rune slots (9).</summary>
    public RuneDef?[] UtilitySlots { get; } = new RuneDef?[9];

    /// <summary>Mythic rune slots (3).</summary>
    public RuneDef?[] MythicSlots { get; } = new RuneDef?[3];

    /// <summary>Total RP cost of all equipped runes.</summary>
    public int TotalCost =>
        OffensiveSlots.Sum(s => s?.Cost ?? 0) +
        DefensiveSlots.Sum(s => s?.Cost ?? 0) +
        UtilitySlots.Sum(s => s?.Cost ?? 0) +
        MythicSlots.Sum(s => s?.Cost ?? 0);

    /// <summary>Number of runes currently equipped (across all slot types).</summary>
    public int EquippedCount =>
        OffensiveSlots.Count(s => s != null) +
        DefensiveSlots.Count(s => s != null) +
        UtilitySlots.Count(s => s != null) +
        MythicSlots.Count(s => s != null);

    /// <summary>
    /// Equip a rune into the first available slot of its type.
    /// Returns false if the slot type is full or the budget would be exceeded.
    /// </summary>
    public bool Equip(RuneDef rune)
    {
        if (rune.Cost < 1 || rune.Cost > 20) return false;
        if (TotalCost + rune.Cost > MaxBudget) return false;

        var slots = GetSlots(rune.SlotType);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = rune;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Remove a rune from a specific slot index.
    /// Returns false if the slot is already empty.
    /// </summary>
    public bool Unequip(RuneSlotType slotType, int slotIndex)
    {
        var slots = GetSlots(slotType);
        if (slotIndex < 0 || slotIndex >= slots.Length) return false;
        if (slots[slotIndex] == null) return false;

        slots[slotIndex] = null;
        return true;
    }

    /// <summary>
    /// Remove a specific rune by ID from whichever slot it occupies.
    /// Returns true if found and removed.
    /// </summary>
    public bool UnequipById(string runeId)
    {
        foreach (var slotType in new[] { RuneSlotType.OFFENSIVE, RuneSlotType.DEFENSIVE, RuneSlotType.UTILITY, RuneSlotType.MYTHIC })
        {
            var slots = GetSlots(slotType);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i]?.Id == runeId)
                {
                    slots[i] = null;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Check whether the page is within budget.
    /// </summary>
    public bool IsWithinBudget() => TotalCost <= MaxBudget;

    /// <summary>
    /// Get all equipped runes as a flat list.
    /// </summary>
    public List<RuneDef> GetAllEquipped()
    {
        var result = new List<RuneDef>(9 + 9 + 9 + 3);
        result.AddRange(OffensiveSlots.Where(s => s != null)!);
        result.AddRange(DefensiveSlots.Where(s => s != null)!);
        result.AddRange(UtilitySlots.Where(s => s != null)!);
        result.AddRange(MythicSlots.Where(s => s != null)!);
        return result;
    }

    private RuneDef?[] GetSlots(RuneSlotType type) => type switch
    {
        RuneSlotType.OFFENSIVE => OffensiveSlots,
        RuneSlotType.DEFENSIVE => DefensiveSlots,
        RuneSlotType.UTILITY => UtilitySlots,
        RuneSlotType.MYTHIC => MythicSlots,
        _ => System.Array.Empty<RuneDef?>()
    };
}