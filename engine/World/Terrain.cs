using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Runewake.Engine.State;

namespace Runewake.Engine.World;

// ═══════════════════════════════════════════════════════════════════════════
// FABLE-105 — THE TERRAIN RULES (Trikzos, 2026-10-25):
//
//   "We need the coding of our game for our infinite expanding world to include
//    rules about the terrain that each map prompt shall end up generating …
//    use the terrain generated in the art in part for naming the locations …
//    the maps should have more dots, thoroughly but accurately using the terrain."
//
// One plan, three readers. For every page of the world the generator first lays
// out TERRAIN — a handful of features (a mountain range in the north, a river
// through the centre, a stronghold in the east …), each with named SPOTS on it
// (the pass, the peak, the ford, the gate). Then:
//
//   1. the PAINTER is told exactly that, in a 3x3-grid sentence it can follow
//      ("in the north-west, a jagged mountain range …") — TerrainPrompt;
//   2. the PLACES snap onto those spots — a Warden holds the pass or the gate,
//      a Dig sits in the cave or the barrow, a Merchant waits at the ford;
//   3. the NAMES come from the spot — "Thornlit Pass", "Rootdeep Barrow".
//
// The same vocabulary (content/world/terrain.json) captions the LoRA training
// maps, so the model learns these words mean these shapes in these places.
//
// Everything here is a pure function of the seeded rng it is given.
// ═══════════════════════════════════════════════════════════════════════════

public sealed class TerrainSpotDef
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("nouns")] public List<string> Nouns { get; set; } = new();
    /// <summary>Which blip / node kinds like this spot (BlipKind names, plus Merchant/Dig for events).</summary>
    [JsonPropertyName("fits")] public List<string> Fits { get; set; } = new();
}

public sealed class TerrainFeatureDef
{
    [JsonPropertyName("paint")] public string Paint { get; set; } = "";
    [JsonPropertyName("cells_wide")] public int CellsWide { get; set; } = 1;
    [JsonPropertyName("spots")] public List<TerrainSpotDef> Spots { get; set; } = new();
}

public sealed class TerrainBiomeDef
{
    [JsonPropertyName("menu")] public List<string> Menu { get; set; } = new();
    [JsonPropertyName("always")] public List<string> Always { get; set; } = new();
    [JsonPropertyName("keep")] public string Keep { get; set; } = "stronghold";
}

public sealed class TerrainPainterDef
{
    [JsonPropertyName("trigger")] public string Trigger { get; set; } = "runewake map";
    [JsonPropertyName("style")] public string Style { get; set; } = "top-down painted fantasy atlas map";
    [JsonPropertyName("roads")] public string Roads { get; set; } = "";
    [JsonPropertyName("negative")] public string Negative { get; set; } = "";
}

public sealed class TerrainGridDef
{
    [JsonPropertyName("cells")] public List<string> Cells { get; set; } = new();
}

/// <summary>content/world/terrain.json.</summary>
public sealed class TerrainLexicon
{
    [JsonPropertyName("grid")] public TerrainGridDef Grid { get; set; } = new();
    [JsonPropertyName("painter")] public TerrainPainterDef Painter { get; set; } = new();
    [JsonPropertyName("features")] public Dictionary<string, TerrainFeatureDef> Features { get; set; } = new();
    [JsonPropertyName("biomes")] public Dictionary<string, TerrainBiomeDef> Biomes { get; set; } = new();

    public static TerrainLexicon FromJson(string json)
    {
        var lex = JsonSerializer.Deserialize<TerrainLexicon>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                  ?? throw new InvalidOperationException("terrain json is empty");
        lex.Validate();
        return lex;
    }

    public void Validate()
    {
        if (Features.Count == 0) throw new InvalidOperationException("terrain has no features");
        if (Grid.Cells.Count != 9) throw new InvalidOperationException("terrain grid needs exactly 9 cell names");
        foreach (var (id, f) in Features)
        {
            if (f.Spots.Count == 0) throw new InvalidOperationException($"terrain feature '{id}' has no spots");
            foreach (var s in f.Spots) if (s.Nouns.Count == 0) throw new InvalidOperationException($"terrain feature '{id}' spot '{s.Kind}' has no nouns");
        }
        foreach (var (id, b) in Biomes)
            foreach (var m in b.Menu.Concat(b.Always).Append(b.Keep))
                if (!Features.ContainsKey(m)) throw new InvalidOperationException($"terrain biome '{id}' names unknown feature '{m}'");
    }

    /// <summary>The 3x3 cell a canvas point falls in.</summary>
    public string CellName(double fx, double fy)
    {
        int cx = Math.Clamp((int)(fx * 3), 0, 2), cy = Math.Clamp((int)(fy * 3), 0, 2);
        return Grid.Cells[cy * 3 + cx];
    }

    /// <summary>A built-in lexicon, so the engine still plans terrain when no content file is handed to it.</summary>
    public static TerrainLexicon BuiltIn()
    {
        var lex = new TerrainLexicon
        {
            Grid = new TerrainGridDef { Cells = new() { "north-west", "north", "north-east", "west", "centre", "east", "south-west", "south", "south-east" } },
            Painter = new TerrainPainterDef { Roads = "a faint road enters from the west and ends at the stronghold in the east" },
        };
        lex.Features["mountain_range"] = new TerrainFeatureDef { Paint = "a jagged mountain range", CellsWide = 2, Spots = new()
        {
            new() { Kind = "pass", Nouns = new() { "Pass", "Gap" }, Fits = new() { "Warden", "AreaBoss", "Elite", "Duel" } },
            new() { Kind = "peak", Nouns = new() { "Peak", "Crag" }, Fits = new() { "Elite", "Duel", "Lore" } },
            new() { Kind = "cave", Nouns = new() { "Cave", "Delve" }, Fits = new() { "Dig", "Lore", "Duel" } },
        } };
        lex.Features["forest"] = new TerrainFeatureDef { Paint = "a dense old forest", Spots = new()
        {
            new() { Kind = "heart", Nouns = new() { "Grove", "Deepwood" }, Fits = new() { "Elite", "Duel", "Lore", "Warden" } },
            new() { Kind = "clearing", Nouns = new() { "Clearing", "Glade" }, Fits = new() { "Duel", "Merchant", "Dig" } },
        } };
        lex.Features["river"] = new TerrainFeatureDef { Paint = "a winding river", CellsWide = 2, Spots = new()
        {
            new() { Kind = "ford", Nouns = new() { "Ford", "Crossing" }, Fits = new() { "Merchant", "Duel", "Warden" } },
            new() { Kind = "bend", Nouns = new() { "Bend", "Reach" }, Fits = new() { "Duel", "Dig" } },
        } };
        lex.Features["ruins"] = new TerrainFeatureDef { Paint = "old ruins", Spots = new()
        {
            new() { Kind = "ruin", Nouns = new() { "Ruins", "Stones" }, Fits = new() { "Dig", "Lore", "Duel", "Elite" } },
            new() { Kind = "tower", Nouns = new() { "Watch", "Tower" }, Fits = new() { "Warden", "Elite", "Duel" } },
        } };
        lex.Features["stronghold"] = new TerrainFeatureDef { Paint = "a dark fortress", Spots = new()
        {
            new() { Kind = "gate", Nouns = new() { "Gate", "Hold" }, Fits = new() { "Warden", "AreaBoss", "Elite" } },
            new() { Kind = "yard", Nouns = new() { "Yard", "Market" }, Fits = new() { "Merchant", "Duel" } },
        } };
        return lex;
    }
}

/// <summary>One named spot on a feature — where a place can stand, and what it is called there.</summary>
public sealed class TerrainSpot
{
    public string Feature { get; init; } = "";
    public string Kind { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public List<string> Nouns { get; init; } = new();
    public List<string> Fits { get; init; } = new();
    public bool Taken { get; set; }
    public bool Fits_(string kind) => Fits.Count == 0 || Fits.Contains(kind);
}

/// <summary>One painted feature: what it is, where it is on the canvas, and its spots.</summary>
public sealed class TerrainFeature
{
    public string Id { get; init; } = "";
    public string Paint { get; init; } = "";
    /// <summary>Bounding box on the canvas (pixels).</summary>
    public int X0 { get; init; }
    public int Y0 { get; init; }
    public int X1 { get; init; }
    public int Y1 { get; init; }
    /// <summary>The 3x3 cells it covers, in painter words ("north-west", or "north-west and north").</summary>
    public string Cells { get; init; } = "";
    public bool IsKeep { get; init; }
    public List<TerrainSpot> Spots { get; init; } = new();
    public int CX => (X0 + X1) / 2;
    public int CY => (Y0 + Y1) / 2;
}

/// <summary>The terrain of one page, and the sentence that paints it.</summary>
public sealed class TerrainPlan
{
    public int Width { get; init; } = 1000;
    public int Height { get; init; } = 600;
    public List<TerrainFeature> Features { get; init; } = new();
    public string Prompt { get; init; } = "";
    public string NegativePrompt { get; init; } = "";
    public IEnumerable<TerrainSpot> Spots => Features.SelectMany(f => f.Spots);
    public TerrainFeature? Keep => Features.FirstOrDefault(f => f.IsKeep);

    /// <summary>
    /// The nearest free spot that fits <paramref name="kind"/>, within <paramref name="reach"/> of (x,y) — or null.
    /// Spots further away in x cost double, so a column of places stays a column.
    /// </summary>
    public TerrainSpot? Claim(int x, int y, string kind, double reach)
    {
        TerrainSpot? best = null; double bestD = double.MaxValue;
        foreach (var s in Spots)
        {
            if (s.Taken || !s.Fits_(kind)) continue;
            double dx = (s.X - x) * 1.5, dy = s.Y - y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestD && d <= reach) { best = s; bestD = d; }
        }
        if (best != null) best.Taken = true;
        return best;
    }

    /// <summary>The feature whose box is nearest to (x,y) — for a place that found no free spot.</summary>
    public TerrainFeature? Nearest(int x, int y)
    {
        TerrainFeature? best = null; double bestD = double.MaxValue;
        foreach (var f in Features)
        {
            double dx = Math.Max(0, Math.Max(f.X0 - x, x - f.X1)), dy = Math.Max(0, Math.Max(f.Y0 - y, y - f.Y1));
            double d = dx * dx + dy * dy;
            if (d < bestD) { best = f; bestD = d; }
        }
        return best;
    }
}

public static class TerrainPlanner
{
    /// <summary>
    /// Lay out the terrain of one page. Deterministic for a given rng. The canvas is the world page's
    /// 1000 x 600; the keep feature always stands in the east where the guardian waits.
    /// </summary>
    public static TerrainPlan Plan(SeededRng rng, TerrainLexicon lex, string biomeId, string biomeName, string strata, int width = 1000, int height = 600, int places = 50)
    {
        var bdef = lex.Biomes.TryGetValue(biomeId, out var b) ? b : new TerrainBiomeDef { Menu = lex.Features.Keys.Take(4).ToList(), Keep = lex.Features.ContainsKey("stronghold") ? "stronghold" : lex.Features.Keys.First() };
        // ── which features ────────────────────────────────────────────
        var picks = new List<string>(bdef.Always);
        int want = 5 + rng.NextInt(3);   // 5–7 features besides the keep, so most of the nine cells carry something
        var menu = bdef.Menu.Count > 0 ? bdef.Menu : lex.Features.Keys.ToList();
        for (int tries = 0; picks.Count < want && tries < 40; tries++)
        {
            var m = menu[rng.NextInt(menu.Count)];
            if (picks.Count(p => p == m) >= 2) continue;   // never three of a kind
            picks.Add(m);
        }
        // ── which cells ───────────────────────────────────────────────
        // Eight free cells (the east-centre cell belongs to the keep). Wide features take their cell plus
        // a free neighbour to the east or south. Cells are claimed in a shuffled order so pages differ.
        var free = new HashSet<int>(Enumerable.Range(0, 9)); free.Remove(5);
        var order = Enumerable.Range(0, 9).Where(c => c != 5).ToList();
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); (order[i], order[j]) = (order[j], order[i]); }
        double cw = width / 3.0, ch = height / 3.0;
        var feats = new List<TerrainFeature>();
        int oi = 0;
        foreach (var id in picks)
        {
            var def = lex.Features[id];
            int cell = -1;
            while (oi < order.Count && !free.Contains(order[oi])) oi++;
            if (oi < order.Count) cell = order[oi];
            if (cell < 0) cell = order[rng.NextInt(order.Count)];   // more features than cells: share
            free.Remove(cell);
            int cx = cell % 3, cy = cell / 3;
            int x0 = (int)(cx * cw), y0 = (int)(cy * ch), x1 = (int)((cx + 1) * cw), y1 = (int)((cy + 1) * ch);
            string cells = lex.Grid.Cells[cell];
            if (def.CellsWide >= 2)
            {
                int east = cx < 2 ? cell + 1 : -1, south = cy < 2 ? cell + 3 : -1;
                int ext = east >= 0 && free.Contains(east) ? east : south >= 0 && free.Contains(south) ? south : -1;
                if (ext >= 0)
                {
                    free.Remove(ext);
                    int ex = ext % 3, ey = ext / 3;
                    x0 = Math.Min(x0, (int)(ex * cw)); y0 = Math.Min(y0, (int)(ey * ch));
                    x1 = Math.Max(x1, (int)((ex + 1) * cw)); y1 = Math.Max(y1, (int)((ey + 1) * ch));
                    cells = lex.Grid.Cells[cell] + " and " + lex.Grid.Cells[ext];
                }
            }
            feats.Add(MakeFeature(rng, id, def, x0, y0, x1, y1, cells, false, places, width * height));
        }
        // ── the keep, in the east ─────────────────────────────────────
        var keepDef = lex.Features[bdef.Keep];
        feats.Add(MakeFeature(rng, bdef.Keep, keepDef, (int)(2 * cw), (int)ch, width, (int)(2 * ch), lex.Grid.Cells[5], true, places, width * height));

        var prompt = TerrainPrompt.Build(lex, feats, biomeName, strata);
        return new TerrainPlan { Width = width, Height = height, Features = feats, Prompt = prompt, NegativePrompt = lex.Painter.Negative };
    }

    private static TerrainFeature MakeFeature(SeededRng rng, string id, TerrainFeatureDef def, int x0, int y0, int x1, int y1, string cells, bool keep, int places, long canvasArea)
    {
        // Spots: every kind the feature has at least once, then more of them in proportion to the feature's share
        // of the canvas, so a page with 60 places has spots enough for all of them (about 1.7 per place), spread
        // through the box with an inset so places stay inside the paint.
        int w = x1 - x0, h = y1 - y0;
        int want = Math.Max(5, (int)Math.Ceiling(places * 1.7 * ((double)w * h / canvasArea)) + rng.NextInt(3));
        var spots = new List<TerrainSpot>();
        var kinds = new List<TerrainSpotDef>(def.Spots);
        while (kinds.Count < want) kinds.Add(def.Spots[rng.NextInt(def.Spots.Count)]);
        // a jittered grid of candidate points, so spots never pile up
        int gx = Math.Max(2, (int)Math.Round(Math.Sqrt(want * (double)w / Math.Max(1, h)))), gy = Math.Max(2, (int)Math.Ceiling(want / (double)gx));
        var cells9 = new List<(int, int)>();
        for (int j = 0; j < gy; j++) for (int i = 0; i < gx; i++) cells9.Add((i, j));
        for (int i = cells9.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); (cells9[i], cells9[j]) = (cells9[j], cells9[i]); }
        for (int k = 0; k < want && k < cells9.Count; k++)
        {
            var (i, j) = cells9[k];
            double sx = (double)w / gx, sy = (double)h / gy;
            int px = x0 + (int)(sx * (i + 0.5) + (rng.NextInt(1000) / 1000.0 - 0.5) * sx * 0.6);
            int py = y0 + (int)(sy * (j + 0.5) + (rng.NextInt(1000) / 1000.0 - 0.5) * sy * 0.6);
            px = Math.Clamp(px, x0 + 18, x1 - 18); py = Math.Clamp(py, y0 + 18, y1 - 18);
            var kd = kinds[k];
            spots.Add(new TerrainSpot { Feature = id, Kind = kd.Kind, X = px, Y = py, Nouns = kd.Nouns, Fits = kd.Fits });
        }
        return new TerrainFeature { Id = id, Paint = def.Paint, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Cells = cells, IsKeep = keep, Spots = spots };
    }
}

public static class TerrainPrompt
{
    /// <summary>
    /// The sentence the painter follows. It names every feature in its 3x3 cell(s), west to east, so the
    /// painting and the plan agree on where things are; the game then puts its places on those features.
    /// </summary>
    public static string Build(TerrainLexicon lex, IReadOnlyList<TerrainFeature> feats, string biomeName, string strata)
    {
        var sb = new StringBuilder();
        sb.Append(lex.Painter.Trigger).Append(", ").Append(lex.Painter.Style).Append(". ");
        sb.Append("The land of ").Append(biomeName).Append(" (").Append(StrataMood(strata)).Append("). ");
        sb.Append("Laid out on a three by three grid, west to east: ");
        bool first = true;
        foreach (var f in feats.OrderBy(f => f.X0).ThenBy(f => f.Y0))
        {
            if (!first) sb.Append("; ");
            first = false;
            sb.Append("in the ").Append(f.Cells).Append(", ").Append(f.Paint);
            if (f.IsKeep) sb.Append(" — the road's end");
        }
        sb.Append(". ");
        if (!string.IsNullOrEmpty(lex.Painter.Roads)) sb.Append(char.ToUpperInvariant(lex.Painter.Roads[0])).Append(lex.Painter.Roads.AsSpan(1)).Append(". ");
        sb.Append("Everything else is open country in the biome's own colours. The whole canvas is land seen from above; nothing is cut off at the edges.");
        return sb.ToString();
    }

    public static string StrataMood(string strata) => strata?.ToUpperInvariant() switch
    {
        "VERDANT" => "deep greens, moss and old wood",
        "EMBER" => "ash greys, rust and ember glow",
        "TIDE" => "slate blues, sea-green and salt white",
        "HOLLOW" => "bone, bruise-purple and black water",
        "DAWN" => "gold, pale sand and warm stone",
        _ => "muted earth tones",
    };
}

public static class TerrainNames
{
    /// <summary>"Thornlit Pass": a biome adjective and the spot's own noun.</summary>
    public static string Place(SeededRng rng, IReadOnlyList<string> adjectives, TerrainSpot spot, HashSet<string> used)
    {
        for (int tries = 0; tries < 12; tries++)
        {
            string adj = adjectives.Count == 0 ? "Quiet" : adjectives[rng.NextInt(adjectives.Count)];
            string noun = spot.Nouns[rng.NextInt(spot.Nouns.Count)];
            string name = $"{adj} {noun}";
            if (used.Add(name)) return name;
        }
        string fb = $"{(adjectives.Count == 0 ? "Quiet" : adjectives[0])} {spot.Nouns[0]} {used.Count + 1}";
        used.Add(fb);
        return fb;
    }

    /// <summary>Which spot kinds a place of this kind likes — BlipKind name, or "Merchant"/"Dig" for events.</summary>
    public static string FitKey(BlipKind kind, EventKind? ev) => kind switch
    {
        BlipKind.Event => ev switch { EventKind.Merchant => "Merchant", EventKind.Dig => "Dig", _ => "Duel" },
        BlipKind.Gateway => "Lore",
        _ => kind.ToString(),
    };
}
