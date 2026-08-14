using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

// ——— Core enums — DSL §2 ———
// Values match the JSON exactly (SCREAMING_SNAKE_CASE).

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Strata { VERDANT, EMBER, TIDE, HOLLOW, DAWN }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardType { CREATURE, RITUAL, RELIC, CURSE, TOKEN, ARTIFACT }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Rarity { COMMON, UNCOMMON, RARE, RELIC }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Duration { PERMANENT, THIS_TURN, NEXT_TURN, WHILE_PRESENT }

// ——— Trigger ———

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Trigger
{
    ON_SUMMON, ON_DEATH, ON_ATTACK, ON_DAMAGED,
    ON_TURN_START, ON_TURN_END, ON_CAST_RITUAL, ON_EXCAVATE,
    ON_RELIC_IDENTIFY, ON_ALLY_DEATH, ON_LANE_VACATED,
    PASSIVE, ACTIVATED, RESOLVE,
    // Artifact-specific triggers
    ON_ARTIFACT_REVEAL, ON_CHARGE_GAINED, ON_CHARGE_FULL,
    ON_ARTIFACT_SUPPRESS, ON_ARTIFACT_UNSUPPRESS,
    ON_CREATURE_ATTACKS, ON_CREATURE_DIES, ON_SPELL_CAST,
    ON_PREY_MARKED, ON_PREY_DESTROYED, ON_HEAL,
    ON_NO_ATTACK_TURN, ON_TURN_END_NO_ATTACK
}

// ——— Effect Operation ———

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Op
{
    DAMAGE, HEAL, BUFF, DEBUFF, DESTROY,
    DRAW, DISCARD, EXCAVATE, BURY, UNBURY,
    SUMMON, GRANT_KEY, REMOVE_KEY, SILENCE, BOUNCE,
    ATTUNE, MOVE_LANE, IDENTIFY, GAIN_VIGOR, LOSE_VIGOR,
    COPY, SET_STAT, REFRESH,
    // Artifact-specific ops
    SUPPRESS, ADD_CHARGE, SET_PREY, REVIVE_TOKEN,
    PREVENT_DAMAGE, COST_MOD, RESET_CHARGES
}

// ——— Target Scope ———

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Scope
{
    SELF, ALLY_CREATURE, ENEMY_CREATURE, ANY_CREATURE,
    PLAYER_SELF, PLAYER_ENEMY, LANE, NONE
}

// ——— Condition Operation ———

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConditionOp
{
    ALLY_COUNT_GTE, ENEMY_COUNT_GTE, BARROW_COUNT_GTE, HAND_COUNT_GTE,
    HAND_COUNT_LTE, TURN_GTE, VIGOR_LTE, VIGOR_GTE, ATTUNEMENT_GTE,
    CONTROLS_KEYWORD, CONTROLS_STRATA, DAMAGED_THIS_TURN, RITUALS_CAST_GTE,
    // Artifact conditions (turn-scoped counters)
    ATTACKERS_THIS_TURN_GTE, ATTACKERS_THIS_TURN_EQ,
    SPELLS_CAST_THIS_TURN_GTE, SPELLS_CAST_THIS_TURN_EQ,
    NO_ATTACKERS_LAST_TURN, CREATURE_DIED_THIS_TURN,
    FEWER_ALLY_CREATURES_THAN_ENEMY, ALLY_CREATURE_EXISTS,
    PARTNER_CHARGES_GTE, DURING_YOUR_TURN,
    NTH_ATTACKER_ON_PREY_THIS_TURN,
    FRIENDLY, ENEMY
}
