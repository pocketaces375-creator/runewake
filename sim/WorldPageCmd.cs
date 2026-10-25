using System.Text.Json;
using Runewake.Engine.World;

namespace Runewake.Sim;

/// <summary>
/// FABLE-105: `world-page` — one page of the shared world as the painter and the map need it: the terrain plan
/// (features in their 3x3 cells, every spot), the prompt that paints it, and every place with its terrain and name.
///
///   Runewake.Sim world-page --biome elvenwood --instance 0 --page 1 [--root <repo>] [--prompt-only]
///
/// tools/world_map_paint.py reads this JSON, paints the page with the LoRA, and files the painting under the page key.
/// </summary>
public static class WorldPageCmd
{
    public static int Run(string[] args)
    {
        string root = ".", biome = "elvenwood"; int instance = 0, page = 0; bool promptOnly = false;
        for (int i = 1; i < args.Length; i++)
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = args[++i]; break;
                case "--biome" when i + 1 < args.Length: biome = args[++i]; break;
                case "--instance" when i + 1 < args.Length && int.TryParse(args[i + 1], out var inst): instance = inst; i++; break;
                case "--page" when i + 1 < args.Length && int.TryParse(args[i + 1], out var pg): page = pg; i++; break;
                case "--prompt-only": promptOnly = true; break;
            }
        var content = Path.Combine(root, "content", "world");
        var atlas = AtlasDef.FromJson(File.ReadAllText(Path.Combine(content, "atlas.json")));
        var terrainPath = Path.Combine(content, "terrain.json");
        var lex = File.Exists(terrainPath) ? TerrainLexicon.FromJson(File.ReadAllText(terrainPath)) : TerrainLexicon.BuiltIn();
        var gen = new WorldGenerator(atlas, lex);
        var p = gen.Page(new PageAddress(biome, instance, page));
        if (promptOnly) { Console.WriteLine(p.Terrain!.Prompt); return 0; }
        var o = new
        {
            key = p.Key,
            biome = p.BiomeName,
            strata = p.Strata,
            canvas = new { w = p.Terrain!.Width, h = p.Terrain.Height },
            prompt = p.Terrain.Prompt,
            negative = p.Terrain.NegativePrompt,
            features = p.Terrain.Features.Select(f => new { f.Id, f.Cells, f.IsKeep, box = new[] { f.X0, f.Y0, f.X1, f.Y1 }, f.Paint, spots = f.Spots.Select(s => new { s.Kind, s.X, s.Y, s.Taken }) }),
            places = p.Blips.Select(b => new { b.Index, kind = b.Kind.ToString(), ev = b.Event?.ToString(), b.Name, b.X, b.Y, b.Column, b.Terrain, b.Spot, b.Next }),
        };
        Console.WriteLine(JsonSerializer.Serialize(o, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
