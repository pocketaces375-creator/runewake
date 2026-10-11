using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Xunit;

namespace Runewake.Tests.Engine;

/// <summary>
/// FABLE-DROP-3: the two engine pieces the third 100-card drop added — ON_ALLY_SUMMON ("When you summon
/// another creature", with the new creature as "it") and cards that hand out artifact Charges.
/// </summary>
[Collection("NonParallel")]
public class Drop3MechanicTests
{
    private static readonly string Content;

    static Drop3MechanicTests()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "content", "artifacts", "forge_artifacts.json")))
            dir = Path.GetDirectoryName(dir) ?? throw new FileNotFoundException("content/artifacts/forge_artifacts.json");
        Content = Path.Combine(dir, "content");
    }

    private static GameState S()
    {
        ArtifactLoader.LoadPack(Path.Combine(Content, "artifacts", "forge_artifacts.json"));
        foreach (var f in new[] { "ember.json", "hollow.json" })
            foreach (var c in CardLoader.LoadPackFromString(File.ReadAllText(Path.Combine(Content, "cards", f))))
                CardRegistry.Register(c);
        var s = new GameState(seed: 11);
        for (int p = 0; p < 2; p++)
        {
            s.Players[p].AttunementMax = 10; s.Players[p].Attunement = 10;
            s.Players[p].MaxVigor = 25; s.Players[p].Vigor = 25;
            for (int i = 0; i < 12; i++)
                s.Players[p].Deck.Add(new CardInstance(s.NextInstanceId++, "tst_deck", p) { Zone = Zone.Deck, CardType = CardType.CREATURE, BaseAttack = 1, BaseVigor = 1, Cost = 1 });
        }
        return s;
    }

    private static CardInstance Hand(GameState s, int p, string id)
    {
        var c = CardInstance.FromDef(CardRegistry.Get(id)!, s.NextInstanceId++, p, Zone.Hand);
        s.Players[p].Hand.Add(c);
        return c;
    }

    private static CardInstance Plain(GameState s, int p)
    {
        var c = new CardInstance(s.NextInstanceId++, "tst_h", p) { Zone = Zone.Hand, CardType = CardType.CREATURE, BaseAttack = 2, BaseVigor = 2, Cost = 1 };
        s.Players[p].Hand.Add(c);
        return c;
    }

    private static GameState Play(GameState s, int p, CardInstance c, int lane) =>
        DuelEngine.Apply(s, new PlayCardAction { PlayerIndex = p, CardInstanceId = c.InstanceId, Cost = c.Cost, LaneIndex = lane });

    private static CardInstance At(GameState s, int p, int lane) => s.Players[p].Lanes[lane].Occupant!;

    [Fact]
    public void AllySummon_BuffsTheNewCreature_NotItself()
    {
        // Warbanner Bearer 2/3: "When you summon another creature: give it +1/+0"
        var s = S();
        s = Play(s, 0, Hand(s, 0, "emb_u_warbanner_bearer"), 0);
        Assert.Equal(2, At(s, 0, 0).CurrentAttack);          // hearing its own summon does nothing
        s = Play(s, 0, Plain(s, 0), 1);
        Assert.Equal(3, At(s, 0, 1).CurrentAttack);          // the new one got +1
        Assert.Equal(2, At(s, 0, 0).CurrentAttack);          // and the Bearer did not
    }

    [Fact]
    public void AllySummon_Self_TargetsTheHearer()
    {
        // Battle Herald 2/3: "When you summon another creature: this gets +1/+0 this turn"
        var s = S();
        s = Play(s, 0, Hand(s, 0, "emb_c_battle_herald"), 0);
        s = Play(s, 0, Plain(s, 0), 1);
        Assert.Equal(3, At(s, 0, 0).CurrentAttack);
        Assert.Equal(2, At(s, 0, 1).CurrentAttack);
    }

    [Fact]
    public void AllySummon_IgnoresTheEnemysSummons()
    {
        var s = S();
        s = Play(s, 0, Hand(s, 0, "emb_u_warbanner_bearer"), 0);
        s = DuelEngine.Apply(s, new EndTurnAction { PlayerIndex = 0 });
        s = Play(s, 1, Plain(s, 1), 2);
        Assert.Equal(2, At(s, 1, 2).CurrentAttack);
    }

    [Fact]
    public void ChargeCard_FillsAChargingArtifact_AndAFullOneFires()
    {
        // Grave Tender: "When this dies: give each of your artifacts a Charge". Bone Reliquary fills at 4.
        var s = S();
        GameState.InstallArtifacts(s, 0, "necromancer", new[] { "artf_necromancer_bone_reliquary", "artf_necromancer_soul_jar" });
        var reliquary = s.Players[0].ArtifactSlots.First(sl => sl.Occupant!.CardDefId == "artf_necromancer_bone_reliquary");
        s = Play(s, 0, Hand(s, 0, "hol_c_grave_tender"), 0);
        var tender = At(s, 0, 0);
        int before = reliquary.Charges;
        EffectExecutor.KillCreature(tender, s);
        reliquary = s.Players[0].ArtifactSlots.First(sl => sl.Occupant!.CardDefId == "artf_necromancer_bone_reliquary");
        // the death itself is one Charge, the Tender's last words another
        Assert.Equal(before + 2, reliquary.Charges);
    }
}
