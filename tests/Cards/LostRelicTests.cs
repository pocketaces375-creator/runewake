using System;
using System.Collections.Generic;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Cards;

public class LostRelicTests
{
    private static readonly Dictionary<string, LostRelicDef> TestDefs = new()
    {
        ["r1_warden_boss"] = new LostRelicDef
        {
            CardId = "dwn_c_aelins_seal",
            Name = "Aelin's Seal",
            EncounterId = "r1_warden_boss",
            Site = "The Fallow Reach — Steward's Barrow",
            EngravingStyle = "verdant_gold"
        }
    };

    private const string TestPackJson = @"{
        ""relics"": [
            {
                ""card_id"": ""dwn_c_aelins_seal"",
                ""name"": ""Aelin's Seal"",
                ""encounter_id"": ""r1_warden_boss"",
                ""site"": ""The Fallow Reach — Steward's Barrow"",
                ""engraving_style"": ""verdant_gold""
            }
        ]
    }";

    // ─── Loader tests ───

    [Fact]
    public void LostRelicLoader_LoadPackFromString_Deserializes()
    {
        var pack = LostRelicLoader.LoadPackFromString(TestPackJson);
        Assert.Single(pack.Relics);
    }

    [Fact]
    public void LostRelicLoader_CorrectFieldValues()
    {
        var relic = LostRelicLoader.LoadPackFromString(TestPackJson).Relics[0];
        Assert.Equal("dwn_c_aelins_seal", relic.CardId);
        Assert.Equal("Aelin's Seal", relic.Name);
        Assert.Equal("r1_warden_boss", relic.EncounterId);
        Assert.Equal("The Fallow Reach — Steward's Barrow", relic.Site);
        Assert.Equal("verdant_gold", relic.EngravingStyle);
    }

    // ─── Minter tests ───

    [Fact]
    public void Mint_CreatesInstance()
    {
        var fixedDate = new DateTime(2026, 8, 4);
        var relic = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "Trikzos", 1, fixedDate);

        Assert.NotNull(relic);
        Assert.Equal("dwn_c_aelins_seal", relic!.CardId);
        Assert.Equal("Trikzos", relic.AcquirerName);
        Assert.Equal("2026-08-04", relic.AcquiredAt);
        Assert.Equal("The Fallow Reach — Steward's Barrow", relic.Site);
        Assert.Equal(1, relic.DiscoveryIndex);
        Assert.Equal("verdant_gold", relic.EngravingStyle);
        Assert.NotNull(relic.RelicInstanceId);
        Assert.NotEmpty(relic.RelicInstanceId);
    }

    [Fact]
    public void Mint_UsesCorrectDiscoveryIndex()
    {
        var fixedDate = new DateTime(2026, 8, 4);
        var relic1 = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "Alice", 1, fixedDate);
        var relic2 = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "Bob", 2, fixedDate);

        Assert.Equal(1, relic1!.DiscoveryIndex);
        Assert.Equal(2, relic2!.DiscoveryIndex);
    }

    [Fact]
    public void Mint_UnknownEncounter_ReturnsNull()
    {
        var relic = LostRelicMinter.Mint("unknown_encounter", TestDefs, "Test", 0);
        Assert.Null(relic);
    }

    [Fact]
    public void Mint_GeneratesUniqueIds()
    {
        var fixedDate = new DateTime(2026, 8, 4);
        var relic1 = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "A", 1, fixedDate);
        var relic2 = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "B", 2, fixedDate);

        Assert.NotEqual(relic1!.RelicInstanceId, relic2!.RelicInstanceId);
    }

    // ─── Engraving text tests ───

    [Fact]
    public void GetEngravingText_FormatsCorrectly()
    {
        var fixedDate = new DateTime(2026, 8, 4);
        var relic = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "Trikzos", 42, fixedDate);

        var text = relic!.GetEngravingText();
        Assert.Contains("Unearthed by Trikzos", text);
        Assert.Contains("The Fallow Reach — Steward's Barrow", text);
        Assert.Contains("4 August 2026", text);
    }

    [Fact]
    public void GetEngravingText_DefaultName_FallsBack()
    {
        var relic = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "Adventurer", 0, new DateTime(2026, 8, 4));
        var text = relic!.GetEngravingText();
        Assert.Contains("Unearthed by Adventurer", text);
    }

    // ─── ProgressionState relic tests ───

    [Fact]
    public void ProgressionState_AddRelic_IncrementsIndex()
    {
        var prog = new ProgressionState();
        var fixedDate = new DateTime(2026, 8, 4);
        var relic = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "Test", 1, fixedDate);

        Assert.NotNull(relic);
        prog.AddRelic(relic!);
        Assert.Single(prog.DiscoveredRelics);
        Assert.Equal(1, prog.GlobalDiscoveryIndex);
    }

    [Fact]
    public void ProgressionState_MultipleRelics_IncrementsCorrectly()
    {
        var prog = new ProgressionState();
        var fixedDate = new DateTime(2026, 8, 4);

        for (int i = 1; i <= 5; i++)
        {
            var relic = LostRelicMinter.Mint("r1_warden_boss", TestDefs, "Test", i, fixedDate);
            prog.AddRelic(relic!);
        }

        Assert.Equal(5, prog.DiscoveredRelics.Count);
        Assert.Equal(5, prog.GlobalDiscoveryIndex);
    }
}