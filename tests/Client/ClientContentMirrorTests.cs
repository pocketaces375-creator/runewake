using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runewake.Tests.World;
using Xunit;

namespace Runewake.Tests.Client;

/// <summary>
/// FABLE-033 guard. The game loads res://content/… — that is client/content, a COPY of the
/// repo's content/ tree. TASK-DROPS-DATA-1 added drop tables to content/encounters and tested
/// them there; the copy under client/ was never updated, so no fight in region 1 ever dropped
/// a card on the phone. This pins the one thing that bit: every encounter the client ships
/// must carry the same drops and rewards as its source under content/.
/// (Other content dirs drift on purpose — the client's map files carry tuned positions — so
/// this deliberately checks encounters only.)
/// </summary>
public class ClientContentMirrorTests
{
    private static Dictionary<string, JsonElement> Encounters(string dir)
    {
        var all = new Dictionary<string, JsonElement>();
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            foreach (var e in doc.RootElement.GetProperty("encounters").EnumerateArray())
                all[e.GetProperty("id").GetString()!] = e.Clone();
        }
        return all;
    }

    [Fact]
    public void Client_Encounters_Carry_The_Same_Drops_And_Rewards_As_The_Source()
    {
        string root = WorldGeneratorTests.Root();
        var src = Encounters(Path.Combine(root, "content", "encounters"));
        var shipped = Encounters(Path.Combine(root, "client", "content", "encounters"));
        Assert.NotEmpty(src);
        var problems = new List<string>();
        foreach (var (id, s) in src)
        {
            if (!shipped.TryGetValue(id, out var c)) { problems.Add($"{id}: missing from client/content/encounters"); continue; }
            foreach (var key in new[] { "drops", "shard_reward", "dig_charge_reward", "fragment_reward", "card_reward", "opening_rule" })
            {
                string a = s.TryGetProperty(key, out var sv) ? sv.GetRawText() : "";
                string b = c.TryGetProperty(key, out var cv) ? cv.GetRawText() : "";
                if (a != b) problems.Add($"{id}.{key} differs between content/ and client/content/ — run: cp content/encounters/*.json client/content/encounters/");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(12)));
    }
}
