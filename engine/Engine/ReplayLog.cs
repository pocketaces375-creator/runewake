using System.Text.Json;
using System.Text.Json.Serialization;
using Runewake.Engine.State;

namespace Runewake.Engine.Engine;

/// <summary>
/// A serializable record of a complete game, sufficient to reproduce the
/// identical final state from scratch.
/// </summary>
public sealed class ReplayLog
{
    /// <summary>Game configuration that fully determines the initial state.</summary>
    [JsonPropertyName("config")]
    public GameConfig Config { get; init; } = new();

    /// <summary>All actions applied in order to produce the final state.</summary>
    [JsonPropertyName("actions")]
    public List<GameAction> Actions { get; init; } = new();

    /// <summary>
    /// Serializes this replay log to a compact JSON string.
    /// </summary>
    public string ToJson()
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new GameActionConverter() }
        };
        return JsonSerializer.Serialize(this, opts);
    }

    /// <summary>
    /// Deserializes a replay log from a JSON string.
    /// </summary>
    public static ReplayLog FromJson(string json)
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new GameActionConverter() }
        };
        return JsonSerializer.Deserialize<ReplayLog>(json, opts)
            ?? throw new InvalidOperationException("Failed to deserialize ReplayLog.");
    }
}

/// <summary>
/// Polymorphic JSON converter for <see cref="GameAction"/> subclasses.
/// Uses a "$type" discriminator: "end_turn", "play_card", "attack".
/// </summary>
public class GameActionConverter : JsonConverter<GameAction>
{
    public override bool CanConvert(Type typeToConvert) =>
        typeof(GameAction).IsAssignableFrom(typeToConvert);

    public override GameAction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var typeName = root.GetProperty("$type").GetString();

        // Build default options without our converter to avoid recursion
        var cleanOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = options.PropertyNamingPolicy
        };

        return typeName switch
        {
            "end_turn" => JsonSerializer.Deserialize<EndTurnAction>(root.GetRawText(), cleanOpts),
            "play_card" => JsonSerializer.Deserialize<PlayCardAction>(root.GetRawText(), cleanOpts),
            "attack" => JsonSerializer.Deserialize<AttackAction>(root.GetRawText(), cleanOpts),
            _ => throw new JsonException($"Unknown action type: {typeName}")
        };
    }

    public override void Write(Utf8JsonWriter writer, GameAction value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        string typeName;
        int? playerIndex = null;
        int? cardInstanceId = null;
        int? cost = null;
        int? laneIndex = null;
        int? sourceLane = null;
        int? targetLane = null;

        switch (value)
        {
            case EndTurnAction e:
                typeName = "end_turn";
                playerIndex = e.PlayerIndex;
                break;
            case PlayCardAction p:
                typeName = "play_card";
                playerIndex = p.PlayerIndex;
                cardInstanceId = p.CardInstanceId;
                cost = p.Cost;
                laneIndex = p.LaneIndex;
                break;
            case AttackAction a:
                typeName = "attack";
                playerIndex = a.PlayerIndex;
                sourceLane = a.SourceLane;
                targetLane = a.TargetLane;
                break;
            default:
                throw new JsonException($"Unknown action type: {value.GetType()}");
        }

        writer.WriteString("$type", typeName);
        if (playerIndex.HasValue) writer.WriteNumber("playerIndex", playerIndex.Value);
        if (cardInstanceId.HasValue) writer.WriteNumber("cardInstanceId", cardInstanceId.Value);
        if (cost.HasValue) writer.WriteNumber("cost", cost.Value);
        if (laneIndex.HasValue) writer.WriteNumber("laneIndex", laneIndex.Value);
        if (sourceLane.HasValue) writer.WriteNumber("sourceLane", sourceLane.Value);
        if (targetLane.HasValue) writer.WriteNumber("targetLane", targetLane.Value);

        writer.WriteEndObject();
    }
}