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
    public void MaxSize_Is30()
    {
        Assert.Equal(30, DeckRules.MaxSize);
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
    public void RangeSpan_Is1()
    {
        // exactly 30 — a single legal size
        Assert.Equal(1, DeckRules.MaxSize - DeckRules.MinSize + 1);
    }
}