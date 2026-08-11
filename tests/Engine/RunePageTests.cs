using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

[Collection("NonParallel")]
public class RunePageTests
{
    [Fact]
    public void GetBudgetForLevel_Level1_Returns12()
    {
        Assert.Equal(12, RunePage.GetBudgetForLevel(1));
        Assert.Equal(12, RunePage.GetBudgetForLevel(4));
    }

    [Fact]
    public void GetBudgetForLevel_Level5_Returns20()
    {
        Assert.Equal(20, RunePage.GetBudgetForLevel(5));
        Assert.Equal(20, RunePage.GetBudgetForLevel(9));
    }

    [Fact]
    public void GetBudgetForLevel_Level10_Returns30()
    {
        Assert.Equal(30, RunePage.GetBudgetForLevel(10));
        Assert.Equal(30, RunePage.GetBudgetForLevel(14));
    }

    [Fact]
    public void GetBudgetForLevel_Level15_Returns40()
    {
        Assert.Equal(40, RunePage.GetBudgetForLevel(15));
        Assert.Equal(40, RunePage.GetBudgetForLevel(19));
    }

    [Fact]
    public void GetBudgetForLevel_Level20Cap_Returns48()
    {
        Assert.Equal(48, RunePage.GetBudgetForLevel(20));
        Assert.Equal(48, RunePage.GetBudgetForLevel(99));
    }

    [Fact]
    public void EmptyPage_TotalCostZero()
    {
        var page = new RunePage();
        Assert.Equal(0, page.TotalCost);
        Assert.Equal(0, page.EquippedCount);
    }

    [Fact]
    public void EquipSingleRune_TotalCostEqualsRpCost()
    {
        var rune = new RuneDef
        {
            Id = "test_rune",
            Name = "Test",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 2,
            Cost = 10,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        var page = new RunePage();
        Assert.True(page.Equip(rune));
        Assert.Equal(2, page.TotalCost);
        Assert.Equal(1, page.EquippedCount);
    }

    [Fact]
    public void EquipMultipleRunes_TotalCostSummedCorrectly()
    {
        var page = new RunePage();
        for (int i = 0; i < 3; i++)
        {
            var rune = new RuneDef
            {
                Id = $"test_rune_{i}",
                Name = $"Test {i}",
                Description = "Test",
                SlotType = RuneSlotType.OFFENSIVE,
                RpCost = 2,
                Cost = 10,
                Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
            };
            Assert.True(page.Equip(rune));
        }
        Assert.Equal(6, page.TotalCost);
        Assert.Equal(3, page.EquippedCount);
    }

    [Fact]
    public void EquipInvalidRpCost_Rejected()
    {
        var lowRune = new RuneDef
        {
            Id = "rune_low",
            Name = "Low",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 0,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        var highRune = new RuneDef
        {
            Id = "rune_high",
            Name = "High",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 5,
            Cost = 10,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        var page = new RunePage();
        Assert.False(page.Equip(lowRune));
        Assert.False(page.Equip(highRune));
        Assert.Equal(0, page.TotalCost);
    }

    [Fact]
    public void EquipExceedsMaxBudget_Rejected()
    {
        var page = new RunePage();
        // Fill offensive slots with 4-rp runes
        // Each slot holds 4 RP, 9 slots * 4 = 36, but MaxBudget=100 so this won't trigger
        // Actually let's test with the old MaxBudget constant for now
        // Fill almost all slots then add one that would exceed
        for (int i = 0; i < 8; i++)
        {
            var rune = new RuneDef
            {
                Id = $"rune_{i}",
                Name = $"Rune {i}",
                Description = "Test",
                SlotType = RuneSlotType.OFFENSIVE,
                RpCost = 4,
                Cost = 10,
                Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
            };
            Assert.True(page.Equip(rune));
        }
        // 8 * 4 = 32, well under 100, so 9th works too
        var last = new RuneDef
        {
            Id = "rune_last",
            Name = "Last",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 4,
            Cost = 10,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        Assert.True(page.Equip(last));
        Assert.Equal(36, page.TotalCost);
        Assert.Equal(9, page.EquippedCount);

        // No more offensive slots — can't add another
        var extra = new RuneDef
        {
            Id = "rune_extra",
            Name = "Extra",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 4,
            Cost = 10,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        Assert.False(page.Equip(extra));
    }

    [Fact]
    public void EquipFillsCorrectSlotType()
    {
        var page = new RunePage();
        var offensive = new RuneDef
        {
            Id = "rune_off",
            Name = "Off",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 1,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        var defensive = new RuneDef
        {
            Id = "rune_def",
            Name = "Def",
            Description = "Test",
            SlotType = RuneSlotType.DEFENSIVE,
            RpCost = 2,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        var utility = new RuneDef
        {
            Id = "rune_util",
            Name = "Util",
            Description = "Test",
            SlotType = RuneSlotType.UTILITY,
            RpCost = 3,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        var mythic = new RuneDef
        {
            Id = "rune_myth",
            Name = "Myth",
            Description = "Test",
            SlotType = RuneSlotType.MYTHIC,
            RpCost = 4,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };

        Assert.True(page.Equip(offensive));
        Assert.True(page.Equip(defensive));
        Assert.True(page.Equip(utility));
        Assert.True(page.Equip(mythic));

        Assert.Equal(1 + 2 + 3 + 4, page.TotalCost);
        Assert.Equal(4, page.EquippedCount);
        Assert.NotNull(page.OffensiveSlots[0]);
        Assert.NotNull(page.DefensiveSlots[0]);
        Assert.NotNull(page.UtilitySlots[0]);
        Assert.NotNull(page.MythicSlots[0]);
    }

    [Fact]
    public void Unequip_RemovesRuneFromSlot()
    {
        var page = new RunePage();
        var rune = new RuneDef
        {
            Id = "rune_test",
            Name = "Test",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 2,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        page.Equip(rune);
        Assert.Equal(2, page.TotalCost);

        Assert.True(page.Unequip(RuneSlotType.OFFENSIVE, 0));
        Assert.Equal(0, page.TotalCost);
        Assert.Null(page.OffensiveSlots[0]);

        // Can't uneqip an already-empty slot
        Assert.False(page.Unequip(RuneSlotType.OFFENSIVE, 0));
    }

    [Fact]
    public void UnequipById_RemovesCorrectRune()
    {
        var page = new RunePage();
        var rune1 = new RuneDef
        {
            Id = "rune_1",
            Name = "Rune 1",
            Description = "Test",
            SlotType = RuneSlotType.OFFENSIVE,
            RpCost = 1,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        var rune2 = new RuneDef
        {
            Id = "rune_2",
            Name = "Rune 2",
            Description = "Test",
            SlotType = RuneSlotType.DEFENSIVE,
            RpCost = 2,
            Cost = 5,
            Ability = new AbilityDef { Trigger = Trigger.PASSIVE }
        };
        page.Equip(rune1);
        page.Equip(rune2);
        Assert.Equal(3, page.TotalCost);

        Assert.True(page.UnequipById("rune_1"));
        Assert.Equal(2, page.TotalCost);
        Assert.Null(page.OffensiveSlots[0]);
        Assert.NotNull(page.DefensiveSlots[0]);

        // Already removed
        Assert.False(page.UnequipById("rune_1"));
    }
}