using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// FABLE-009: the opponent must never turn up carrying the player's own relics.
///
/// Reported from a real duel: a battlemage player met an opponent holding the
/// identical pair. The cause was DuelScene picking the opponent's class with
/// encounterId.GetHashCode() % 7 and then calling DefaultLoadoutFor, which
/// returns one fixed pair per class — so roughly one duel in seven was an exact
/// mirror, guaranteed and total for a battlemage because battlemage owns exactly
/// one artifact per slot pool.
///
/// These tests pin the three properties the fix has to keep: no overlap, ever;
/// the same seed replays the same opponent; and the variant artifacts are
/// actually reachable instead of the same seven pairs forever.
/// </summary>
[Collection("NonParallel")]
public class OpponentLoadoutTests
{
    private static readonly string ContentRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content"));

    private static readonly string ArtifactsDir = Path.Combine(ContentRoot, "artifacts");

    private static void LoadAllArtifacts()
    {
        ArtifactRegistry.Clear();
        ArtifactLoader.LoadPack(Path.Combine(ArtifactsDir, "launch_artifacts.json"));
        var variants = Path.Combine(ArtifactsDir, "variants");
        if (Directory.Exists(variants))
            ArtifactLoader.LoadAllVariants(variants);
    }

    private static IEnumerable<string> AllClasses() =>
        ArtifactRegistry.GetAll().Select(a => a.Class)
                        .Where(c => !string.IsNullOrEmpty(c))
                        .Distinct().OrderBy(c => c, StringComparer.Ordinal);

    [Fact]
    public void OpponentNeverShares_AnyArtifact_WithThePlayer()
    {
        try
        {
            LoadAllArtifacts();
            ulong[] seeds = { 1UL, 7UL, 42UL, 1234UL, 99999UL, 1UL << 31, ulong.MaxValue };
            string[] encounters = { "r1_duel_wayfarer", "r1_duel_thornbark", "r2_n04", "arena_warden_01" };

            int checks = 0;
            foreach (var playerClass in AllClasses())
            {
                var playerPair = ArtifactRegistry.DefaultLoadoutFor(playerClass);
                foreach (var encounterId in encounters)
                {
                    foreach (var seed in seeds)
                    {
                        var (_, opponent) = ArtifactRegistry.OpponentLoadout(
                            null, playerClass, playerPair, encounterId, seed);
                        checks++;

                        Assert.Equal(2, opponent.Length);
                        Assert.Equal(2, opponent.Distinct().Count());
                        Assert.Empty(opponent.Intersect(playerPair));
                    }
                }
            }
            Assert.True(checks > 100, $"expected a broad sweep, only ran {checks}");
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void Battlemage_TheWorstCase_StillGetsANonMirrorOpponent()
    {
        // battlemage owns exactly one artifact per slot pool, so under the old
        // code a battlemage-vs-battlemage roll could not avoid being identical.
        try
        {
            LoadAllArtifacts();
            var player = ArtifactRegistry.DefaultLoadoutFor("battlemage");
            Assert.Equal(2, player.Length);

            for (ulong seed = 0; seed < 64; seed++)
            {
                var (cls, opponent) = ArtifactRegistry.OpponentLoadout(
                    null, "battlemage", player, "r1_duel_wayfarer", seed);
                Assert.Empty(opponent.Intersect(player));
                Assert.NotEqual("battlemage", cls);
            }
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void ExplicitEncounterClass_IsHonoured_WhenItDoesNotMirrorThePlayer()
    {
        try
        {
            LoadAllArtifacts();
            var player = ArtifactRegistry.DefaultLoadoutFor("battlemage");
            var (cls, opponent) = ArtifactRegistry.OpponentLoadout(
                "warrior", "battlemage", player, "r1_duel_thornbark", 42UL);

            Assert.Equal("warrior", cls);
            Assert.All(opponent, id => Assert.Equal("warrior", ArtifactRegistry.Get(id)!.Class));
            Assert.Empty(opponent.Intersect(player));
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void ExplicitClassThatWouldMirror_StepsAsideRatherThanRepeatThePlayer()
    {
        // A battlemage player against an encounter the content marks "battlemage":
        // honouring it literally can only produce the player's own pair, so the
        // loadout must move to another class instead of handing back a mirror.
        try
        {
            LoadAllArtifacts();
            var player = ArtifactRegistry.DefaultLoadoutFor("battlemage");
            var (cls, opponent) = ArtifactRegistry.OpponentLoadout(
                "battlemage", "battlemage", player, "r1_duel_wayfarer", 7UL);

            Assert.Empty(opponent.Intersect(player));
            Assert.NotEqual("battlemage", cls);
            Assert.Equal(2, opponent.Length);
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void SameSeed_SameOpponent_DifferentSeed_DifferentOpponent()
    {
        try
        {
            LoadAllArtifacts();
            var player = ArtifactRegistry.DefaultLoadoutFor("battlemage");

            var first = ArtifactRegistry.OpponentLoadout(null, "battlemage", player, "r1_duel_wayfarer", 42UL);
            for (int i = 0; i < 5; i++)
            {
                var again = ArtifactRegistry.OpponentLoadout(null, "battlemage", player, "r1_duel_wayfarer", 42UL);
                Assert.Equal(first.ClassId, again.ClassId);
                Assert.Equal(first.Artifacts, again.Artifacts);
            }

            // Across a spread of seeds the opponent must actually vary, or the
            // seed is being ignored and every duel looks the same again.
            var distinct = new HashSet<string>();
            for (ulong seed = 0; seed < 50; seed++)
            {
                var (_, arts) = ArtifactRegistry.OpponentLoadout(
                    null, "battlemage", player, "r1_duel_wayfarer", seed);
                distinct.Add(string.Join("+", arts));
            }
            Assert.True(distinct.Count > 5,
                $"50 seeds produced only {distinct.Count} distinct loadouts — the seed is barely used");
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void VariantArtifacts_AreReachable_NotJustTheFirstOfEachPool()
    {
        // DefaultLoadoutFor takes the first entry of each slot pool, so before this
        // change only 14 of the 47 artifacts could ever appear on an opponent.
        try
        {
            LoadAllArtifacts();
            var player = ArtifactRegistry.DefaultLoadoutFor("battlemage");
            var seen = new HashSet<string>();
            foreach (var encounterId in new[] { "r1_duel_wayfarer", "r1_duel_thornbark", "r2_n04" })
                for (ulong seed = 0; seed < 100; seed++)
                {
                    var (_, arts) = ArtifactRegistry.OpponentLoadout(
                        null, "battlemage", player, encounterId, seed);
                    foreach (var a in arts) seen.Add(a);
                }

            var oldReach = AllClasses().SelectMany(ArtifactRegistry.DefaultLoadoutFor).Distinct().Count();
            Assert.True(seen.Count > oldReach,
                $"opponents can reach {seen.Count} artifacts; the old fixed pairs reached {oldReach}");
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void StableHash_IsActuallyStable()
    {
        // string.GetHashCode() is randomised per process in .NET, which is why the
        // old "stable class pick" re-rolled on every launch. These values are
        // hard-coded so a change to the hash has to be deliberate.
        Assert.Equal(ArtifactRegistry.StableHash("r1_duel_wayfarer"),
                     ArtifactRegistry.StableHash("r1_duel_wayfarer"));
        Assert.NotEqual(ArtifactRegistry.StableHash("r1_duel_wayfarer"),
                        ArtifactRegistry.StableHash("r1_duel_thornbark"));
        Assert.Equal(14695981039346656037UL, ArtifactRegistry.StableHash(""));
    }

    [Fact]
    public void EveryClassLoadout_ContainsOnlyThatClassesArtifacts()
    {
        // FABLE-011, from a real screenshot: the tutorial put a Warrior's Sword
        // and Shield in a Battlemage's hands because the script hard-coded them.
        // An Artifact belongs to a class; nothing may hand a player or an
        // opponent a kit that is not theirs. This pins BOTH sides.
        try
        {
            LoadAllArtifacts();
            foreach (var cls in AllClasses())
            {
                var pair = ArtifactRegistry.DefaultLoadoutFor(cls);
                Assert.Equal(2, pair.Length);
                foreach (var id in pair)
                {
                    var def = ArtifactRegistry.Get(id);
                    Assert.NotNull(def);
                    Assert.Equal(cls, def!.Class);
                }
            }
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void OpponentArtifacts_AlwaysMatch_TheClassTheyAreServedWith()
    {
        // The opponent's plates and the opponent's class name are shown side by
        // side in the duel HUD. If OpponentLoadout ever returned a class that
        // disagrees with its artifacts, the player would read a Necromancer
        // carrying a paladin's hammer.
        try
        {
            LoadAllArtifacts();
            foreach (var playerClass in AllClasses())
            {
                var player = ArtifactRegistry.DefaultLoadoutFor(playerClass);
                foreach (var encounterId in new[] { "r1_duel_wayfarer", "r1_duel_thornbark", "r2_n04" })
                    for (ulong seed = 0; seed < 24; seed++)
                    {
                        var (cls, arts) = ArtifactRegistry.OpponentLoadout(
                            null, playerClass, player, encounterId, seed);
                        foreach (var id in arts)
                        {
                            var def = ArtifactRegistry.Get(id);
                            Assert.NotNull(def);
                            Assert.Equal(cls, def!.Class);
                        }
                    }
            }
        }
        finally { ArtifactRegistry.Clear(); }
    }

    [Fact]
    public void TutorialScript_DoesNotHardCode_AClassOrItsArtifacts()
    {
        // The guided first duel must run with whatever class the player chose.
        // A non-empty "class" or "artifacts" here is the exact bug that shipped:
        // Warrior gear on a Battlemage, with the coach naming a sword.
        var scriptPath = Path.Combine(ContentRoot, "tutorial", "scripts", "first_duel.json");
        Assert.True(File.Exists(scriptPath), $"tutorial script not found at {scriptPath}");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(scriptPath));
        var root = doc.RootElement;

        Assert.True(string.IsNullOrEmpty(root.GetProperty("class").GetString()),
            "first_duel.json pins a class — the tutorial must use the player's own");
        Assert.Empty(root.GetProperty("artifacts").EnumerateArray());

        // And the copy must ask for the names rather than spelling them out.
        string all = File.ReadAllText(scriptPath);
        foreach (var banned in new[] { "the Sword and the Shield", "Sword and Shield" })
            Assert.DoesNotContain(banned, all, System.StringComparison.OrdinalIgnoreCase);
    }
}
