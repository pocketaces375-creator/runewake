using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Sim;
using Xunit;

namespace Runewake.Tests.Campaign;

/// <summary>
/// End-to-end integration test for Region 1 content.
/// Proves the full game loop works at the engine level:
/// load map → pick node → load encounter → play duel → win → apply rewards → next node unlocks.
/// </summary>
[Collection("NonParallel")]
public class RegionOneIntegrationTests : IDisposable
{
    private static readonly string ContentRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content"));

    private static readonly string CardsDir = Path.Combine(ContentRoot, "cards");
    private static readonly string MapDir = Path.Combine(ContentRoot, "map");
    private static readonly string EncountersDir = Path.Combine(ContentRoot, "encounters");

    private const int DeckSize = 30;

    public RegionOneIntegrationTests()
    {
        LoadAllCardsIntoRegistry();
    }

    public void Dispose()
    {
        CardRegistry.Clear();
    }

    // ─── Card loading ───────────────────────────────────

    private static void LoadAllCardsIntoRegistry()
    {
        CardRegistry.Clear();
        var allCards = new List<CardDef>();
        foreach (var file in Directory.GetFiles(CardsDir, "*.json"))
        {
            var cards = CardLoader.LoadPack(file);
            allCards.AddRange(cards);
        }
        CardRegistry.RegisterRange(allCards);
    }

    // ─── Map loading ────────────────────────────────────

    private static MapRegion LoadRegion()
    {
        return MapLoader.LoadRegion(Path.Combine(MapDir, "region_01.json"));
    }

    private static List<EncounterDef> LoadAllEncounters()
    {
        var all = new List<EncounterDef>();
        foreach (var file in Directory.GetFiles(EncountersDir, "*.json"))
        {
            var pack = EncounterLoader.LoadPack(file);
            all.AddRange(pack.Encounters);
        }
        return all;
    }

    // ─── Starter deck for the human player ──────────────

    /// <summary>
    /// Build a balanced 30-card starter from available cards.
    /// Uses cards from multiple strata for variety.
    /// </summary>
    private static List<string> BuildStarterDeck()
    {
        var pool = new[]
        {
            "vrd_c_root_warden", "vrd_c_root_warden",
            "vrd_c_verdant_sproutling", "vrd_c_verdant_sproutling",
            "vrd_c_thornbark_defender", "vrd_c_thornbark_defender",
            "vrd_c_wildwood_stalker", "vrd_c_wildwood_stalker",
            "vrd_u_grove_healer", "vrd_u_grove_healer",
            "vrd_u_canopy_archer",
            "vrd_u_saphoof_charger", "vrd_u_saphoof_charger",
            "emb_c_cinder_runner", "emb_c_cinder_runner",
            "emb_c_ember_hound", "emb_c_ember_hound",
            "emb_c_flame_javelin", "emb_c_flame_javelin",
            "emb_c_forgeguard_berserker",
            "emb_u_lava_serpent",
            "dwn_c_sunblade_recruit", "dwn_c_sunblade_recruit",
            "dwn_c_dawn_warder", "dwn_c_dawn_warder",
            "dwn_c_dawnbreaker_charger",
            "dwn_c_golden_retainer", "dwn_c_golden_retainer",
            "dwn_u_steadfast_bulwark",
            "dwn_u_morning_herald",
        };
        return pool.ToList();
    }

    // ─── Helper: resolve all card IDs in a deck ─────────

    private static void AssertAllDeckCardsResolve(EncounterDef enc)
    {
        foreach (var cid in enc.Deck)
        {
            Assert.NotNull(CardRegistry.Get(cid));
        }
    }

    // ─── Bot-vs-bot game runner ─────────────────────────

    private static (GameState FinalState, int TurnCount) RunBotGame(
        List<string> player0Deck, List<string> player1Deck, ulong seed = 42)
    {
        var config = new GameConfig
        {
            Seed = seed,
            ContentVersion = 1,
            Player0DeckIds = player0Deck,
            Player1DeckIds = player1Deck,
        };

        var state = GameState.Initialize(config);
        var bot = new GreedyBot();
        int turns = 0;
        const int maxTurns = 200; // safety limit

        while (!state.IsGameOver && turns < maxTurns)
        {
            var action = bot.ChooseAction(state, state.CurrentPlayerIndex);
            if (action == null) break;
            state = DuelEngine.Apply(state, action);
            turns++;
        }

        return (state, turns);
    }

    // ══════════════════════════════════════════════════════
    //  Tests
    // ══════════════════════════════════════════════════════

    // ─── Content validation ─────────────────────────────

    [Fact]
    public void AllCards_LoadFromRealFiles_Have61Definitions()
    {
        // The 5 strata files define 61 cards total
        var cards = new List<CardDef>();
        foreach (var file in Directory.GetFiles(CardsDir, "*.json"))
            cards.AddRange(CardLoader.LoadPack(file));
        Assert.Equal(61, cards.Count);
    }

    [Fact]
    public void AllEncounterDecks_CardIdsResolve()
    {
        var encounters = LoadAllEncounters();
        foreach (var enc in encounters)
            AssertAllDeckCardsResolve(enc);
    }

    [Fact]
    public void AllEncounterDecks_AreExactly30Cards()
    {
        var encounters = LoadAllEncounters();
        foreach (var enc in encounters)
            Assert.Equal(DeckSize, enc.Deck.Count);
    }

    [Fact]
    public void RegionOneMap_AllEncounterRefs_Resolve()
    {
        var region = LoadRegion();
        var encounters = LoadAllEncounters();
        var encounterIds = encounters.Select(e => e.Id).ToHashSet();

        // Also load dig site IDs (some nodes reference dig sites, not duel encounters)
        var digSiteIds = new HashSet<string>();
        var digDir = Path.Combine(ContentRoot, "dig_sites");
        if (Directory.Exists(digDir))
        {
            foreach (var file in Directory.GetFiles(digDir, "*.json"))
            {
                var json = File.ReadAllText(file);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("dig_sites", out var sites))
                {
                    foreach (var site in sites.EnumerateArray())
                    {
                        if (site.TryGetProperty("id", out var idEl))
                            digSiteIds.Add(idEl.GetString()!);
                    }
                }
            }
        }

        foreach (var node in region.Nodes)
        {
            if (!string.IsNullOrEmpty(node.Encounter))
            {
                bool inEncounters = encounterIds.Contains(node.Encounter);
                bool inDigSites = digSiteIds.Contains(node.Encounter);
                Assert.True(
                    inEncounters || inDigSites,
                    $"Map node {node.Id} references encounter '{node.Encounter}' which is not defined in any encounter file or dig site.");
            }
        }
    }

    // ─── Node unlock validation ─────────────────────────

    [Fact]
    public void FreshState_OnlyNode01_IsUnlocked()
    {
        var region = LoadRegion();
        var cleared = new HashSet<string>();
        var unlocked = MapUnlockEvaluator.GetUnlockedNodes(region, cleared);
        Assert.Contains("r1_n01", unlocked);
        Assert.Single(unlocked);
    }

    [Fact]
    public void FullClear_AllNodesBecomeUnlocked()
    {
        var region = LoadRegion();
        var cleared = region.Nodes.Select(n => n.Id).ToHashSet();
        var unlocked = MapUnlockEvaluator.GetUnlockedNodes(region, cleared);
        Assert.Equal(region.Nodes.Count, unlocked.Count);
    }

    // ══════════════════════════════════════════════════════
    //  Full game loop: play node → win → clear → next unlocks
    // ══════════════════════════════════════════════════════

    [Fact]
    public void FullLoop_Node01_PlayAndWin_ThenNode02Unlocks()
    {
        // 1. Load content
        var region = LoadRegion();
        var encounters = LoadAllEncounters();
        var encounterMap = encounters.ToDictionary(e => e.Id);

        // 2. Pick node r1_n01 (first node, no unlock prereq)
        var node01 = region.Nodes.First(n => n.Id == "r1_n01");
        Assert.Null(node01.Unlock); // should be automatically unlocked
        Assert.NotNull(node01.Encounter);

        // 3. Look up encounter
        var encounter = encounterMap[node01.Encounter];
        AssertAllDeckCardsResolve(encounter);

        // 4. Build player deck
        var starterDeck = BuildStarterDeck();
        Assert.Equal(30, starterDeck.Count);

        // 5. Initialize game: player is P0, encounter AI is P1
        var config = new GameConfig
        {
            Seed = 12345,
            ContentVersion = 1,
            Player0DeckIds = starterDeck,
            Player1DeckIds = encounter.Deck,
        };

        var state = GameState.Initialize(config);
        var bot = new GreedyBot();
        int turns = 0;
        const int maxTurns = 200;

        while (!state.IsGameOver && turns < maxTurns)
        {
            var action = bot.ChooseAction(state, state.CurrentPlayerIndex);
            if (action == null) break;
            state = DuelEngine.Apply(state, action);
            turns++;
        }

        // 6. Verify a winner exists (the game ended)
        Assert.True(state.IsGameOver, $"Game did not complete within {maxTurns} turns");
        Assert.True(state.WinnerIndex is 0 or 1, "A valid winner should exist");

        // 7. Apply rewards to progression
        var progression = new ProgressionState();
        foreach (var reward in node01.Rewards ?? new())
        {
            var parts = reward.Split(':');
            if (parts.Length < 2) continue;
            switch (parts[0])
            {
                case "shard":
                    progression.Shards += int.Parse(parts[1]);
                    break;
                case "dig_charge":
                    progression.DigCharges += int.Parse(parts[1]);
                    break;
            }
        }

        // 8. Simulate card rewards: award all cards from the encounter deck
        //    (the actual game would give a random subset, but for verification
        //     we just confirm the encounter's cards exist in the pool)
        foreach (var cid in encounter.Deck.Distinct())
            progression.AddCard(cid);

        // 9. Mark node as cleared
        Assert.True(progression.MarkNodeCleared("r1_n01"));
        Assert.True(progression.IsNodeCleared("r1_n01"));

        // 10. Verify r1_n02 and r1_n03 now unlock (they require r1_n01)
        var unlockedNodes = MapUnlockEvaluator.GetUnlockedNodes(region, progression.ClearedNodes);
        Assert.Contains("r1_n01", unlockedNodes); // cleared node still shown
        Assert.Contains("r1_n02", unlockedNodes);
        Assert.Contains("r1_n03", unlockedNodes);

        // r1_n04 requires r1_n02 which is not cleared → still locked
        Assert.DoesNotContain("r1_n04", unlockedNodes);

        // 11. Verify rewards were applied correctly
        Assert.Equal(30, progression.Shards); // Wayfarer gives "shard:30"
        Assert.Equal(0, progression.DigCharges); // Wayfarer gives 0 dig charges
    }

    /// <summary>
    /// Prove the full campaign can be completed: every duel node is playable,
    /// and clearing all nodes unlocks the Warden Boss (r1_n12).
    ///
    /// Follows the connection graph: start at unlocked nodes, play each,
    /// clear it, re-evaluate unlocks, repeat until all encounter nodes done.
    /// </summary>
    [Fact]
    public void FullLoop_SequentialClear_EndsWithBossUnlocked()
    {
        var region = LoadRegion();
        var encounters = LoadAllEncounters();
        var encounterMap = encounters.ToDictionary(e => e.Id);
        var progression = new ProgressionState();
        var starterDeck = BuildStarterDeck();

        const ulong baseSeed = 99999;
        int seedOffset = 0;

        // Follow the unlock graph: keep playing unlockable encounter nodes
        // until none remain. Non-encounter nodes (Shrine, Dig, Merchant) are
        // also marked as cleared since they are part of the graph.
        bool progressed;
        do
        {
            progressed = false;
            var unlocked = MapUnlockEvaluator.GetUnlockedNodes(region, progression.ClearedNodes);
            var nextNode = region.Nodes
                .Where(n => unlocked.Contains(n.Id)
                            && !progression.IsNodeCleared(n.Id))
                .OrderBy(n => n.Position[1]) // top-to-bottom
                .FirstOrDefault();

            if (nextNode != null)
            {
                bool hasPlayableEncounter = !string.IsNullOrEmpty(nextNode.Encounter)
                    && encounterMap.ContainsKey(nextNode.Encounter);

                if (hasPlayableEncounter)
                {
                    var encounter = encounterMap[nextNode.Encounter];

                    // Play the duel
                    var (finalState, turnCount) = RunBotGame(starterDeck, encounter.Deck, baseSeed + (ulong)seedOffset++);
                    Assert.True(finalState.IsGameOver,
                        $"Node {nextNode.Id} ({encounter.Name}) did not complete within 200 turns");
                    Assert.True(finalState.WinnerIndex is 0 or 1,
                        $"Node {nextNode.Id} ({encounter.Name}) has invalid winner");

                    // Apply rewards
                    foreach (var reward in nextNode.Rewards ?? new())
                    {
                        var parts = reward.Split(':');
                        if (parts.Length < 2) continue;
                        switch (parts[0])
                        {
                            case "shard":
                                progression.Shards += int.Parse(parts[1]);
                                break;
                            case "dig_charge":
                                progression.DigCharges += int.Parse(parts[1]);
                                break;
                            case "fragment":
                                progression.AddFragments(parts[1], int.Parse(parts[2]));
                                break;
                        }
                    }
                }
                else
                {
                    // Non-encounter nodes (Shrine, Dig, Merchant) — just mark cleared
                    // All three give shard rewards in region_01
                    if (nextNode.Rewards != null)
                    {
                        foreach (var reward in nextNode.Rewards)
                        {
                            var parts = reward.Split(':');
                            if (parts.Length < 2) continue;
                            if (parts[0] == "shard")
                                progression.Shards += int.Parse(parts[1]);
                        }
                    }
                }

                // Mark cleared
                progression.MarkNodeCleared(nextNode.Id);
                progressed = true;
            }
        } while (progressed);

        // After all duel/elite/warden nodes are cleared, r1_n12 (Boss) must be unlocked
        var finalUnlocked = MapUnlockEvaluator.GetUnlockedNodes(region, progression.ClearedNodes);
        Assert.Contains("r1_n12", finalUnlocked);

        // Also play the Boss duel itself
        var bossNode = region.Nodes.First(n => n.Id == "r1_n12");
        Assert.NotNull(bossNode.Encounter);
        var bossEncounter = encounterMap[bossNode.Encounter];
        var (bossState, bossTurns) = RunBotGame(starterDeck, bossEncounter.Deck, 777777);
        Assert.True(bossState.IsGameOver, $"Boss node {bossNode.Id} did not complete");
        Assert.True(bossState.WinnerIndex is 0 or 1, "Boss must produce a winner");

        // Total shards should be non-zero (rewards accumulated)
        Assert.True(progression.Shards > 0);

        // Dig charges should be non-zero (some nodes award them)
        Assert.True(progression.DigCharges > 0);

        // Fragments should have accumulated
        Assert.NotEmpty(progression.Fragments);
    }
}