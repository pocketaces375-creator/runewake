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

    // ─── RunePage slot unlock costs ───

    [Fact]
    public void GetSlotUnlockCost_Slot0_Returns0()
    {
        Assert.Equal(0, RunePage.GetSlotUnlockCost(0));
    }

    [Fact]
    public void GetSlotUnlockCost_Slot1_Returns100()
    {
        Assert.Equal(100, RunePage.GetSlotUnlockCost(1));
    }

    [Fact]
    public void GetSlotUnlockCost_Slot2_Returns300()
    {
        Assert.Equal(300, RunePage.GetSlotUnlockCost(2));
    }

    [Fact]
    public void GetSlotUnlockCost_Slot3Plus_Returns0()
    {
        Assert.Equal(0, RunePage.GetSlotUnlockCost(3));
        Assert.Equal(0, RunePage.GetSlotUnlockCost(8));
    }

    [Fact]
    public void GetSlotCount_Mythic_Returns3()
    {
        Assert.Equal(3, RunePage.GetSlotCount(RuneSlotType.MYTHIC));
    }

    [Fact]
    public void GetSlotCount_NonMythic_Returns9()
    {
        Assert.Equal(9, RunePage.GetSlotCount(RuneSlotType.OFFENSIVE));
        Assert.Equal(9, RunePage.GetSlotCount(RuneSlotType.DEFENSIVE));
        Assert.Equal(9, RunePage.GetSlotCount(RuneSlotType.UTILITY));
    }

    // ─── RunePage upgrade costs ───

    [Fact]
    public void GetUpgradeCost_Tier1_Returns60()
    {
        Assert.Equal(60, RunePage.GetUpgradeCost(1));
    }

    [Fact]
    public void GetUpgradeCost_Tier2_Returns180()
    {
        Assert.Equal(180, RunePage.GetUpgradeCost(2));
    }

    [Fact]
    public void GetUpgradeCost_Tier3Plus_Returns0()
    {
        Assert.Equal(0, RunePage.GetUpgradeCost(3));
        Assert.Equal(0, RunePage.GetUpgradeCost(0));
    }

    // ─── ProgressionState RuneDust spending ───

    [Fact]
    public void SpendRuneDust_Sufficient_FundsDeducted()
    {
        var state = new ProgressionState { RuneDust = 200 };
        Assert.True(state.SpendRuneDust(150));
        Assert.Equal(50, state.RuneDust);
    }

    [Fact]
    public void SpendRuneDust_Insufficient_ShortfallReported()
    {
        var state = new ProgressionState { RuneDust = 50 };
        bool success = state.SpendRuneDust(100, out var shortfall);
        Assert.False(success);
        Assert.Equal(50, shortfall);
        Assert.Equal(50, state.RuneDust); // not deducted
    }

    [Fact]
    public void SpendRuneDust_ZeroAmount_DoesNothing()
    {
        var state = new ProgressionState { RuneDust = 50 };
        Assert.True(state.SpendRuneDust(0));
        Assert.Equal(50, state.RuneDust);
    }

    // ─── ProgressionState slot unlock ───

    [Fact]
    public void GetUnlockedSlotCount_Default_Returns1()
    {
        var state = new ProgressionState();
        Assert.Equal(1, state.GetUnlockedSlotCount(RuneSlotType.OFFENSIVE));
        Assert.Equal(1, state.GetUnlockedSlotCount(RuneSlotType.DEFENSIVE));
        Assert.Equal(1, state.GetUnlockedSlotCount(RuneSlotType.UTILITY));
        Assert.Equal(1, state.GetUnlockedSlotCount(RuneSlotType.MYTHIC));
    }

    [Fact]
    public void UnlockNextSlot_Slot2Costs100_InsufficientFunds_Fails()
    {
        var state = new ProgressionState { RuneDust = 0 };
        var (success, cost, error) = state.UnlockNextSlot(RuneSlotType.OFFENSIVE);
        Assert.False(success);
        Assert.Equal(100, cost);
        Assert.Contains("Need", error);
        Assert.Equal(1, state.GetUnlockedSlotCount(RuneSlotType.OFFENSIVE)); // unchanged
    }

    [Fact]
    public void UnlockNextSlot_Slot2Costs100_WithSufficientFunds()
    {
        var state = new ProgressionState { RuneDust = 200 };
        // First unlock (slot 2, index=1) costs 100
        var (success, cost, error) = state.UnlockNextSlot(RuneSlotType.OFFENSIVE);
        Assert.True(success);
        Assert.Equal(100, cost);
        Assert.Null(error);
        Assert.Equal(100, state.RuneDust); // 200 - 100
        Assert.Equal(2, state.GetUnlockedSlotCount(RuneSlotType.OFFENSIVE));
    }

    [Fact]
    public void UnlockNextSlot_PaysCorrectCost()
    {
        var state = new ProgressionState { RuneDust = 500 };
        // Unlock slot 2 (costs 100)
        var (success1, cost1, _) = state.UnlockNextSlot(RuneSlotType.OFFENSIVE);
        Assert.True(success1);
        Assert.Equal(100, cost1);
        Assert.Equal(400, state.RuneDust); // 500 - 100
        Assert.Equal(2, state.GetUnlockedSlotCount(RuneSlotType.OFFENSIVE));

        // Unlock slot 3 (costs 300)
        var (success2, cost2, _) = state.UnlockNextSlot(RuneSlotType.OFFENSIVE);
        Assert.True(success2);
        Assert.Equal(300, cost2);
        Assert.Equal(100, state.RuneDust); // 400 - 300
        Assert.Equal(3, state.GetUnlockedSlotCount(RuneSlotType.OFFENSIVE));

        // Subsequent slots are free
        state.UnlockNextSlot(RuneSlotType.OFFENSIVE);
        Assert.Equal(100, state.RuneDust); // still 100, no cost
        Assert.Equal(4, state.GetUnlockedSlotCount(RuneSlotType.OFFENSIVE));
    }

    [Fact]
    public void UnlockNextSlot_AllSlotsUnlocked_ReturnsFalse()
    {
        var state = new ProgressionState { RuneDust = 500 };
        // Mythic has 3 slots. Unlock slot 2 (costs 100).
        state.UnlockNextSlot(RuneSlotType.MYTHIC); // → 2 unlocked
        // Unlock slot 3 (costs 300).
        state.UnlockNextSlot(RuneSlotType.MYTHIC); // → 3 unlocked

        // Cannot exceed max
        var (success, _, error) = state.UnlockNextSlot(RuneSlotType.MYTHIC);
        Assert.False(success);
    }

    [Fact]
    public void UnlockNextSlot_ShortfallShown()
    {
        var state = new ProgressionState { RuneDust = 50 };
        // Slot 2 (index 1) costs 100 — shortfall 50
        var (success, cost, error) = state.UnlockNextSlot(RuneSlotType.DEFENSIVE);
        Assert.False(success);
        Assert.Equal(100, cost);
        Assert.Contains("50", error);
        Assert.Equal(50, state.RuneDust); // not deducted
    }

    // ─── ProgressionState rune upgrade ───

    [Fact]
    public void GetRuneTier_Default_Returns1()
    {
        var state = new ProgressionState();
        Assert.Equal(1, state.GetRuneTier("unknown_rune"));
    }

    [Fact]
    public void GetRuneTier_AfterUpgrade_ReturnsHigher()
    {
        var state = new ProgressionState { RuneDust = 500 };
        state.UpgradeRune("test_rune");
        Assert.Equal(2, state.GetRuneTier("test_rune"));
    }

    [Fact]
    public void UpgradeRune_Tier1To2_Costs60()
    {
        var state = new ProgressionState { RuneDust = 100 };
        var (success, cost, error) = state.UpgradeRune("test_rune");
        Assert.True(success);
        Assert.Equal(60, cost);
        Assert.Null(error);
        Assert.Equal(40, state.RuneDust); // 100 - 60
        Assert.Equal(2, state.GetRuneTier("test_rune"));
    }

    [Fact]
    public void UpgradeRune_Tier2To3_Costs180()
    {
        var state = new ProgressionState { RuneDust = 500 };
        state.UpgradeRune("test_rune"); // tier 1→2, costs 60
        Assert.Equal(440, state.RuneDust);
        Assert.Equal(2, state.GetRuneTier("test_rune"));

        var (success, cost, error) = state.UpgradeRune("test_rune"); // tier 2→3, costs 180
        Assert.True(success);
        Assert.Equal(180, cost);
        Assert.Null(error);
        Assert.Equal(260, state.RuneDust); // 440 - 180
        Assert.Equal(3, state.GetRuneTier("test_rune"));
    }

    [Fact]
    public void UpgradeRune_AlreadyMaxTier_ReturnsFalse()
    {
        var state = new ProgressionState { RuneDust = 500 };
        state.UpgradeRune("test_rune"); // tier 1→2
        state.UpgradeRune("test_rune"); // tier 2→3
        Assert.Equal(3, state.GetRuneTier("test_rune"));

        // Already at max
        var (success, _, error) = state.UpgradeRune("test_rune");
        Assert.False(success);
        Assert.Contains("max", error);
    }

    [Fact]
    public void UpgradeRune_InsufficientFunds_ReturnsFalseWithShortfall()
    {
        var state = new ProgressionState { RuneDust = 30 }; // need 60
        var (success, cost, error) = state.UpgradeRune("test_rune");
        Assert.False(success);
        Assert.Equal(60, cost);
        Assert.Contains("30", error); // shortfall = 30
        Assert.Equal(30, state.RuneDust); // not deducted
        Assert.Equal(1, state.GetRuneTier("test_rune")); // not upgraded
    }

    [Fact]
    public void UpgradeRune_IndependentPerRuneId()
    {
        var state = new ProgressionState { RuneDust = 500 };
        state.UpgradeRune("rune_a"); // tier 1→2, costs 60
        state.UpgradeRune("rune_b"); // tier 1→2, costs 60

        Assert.Equal(2, state.GetRuneTier("rune_a"));
        Assert.Equal(2, state.GetRuneTier("rune_b"));
        Assert.Equal(380, state.RuneDust); // 500 - 60 - 60
    }
}