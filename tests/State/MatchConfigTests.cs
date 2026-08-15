using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

/// <summary>
/// Tests for MatchConfig — starting vigor clamping and default values.
/// </summary>
public class MatchConfigTests
{
    [Fact]
    public void Default_StartingVigor_Is25()
    {
        var config = new MatchConfig();
        Assert.Equal(25, config.StartingVigor);
    }

    [Fact]
    public void StartingVigor_ClampsBelow20_To20()
    {
        var config = new MatchConfig(15);
        Assert.Equal(20, config.StartingVigor);
    }

    [Fact]
    public void StartingVigor_ClampsAbove30_To30()
    {
        var config = new MatchConfig(35);
        Assert.Equal(30, config.StartingVigor);
    }

    [Fact]
    public void StartingVigor_AtMinBoundary_Is20()
    {
        var config = new MatchConfig(20);
        Assert.Equal(20, config.StartingVigor);
    }

    [Fact]
    public void StartingVigor_AtMaxBoundary_Is30()
    {
        var config = new MatchConfig(30);
        Assert.Equal(30, config.StartingVigor);
    }

    [Fact]
    public void StartingVigor_DefaultConstructor_Is25()
    {
        var config = new MatchConfig();
        Assert.Equal(MatchConfig.DefaultStartingVigor, config.StartingVigor);
    }

    [Fact]
    public void StartingVigor_PropertySetter_Clamps()
    {
        var config = new MatchConfig();
        config.StartingVigor = 22;
        Assert.Equal(22, config.StartingVigor);
        config.StartingVigor = 40;
        Assert.Equal(30, config.StartingVigor);
        config.StartingVigor = 10;
        Assert.Equal(20, config.StartingVigor);
    }
}