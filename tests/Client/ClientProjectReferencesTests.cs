using System.IO;
using System.Linq;
using System.Xml.Linq;
using Runewake.Tests.World;
using Xunit;

namespace Runewake.Tests.Client;

/// <summary>
/// FABLE-023 guard. The phone could not load Runewake.Sim — an EXE project the client referenced
/// for its bot — so every duel with a bot opponent failed to open ("Could not load file or
/// assembly 'Runewake.Sim'"), which the player saw as a grey screen after the tutorial.
/// The desktop never shows this, so it is pinned here: everything the game ships with must be a
/// library.
/// </summary>
public class ClientProjectReferencesTests
{
    [Fact]
    public void Client_References_Only_Library_Projects()
    {
        string root = WorldGeneratorTests.Root();
        string client = Path.Combine(root, "client", "Runewake.Client.csproj");
        var refs = XDocument.Load(client).Descendants("ProjectReference")
            .Select(r => (string)r.Attribute("Include")!)
            .ToList();
        Assert.NotEmpty(refs);
        foreach (var r in refs)
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(client)!, r.Replace('\\', Path.DirectorySeparatorChar)));
            var outputType = XDocument.Load(path).Descendants("OutputType").Select(e => e.Value.Trim()).FirstOrDefault() ?? "Library";
            Assert.True(outputType.Equals("Library", System.StringComparison.OrdinalIgnoreCase),
                $"client references {Path.GetFileName(path)}, which is an {outputType} — Android cannot load it. Move the code the client needs into a library.");
        }
    }

    [Fact]
    public void The_Bot_Lives_In_The_Engine()
    {
        Assert.Equal("Runewake.Engine", typeof(Runewake.Sim.GreedyBot).Assembly.GetName().Name);
    }
}
