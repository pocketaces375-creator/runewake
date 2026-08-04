using System.IO;
using Runewake.Engine.Cards;
using Xunit;

namespace Runewake.Tests.Cards;

/// <summary>
/// Snapshot tests for the RulesTextRenderer.
/// Each card is rendered and compared to an expected string.
/// When updating cards, update these strings to match.
/// </summary>
public class RulesTextSnapshotTests
{
    private const string ContentRoot = "../../../../content/cards";

    [Fact]
    public void Root_Warden()
    {
        var card = LoadById("vrd_c_root_warden");
        Assert.Equal(
            "2/4 — Guard\n" +
            "When this enters play: Give all adjacent ally creatures +1 vigor\n" +
            "\"The grove keeps its own ledgers, and it does not forgive debts.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Verdant_Sproutling()
    {
        var card = LoadById("vrd_c_verdant_sproutling");
        Assert.Equal(
            "1/2\n" +
            "\"Life finds a way through every crack.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Thornbark_Defender()
    {
        var card = LoadById("vrd_c_thornbark_defender");
        Assert.Equal(
            "2/6 — Guard, Fragile\n" +
            "\"A wall of living thorns that withers when the battle ends.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Wildwood_Stalker()
    {
        var card = LoadById("vrd_c_wildwood_stalker");
        Assert.Equal(
            "3/2\n" +
            "\"It hunts between the roots, patient and swift.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Grove_Healer()
    {
        var card = LoadById("vrd_u_grove_healer");
        Assert.Equal(
            "1/3\n" +
            "When this enters play: Heal 3 from ally creature\n" +
            "\"The touch of living bark closes wounds in moments.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Canopy_Archer()
    {
        var card = LoadById("vrd_u_canopy_archer");
        Assert.Equal(
            "3/3 — Reach\n" +
            "\"She waits in the high branches, arrow nocked.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Elder_Treant()
    {
        var card = LoadById("vrd_u_elder_treant");
        Assert.Equal(
            "5/7 — Guard, Rooted\n" +
            "\"Older than the barrow, rooted before the first stone was laid.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Saphoof_Charger()
    {
        var card = LoadById("vrd_u_saphoof_charger");
        Assert.Equal(
            "5/4 — Pierce\n" +
            "\"When the great beast runs, the ground remembers.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Bloomweaver()
    {
        var card = LoadById("vrd_r_bloomweaver");
        Assert.Equal(
            "1/4\n" +
            "At the start of your turn: Summon a Verdant Bud\n" +
            "\"Each morning, a new bloom unfolds in her footsteps.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Undergrowth_Eruption()
    {
        var card = LoadById("vrd_r_undergrowth_eruption");
        Assert.Equal(
            "Deal 2 damage to all enemy creatures\n" +
            "\"The soil remembers the blood it has drunk.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Natures_Renewal()
    {
        var card = LoadById("vrd_r_natures_renewal");
        Assert.Equal(
            "Heal 2 from all ally creatures\n" +
            "\"Spring comes even to the deepest shadows.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Heartwood_Relic()
    {
        var card = LoadById("vrd_x_heartwood_relic");
        Assert.Equal(
            "Sealed\n" +
            "⛭ Identify: turn ≥ 5\n" +
            "Passive: Give all ally creatures +1 vigor\n" +
            "\"A splinter of the First Tree, still warm with life.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Verdant_Bud_Token()
    {
        var card = LoadById("vrd_t_verdant_bud");
        Assert.Equal("1/1 — Swift", RulesTextRenderer.Render(card));
    }

    // ——— EMBER ———

    [Fact]
    public void Cinder_Runner()
    {
        var card = LoadById("emb_c_cinder_runner");
        Assert.Equal(
            "3/1 — Swift\n" +
            "\"Forge-children learned to run before they learned to breathe.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Ember_Hound()
    {
        var card = LoadById("emb_c_ember_hound");
        Assert.Equal(
            "2/1 — Swift\n" +
            "\"Pack-forged and flame-tempered.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Flame_Javelin()
    {
        var card = LoadById("emb_c_flame_javelin");
        Assert.Equal(
            "Deal 2 damage to enemy creature\n" +
            "\"A spear of fire, thrown from the heart of the forge.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Forgeguard_Berserker()
    {
        var card = LoadById("emb_c_forgeguard_berserker");
        Assert.Equal(
            "4/3\n" +
            "\"He fights like the fire — without mercy, without memory.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Wildfire_Adept()
    {
        var card = LoadById("emb_u_wildfire_adept");
        Assert.Equal(
            "2/2\n" +
            "When you play a Ritual: Deal 1 damage to enemy creature\n" +
            "\"Every spell she casts fans the flames higher.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Lava_Serpent()
    {
        var card = LoadById("emb_u_lava_serpent");
        Assert.Equal(
            "5/3 — Pierce, Fragile\n" +
            "\"Molten blood and a temper to match.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Searing_Blast()
    {
        var card = LoadById("emb_u_searing_blast");
        Assert.Equal(
            "Deal 4 damage to the enemy\n" +
            "\"The heat of a dying star, focused to a point.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Cinderstorm_Elemental()
    {
        var card = LoadById("emb_u_cinderstorm_elemental");
        Assert.Equal(
            "4/4\n" +
            "When this dies: Deal 2 damage to all enemy creatures\n" +
            "\"Even in dying, it burns.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Magma_Forger()
    {
        var card = LoadById("emb_r_magma_forger");
        Assert.Equal(
            "2/3\n" +
            "When this enters play: Give all ally creatures +1 attack\n" +
            "\"He hammers strength into every ally's blade.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Inferno_Burst()
    {
        var card = LoadById("emb_r_inferno_burst");
        Assert.Equal(
            "Deal 5 damage to the enemy. Deal 1 damage to all enemy creatures\n" +
            "\"The forge-gods demand sacrifice.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Phoenix_Ash()
    {
        var card = LoadById("emb_r_phoenix_ash");
        Assert.Equal(
            "4/4 — Unearth, Echo\n" +
            "\"From ash, she rises. From ash, she burns again.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void The_Last_Ember()
    {
        var card = LoadById("emb_x_the_last_ember");
        Assert.Equal(
            "Sealed\n" +
            "⛭ Identify: a creature was damaged this turn\n" +
            "At the start of your turn: Deal 1 damage to the enemy\n" +
            "\"The last spark of a world that burned too brightly.\"",
            RulesTextRenderer.Render(card));
    }

    // ——— TIDE ———

    [Fact]
    public void Silt_Reader()
    {
        var card = LoadById("tid_c_silt_reader");
        Assert.Equal(
            "2/5\n" +
            "When this enters play: Excavate 3\n" +
            "At the start of your turn: if your barrow has 4+ cards, Draw 1 card\n" +
            "\"She read the riverbed the way her mother read faces.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Tidal_Scholar()
    {
        var card = LoadById("tid_c_tidal_scholar");
        Assert.Equal(
            "1/3\n" +
            "When this enters play: Draw 1 card\n" +
            "\"Knowledge flows like water — endlessly, unstoppably.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Deep_One()
    {
        var card = LoadById("tid_c_deep_one");
        Assert.Equal(
            "3/3\n" +
            "\"From the abyss it rises, silent and patient.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Abyssal_Gaze()
    {
        var card = LoadById("tid_c_abyssal_gaze");
        Assert.Equal(
            "Excavate 2\n" +
            "\"The depths see you as clearly as you see them.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Brine_Witch()
    {
        var card = LoadById("tid_u_brine_witch");
        Assert.Equal(
            "3/3\n" +
            "When this enters play: Bury 2\n" +
            "\"Salt and spell, wrought together.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Coral_Guardian()
    {
        var card = LoadById("tid_u_coral_guardian");
        Assert.Equal(
            "3/6 — Guard\n" +
            "\"A living reef that remembers every ship that passed.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Memory_Tides()
    {
        var card = LoadById("tid_u_memory_tides");
        Assert.Equal(
            "Excavate 2. Discard 1\n" +
            "\"The tide brings, and the tide takes away.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Whirlpool_Elemental()
    {
        var card = LoadById("tid_c_whirlpool_elemental");
        Assert.Equal(
            "2/4\n" +
            "When this dies: Return enemy creature to hand\n" +
            "\"It unravels into foam, dragging you down with it.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Hydrokinetic_Adept()
    {
        var card = LoadById("tid_r_hydrokinetic_adept");
        Assert.Equal(
            "2/3\n" +
            "When an ally dies: Draw 1 card\n" +
            "\"Every drop that falls tells her a story.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Flood_of_Secrets()
    {
        var card = LoadById("tid_r_flood_of_secrets");
        Assert.Equal(
            "the enemy discards 2\n" +
            "\"The tide washes away all hidden things.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Sunken_Leviathan()
    {
        var card = LoadById("tid_r_sunken_leviathan");
        Assert.Equal(
            "7/7\n" +
            "\"It sleeps in the deep, dreaming of cities it swallowed.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Tidal_Seal()
    {
        var card = LoadById("tid_x_tidal_seal");
        Assert.Equal(
            "Sealed\n" +
            "⛭ Identify: you have 8+ cards in hand\n" +
            "Passive: Give all ally creatures +2 vigor\n" +
            "\"Bound with the seal of the deep, holding back the flood.\"",
            RulesTextRenderer.Render(card));
    }

    // ——— HOLLOW ———

    [Fact]
    public void Gravewrit_Thrall()
    {
        var card = LoadById("hol_c_gravewrit_thrall");
        Assert.Equal(
            "4/2 — Unearth\n" +
            "When this dies: Deal 1 damage to the enemy. Bury 1\n" +
            "\"Its name was scraped off the stone. It came anyway.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Skeletal_Reaver()
    {
        var card = LoadById("hol_c_skeletal_reaver");
        Assert.Equal(
            "2/1\n" +
            "\"Bones that remember the sword.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Deathspeaker()
    {
        var card = LoadById("hol_c_deathspeaker");
        Assert.Equal(
            "2/3\n" +
            "At the end of your turn: Deal 1 damage to all damaged enemy creatures\n" +
            "\"He whispers to the wounded, promising the quiet.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Bone_Shard_Volley()
    {
        var card = LoadById("hol_c_bone_shard_volley");
        Assert.Equal(
            "Deal 3 damage to enemy creature\n" +
            "\"The dead do not miss.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Crypt_Crawler()
    {
        var card = LoadById("hol_u_crypt_crawler");
        Assert.Equal(
            "4/3\n" +
            "When this dies: Excavate 2\n" +
            "\"It drags itself from the dark, clutching forgotten things.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Soul_Harvest()
    {
        var card = LoadById("hol_u_soul_harvest");
        Assert.Equal(
            "Destroy exhausted ally creature. Gain 3 attunement\n" +
            "\"The barrow gives. And takes.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Barrow_Revenant()
    {
        var card = LoadById("hol_u_barrow_revenant");
        Assert.Equal(
            "5/5 — Unearth\n" +
            "\"It rose when the barrow was unsealed. It will not go back.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Ossuary_Guard()
    {
        var card = LoadById("hol_c_ossuary_guard");
        Assert.Equal(
            "1/4 — Guard\n" +
            "\"Bone-walls remember every siege.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Wraith_Stalker()
    {
        var card = LoadById("hol_r_wraith_stalker");
        Assert.Equal(
            "3/2 — Venom, Unearth\n" +
            "\"One touch is all it needs.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Curse_of_Binding()
    {
        var card = LoadById("hol_r_curse_of_binding");
        Assert.Equal(
            "Silence enemy creature. Deal 2 damage to enemy creature\n" +
            "\"Words that bind the soul and break the will.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Hollow_Herald()
    {
        var card = LoadById("hol_r_hollow_herald");
        Assert.Equal(
            "5/6\n" +
            "When this enters play: Unbury 2\n" +
            "\"Her voice echoes from the barrow, calling the buried home.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void The_Black_Barrow()
    {
        var card = LoadById("hol_x_the_black_barrow");
        Assert.Equal(
            "Sealed\n" +
            "⛭ Identify: your barrow has 3+ cards\n" +
            "Passive: Give all enemy creatures -1 attack\n" +
            "\"The barrow's hunger reaches beyond the grave.\"",
            RulesTextRenderer.Render(card));
    }

    // ——— DAWN ———

    [Fact]
    public void Sealing_Light()
    {
        var card = LoadById("dwn_r_sealing_light");
        Assert.Equal(
            "Grant chosen ally creature Ward. Heal 2 from chosen ally creature\n" +
            "\"The wardens did not build doors. They built reasons not to open them.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Dawn_Warder()
    {
        var card = LoadById("dwn_c_dawn_warder");
        Assert.Equal(
            "1/3 — Guard\n" +
            "\"The first light holds the line.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Sunblade_Recruit()
    {
        var card = LoadById("dwn_c_sunblade_recruit");
        Assert.Equal(
            "3/3\n" +
            "\"Steel and sunlight, sworn to the covenant.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Golden_Retainer()
    {
        var card = LoadById("dwn_c_golden_retainer");
        Assert.Equal(
            "3/4\n" +
            "When this enters play: Give all adjacent ally creatures +1/+1\n" +
            "\"Gold and duty, inseparable in service.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Purifying_Light()
    {
        var card = LoadById("dwn_u_purifying_light");
        Assert.Equal(
            "Silence enemy creature\n" +
            "\"Light purges corruption.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Morning_Herald()
    {
        var card = LoadById("dwn_u_morning_herald");
        Assert.Equal(
            "2/4\n" +
            "At the start of your turn: Heal 2 from damaged ally creature\n" +
            "\"Each dawn brings the promise of renewal.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Steadfast_Bulwark()
    {
        var card = LoadById("dwn_u_steadfast_bulwark");
        Assert.Equal(
            "3/8 — Guard\n" +
            "\"It stands. It always stands.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Dawnbreaker_Charger()
    {
        var card = LoadById("dwn_c_dawnbreaker_charger");
        Assert.Equal(
            "4/3 — Swift\n" +
            "\"Light strikes first.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Radiant_Prophet()
    {
        var card = LoadById("dwn_r_radiant_prophet");
        Assert.Equal(
            "3/3\n" +
            "When this enters play: Excavate 2. Gain 2 max vigor\n" +
            "\"She sees what lies buried and strengthens those who seek it.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Holy_Edict()
    {
        var card = LoadById("dwn_r_holy_edict");
        Assert.Equal(
            "Destroy damaged enemy creature\n" +
            "\"Judgment is passed. Sentence is executed.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Archangel_of_Order()
    {
        var card = LoadById("dwn_r_archangel_of_order");
        Assert.Equal(
            "6/6 — Ward, Guard\n" +
            "\"She does not fight — she enforces.\"",
            RulesTextRenderer.Render(card));
    }

    [Fact]
    public void Dawn_Relic()
    {
        var card = LoadById("dwn_x_dawn_relic");
        Assert.Equal(
            "Sealed\n" +
            "⛭ Identify: turn ≥ 8\n" +
            "Passive: Grant all ally creatures Ward\n" +
            "\"A fragment of the First Dawn, imbued with unbreakable light.\"",
            RulesTextRenderer.Render(card));
    }

    // ——— Helpers ———

    private static readonly object _lock = new();
    private static bool _loaded = false;

    private static CardDef LoadById(string id)
    {
        if (!_loaded)
        {
            lock (_lock)
            {
                if (!_loaded)
                {
                    var packs = new[]
                    {
                        $"{ContentRoot}/verdant.json",
                        $"{ContentRoot}/ember.json",
                        $"{ContentRoot}/tide.json",
                        $"{ContentRoot}/hollow.json",
                        $"{ContentRoot}/dawn.json"
                    };
                    foreach (var p in packs)
                    {
                        var fullPath = Path.GetFullPath(p);
                        var cards = CardLoader.LoadPack(fullPath);
                        CardRegistry.RegisterRange(cards);
                    }
                    _loaded = true;
                }
            }
        }

        return CardRegistry.Get(id)
            ?? throw new KeyNotFoundException($"Card '{id}' not found.");
    }
}