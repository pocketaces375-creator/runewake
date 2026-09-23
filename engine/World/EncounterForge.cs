using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Engine.World;

/// <summary>
/// FABLE-020: turns a fight blip into a playable EncounterDef — deck, name,
/// difficulty levers, rewards, drops. Deterministic per blip id, so every
/// player meets the same foe at the same place.
///
/// Port of tools/region_gen.py's rules, moved into the game so zones can be
/// generated on demand instead of shipped as files:
///   * 30 unique cards, weighted to the biome's strata (60% / 25% / 15% other)
///   * rarity mix slides from "early" (70/20/10/0) to "boss" (15/25/35/25)
///     as difficulty rises
///   * deeper = stronger cards (a stat budget calibrated on the authored
///     Fallow Reach, rising with depth), more Vigor
///   * drop table at the standard rates (C .40 / U .25 / R .10 / Relic .03),
///     bosses guarantee their best card
/// </summary>
public static class EncounterForge
{
    private static readonly double[] Early = { 0.70, 0.20, 0.10, 0.00 };
    private static readonly double[] Boss = { 0.15, 0.25, 0.35, 0.25 };
    private static readonly Rarity[] Rarities = { Rarity.COMMON, Rarity.UNCOMMON, Rarity.RARE, Rarity.RELIC };
    private static readonly Dictionary<Rarity, double> DropRate = new()
    {
        [Rarity.COMMON] = 0.40, [Rarity.UNCOMMON] = 0.25, [Rarity.RARE] = 0.10, [Rarity.RELIC] = 0.03,
    };

    public const int DeckSize = 30;

    /// <summary>True if this blip is a fight.</summary>
    public static bool IsFight(Blip b) => b.Kind is BlipKind.Duel or BlipKind.Elite or BlipKind.Warden or BlipKind.AreaBoss;

    /// <summary>Cards an encounter may use: real, playable, not tutorial, not tokens/artifacts.</summary>
    public static List<CardDef> PlayablePool(IEnumerable<CardDef> all) =>
        all.Where(c => c.Type is not (CardType.TOKEN or CardType.ARTIFACT)
                       && !c.Id.StartsWith("tut_")
                       && !string.Equals(c.Set, "tutorial", StringComparison.OrdinalIgnoreCase))
           .OrderBy(c => c.Id, StringComparer.Ordinal)   // registry order must not matter
           .ToList();

    public static EncounterDef For(AtlasDef atlas, WorldPage page, Blip blip, IReadOnlyList<CardDef> pool)
    {
        if (!IsFight(blip)) throw new ArgumentException($"{blip.Id} is a {blip.Kind}, not a fight");
        var biome = atlas.Biome(page.Address.Biome);
        var rng = new SeededRng(StableHash.Of(atlas.Seed, atlas.Version, "enc", blip.Id));
        double d = blip.Difficulty;

        var deck = BuildDeck(rng, pool, biome, d);
        int inst = page.Address.Instance;

        // Calibrated with `Runewake.Sim world-soak` against the class starter
        // decks so the first Elvenwood page plays like the authored Fallow
        // Reach (~70% starter win rate) and the climb is felt area by area.
        int vigor = 25 + Math.Min(40, inst * 2 + page.Address.Page / 3) + blip.Kind switch
        {
            BlipKind.Elite => 3,
            BlipKind.Warden => 5,
            BlipKind.AreaBoss => 10,
            _ => 0,
        };
        int attune = Math.Min(3, inst / 5) + (blip.Kind == BlipKind.AreaBoss ? 1 : 0);

        string foe = biome.Foes.Count > 0 ? biome.Foes[rng.NextInt(biome.Foes.Count)] : "Wanderer";
        string title = biome.FoeTitles.Count > 0 ? biome.FoeTitles[rng.NextInt(biome.FoeTitles.Count)] : "";
        string name = blip.Kind switch
        {
            BlipKind.Warden or BlipKind.AreaBoss => blip.Name,
            BlipKind.Elite => $"{title} {foe}".Trim(),
            _ => rng.NextBool(0.35) && title.Length > 0 ? $"{title} {foe}" : $"{foe} of {blip.Name}",
        };

        var drops = deck.Select(id => pool.First(c => c.Id == id))
                        .Select(c => new DropEntry { CardId = c.Id, Rate = DropRate[c.Rarity] })
                        .OrderByDescending(x => x.Rate).ThenBy(x => x.CardId, StringComparer.Ordinal)
                        .ToList();
        string? cardReward = null;
        if (blip.Kind is BlipKind.Warden or BlipKind.AreaBoss)
        {
            var best = deck.Select(id => pool.First(c => c.Id == id))
                           .OrderByDescending(c => (int)c.Rarity).ThenByDescending(c => c.PowerScore ?? 0).ThenBy(c => c.Id, StringComparer.Ordinal)
                           .First();
            drops.Insert(0, new DropEntry { CardId = best.Id, Rate = 1.0 });
            if (blip.Kind == BlipKind.AreaBoss) cardReward = best.Id;
        }

        string cls = biome.Classes.Count > 0 ? biome.Classes[rng.NextInt(biome.Classes.Count)] : "";
        return new EncounterDef
        {
            Id = "enc:" + blip.Id,
            Name = name,
            Deck = deck,
            Class = string.IsNullOrEmpty(cls) ? null : cls,
            ShardReward = (int)Math.Round(20 + d * 120 + (blip.Kind == BlipKind.AreaBoss ? 150 : blip.Kind == BlipKind.Warden ? 60 : 0)),
            DigChargeReward = blip.Kind is BlipKind.Warden or BlipKind.AreaBoss ? 1 : 0,
            FragmentReward = $"{biome.Strata.ToLowerInvariant()}:{1 + (int)(d * 3)}",
            CardReward = cardReward,
            Modifier = blip.Kind switch
            {
                BlipKind.Elite => $"Elite: starts with {vigor} Vigor and +{attune} Attunement",
                BlipKind.Warden => $"Warden of {page.BiomeName}",
                BlipKind.AreaBoss => $"Master of {page.BiomeName} #{inst + 1}",
                _ => null,
            },
            EnemyVigor = vigor == 25 ? null : vigor,
            EnemyBonusAttunement = attune,
            DialogueIntro = new List<string> { IntroLine(rng, blip, page) },
            Drops = drops,
        };
    }

    private static string IntroLine(SeededRng rng, Blip blip, WorldPage page) => blip.Kind switch
    {
        BlipKind.AreaBoss => $"The heart of {page.BiomeName} stirs. {blip.Name} rises to meet you.",
        BlipKind.Warden => $"{blip.Name} bars the road onward. Beat them and the way opens.",
        BlipKind.Elite => $"Something stronger than the rest waits at {blip.Name}.",
        _ => new[]
        {
            $"A shape moves at {blip.Name}.",
            $"You are not alone at {blip.Name}.",
            $"The path to {blip.Name} is guarded.",
            $"Steel rings out across {blip.Name}.",
        }[rng.NextInt(4)],
    };

    private static List<string> BuildDeck(SeededRng rng, IReadOnlyList<CardDef> pool, BiomeDef biome, double d)
    {
        var weights = new double[4];
        for (int i = 0; i < 4; i++) weights[i] = Early[i] + (Boss[i] - Early[i]) * Math.Clamp(d, 0, 1);

        bool Primary(CardDef c) => c.Strata.ToString() == biome.Strata;
        bool Secondary(CardDef c) => biome.Strata2 != null && c.Strata.ToString() == biome.Strata2;
        var groups = new[]
        {
            pool.Where(Primary).ToList(),
            pool.Where(Secondary).ToList(),
            pool.Where(c => !Primary(c) && !Secondary(c)).ToList(),
        };
        double[] groupW = { 0.60, biome.Strata2 == null ? 0 : 0.25, 0.15 };

        // Stat budget, calibrated on the authored decks: the Fallow Reach runs
        // from Thornbark (creature Attack+Vigor 5.8, avg cost 2.6) to Aelin, the
        // Last Steward (6.6, 3.6). Each pick samples a few legal candidates and
        // keeps the one that holds the deck's running average closest to the
        // target, so a generated deck is as strong as its depth says. (PowerScore
        // is missing on many cards, so it cannot be the yardstick.)
        double targetPower = 5.0 + 2.3 * Math.Clamp(d, 0, 1);
        double targetCost = 2.4 + 1.3 * Math.Clamp(d, 0, 1);
        var chosen = new List<string>(DeckSize);
        var used = new HashSet<string>();
        double sumP = 0, sumC = 0;
        int guard = 0;
        int target = Math.Min(DeckSize, pool.Count);
        while (chosen.Count < target && guard++ < 5000)
        {
            int g = Roll(rng, groupW);
            int ri = Roll(rng, weights);
            List<CardDef> cands = new();
            // Same group at the rolled rarity, then any group at that rarity,
            // then the nearest rarity — never jump straight to "anything".
            for (int dr = 0; dr < 4 && cands.Count == 0; dr++)
                foreach (int r in new[] { ri - dr, ri + dr }.Distinct())
                {
                    if (r < 0 || r > 3) continue;
                    cands.AddRange(groups[g].Where(c => c.Rarity == Rarities[r] && !used.Contains(c.Id)));
                    if (cands.Count == 0) cands.AddRange(pool.Where(c => c.Rarity == Rarities[r] && !used.Contains(c.Id)));
                }
            if (cands.Count == 0) break;
            CardDef? pick = null; double best = double.MaxValue;
            int n = chosen.Count + 1;
            for (int k = 0; k < Math.Min(4, cands.Count); k++)
            {
                var c = cands[rng.NextInt(cands.Count)];
                double score = Math.Abs((sumP + Value(c)) / n - targetPower)
                             + Math.Abs((sumC + c.Cost) / n - targetCost);
                if (score < best) { best = score; pick = c; }
            }
            used.Add(pick!.Id);
            chosen.Add(pick.Id);
            sumP += Value(pick);
            sumC += pick.Cost;
        }
        return chosen;
    }

    /// <summary>A card's weight on the board: a creature's Attack+Vigor, else what its cost buys.</summary>
    private static double Value(CardDef c) =>
        c.Type == CardType.CREATURE ? (c.Attack ?? 0) + (c.Vigor ?? 0) : 2.0 * c.Cost + 1.0;

    private static int Roll(SeededRng rng, double[] w)
    {
        double total = w.Sum();
        if (total <= 0) return 0;
        double r = rng.NextU64() / (double)ulong.MaxValue * total;
        for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r < 0) return i; }
        return w.Length - 1;
    }
}
