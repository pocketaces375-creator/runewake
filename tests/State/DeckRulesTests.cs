using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>
/// Tests for DeckRules constants and boundary values.
/// </summary>
public class DeckRulesTests
{
    [Fact]
    public void MinSize_Is30()
    {
        Assert.Equal(30, DeckRules.MinSize);
    }

    [Fact]
    public void MaxSize_Is40()
    {
        Assert.Equal(40, DeckRules.MaxSize);
    }

    [Fact]
    public void IsSingleton_IsTrue()
    {
        Assert.True(DeckRules.IsSingleton);
    }

    [Fact]
    public void MinSize_Le_MaxSize()
    {
        Assert.True(DeckRules.MinSize <= DeckRules.MaxSize);
    }

    [Fact]
    public void RangeSpan_Is11()
    {
        // 30..40 inclusive = 11 possible sizes
        Assert.Equal(11, DeckRules.MaxSize - DeckRules.MinSize + 1);
    }
}