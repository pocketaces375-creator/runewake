using System;
using System.IO;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Cards;

public class RuneLoaderTests
{
    private static readonly RunePack StarterPack = RuneLoader.LoadPack(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "content", "runes", "starter_runes.json"));

    [Fact]
    public void Loader_LoadsStarterRunes_Successfully()
    {
        Assert.NotNull(StarterPack);
        Assert.NotEmpty(StarterPack.Runes);
    }

    [Fact]
    public void AllRunes_HaveRequiredFields()
    {
        foreach (var rune in StarterPack.Runes)
        {
            Assert.False(string.IsNullOrWhiteSpace(rune.Id));
            Assert.False(string.IsNullOrWhiteSpace(rune.Name));
            Assert.False(string.IsNullOrWhiteSpace(rune.Description));
            Assert.InRange(rune.Cost, 1, 20);
        }
    }

    [Fact]
    public void AllRunes_HaveValidAbility()
    {
        foreach (var rune in StarterPack.Runes)
        {
            Assert.NotNull(rune.Ability);
            Assert.NotEmpty(rune.Ability.Effects);
            Assert.True(Enum.IsDefined(typeof(Trigger), rune.Ability.Trigger));
        }
    }

    [Fact]
    public void AllRunes_HaveValidSlotType()
    {
        foreach (var rune in StarterPack.Runes)
        {
            Assert.True(Enum.IsDefined(typeof(RuneSlotType), rune.SlotType));
        }
    }

    [Fact]
    public void StarterPack_HasAtLeastOneRunePerSlotType()
    {
        var offensive = StarterPack.Runes.Count(r => r.SlotType == RuneSlotType.OFFENSIVE);
        var defensive = StarterPack.Runes.Count(r => r.SlotType == RuneSlotType.DEFENSIVE);
        var utility = StarterPack.Runes.Count(r => r.SlotType == RuneSlotType.UTILITY);
        var mythic = StarterPack.Runes.Count(r => r.SlotType == RuneSlotType.MYTHIC);

        Assert.True(offensive >= 1);
        Assert.True(defensive >= 1);
        Assert.True(utility >= 1);
        Assert.True(mythic >= 1);
    }

    [Fact]
    public void LoadFromString_DeserializesCorrectly()
    {
        const string json = """
        {
          "runes": [
            {
              "id": "rune_test_example",
              "name": "Test Rune",
              "description": "A test rune.",
              "slot_type": "OFFENSIVE",
              "cost": 5,
              "ability": {
                "trigger": "PASSIVE",
                "effects": [
                  { "op": "BUFF", "target": { "scope": "ALLY_CREATURE", "filter": "ALL", "count": 0 }, "attack": 1, "duration": "PERMANENT" }
                ]
              }
            }
          ]
        }
        """;

        var pack = RuneLoader.LoadPackFromString(json);
        Assert.Single(pack.Runes);
        Assert.Equal("rune_test_example", pack.Runes[0].Id);
        Assert.Equal(RuneSlotType.OFFENSIVE, pack.Runes[0].SlotType);
        Assert.Equal(5, pack.Runes[0].Cost);
        Assert.Equal(Trigger.PASSIVE, pack.Runes[0].Ability.Trigger);
        Assert.Single(pack.Runes[0].Ability.Effects);
    }
}

public class RunePageTests
{
    private readonly RuneDef _offensiveRune = new()
    {
        Id = "rune_test_off",
        Name = "Test Offensive",
        Description = "Offensive test",
        SlotType = RuneSlotType.OFFENSIVE,
        Cost = 8,
        RpCost = 4,
        Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
    };

    private readonly RuneDef _defensiveRune = new()
    {
        Id = "rune_test_def",
        Name = "Test Defensive",
        Description = "Defensive test",
        SlotType = RuneSlotType.DEFENSIVE,
        Cost = 10,
        RpCost = 3,
        Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
    };

    private readonly RuneDef _utilityRune = new()
    {
        Id = "rune_test_util",
        Name = "Test Utility",
        Description = "Utility test",
        SlotType = RuneSlotType.UTILITY,
        Cost = 12,
        RpCost = 2,
        Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
    };

    private readonly RuneDef _mythicRune = new()
    {
        Id = "rune_test_myth",
        Name = "Test Mythic",
        Description = "Mythic test",
        SlotType = RuneSlotType.MYTHIC,
        Cost = 20,
        RpCost = 4,
        Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
    };

    [Fact]
    public void EmptyPage_HasZeroCostAndZeroCount()
    {
        var page = new RunePage();
        Assert.Equal(0, page.TotalCost);
        Assert.Equal(0, page.EquippedCount);
        Assert.True(page.IsWithinBudget());
    }

    [Fact]
    public void Equip_SingleRune_IncreasesCostAndCount()
    {
        var page = new RunePage();
        Assert.True(page.Equip(_offensiveRune));
        Assert.Equal(4, page.TotalCost);
        Assert.Equal(1, page.EquippedCount);
    }

    [Fact]
    public void Equip_RuneInWrongSlotType_FillsFirstAvailable()
    {
        var page = new RunePage();
        Assert.True(page.Equip(_offensiveRune));
        Assert.NotNull(page.OffensiveSlots[0]);
        Assert.Equal("rune_test_off", page.OffensiveSlots[0]!.Id);
    }

    [Fact]
    public void Equip_MultipleRunes_SumCost()
    {
        var page = new RunePage();
        page.Equip(_offensiveRune);  // 8
        page.Equip(_defensiveRune);  // 10
        page.Equip(_utilityRune);    // 12
        page.Equip(_mythicRune);     // 4
        Assert.Equal(13, page.TotalCost);
        Assert.Equal(4, page.EquippedCount);
    }

    [Fact]
    public void Equip_OverBudget_ReturnsFalse()
    {
        var page = new RunePage();
        var bigRune = new RuneDef
        {
            Id = "rune_big",
            Name = "Big",
            Description = "Expensive",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 12,
            RpCost = 4,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
        };
        // 8 × 4 = 32, under MaxBudget=100
        for (int i = 0; i < 8; i++)
            Assert.True(page.Equip(bigRune));

        Assert.Equal(32, page.TotalCost);
        Assert.True(page.IsWithinBudget());

        // 9th is still under budget (36 < 100) and slot is free
        Assert.True(page.Equip(bigRune));
        Assert.Equal(36, page.TotalCost);
    }

    [Fact]
    public void Equip_WhenSlotTypeFull_ReturnsFalse()
    {
        var page = new RunePage();
        // Offensive has 9 slots, fill them
        for (int i = 0; i < 9; i++)
        {
            var r = new RuneDef
            {
                Id = $"rune_fill_{i}",
                Name = "Fill",
                Description = "Filler",
                SlotType = RuneSlotType.OFFENSIVE,
                Cost = 1,
                RpCost = 1,
                Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
            };
            Assert.True(page.Equip(r));
        }

        // 10th should fail
        var extra = new RuneDef
        {
            Id = "rune_extra",
            Name = "Extra",
            Description = "Extra",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 1,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
        };
        Assert.False(page.Equip(extra));
    }

    [Fact]
    public void Unequip_BySlot_RemovesRune()
    {
        var page = new RunePage();
        page.Equip(_offensiveRune);
        Assert.Equal(4, page.TotalCost);
        // _offensiveRune has cost=8 (shard) and RpCost=4 (rp)
        // Test TotalCost uses RpCost
        Assert.True(page.Unequip(RuneSlotType.OFFENSIVE, 0));
        Assert.Equal(0, page.TotalCost);
        Assert.Equal(0, page.EquippedCount);
    }

    [Fact]
    public void Unequip_EmptySlot_ReturnsFalse()
    {
        var page = new RunePage();
        Assert.False(page.Unequip(RuneSlotType.OFFENSIVE, 0));
    }

    [Fact]
    public void Unequip_InvalidSlotIndex_ReturnsFalse()
    {
        var page = new RunePage();
        Assert.False(page.Unequip(RuneSlotType.OFFENSIVE, -1));
        Assert.False(page.Unequip(RuneSlotType.OFFENSIVE, 99));
    }

    [Fact]
    public void UnequipById_FindsAndRemovesRune()
    {
        var page = new RunePage();
        page.Equip(_offensiveRune);
        page.Equip(_defensiveRune);

        Assert.True(page.UnequipById("rune_test_off"));
        Assert.Equal(3, page.TotalCost);
        // _offensive removed (RpCost=4), defensive remains (RpCost=3)
        Assert.Single(page.GetAllEquipped());

        // Removing again should fail
        Assert.False(page.UnequipById("rune_test_off"));
    }

    [Fact]
    public void Equip_RuneWithInvalidCost_ReturnsFalse()
    {
        var page = new RunePage();
        var invalid = new RuneDef
        {
            Id = "rune_invalid",
            Name = "Invalid",
            Description = "Zero cost",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 0,
            RpCost = 0,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
        };
        Assert.False(page.Equip(invalid));

        var tooBig = new RuneDef
        {
            Id = "rune_toobig",
            Name = "Too Big",
            Description = "Over max RpCost (4)",
            SlotType = RuneSlotType.OFFENSIVE,
            Cost = 10,
            RpCost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
        };
        Assert.False(page.Equip(tooBig));
    }

    [Fact]
    public void GetAllEquipped_ReturnsAllEquippedRunes()
    {
        var page = new RunePage();
        page.Equip(_offensiveRune);
        page.Equip(_defensiveRune);
        page.Equip(_utilityRune);
        page.Equip(_mythicRune);

        var all = page.GetAllEquipped();
        Assert.Equal(4, all.Count);
        Assert.Contains(all, r => r.Id == "rune_test_off");
        Assert.Contains(all, r => r.Id == "rune_test_def");
        Assert.Contains(all, r => r.Id == "rune_test_util");
        Assert.Contains(all, r => r.Id == "rune_test_myth");
    }

    [Fact]
    public void MythicSlots_LimitedToThree()
    {
        var page = new RunePage();
        for (int i = 0; i < 3; i++)
        {
            var r = new RuneDef
            {
                Id = $"rune_myth_{i}",
                Name = "Mythic",
                Description = "Mythic filler",
                SlotType = RuneSlotType.MYTHIC,
                Cost = 1,
                Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
            };
            Assert.True(page.Equip(r));
        }

        var fourth = new RuneDef
        {
            Id = "rune_myth_extra",
            Name = "Extra Mythic",
            Description = "Should not fit",
            SlotType = RuneSlotType.MYTHIC,
            Cost = 1,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE, Effects = new() }
        };
        Assert.False(page.Equip(fourth));
        Assert.Equal(3, page.EquippedCount);
    }
}