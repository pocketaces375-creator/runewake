using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Cards;

/// <summary>
/// Tests for the rune forge system and dig tool definitions.
/// </summary>
public class ForgeAndToolTests
{
    private static readonly Dictionary<string, List<string>> TestRecipes = new()
    {
        ["verdant"] = new() { "rune_vrd_sharp_roots", "rune_vrd_bark_armour" },
        ["ember"] = new() { "rune_emb_kindling" }
    };

    private static ProgressionState MakeProg(int verdantFrags = 4, int emberFrags = 0)
    {
        var p = new ProgressionState();
        if (verdantFrags > 0) p.AddFragments("verdant", verdantFrags);
        if (emberFrags > 0) p.AddFragments("ember", emberFrags);
        return p;
    }

    private static Dictionary<string, RuneDef> MakeRuneIndex()
    {
        // Create just enough RuneDef stubs so the forge can look them up by ID
        return new Dictionary<string, RuneDef> { { "placeholder", new RuneDef() } };
    }

    // ─── ForgeSystem tests ───

    [Fact]
    public void Forge_Success_DeductsFragmentsAndAddsRune()
    {
        var prog = MakeProg(verdantFrags: 4);
        var fixedRng = new Random(42);

        var (result, runeId) = ForgeSystem.Forge("verdant", prog, MakeRuneIndex(), TestRecipes, fixedRng);

        Assert.Equal(ForgeResult.Success, result);
        Assert.NotNull(runeId);
        Assert.Contains(runeId!, TestRecipes["verdant"]);
        Assert.Equal(0, prog.Fragments["verdant"]);
        Assert.Contains(runeId!, prog.OwnedRuneIds);
    }

    [Fact]
    public void Forge_InsufficientFragments_ReturnsFailure()
    {
        var prog = MakeProg(verdantFrags: 3);
        var (result, runeId) = ForgeSystem.Forge("verdant", prog, MakeRuneIndex(), TestRecipes);

        Assert.Equal(ForgeResult.InsufficientFragments, result);
        Assert.Null(runeId);
        Assert.Equal(3, prog.Fragments["verdant"]);
    }

    [Fact]
    public void Forge_InvalidStrata_ReturnsFailure()
    {
        var prog = MakeProg(verdantFrags: 4);
        var (result, runeId) = ForgeSystem.Forge("nonexistent", prog, MakeRuneIndex(), TestRecipes);

        Assert.Equal(ForgeResult.InvalidStrata, result);
        Assert.Null(runeId);
    }

    [Fact]
    public void Forge_AllRunesOwned_ReturnsFailure()
    {
        var prog = MakeProg(verdantFrags: 4);
        // Own all runes in verdant
        prog.OwnedRuneIds.Add("rune_vrd_sharp_roots");
        prog.OwnedRuneIds.Add("rune_vrd_bark_armour");

        var (result, runeId) = ForgeSystem.Forge("verdant", prog, MakeRuneIndex(), TestRecipes);

        Assert.Equal(ForgeResult.AllRunesOwned, result);
        Assert.Null(runeId);
        Assert.Equal(4, prog.Fragments["verdant"]); // not deducted
    }

    [Fact]
    public void Forge_CanForge_ReturnsTrueWhenPossible()
    {
        var prog = MakeProg(verdantFrags: 4);
        Assert.True(ForgeSystem.CanForge("verdant", prog, TestRecipes));
    }

    [Fact]
    public void Forge_CanForge_ReturnsFalseWhenNoFragments()
    {
        var prog = MakeProg(verdantFrags: 0);
        Assert.False(ForgeSystem.CanForge("verdant", prog, TestRecipes));
    }

    [Fact]
    public void Forge_CanForge_ReturnsFalseWhenAllOwned()
    {
        var prog = MakeProg(verdantFrags: 8);
        prog.OwnedRuneIds.Add("rune_vrd_sharp_roots");
        prog.OwnedRuneIds.Add("rune_vrd_bark_armour");

        Assert.False(ForgeSystem.CanForge("verdant", prog, TestRecipes));
    }

    [Fact]
    public void Forge_CanForge_ReturnsFalseForInvalidStrata()
    {
        var prog = MakeProg(verdantFrags: 4);
        Assert.False(ForgeSystem.CanForge("mythic", prog, TestRecipes));
    }

    [Fact]
    public void Forge_ExcessFragments_KeepsRemainder()
    {
        var prog = MakeProg(verdantFrags: 10);
        var fixedRng = new Random(99);

        var (result, _) = ForgeSystem.Forge("verdant", prog, MakeRuneIndex(), TestRecipes, fixedRng);

        Assert.Equal(ForgeResult.Success, result);
        Assert.Equal(6, prog.Fragments["verdant"]); // 10 - 4 = 6
    }

    [Fact]
    public void Forge_SecondForgePicksDifferentRune()
    {
        var prog = MakeProg(verdantFrags: 8);
        var rng = new Random(42);

        // First forge
        var (r1, id1) = ForgeSystem.Forge("verdant", prog, MakeRuneIndex(), TestRecipes, rng);
        Assert.Equal(ForgeResult.Success, r1);
        Assert.Equal(4, prog.Fragments["verdant"]);

        // Second forge — the first rune is owned, so only the other is available
        var (r2, id2) = ForgeSystem.Forge("verdant", prog, MakeRuneIndex(), TestRecipes, rng);
        Assert.Equal(ForgeResult.Success, r2);
        Assert.NotNull(id2);
        Assert.NotEqual(id1, id2);
        Assert.Equal(0, prog.Fragments["verdant"]);
    }

    // ─── DigToolDef & Loader tests ───

    private const string TestToolJson = @"{
        ""tools"": [
            {
                ""id"": ""tool_brush"",
                ""name"": ""Brush"",
                ""description"": ""A fine brush."",
                ""effect"": ""EXTRA_STRIKE"",
                ""value"": 1
            },
            {
                ""id"": ""tool_loadstone_rod"",
                ""name"": ""Loadstone Rod"",
                ""description"": ""Divining rod."",
                ""effect"": ""REVEAL_RADIUS"",
                ""value"": 1,
                ""strata"": ""DAWN""
            }
        ]
    }";

    [Fact]
    public void DigToolLoader_LoadPackFromString_Deserializes()
    {
        var pack = DigToolLoader.LoadPackFromString(TestToolJson);
        Assert.Equal(2, pack.Tools.Count);
    }

    [Fact]
    public void DigToolLoader_CorrectFieldValues()
    {
        var pack = DigToolLoader.LoadPackFromString(TestToolJson);
        var brush = pack.Tools[0];
        Assert.Equal("tool_brush", brush.Id);
        Assert.Equal("Brush", brush.Name);
        Assert.Equal("A fine brush.", brush.Description);
        Assert.Equal(DigToolEffect.EXTRA_STRIKE, brush.Effect);
        Assert.Equal(1, brush.Value);
        Assert.Null(brush.Strata);

        var rod = pack.Tools[1];
        Assert.Equal(DigToolEffect.REVEAL_RADIUS, rod.Effect);
        Assert.Equal(Strata.DAWN, rod.Strata);
    }

    // ─── ProgressionState tool + rune helpers ───

    [Fact]
    public void ProgressionState_OwnedRuneHelpers()
    {
        var p = new ProgressionState();
        Assert.False(p.OwnsRune("test_rune"));

        Assert.True(p.AddOwnedRune("test_rune"));
        Assert.True(p.OwnsRune("test_rune"));

        // Duplicate add returns false
        Assert.False(p.AddOwnedRune("test_rune"));
    }

    [Fact]
    public void ProgressionState_ToolUnlockHelpers()
    {
        var p = new ProgressionState();
        Assert.False(p.HasTool("tool_brush"));

        Assert.True(p.UnlockTool("tool_brush"));
        Assert.True(p.HasTool("tool_brush"));

        // Duplicate unlock returns false
        Assert.False(p.UnlockTool("tool_brush"));
    }

    // ─── ForgeRecipe content validation ───

    [Fact]
    public void ForgeRecipeContent_Deserializes()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(ForgeAndToolTests).Assembly.Location)!,
            "../../../../content/forge/recipes.json"
        );
        var json = System.IO.File.ReadAllText(path);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement.GetProperty("recipes");
        Assert.Equal(5, root.EnumerateObject().Count()); // verdant, ember, tide, hollow, dawn

        var verdant = root.GetProperty("verdant").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("rune_vrd_sharp_roots", verdant);
        Assert.Contains("rune_vrd_bark_armour", verdant);
    }

    [Fact]
    public void DigToolContent_Deserializes()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(ForgeAndToolTests).Assembly.Location)!,
            "../../../../content/dig_tools/tools.json"
        );
        var pack = DigToolLoader.LoadPack(path);
        Assert.Equal(4, pack.Tools.Count);
        Assert.Contains(pack.Tools, t => t.Id == "tool_brush");
        Assert.Contains(pack.Tools, t => t.Id == "tool_iron_spade");
        Assert.Contains(pack.Tools, t => t.Id == "tool_loadstone_rod");
        Assert.Contains(pack.Tools, t => t.Id == "tool_seers_lens");
    }
}