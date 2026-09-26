using System.IO;
using System.Linq;
using System.Text.Json;
using Runewake.Tests.World;
using Xunit;

namespace Runewake.Tests.Client;

/// <summary>
/// FABLE-037 guard — the RUNESTONE card faces. Every card has a bake, the bake says where the
/// game draws each live numeral, and the layout keeps Trikzos's rule from the final mock-up round:
/// "no overlapping information" — the shields never touch the name band, the cost diamond stays
/// inside the card, and only cards with Strength and Vigor get shields.
/// </summary>
public class CardBakeTests
{
    private static JsonElement Layout(string root) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "client", "content", "art", "cards_baked", "layout.json"))).RootElement;

    private static float[] Arr(JsonElement card, string key) =>
        card.GetProperty(key).EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();

    [Fact]
    public void Every_Card_Has_A_Runestone_Bake_With_Live_Numeral_Slots()
    {
        string root = WorldGeneratorTests.Root();
        var layout = Layout(root);
        Assert.Equal("FABLE-037-RUNESTONE", layout.GetProperty("format").GetString());
        float mid = (float)layout.GetProperty("numeral_mid").GetDouble();
        Assert.InRange(mid, 0.2f, 0.5f);
        var cards = layout.GetProperty("cards");

        foreach (var file in Directory.GetFiles(Path.Combine(root, "client", "content", "cards"), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var list = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("cards");
            foreach (var c in list.EnumerateArray())
            {
                string id = c.GetProperty("id").GetString()!;
                Assert.True(cards.TryGetProperty(id, out var bake), $"{id}: not in cards_baked/layout.json — run python3 pipeline/bake_cards.py {id}");
                Assert.True(File.Exists(Path.Combine(root, "client", "content", "art", "cards_baked", id + ".webp")), $"{id}: baked face missing");

                bool hasStats = c.TryGetProperty("attack", out var a) && a.ValueKind == JsonValueKind.Number
                             && c.TryGetProperty("vigor", out var v) && v.ValueKind == JsonValueKind.Number;
                var atk = Arr(bake, "attack_num"); var vig = Arr(bake, "vigor_num"); var cost = Arr(bake, "cost_num");
                Assert.True(cost[2] > 0, $"{id}: no cost numeral slot");
                Assert.Equal(hasStats, atk[2] > 0);
                Assert.Equal(hasStats, vig[2] > 0);
                foreach (var n in new[] { atk, vig, cost }.Where(n => n[2] > 0))
                    Assert.True(n[0] > 0 && n[0] < 1 && n[1] > 0 && n[1] < 1, $"{id}: numeral centre off the card");

                // the diamond stays inside the card
                var d = Arr(bake, "cost_badge");
                Assert.True(d[0] >= 0 && d[1] >= 0 && d[0] + d[2] <= 1 && d[1] + d[3] <= 1, $"{id}: cost diamond pokes past the card edge");

                // the shields never reach the name band
                if (hasStats)
                {
                    var name = Arr(bake, "name_plate");
                    foreach (var key in new[] { "attack_badge", "vigor_badge" })
                    {
                        var s = Arr(bake, key);
                        Assert.True(s[1] + s[3] < name[1], $"{id}: {key} overlaps the name band");
                    }
                    var ab = Arr(bake, "attack_badge"); var vb = Arr(bake, "vigor_badge");
                    Assert.True(ab[0] + ab[2] <= vb[0], $"{id}: the two shields overlap");
                }
            }
        }
    }
}
