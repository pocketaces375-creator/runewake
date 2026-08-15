using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Runewake.Client;

/// <summary>
/// C# data models mirroring tutorial_script.schema.json.
/// Deserialized from tutorial script JSON files for the TutorialRunner.
/// </summary>

public class TutorialScript
{
    [JsonPropertyName("tutorial_id")]
    public string TutorialId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("class")]
    public string Class { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("artifacts")]
    public List<string> Artifacts { get; set; } = new();

    [JsonPropertyName("player_deck")]
    public List<string> PlayerDeck { get; set; } = new();

    [JsonPropertyName("opponent_deck")]
    public List<string> OpponentDeck { get; set; } = new();

    [JsonPropertyName("turns")]
    public List<TurnScript> Turns { get; set; } = new();
}

public class TurnScript
{
    [JsonPropertyName("turn_number")]
    public int TurnNumber { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "player" or "opponent"

    [JsonPropertyName("player_hand_override")]
    public List<string>? PlayerHandOverride { get; set; }

    [JsonPropertyName("opponent_hand_override")]
    public List<string>? OpponentHandOverride { get; set; }

    [JsonPropertyName("player_attunement_override")]
    public int? PlayerAttunementOverride { get; set; }

    [JsonPropertyName("player_beats")]
    public List<TutorialBeat>? PlayerBeats { get; set; }

    [JsonPropertyName("opponent_actions")]
    public List<ScriptedAction>? OpponentActions { get; set; }
}

public class TutorialBeat
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("trigger_event")]
    public string TriggerEvent { get; set; } = string.Empty;

    [JsonPropertyName("condition")]
    public BeatCondition? Condition { get; set; }

    [JsonPropertyName("popup")]
    public string? Popup { get; set; }

    [JsonPropertyName("highlight")]
    public List<string>? Highlight { get; set; }

    [JsonPropertyName("restrict_actions_to")]
    public List<string>? RestrictActionsTo { get; set; }
}

public class BeatCondition
{
    [JsonPropertyName("not_attacked_this_turn")]
    public bool NotAttackedThisTurn { get; set; }

    [JsonPropertyName("attacked_count_gte")]
    public int? AttackedCountGte { get; set; }

    [JsonPropertyName("creatures_summoned_gte")]
    public int? CreaturesSummonedGte { get; set; }
}

public class ScriptedAction
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("card_id")]
    public string? CardId { get; set; }

    [JsonPropertyName("lane")]
    public int? Lane { get; set; }

    [JsonPropertyName("target_lane")]
    public int? TargetLane { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("delay_ms")]
    public int? DelayMs { get; set; }
}