using System;
using System.IO;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Cards;

/// <summary>
/// Tests for dig site data models, loader, and runtime state.
/// </summary>
[Collection("NonParallel")] // shared static CardRegistry not needed, but follows pattern
public class DigSiteTests
{
    private const string TestJson = @"{
        ""dig_sites"": [
            {
                ""id"": ""test_dig"",
                ""name"": ""Test Site"",
                ""description"": ""A test dig site."",
                ""rows"": 3,
                ""cols"": 3,
                ""strikes"": 3,
                ""headline_threshold"": 2,
                ""headline_reward"": ""relic:test_relic"",
                ""tiles"": [
                    { ""type"": ""SHARD"", ""value"": ""10"" },
                    { ""type"": ""EMPTY"", ""value"": null },
                    { ""type"": ""RUNE_FRAGMENT"", ""value"": ""ember:1"" },
                    { ""type"": ""CODEX_PAGE"", ""value"": ""r1_test"" },
                    { ""type"": ""EMPTY"", ""value"": null },
                    { ""type"": ""SHARD"", ""value"": ""20"" },
                    { ""type"": ""RELIC"", ""value"": ""r1_amber_shard"" },
                    { ""type"": ""SHARD"", ""value"": ""5"" },
                    { ""type"": ""EMPTY"", ""value"": null }
                ]
            }
        ]
    }";

    // ─── Loader tests ───

    [Fact]
    public void LoadPackFromString_DeserializesCorrectly()
    {
        var pack = DigSiteLoader.LoadPackFromString(TestJson);
        Assert.Single(pack.DigSites);
    }

    [Fact]
    public void LoadPackFromString_HasCorrectFieldValues()
    {
        var site = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        Assert.Equal("test_dig", site.Id);
        Assert.Equal("Test Site", site.Name);
        Assert.Equal("A test dig site.", site.Description);
        Assert.Equal(3, site.Rows);
        Assert.Equal(3, site.Cols);
        Assert.Equal(3, site.Strikes);
        Assert.Equal(2, site.HeadlineThreshold);
        Assert.Equal("relic:test_relic", site.HeadlineReward);
    }

    [Fact]
    public void LoadPackFromString_HasCorrectTileCount()
    {
        var site = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        Assert.Equal(9, site.Tiles.Count); // 3x3
    }

    [Fact]
    public void LoadPackFromString_HasCorrectTileTypes()
    {
        var tiles = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0].Tiles;
        Assert.Equal(DigRewardType.SHARD, tiles[0].Type);
        Assert.Equal("10", tiles[0].Value);
        Assert.Equal(DigRewardType.EMPTY, tiles[1].Type);
        Assert.Null(tiles[1].Value);
        Assert.Equal(DigRewardType.RUNE_FRAGMENT, tiles[2].Type);
        Assert.Equal("ember:1", tiles[2].Value);
        Assert.Equal(DigRewardType.CODEX_PAGE, tiles[3].Type);
        Assert.Equal("r1_test", tiles[3].Value);
        Assert.Equal(DigRewardType.RELIC, tiles[6].Type);
    }

    [Fact]
    public void LoadFromFile_WithValidPath_DeserializesCorrectly()
    {
        // Write test JSON to temp file
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, TestJson);
            var pack = DigSiteLoader.LoadPack(path);
            Assert.Single(pack.DigSites);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_ThrowsOnInvalidPath()
    {
        // Use a path in /tmp (which exists) with a nonexistent filename
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".json");
        Assert.Throws<FileNotFoundException>(() =>
            DigSiteLoader.LoadPack(path));
    }

    // ─── DigState tests ───

    [Fact]
    public void DigState_FromDef_CreatesCorrectState()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        Assert.Equal("test_dig", state.DigSiteId);
        Assert.Equal(3, state.StrikesRemaining);
        Assert.Equal(9, state.TilesRevealed.Length);
        Assert.All(state.TilesRevealed, revealed => Assert.False(revealed));
        Assert.Equal(0, state.TilesCleared);
        Assert.False(state.HeadlineClaimed);
        Assert.Empty(state.RewardsEarned);
        Assert.False(state.IsComplete);
    }

    [Fact]
    public void ApplyStrike_RevealsTileAndDecrementsStrikes()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        var reward = state.ApplyStrike(0, siteDef);

        Assert.NotNull(reward);
        Assert.Equal(DigRewardType.SHARD, reward!.Type);
        Assert.Equal("10", reward.Value);
        Assert.True(state.TilesRevealed[0]);
        Assert.Equal(2, state.StrikesRemaining);
        Assert.Equal(1, state.TilesCleared);
        Assert.Single(state.RewardsEarned);
    }

    [Fact]
    public void ApplyStrike_OnAlreadyRevealedTile_ReturnsNull()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        state.ApplyStrike(0, siteDef);
        var second = state.ApplyStrike(0, siteDef);

        Assert.Null(second);
        Assert.Equal(2, state.StrikesRemaining); // not decremented again
        Assert.Single(state.RewardsEarned);
    }

    [Fact]
    public void ApplyStrike_OnInvalidIndex_ReturnsNull()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        Assert.Null(state.ApplyStrike(-1, siteDef));
        Assert.Null(state.ApplyStrike(999, siteDef));
        Assert.Equal(3, state.StrikesRemaining);
    }

    [Fact]
    public void ApplyStrike_WithEmptyTile_HasNullValue()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        var reward = state.ApplyStrike(1, siteDef);

        Assert.NotNull(reward);
        Assert.Equal(DigRewardType.EMPTY, reward!.Type);
        Assert.Null(reward.Value);
    }

    [Fact]
    public void ApplyStrike_RevealsCodexPage_ReturnsCorrectType()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        var reward = state.ApplyStrike(3, siteDef);

        Assert.Equal(DigRewardType.CODEX_PAGE, reward!.Type);
        Assert.Equal("r1_test", reward.Value);
    }

    [Fact]
    public void ApplyStrike_RevealsRelicTile_ReturnsCorrectType()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        var reward = state.ApplyStrike(6, siteDef);

        Assert.Equal(DigRewardType.RELIC, reward!.Type);
        Assert.Equal("r1_amber_shard", reward.Value);
    }

    [Fact]
    public void HeadlineFind_TriggeredOnThreshold()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        // threshold is 2; strike tiles 0, 1
        state.ApplyStrike(0, siteDef);
        state.ApplyStrike(1, siteDef);

        Assert.True(state.HeadlineClaimed);
        Assert.Equal(3, state.RewardsEarned.Count); // 2 tile rewards + 1 headline
        Assert.Equal(DigRewardType.RELIC, state.RewardsEarned[^1].Type);
        Assert.Equal("relic:test_relic", state.RewardsEarned[^1].Value);
    }

    [Fact]
    public void IsComplete_TrueWhenStrikesDepleted()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        state.ApplyStrike(0, siteDef);
        state.ApplyStrike(1, siteDef);
        state.ApplyStrike(2, siteDef); // all 3 strikes used

        Assert.True(state.IsComplete);
    }

    [Fact]
    public void ApplyStrike_AfterDepleted_ReturnsNull()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        state.ApplyStrike(0, siteDef);
        state.ApplyStrike(1, siteDef);
        state.ApplyStrike(2, siteDef);
        var extra = state.ApplyStrike(3, siteDef);

        Assert.Null(extra);
    }

    [Fact]
    public void Clone_IsDeepCopy()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);
        state.ApplyStrike(0, siteDef); // 1 strike, still in progress

        var clone = state.Clone();

        Assert.Equal(state.DigSiteId, clone.DigSiteId);
        Assert.Equal(state.StrikesRemaining, clone.StrikesRemaining);
        Assert.Equal(state.TilesCleared, clone.TilesCleared);
        Assert.Equal(state.HeadlineClaimed, clone.HeadlineClaimed);
        Assert.Equal(state.RewardsEarned.Count, clone.RewardsEarned.Count);

        // Mutate clone and confirm original is unchanged
        clone.ApplyStrike(1, siteDef);
        Assert.NotEqual(state.TilesCleared, clone.TilesCleared);
        Assert.NotEqual(state.StrikesRemaining, clone.StrikesRemaining);
        Assert.NotEqual(state.RewardsEarned.Count, clone.RewardsEarned.Count);
        Assert.False(state.TilesRevealed[1]); // original should still have tile 1 hidden
    }

    [Fact]
    public void HeadlineFind_OnlyAwardedOnce()
    {
        var siteDef = DigSiteLoader.LoadPackFromString(TestJson).DigSites[0];
        var state = DigState.FromDef(siteDef);

        // threshold is 2, so tiles 0+1 trigger headline
        state.ApplyStrike(0, siteDef);
        state.ApplyStrike(1, siteDef);

        // Tile 3 would normally also trigger it, but it's already claimed
        state.ApplyStrike(3, siteDef);

        // Count headline entries — should be exactly 1
        var headlineCount = state.RewardsEarned.Count(r => r.Value == "relic:test_relic");
        Assert.Equal(1, headlineCount);
    }
}