using Runewake.Engine.Cards;
using Xunit;

namespace Runewake.Tests.Cards;

public class CardLoaderTests
{
    private static readonly List<CardDef> Cards = CardLoader.LoadPack(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "schema", "example_cards.json"));

    [Fact]
    public void LoadsAllSixExampleCards()
    {
        Assert.Equal(6, Cards.Count);
    }

    [Fact]
    public void RootWarden_HasCorrectValues()
    {
        var c = Cards.Find(x => x.Id == "vrd_c_root_warden");
        Assert.NotNull(c);

        Assert.Equal("buried_age", c!.Set);
        Assert.Equal("Root Warden", c.Name);
        Assert.Equal(Strata.VERDANT, c.Strata);
        Assert.Equal(CardType.CREATURE, c.Type);
        Assert.Equal(Rarity.COMMON, c.Rarity);
        Assert.Equal(3, c.Cost);
        Assert.Equal(2, c.Attack);
        Assert.Equal(4, c.Vigor);
        Assert.Single(c.Keywords);
        Assert.Equal("GUARD", c.Keywords[0]);
        Assert.Single(c.Abilities);
        Assert.Equal(Trigger.ON_SUMMON, c.Abilities[0].Trigger);
        Assert.Null(c.Abilities[0].Condition);
        Assert.Single(c.Abilities[0].Effects);
        Assert.Equal(Op.BUFF, c.Abilities[0].Effects[0].Op);
        Assert.Equal(Scope.ALLY_CREATURE, c.Abilities[0].Effects[0].Target!.Scope);
        Assert.Equal("ADJACENT", c.Abilities[0].Effects[0].Target.Filter);
        Assert.True(c.Abilities[0].Effects[0].Target.Count!.Value.IsAll);
        Assert.Equal(0, c.Abilities[0].Effects[0].Attack);
        Assert.Equal(1, c.Abilities[0].Effects[0].Vigor);
        Assert.Equal(Duration.PERMANENT, c.Abilities[0].Effects[0].Duration);
        Assert.Null(c.IdentifyCondition);
        Assert.Equal("The grove keeps its own ledgers, and it does not forgive debts.", c.Flavor);
        Assert.NotNull(c.Art);
        Assert.Equal(7.1, c.PowerScore);
        Assert.Equal(1, c.ContentVersion);
    }

    [Fact]
    public void CinderRunner_HasCorrectValues()
    {
        var c = Cards.Find(x => x.Id == "emb_c_cinder_runner");
        Assert.NotNull(c);

        Assert.Equal("Cinder Runner", c!.Name);
        Assert.Equal(Strata.EMBER, c.Strata);
        Assert.Equal(CardType.CREATURE, c.Type);
        Assert.Equal(Rarity.COMMON, c.Rarity);
        Assert.Equal(2, c.Cost);
        Assert.Equal(3, c.Attack);
        Assert.Equal(1, c.Vigor);
        Assert.Single(c.Keywords);
        Assert.Equal("SWIFT", c.Keywords[0]);
        Assert.Empty(c.Abilities);
        Assert.Equal(4.85, c.PowerScore);
        Assert.Equal(1, c.ContentVersion);
    }

    [Fact]
    public void SiltReader_HasCorrectValues()
    {
        var c = Cards.Find(x => x.Id == "tid_c_silt_reader");
        Assert.NotNull(c);

        Assert.Equal("Silt Reader", c!.Name);
        Assert.Equal(Strata.TIDE, c.Strata);
        Assert.Equal(CardType.CREATURE, c.Type);
        Assert.Equal(Rarity.UNCOMMON, c.Rarity);
        Assert.Equal(4, c.Cost);
        Assert.Equal(2, c.Attack);
        Assert.Equal(5, c.Vigor);
        Assert.Empty(c.Keywords);

        // OnSummon: Excavate 3
        Assert.Equal(2, c.Abilities.Count);
        Assert.Equal(Trigger.ON_SUMMON, c.Abilities[0].Trigger);
        Assert.Null(c.Abilities[0].Condition);
        Assert.Single(c.Abilities[0].Effects);
        Assert.Equal(Op.EXCAVATE, c.Abilities[0].Effects[0].Op);
        Assert.Equal(Scope.PLAYER_SELF, c.Abilities[0].Effects[0].Target!.Scope);
        Assert.Equal(3, c.Abilities[0].Effects[0].Amount);

        // OnTurnStart: if Barrow >= 4, Draw 1
        Assert.Equal(Trigger.ON_TURN_START, c.Abilities[1].Trigger);
        Assert.NotNull(c.Abilities[1].Condition);
        Assert.Equal(ConditionOp.BARROW_COUNT_GTE, c.Abilities[1].Condition!.Op);
        Assert.Single(c.Abilities[1].Effects);
        Assert.Equal(Op.DRAW, c.Abilities[1].Effects[0].Op);
        Assert.Equal(1, c.Abilities[1].Effects[0].Amount);

        Assert.Equal(10.4, c.PowerScore);
    }

    [Fact]
    public void GravewritThrall_HasCorrectValues()
    {
        var c = Cards.Find(x => x.Id == "hol_c_gravewrit_thrall");
        Assert.NotNull(c);

        Assert.Equal("Gravewrit Thrall", c!.Name);
        Assert.Equal(Strata.HOLLOW, c.Strata);
        Assert.Equal(CardType.CREATURE, c.Type);
        Assert.Equal(Rarity.UNCOMMON, c.Rarity);
        Assert.Equal(3, c.Cost);
        Assert.Equal(4, c.Attack);
        Assert.Equal(2, c.Vigor);
        Assert.Single(c.Keywords);
        Assert.Equal("UNEARTH", c.Keywords[0]);

        Assert.Single(c.Abilities);
        Assert.Equal(Trigger.ON_DEATH, c.Abilities[0].Trigger);
        Assert.Equal(2, c.Abilities[0].Effects.Count);
        Assert.Equal(Op.DAMAGE, c.Abilities[0].Effects[0].Op);
        Assert.Equal(Scope.PLAYER_ENEMY, c.Abilities[0].Effects[0].Target!.Scope);
        Assert.Equal(1, c.Abilities[0].Effects[0].Amount);
        Assert.Equal(Op.BURY, c.Abilities[0].Effects[1].Op);
        Assert.Equal(Scope.PLAYER_SELF, c.Abilities[0].Effects[1].Target!.Scope);
        Assert.Equal(1, c.Abilities[0].Effects[1].Amount);

        Assert.Equal(8.2, c.PowerScore);
    }

    [Fact]
    public void SealingLight_HasCorrectValues()
    {
        var c = Cards.Find(x => x.Id == "dwn_r_sealing_light");
        Assert.NotNull(c);

        Assert.Equal("Sealing Light", c!.Name);
        Assert.Equal(Strata.DAWN, c.Strata);
        Assert.Equal(CardType.RITUAL, c.Type);
        Assert.Equal(Rarity.COMMON, c.Rarity);
        Assert.Equal(2, c.Cost);
        Assert.Null(c.Attack);
        Assert.Null(c.Vigor);
        Assert.Empty(c.Keywords);

        Assert.Single(c.Abilities);
        Assert.Equal(Trigger.RESOLVE, c.Abilities[0].Trigger);
        Assert.Equal(2, c.Abilities[0].Effects.Count);

        // GrantKey
        Assert.Equal(Op.GRANT_KEY, c.Abilities[0].Effects[0].Op);
        Assert.Equal(Scope.ALLY_CREATURE, c.Abilities[0].Effects[0].Target!.Scope);
        Assert.Equal("CHOSEN", c.Abilities[0].Effects[0].Target.Filter);
        Assert.False(c.Abilities[0].Effects[0].Target.Count!.Value.IsAll);
        Assert.Equal(1, c.Abilities[0].Effects[0].Target.Count.Value.Value);
        Assert.Equal("WARD", c.Abilities[0].Effects[0].Keyword);
        Assert.Equal(Duration.PERMANENT, c.Abilities[0].Effects[0].Duration);

        // Heal
        Assert.Equal(Op.HEAL, c.Abilities[0].Effects[1].Op);
        Assert.Equal(2, c.Abilities[0].Effects[1].Amount);

        Assert.Equal(5.6, c.PowerScore);
    }

    [Fact]
    public void AelinsSeal_HasCorrectValues()
    {
        var c = Cards.Find(x => x.Id == "hol_x_aelins_seal");
        Assert.NotNull(c);

        Assert.Equal("Aelin's Seal", c!.Name);
        Assert.Equal(Strata.HOLLOW, c.Strata);
        Assert.Equal(CardType.RELIC, c.Type);
        Assert.Equal(Rarity.RELIC, c.Rarity);
        Assert.Equal(5, c.Cost);
        Assert.Null(c.Attack);
        Assert.Null(c.Vigor);
        Assert.Single(c.Keywords);
        Assert.Equal("SEALED", c.Keywords[0]);

        Assert.Equal(2, c.Abilities.Count);

        // OnRelicIdentify: Unbury 2
        Assert.Equal(Trigger.ON_RELIC_IDENTIFY, c.Abilities[0].Trigger);
        Assert.Single(c.Abilities[0].Effects);
        Assert.Equal(Op.UNBURY, c.Abilities[0].Effects[0].Op);
        Assert.Equal(2, c.Abilities[0].Effects[0].Amount);

        // Passive: Buff Hollow allies +1/+0
        Assert.Equal(Trigger.PASSIVE, c.Abilities[1].Trigger);
        Assert.Single(c.Abilities[1].Effects);
        Assert.Equal(Op.BUFF, c.Abilities[1].Effects[0].Op);
        Assert.Equal("STRATA:HOLLOW", c.Abilities[1].Effects[0].Target!.Filter);
        Assert.True(c.Abilities[1].Effects[0].Target.Count!.Value.IsAll);
        Assert.Equal(1, c.Abilities[1].Effects[0].Attack);
        Assert.Equal(0, c.Abilities[1].Effects[0].Vigor);
        Assert.Equal(Duration.WHILE_PRESENT, c.Abilities[1].Effects[0].Duration);

        // Identify condition
        Assert.NotNull(c.IdentifyCondition);
        Assert.Equal(ConditionOp.BARROW_COUNT_GTE, c.IdentifyCondition!.Op);

        Assert.Equal(13.9, c.PowerScore);
    }
}
