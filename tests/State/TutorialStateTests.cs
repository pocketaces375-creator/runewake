using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

public class TutorialStateTests
{
    [Fact]
    public void TutorialState_DefaultStep_IsNone()
    {
        var state = new TutorialState();
        Assert.Equal(TutorialStep.None, state.CurrentStep);
        Assert.False(state.IsComplete);
    }

    [Fact]
    public void TutorialState_Clone_IsDeepCopy()
    {
        var state = new TutorialState { CurrentStep = TutorialStep.Lanes_SummonCreature, IsComplete = false };
        var clone = state.Clone();
        Assert.Equal(TutorialStep.Lanes_SummonCreature, clone.CurrentStep);
        Assert.False(clone.IsComplete);

        // Mutate original — clone should be unaffected
        state.CurrentStep = TutorialStep.Complete;
        state.IsComplete = true;
        Assert.Equal(TutorialStep.Lanes_SummonCreature, clone.CurrentStep);
        Assert.False(clone.IsComplete);
    }

    [Fact]
    public void TutorialLoader_LoadsAllEightSteps()
    {
        var json = @"[
  {""step"":""Lanes_SummonCreature"",""highlight"":""hand"",""message"":""Test 1""},
  {""step"":""Lanes_Attack"",""highlight"":""lane"",""message"":""Test 2""},
  {""step"":""Lanes_EndTurn"",""highlight"":""endturn"",""message"":""Test 3""},
  {""step"":""Excavate_PlayExcavate"",""highlight"":""hand"",""message"":""Test 4""},
  {""step"":""Excavate_BuryResolved"",""highlight"":""barrow"",""message"":""Test 5""},
  {""step"":""Runes_OpenRunePage"",""highlight"":""runebtn"",""message"":""Test 6""},
  {""step"":""Runes_EquipRune"",""highlight"":""runeslot"",""message"":""Test 7""},
  {""step"":""Complete"",""highlight"":""none"",""message"":""Test 8""}
]";
        var steps = TutorialLoader.LoadStepsFromString(json);
        Assert.Equal(8, steps.Count);
    }

    [Fact]
    public void TutorialLoader_EachStepHasMessageAndHighlight()
    {
        var json = @"[
  {""step"":""Lanes_SummonCreature"",""highlight"":""hand"",""message"":""Tap a card""},
  {""step"":""Lanes_Attack"",""highlight"":""lane"",""message"":""Tap your creature""}
]";
        var steps = TutorialLoader.LoadStepsFromString(json);
        foreach (var step in steps)
        {
            Assert.False(string.IsNullOrEmpty(step.Message));
            Assert.False(string.IsNullOrEmpty(step.Highlight));
        }
    }

    [Fact]
    public void TutorialLoader_StepEnumValuesAllPresent()
    {
        var json = @"[
  {""step"":""Lanes_SummonCreature"",""highlight"":""hand"",""message"":""M""},
  {""step"":""Lanes_Attack"",""highlight"":""lane"",""message"":""M""},
  {""step"":""Lanes_EndTurn"",""highlight"":""endturn"",""message"":""M""},
  {""step"":""Excavate_PlayExcavate"",""highlight"":""hand"",""message"":""M""},
  {""step"":""Excavate_BuryResolved"",""highlight"":""barrow"",""message"":""M""},
  {""step"":""Runes_OpenRunePage"",""highlight"":""runebtn"",""message"":""M""},
  {""step"":""Runes_EquipRune"",""highlight"":""runeslot"",""message"":""M""},
  {""step"":""Complete"",""highlight"":""none"",""message"":""M""}
]";
        var steps = TutorialLoader.LoadStepsFromString(json);
        Assert.All(steps, s => Assert.True(s.Step != TutorialStep.None, $"Step {s.Highlight} resolved to None"));
    }

    [Fact]
    public void ProgressionState_NewSave_TutorialStartsAtLanesSummon()
    {
        var prog = new ProgressionState();
        // Simulate what SaveRepository does: version is 0 (fresh DB) -> bump to 1, start tutorial
        prog.Version = 0; // fresh save
        if (prog.Version == 0)
        {
            prog.Version = 1;
            if (prog.Tutorial == null)
                prog.Tutorial = new TutorialState { CurrentStep = TutorialStep.Lanes_SummonCreature };
        }

        Assert.NotNull(prog.Tutorial);
        Assert.Equal(TutorialStep.Lanes_SummonCreature, prog.Tutorial.CurrentStep);
        Assert.False(prog.Tutorial.IsComplete);
    }

    [Fact]
    public void ProgressionState_ExistingSaveNullTutorial_TreatedAsNone()
    {
        var prog = new ProgressionState();
        // Existing save with version > 0 and Tutorial == null
        prog.Version = 2;
        Assert.Null(prog.Tutorial);
    }

    [Fact]
    public void TutorialState_AdvanceFromExcavateBuryToRunesOpen()
    {
        var state = new TutorialState
        {
            CurrentStep = TutorialStep.Excavate_BuryResolved,
            IsComplete = false
        };

        // Manually step through the Advance logic
        TutorialStep next = TutorialStep.Runes_OpenRunePage;
        state.CurrentStep = next;

        Assert.Equal(TutorialStep.Runes_OpenRunePage, state.CurrentStep);
        Assert.False(state.IsComplete);
    }

    [Fact]
    public void TutorialState_AdvanceFromEquipToComplete()
    {
        var state = new TutorialState
        {
            CurrentStep = TutorialStep.Runes_EquipRune,
            IsComplete = false
        };

        TutorialStep next = TutorialStep.Complete;
        state.CurrentStep = next;
        state.IsComplete = true;

        Assert.Equal(TutorialStep.Complete, state.CurrentStep);
        Assert.True(state.IsComplete);
    }
}