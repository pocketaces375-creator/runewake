using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// FABLE-054: the Deck Forge artifacts (content/artifacts/forge_artifacts.json). Every artifact's printed
/// text is checked against what the engine actually does, plus the pool rules (two per deck, from your
/// class) and the engine pieces they needed (an artifact's own Charges, the event creature).
/// </summary>
[Collection("NonParallel")]
public class ForgeArtifactTests
{
    static ForgeArtifactTests()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "content", "artifacts", "forge_artifacts.json")))
            dir = Path.GetDirectoryName(dir) ?? throw new FileNotFoundException("content/artifacts/forge_artifacts.json");
        Content = Path.Combine(dir, "content");
    }

    private static readonly string Content;

    private static void Load()
    {
        ArtifactLoader.LoadPack(Path.Combine(Content, "artifacts", "forge_artifacts.json"));
        foreach (var f in new[] { "hollow.json", "dawn.json", "verdant.json" })
            foreach (var c in CardLoader.LoadPackFromString(File.ReadAllText(Path.Combine(Content, "cards", f))))
                CardRegistry.Register(c);
    }

    // ——— board helpers ———

    private static GameState S(int p0Attune = 10)
    {
        Load();
        var s = new GameState(seed: 7);
        for (int p = 0; p < 2; p++)
        {
            s.Players[p].AttunementMax = 10; s.Players[p].Attunement = 10;
            s.Players[p].MaxVigor = 25; s.Players[p].Vigor = 25;
            for (int i = 0; i < 12; i++)
                s.Players[p].Deck.Add(new CardInstance(s.NextInstanceId++, "tst_deck", p) { Zone = Zone.Deck, CardType = CardType.CREATURE, BaseAttack = 1, BaseVigor = 1, Cost = 1 });
        }
        s.Players[0].AttunementMax = p0Attune; s.Players[0].Attunement = p0Attune;
        return s;
    }

    private static void Arm(GameState s, int p, string cls, params string[] keys) =>
        GameState.InstallArtifacts(s, p, cls, keys.Select(k => $"artf_{cls}_{k}").ToArray());

    private static ArtifactSlot Slot(GameState s, int p, string id) =>
        s.Players[p].ArtifactSlots.First(sl => sl.Occupant!.CardDefId == id);

    private static CardInstance Put(GameState s, int p, int lane, int atk, int vig, int cost = 1, params string[] keywords)
    {
        var c = new CardInstance(s.NextInstanceId++, "tst_c", p)
        {
            Zone = Zone.Lane, LaneIndex = lane, CardType = CardType.CREATURE,
            BaseAttack = atk, BaseVigor = vig, Cost = cost, IsExhausted = false
        };
        c.Keywords.AddRange(keywords);
        s.Players[p].Lanes[lane].Occupant = c;
        return c;
    }

    private static CardInstance HandCreature(GameState s, int p, int atk = 2, int vig = 2)
    {
        var c = new CardInstance(s.NextInstanceId++, "tst_h", p) { Zone = Zone.Hand, CardType = CardType.CREATURE, BaseAttack = atk, BaseVigor = vig, Cost = 1 };
        s.Players[p].Hand.Add(c);
        return c;
    }

    private static CardInstance Ritual(GameState s, int p)
    {
        var r = new CardInstance(s.NextInstanceId++, "tst_r", p)
        {
            Zone = Zone.Hand, CardType = CardType.RITUAL, Cost = 1,
            Abilities = { new AbilityDef { Trigger = Trigger.RESOLVE, Effects = { new EffectDef { Op = Op.GAIN_VIGOR, Amount = 1, Target = new TargetDef { Scope = Scope.PLAYER_SELF } } } } }
        };
        s.Players[p].Hand.Add(r);
        return r;
    }

    private static GameState Play(GameState s, int p, CardInstance c, int lane = 0) =>
        DuelEngine.Apply(s, new PlayCardAction { PlayerIndex = p, CardInstanceId = c.InstanceId, Cost = c.Cost, LaneIndex = lane });
    private static GameState Attack(GameState s, int p, int lane, int? target = null) =>
        DuelEngine.Apply(s, new AttackAction { PlayerIndex = p, SourceLane = lane, TargetLane = target ?? lane });
    private static GameState End(GameState s) => DuelEngine.Apply(s, new EndTurnAction { PlayerIndex = s.CurrentPlayerIndex });
    /// <summary>End this turn and the opponent's: back to player 0's turn start.</summary>
    private static GameState Round(GameState s) => End(End(s));
    private static CardInstance? At(GameState s, int p, int lane) => s.Players[p].Lanes[lane].Occupant;

    // ══════════════ the pool ══════════════

    [Fact]
    public void EveryClass_HasFour_TwoDefaults_AndRulesText()
    {
        Load();
        foreach (var cls in new[] { "warrior", "battlemage", "necromancer", "paladin", "druid", "rogue", "astrologist" })
        {
            var pool = ArtifactRegistry.ForgePool(cls);
            Assert.Equal(4, pool.Count);
            Assert.Equal(2, pool.Count(a => a.IsDefault));
            Assert.All(pool, a => Assert.False(string.IsNullOrWhiteSpace(a.Text)));
            Assert.True(ArtifactRegistry.IsValidLoadout(cls, ArtifactRegistry.ForgeDefaults(cls)));
        }
    }

    [Fact]
    public void EveryTermAnArtifactUses_IsExplained()
    {
        Load();
        var terms = new[] { "Burn", "Stun", "Lock", "Drain", "Sigil", "Suppressed", "Venom", "Dodge", "Guard", "Charge" };
        foreach (var a in ArtifactRegistry.GetAll().Where(x => x.Forge))
        {
            var lines = RulesTextRenderer.ArtifactReminderLines(a);
            foreach (var t in terms.Where(t => a.Text!.Contains(t)))
                Assert.True(lines.Any(l => l.StartsWith(t) || l.StartsWith(t + "s")), $"{a.Name}: '{t}' is never explained");
        }
    }

    [Fact]
    public void Loadout_MustBeTwoDifferent_FromYourClass()
    {
        Load();
        Assert.False(ArtifactRegistry.IsValidLoadout("warrior", new[] { "artf_warrior_warbrand" }));
        Assert.False(ArtifactRegistry.IsValidLoadout("warrior", new[] { "artf_warrior_warbrand", "artf_warrior_warbrand" }));
        Assert.False(ArtifactRegistry.IsValidLoadout("warrior", new[] { "artf_warrior_warbrand", "artf_rogue_venomfang" }));
        Assert.True(ArtifactRegistry.IsValidLoadout("warrior", new[] { "artf_warrior_champions_oath", "artf_warrior_warbrand" }));
        // a bad pick never reaches a duel: the class's defaults step in
        Assert.Equal(ArtifactRegistry.ForgeDefaults("rogue"), ArtifactRegistry.PlayerLoadout("rogue", new[] { "artf_warrior_warbrand", "x" }));
    }

    [Fact]
    public void ForgeArtifacts_DoNotChangeTheOldLaunchPairs()
    {
        Load();
        foreach (var cls in new[] { "warrior", "rogue", "druid" })
            Assert.All(ArtifactRegistry.DefaultLoadoutFor(cls), id => Assert.False(ArtifactRegistry.Get(id)?.Forge ?? false));
    }

    [Fact]
    public void Opponent_IsAnotherClass_TwoDifferent_SameEverySeed()
    {
        Load();
        var a = ArtifactRegistry.OpponentForgeLoadout(null, "warrior", "enc_x", 99);
        var b = ArtifactRegistry.OpponentForgeLoadout(null, "warrior", "enc_x", 99);
        Assert.NotEqual("warrior", a.ClassId);
        Assert.Equal(a.Artifacts, b.Artifacts);
        Assert.True(ArtifactRegistry.IsValidLoadout(a.ClassId, a.Artifacts));
        Assert.Equal("paladin", ArtifactRegistry.OpponentForgeLoadout("paladin", "warrior", "enc_y", 3).ClassId);
    }

    [Fact]
    public void AnArtifactsCharges_AreItsOwn()
    {
        var s = S();
        Arm(s, 0, "battlemage", "undertow_ring", "null_sigil");
        s = Play(s, 0, Ritual(s, 0));
        Assert.Equal(1, Slot(s, 0, "artf_battlemage_undertow_ring").Charges);
        Assert.Equal(0, Slot(s, 0, "artf_battlemage_null_sigil").Charges);
    }

    [Fact]
    public void Warbrand_FirstAttackerOnly_Plus3()
    {
        var s = S();
        Arm(s, 0, "warrior", "warbrand", "bloodied_standard");
        Put(s, 0, 0, 2, 9); Put(s, 0, 1, 2, 9);
        Put(s, 1, 0, 1, 9); Put(s, 1, 1, 1, 9);
        s = Attack(s, 0, 0); s = Attack(s, 0, 1);
        Assert.Equal(5, At(s, 1, 0)!.Damage);
        Assert.Equal(2, At(s, 1, 1)!.Damage);
    }

    [Fact]
    public void BloodiedStandard_DamagedCreaturesGrow()
    {
        var s = S();
        Arm(s, 0, "warrior", "bloodied_standard", "warbrand");
        Put(s, 0, 0, 2, 5).Damage = 1; Put(s, 0, 1, 2, 5);
        s = End(s);
        Assert.Equal(3, At(s, 0, 0)!.CurrentAttack);
        Assert.Equal(6 - 1, At(s, 0, 0)!.CurrentVigor);
        Assert.Equal(2, At(s, 0, 1)!.CurrentAttack);
    }

    [Fact]
    public void ChampionsOath_OnlyALoneChampion()
    {
        var s = S();
        Arm(s, 0, "warrior", "champions_oath", "warbrand");
        Put(s, 0, 2, 2, 2);
        s = Round(s);
        Assert.Equal(3, At(s, 0, 2)!.CurrentAttack);
        Assert.Equal(3, At(s, 0, 2)!.CurrentVigor);
        Put(s, 0, 3, 1, 1);
        s = Round(s);
        Assert.Equal(3, At(s, 0, 2)!.CurrentAttack);
    }

    [Fact]
    public void Twinblade_SecondAttack_LetsTheStrongestSwingAgain()
    {
        var s = S();
        Arm(s, 0, "warrior", "twinblade", "bloodied_standard");
        Put(s, 0, 0, 4, 4); Put(s, 0, 1, 2, 2);
        s = Attack(s, 0, 0); s = Attack(s, 0, 1);
        Assert.Equal(25 - 6, s.Players[1].Vigor);
        s = Attack(s, 0, 0);
        Assert.Equal(25 - 6 - 6, s.Players[1].Vigor);
    }

    [Fact]
    public void SpellwakeFocus_EachRitualHitsTheirBiggestHitter()
    {
        var s = S();
        Arm(s, 0, "battlemage", "spellwake_focus", "tidecallers_vigil");
        Put(s, 1, 0, 3, 5); Put(s, 1, 1, 1, 5);
        s = Play(s, 0, Ritual(s, 0));
        Assert.Equal(2, At(s, 1, 0)!.Damage);
        Assert.Equal(0, At(s, 1, 1)!.Damage);
    }

    [Fact]
    public void TidecallersVigil_FirstRitualEachTurn_DrawsAndRallies()
    {
        var s = S();
        Arm(s, 0, "battlemage", "tidecallers_vigil", "spellwake_focus");
        Put(s, 0, 0, 2, 2);
        var a = Ritual(s, 0); var b = Ritual(s, 0);
        s = Play(s, 0, a);
        Assert.Equal(2, s.Players[0].Hand.Count);           // b + the drawn card
        Assert.Equal(3, At(s, 0, 0)!.CurrentAttack);
        s = Play(s, 0, b);
        Assert.Equal(1, s.Players[0].Hand.Count);           // the second Ritual draws nothing
        Assert.Equal(3, At(s, 0, 0)!.CurrentAttack);
        s = Round(s);
        Assert.Equal(2, At(s, 0, 0)!.CurrentAttack);        // the rally was this turn only
    }

    [Fact]
    public void NullSigil_SecondTurnEnd_SuppressesAndSetsACounter()
    {
        var s = S();
        Arm(s, 0, "battlemage", "null_sigil", "spellwake_focus");
        Arm(s, 1, "warrior", "warbrand", "bloodied_standard");
        s = Round(s);
        Assert.False(s.Players[1].ArtifactSlots[0].IsSuppressed);
        s = End(s);
        Assert.All(s.Players[1].ArtifactSlots, sl => Assert.True(sl.IsSuppressed));
        Assert.Contains(s.Players[0].Traps, t => t.Kind == "COUNTER_RITUAL");
    }

    [Fact]
    public void UndertowRing_TwoRituals_BounceTheirMostExpensive()
    {
        var s = S();
        Arm(s, 0, "battlemage", "undertow_ring", "tidecallers_vigil");
        Put(s, 1, 0, 1, 1, cost: 2); var big = Put(s, 1, 1, 5, 5, cost: 6);
        s = Play(s, 0, Ritual(s, 0)); s = Play(s, 0, Ritual(s, 0));
        Assert.Null(At(s, 1, 1));
        Assert.Contains(s.Players[1].Hand, c => c.InstanceId == big.InstanceId);
        Assert.NotNull(At(s, 1, 0));
    }

    // ══════════════ necromancer ══════════════

    [Fact]
    public void BoneReliquary_FourthDeath_RaisesTheStrongestCheapOne()
    {
        var s = S();
        Arm(s, 0, "necromancer", "bone_reliquary", "gravecallers_skull");
        int[] costs = { 1, 2, 3, 4 };
        for (int i = 0; i < 4; i++) { Put(s, 0, i, 1 + i, 1, cost: costs[i]); Put(s, 1, i, 9, 9); }
        for (int i = 0; i < 3; i++) s = Attack(s, 0, i);
        Assert.Equal(1, Enumerable.Range(0, 5).Count(i => At(s, 0, i) != null));   // three have died, nothing back yet
        s = Attack(s, 0, 3);
        var back = Enumerable.Range(0, 5).Select(i => At(s, 0, i)).Single(c => c != null)!;
        Assert.Equal(3, back.Cost);                         // the 4-cost one is too big to rise
    }

    [Fact]
    public void GravecallersSkull_ADeathThisTurn_RaisesBones()
    {
        var s = S();
        Arm(s, 0, "necromancer", "gravecallers_skull", "bone_reliquary");
        Put(s, 0, 0, 1, 1); Put(s, 1, 0, 5, 9);
        s = End(s);
        Assert.DoesNotContain(Enumerable.Range(0, 5), i => At(s, 0, i)?.CardDefId == "hol_t_risen_bones");
        s = End(s);
        s = Attack(s, 0, 0);
        s = End(s);
        Assert.Contains(Enumerable.Range(0, 5), i => At(s, 0, i)?.CardDefId == "hol_t_risen_bones");
    }

    [Fact]
    public void BlighthornSkull_BurnsTheirToughest()
    {
        var s = S();
        Arm(s, 0, "necromancer", "blighthorn_skull", "soul_jar");
        Put(s, 1, 0, 2, 9); Put(s, 1, 1, 2, 3);
        s = Round(s);
        Assert.Equal(2, At(s, 1, 0)!.Burn + At(s, 1, 0)!.Damage);   // Burn 2 (its own tick may have landed)
        Assert.Equal(0, At(s, 1, 1)!.Burn);
    }

    [Fact]
    public void SoulJar_YourDeathsHurtThem()
    {
        var s = S();
        Arm(s, 0, "necromancer", "soul_jar", "blighthorn_skull");
        Put(s, 0, 0, 1, 1); Put(s, 1, 0, 5, 9);
        s = Attack(s, 0, 0);
        Assert.Equal(24, s.Players[1].Vigor);
    }

    // ══════════════ paladin ══════════════

    [Fact]
    public void AegisOfSunspire_FirstSummonGetsTougher()
    {
        var s = S();
        Arm(s, 0, "paladin", "aegis_of_sunspire", "oathkeepers_banner");
        var a = HandCreature(s, 0); var b = HandCreature(s, 0);
        s = Play(s, 0, a, 0); s = Play(s, 0, b, 1);
        Assert.Equal(4, At(s, 0, 0)!.CurrentVigor);
        Assert.Equal(2, At(s, 0, 1)!.CurrentVigor);
    }

    [Fact]
    public void OathkeepersBanner_HealsAtEndOfTurn_GuardsMore()
    {
        var s = S();
        Arm(s, 0, "paladin", "oathkeepers_banner", "aegis_of_sunspire");
        Put(s, 0, 0, 2, 5).Damage = 3; Put(s, 0, 1, 2, 5, 1, "GUARD").Damage = 3;
        s = End(s);
        Assert.Equal(2, At(s, 0, 0)!.Damage);
        Assert.Equal(1, At(s, 0, 1)!.Damage);
    }

    [Fact]
    public void UnbrokenBulwark_FirstBlowEachTurn_Softened()
    {
        var s = S();
        Arm(s, 1, "paladin", "unbroken_bulwark", "oathkeepers_banner");
        Put(s, 0, 0, 4, 9); Put(s, 0, 1, 4, 9);
        Put(s, 1, 0, 1, 9); Put(s, 1, 1, 1, 9);
        s = Attack(s, 0, 0); s = Attack(s, 0, 1);
        Assert.Equal(2, At(s, 1, 0)!.Damage);
        Assert.Equal(4, At(s, 1, 1)!.Damage);
    }

    [Fact]
    public void KnightCommandersHammer_AKnight_CallsASquire()
    {
        var s = S();
        Arm(s, 0, "paladin", "knight_commanders_hammer", "aegis_of_sunspire");
        Put(s, 0, 0, 2, 2);
        s = Round(s);
        Assert.DoesNotContain(Enumerable.Range(0, 5), i => At(s, 0, i)?.CardDefId == "dwn_t_squire");
        At(s, 0, 0)!.Types.Add("KNIGHT");
        s = Round(s);
        var sq = Enumerable.Range(0, 5).Select(i => At(s, 0, i)).Single(c => c?.CardDefId == "dwn_t_squire")!;
        Assert.Contains("GUARD", sq.EffectiveKeywords);
    }

    [Fact]
    public void HeartrootTotem_MostWoundedGrowsAndMends()
    {
        var s = S();
        Arm(s, 0, "druid", "heartroot_totem", "grove_seedbook");
        Put(s, 0, 0, 2, 6).Damage = 3; Put(s, 0, 1, 2, 6).Damage = 1;
        s = Round(s);
        Assert.Equal(3, At(s, 0, 0)!.CurrentAttack);
        Assert.Equal(1, At(s, 0, 0)!.Damage);
        Assert.Equal(2, At(s, 0, 1)!.CurrentAttack);
    }

    [Fact]
    public void GroveSeedbook_EveryThirdTurn_OneMoreAttunement()
    {
        var s = S(p0Attune: 3);
        Arm(s, 0, "druid", "grove_seedbook", "heartroot_totem");
        s = Round(s); s = Round(s); s = Round(s);
        Assert.Equal(3 + 3 + 1, s.Players[0].AttunementMax);
    }

    [Fact]
    public void GrovewingMoth_NeighboursOfANewCreatureGrow()
    {
        var s = S();
        Arm(s, 0, "druid", "grovewing_moth", "grove_seedbook");
        Put(s, 0, 1, 2, 2); Put(s, 0, 3, 2, 2); Put(s, 0, 4, 2, 2);
        s = Play(s, 0, HandCreature(s, 0), 2);
        Assert.Equal(3, At(s, 0, 1)!.CurrentAttack);
        Assert.Equal(3, At(s, 0, 3)!.CurrentAttack);
        Assert.Equal(2, At(s, 0, 4)!.CurrentAttack);
        Assert.Equal(2, At(s, 0, 2)!.CurrentAttack);
    }

    [Fact]
    public void WildheartGrimoire_ABeast_CallsACub_EveryOtherTurn()
    {
        var s = S();
        Arm(s, 0, "druid", "wildheart_grimoire", "grove_seedbook");
        Put(s, 0, 0, 2, 2);
        s = Round(s); s = Round(s);
        Assert.DoesNotContain(Enumerable.Range(0, 5), i => At(s, 0, i)?.CardDefId == "vrd_t_wolf_cub");   // no Beast, Charges spent anyway
        At(s, 0, 0)!.Types.Add("BEAST");
        s = Round(s);
        Assert.DoesNotContain(Enumerable.Range(0, 5), i => At(s, 0, i)?.CardDefId == "vrd_t_wolf_cub");
        s = Round(s);
        Assert.Contains(Enumerable.Range(0, 5), i => At(s, 0, i)?.CardDefId == "vrd_t_wolf_cub");
    }

    [Fact]
    public void Venomfang_FirstStrikeIsVenomous()
    {
        var s = S();
        Arm(s, 0, "rogue", "venomfang", "gloom_hook");
        Put(s, 0, 0, 1, 5); Put(s, 0, 1, 1, 5);
        Put(s, 1, 0, 1, 9); Put(s, 1, 1, 1, 9);
        s = Attack(s, 0, 0); s = Attack(s, 0, 1);
        Assert.Null(At(s, 1, 0));
        Assert.NotNull(At(s, 1, 1));
    }

    [Fact]
    public void CutpurseDagger_OneChargeATurn_ThenSteals()
    {
        var s = S();
        Arm(s, 0, "rogue", "cutpurse_dagger", "venomfang");
        Put(s, 0, 0, 1, 5); Put(s, 0, 1, 1, 5);
        s = Attack(s, 0, 0); s = Attack(s, 0, 1);
        Assert.Equal(1, Slot(s, 0, "artf_rogue_cutpurse_dagger").Charges);
        s = Round(s);
        int theirs = s.Players[1].Hand.Count, mine = s.Players[0].Hand.Count;
        s = Attack(s, 0, 0);
        Assert.Equal(theirs - 1, s.Players[1].Hand.Count);
        Assert.Equal(mine + 1, s.Players[0].Hand.Count);
        Assert.Equal(0, Slot(s, 0, "artf_rogue_cutpurse_dagger").Charges);
    }

    [Fact]
    public void GloomHook_FirstSummonGetsDodge()
    {
        var s = S();
        Arm(s, 0, "rogue", "gloom_hook", "venomfang");
        var a = HandCreature(s, 0); var b = HandCreature(s, 0);
        s = Play(s, 0, a, 0); s = Play(s, 0, b, 1);
        Assert.Equal(30, MechanicOps.KeywordValue(At(s, 0, 0)!, "DODGE"));
        Assert.Equal(0, MechanicOps.KeywordValue(At(s, 0, 1)!, "DODGE"));
    }

    [Fact]
    public void SaboteursSpike_FullCharge_SuppressesAndBorrows()
    {
        var s = S();
        Arm(s, 0, "rogue", "saboteurs_spike", "venomfang");
        Arm(s, 1, "warrior", "warbrand", "bloodied_standard");
        var cheap = Put(s, 1, 3, 2, 2, cost: 1); Put(s, 1, 4, 5, 5, cost: 5);
        Slot(s, 0, "artf_rogue_saboteurs_spike").Charges = 2;
        s = Round(s);
        Assert.All(s.Players[1].ArtifactSlots, sl => Assert.True(sl.IsSuppressed));
        Assert.Contains(Enumerable.Range(0, 5), i => At(s, 0, i)?.InstanceId == cheap.InstanceId);
    }

    // ══════════════ astrologist ══════════════

    [Fact]
    public void StillwaterOrb_StunsTheirThreeBiggest()
    {
        var s = S();
        Arm(s, 0, "astrologist", "stillwater_orb", "lockstar_astrolabe");
        Put(s, 1, 0, 5, 5); Put(s, 1, 1, 4, 4); Put(s, 1, 2, 3, 3); Put(s, 1, 3, 1, 1);
        Slot(s, 0, "artf_astrologist_stillwater_orb").Charges = 1;
        s = Round(s);
        Assert.True(At(s, 1, 0)!.Stunned && At(s, 1, 1)!.Stunned && At(s, 1, 2)!.Stunned);
        Assert.False(At(s, 1, 3)!.Stunned);
    }

    [Fact]
    public void LockstarAstrolabe_LocksALane_AndBluntsTheirBiggest()
    {
        var s = S();
        Arm(s, 0, "astrologist", "lockstar_astrolabe", "stillwater_orb");
        Put(s, 1, 0, 5, 5);
        s = Round(s);
        Assert.Contains(Enumerable.Range(1, 4), i => s.Players[1].Lanes[i].LockedTurns > 0);
        Assert.Equal(3, At(s, 1, 0)!.CurrentAttack);
    }

    [Fact]
    public void GravitySphere_EveryTurn_Drains()
    {
        var s = S();
        Arm(s, 0, "astrologist", "gravity_sphere", "stillwater_orb");
        s = End(s);
        Assert.Equal(9, s.Players[1].Attunement);
    }

    [Fact]
    public void InvertedSky_FlipsTheGlassCannons_ThenBluntsTheBiggest()
    {
        var s = S();
        Arm(s, 0, "astrologist", "inverted_sky", "stillwater_orb");
        Put(s, 1, 0, 6, 2); Put(s, 1, 1, 3, 4);
        s = Round(s);
        Assert.Equal(6, At(s, 1, 0)!.CurrentVigor);
        Assert.Equal(2, At(s, 1, 0)!.CurrentAttack);       // flipped, untouched after: 3/4 is now the biggest hitter
        Assert.Equal(2, At(s, 1, 1)!.CurrentAttack);       // 3/4 isn't flipped, but loses 1 Attack for good
    }

}
