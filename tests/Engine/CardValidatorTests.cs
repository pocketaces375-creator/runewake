using Runewake.Engine.Cards;
using Runewake.Sim;
using Xunit;

namespace Runewake.Tests.Engine;

public class CardValidatorTests
{
    private static CardDef MakeValidCreature() => new()
    {
        Id = "vrd_c_test_warden",
        Set = "test",
        Name = "Test Warden",
        Strata = Strata.VERDANT,
        Type = CardType.CREATURE,
        Rarity = Rarity.COMMON,
        Cost = 3,
        Attack = 2,
        Vigor = 4,
        ContentVersion = 1,
        Keywords = new List<string> { "GUARD" },
        Abilities = new List<AbilityDef>(),
    };

    // ——— Valid cards ———

    [Fact]
    public void Validate_ValidCreature_ReturnsNoErrors()
    {
        var card = MakeValidCreature();
        var errors = CardValidator.Validate(card);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidRitual_ReturnsNoErrors()
    {
        var card = new CardDef
        {
            Id = "dwn_r_sealing_light",
            Set = "buried_age",
            Name = "Sealing Light",
            Strata = Strata.DAWN,
            Type = CardType.RITUAL,
            Rarity = Rarity.COMMON,
            Cost = 2,
            ContentVersion = 1,
            Abilities = new List<AbilityDef>
            {
                new AbilityDef
                {
                    Trigger = Trigger.RESOLVE,
                    Effects = new List<EffectDef>
                    {
                        new EffectDef { Op = Op.HEAL, Amount = 2 }
                    }
                }
            },
        };
        var errors = CardValidator.Validate(card);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidRelic_ReturnsNoErrors()
    {
        var card = new CardDef
        {
            Id = "hol_x_aelins_seal",
            Set = "buried_age",
            Name = "Aelin's Seal",
            Strata = Strata.HOLLOW,
            Type = CardType.RELIC,
            Rarity = Rarity.RELIC,
            Cost = 5,
            ContentVersion = 1,
            IdentifyCondition = new ConditionDef { Op = ConditionOp.BARROW_COUNT_GTE, Value = System.Text.Json.JsonDocument.Parse("3").RootElement },
            Keywords = new List<string> { "SEALED" },
        };
        var errors = CardValidator.Validate(card);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidRitualNoAbilities_ReturnsNoErrors()
    {
        var card = new CardDef
        {
            Id = "dwn_r_blank_rite",
            Set = "test",
            Name = "Blank Rite",
            Strata = Strata.DAWN,
            Type = CardType.RITUAL,
            Rarity = Rarity.COMMON,
            Cost = 1,
            ContentVersion = 1,
            Abilities = new List<AbilityDef>(),
        };
        var errors = CardValidator.Validate(card);
        Assert.Empty(errors);
    }

    // ——— Invalid: required fields ———

    [Fact]
    public void Validate_MissingId_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Id = "";
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("id"));
    }

    [Fact]
    public void Validate_BadIdFormat_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Id = "BAD-ID";
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("id") && e.Contains("pattern"));
    }

    [Fact]
    public void Validate_MissingName_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Name = "";
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("name"));
    }

    [Fact]
    public void Validate_NameTooLong_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Name = new string('x', 41);
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("40"));
    }

    [Fact]
    public void Validate_CostOutOfRange_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Cost = 11;
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("cost") && e.Contains("10"));
    }

    // ——— Invalid: type-specific ———

    [Fact]
    public void Validate_CreatureMissingAttack_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Attack = null;
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("attack"));
    }

    [Fact]
    public void Validate_CreatureAttackOutOfRange_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Attack = 13;
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("0-12"));
    }

    [Fact]
    public void Validate_CreatureMissingVigor_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Vigor = null;
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("vigor"));
    }

    [Fact]
    public void Validate_RitualHasAttack_ReturnsError()
    {
        var card = new CardDef
        {
            Id = "tst_r_bad",
            Set = "test",
            Name = "Bad Ritual",
            Strata = Strata.EMBER,
            Type = CardType.RITUAL,
            Rarity = Rarity.COMMON,
            Cost = 1,
            ContentVersion = 1,
            Attack = 2,
        };
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("RITUAL") && e.Contains("attack"));
    }

    [Fact]
    public void Validate_RelicMissingIdentifyCondition_ReturnsError()
    {
        var card = new CardDef
        {
            Id = "tst_x_bad",
            Set = "test",
            Name = "Bad Relic",
            Strata = Strata.HOLLOW,
            Type = CardType.RELIC,
            Rarity = Rarity.RELIC,
            Cost = 3,
            ContentVersion = 1,
        };
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("identify_condition"));
    }

    [Fact]
    public void Validate_TooManyKeywords_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Keywords = new List<string> { "GUARD", "SWIFT", "PIERCE", "WARD" };
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("max 3 keywords"));
    }

    [Fact]
    public void Validate_UnknownKeyword_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Keywords = new List<string> { "FLYING" };
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("unknown keyword"));
    }

    [Fact]
    public void Validate_TooManyAbilities_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Abilities = new List<AbilityDef>
        {
            new() { Trigger = Trigger.ON_SUMMON, Effects = new List<EffectDef> { new() { Op = Op.DRAW, Amount = 1 } } },
            new() { Trigger = Trigger.ON_DEATH, Effects = new List<EffectDef> { new() { Op = Op.HEAL, Amount = 1 } } },
            new() { Trigger = Trigger.ON_TURN_START, Effects = new List<EffectDef> { new() { Op = Op.BUFF, Attack = 1 } } }
        };
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("max 2 abilities"));
    }

    [Fact]
    public void Validate_NegativePowerScore_ReturnsError()
    {
        var card = MakeValidCreature();
        card.PowerScore = -1;
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("power_score"));
    }

    [Fact]
    public void Validate_LowContentVersion_ReturnsError()
    {
        var card = MakeValidCreature();
        card.ContentVersion = 0;
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("content_version"));
    }

    [Fact]
    public void Validate_FlavorTooLong_ReturnsError()
    {
        var card = MakeValidCreature();
        card.Flavor = new string('x', 141);
        var errors = CardValidator.Validate(card);
        Assert.Contains(errors, e => e.Contains("flavor") && e.Contains("140"));
    }
}