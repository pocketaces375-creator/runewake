using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Client;

/// <summary>
/// FABLE-DROP-1: a new card drop reaches players who already have a save. A fresh profile gets every card
/// (Main.cs), but an existing collection never heard of cards added after it was made. Once per save
/// slot, each drop grants one copy of every card in it — two of the cards built for the player's own class.
/// Which slots have had which drop is remembered in user://drop_grants.json, outside the save database, so
/// no save migration is needed.
/// </summary>
public static class DropGrants
{
    private const string FilePath = "user://drop_grants.json";

    /// <summary>The drops, oldest first: the card set name, and the class each card was built for.</summary>
    private static readonly string[] Drops = { "class_drop_1" };

    public static void Ensure(ProgressionState progression, int saveSlot, string classId)
    {
        try
        {
            var granted = Load();
            string key = saveSlot.ToString();
            if (!granted.TryGetValue(key, out var have)) granted[key] = have = new List<string>();
            bool changed = false;
            foreach (var drop in Drops)
            {
                if (have.Contains(drop)) continue;
                var cards = CardRegistry.GetAll()
                    .Where(c => c.Set == drop && c.Type != CardType.TOKEN)
                    .OrderBy(c => c.Id, System.StringComparer.Ordinal)
                    .ToList();
                if (cards.Count == 0) continue;     // content not loaded yet: try again next time
                var mine = Synergy.Shelf(classId ?? "", cards).Take(7).Select(c => c.Id).ToHashSet();
                foreach (var c in cards)
                {
                    int want = mine.Contains(c.Id) ? 2 : 1;
                    int owned = progression.Collection.TryGetValue(c.Id, out var n) ? n : 0;
                    if (owned < want) progression.AddCard(c.Id, want - owned);
                }
                have.Add(drop);
                changed = true;
                GD.Print($"[DropGrants] slot {key}: granted {drop} ({cards.Count} cards, 2× for {classId})");
            }
            if (changed)
            {
                Save(granted);
                CampaignContext.SaveManager.Save();
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[DropGrants] {ex.Message}");
        }
    }

    private static Dictionary<string, List<string>> Load()
    {
        if (!Godot.FileAccess.FileExists(FilePath)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(Godot.FileAccess.GetFileAsString(FilePath)) ?? new();
        }
        catch { return new(); }
    }

    private static void Save(Dictionary<string, List<string>> granted)
    {
        using var f = Godot.FileAccess.Open(FilePath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(granted));
    }
}
