using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.State;

public class MatchConfigTests
{
    [Fact]
    public void StartingVigor_Always25()
    {
        var c = new MatchConfig();
        Assert.Equal(25, c.StartingVigor);
    }
}