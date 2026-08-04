using System.Text.Json;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Represents a target count: either a specific number (1–3) or "ALL".
/// Serialized as integer or the string "ALL" in JSON.
/// </summary>
[JsonConverter(typeof(TargetCountConverter))]
public readonly struct TargetCount
{
    public int Value { get; }
    public bool IsAll { get; }

    private TargetCount(int value, bool isAll)
    {
        Value = value;
        IsAll = isAll;
    }

    public static TargetCount All => new(0, true);
    public static TargetCount Exactly(int n) => new(n, false);

    public override string ToString() => IsAll ? "ALL" : Value.ToString();
}

/// <summary>
/// Custom JSON converter for TargetCount. Accepts an integer (1–3) or the string "ALL".
/// </summary>
public class TargetCountConverter : JsonConverter<TargetCount>
{
    public override TargetCount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() == "ALL")
            return TargetCount.All;
        if (reader.TokenType == JsonTokenType.Number)
            return TargetCount.Exactly(reader.GetInt32());
        throw new JsonException("TargetCount must be an integer (1–3) or the string \"ALL\".");
    }

    public override void Write(Utf8JsonWriter writer, TargetCount value, JsonSerializerOptions options)
    {
        if (value.IsAll)
            writer.WriteStringValue("ALL");
        else
            writer.WriteNumberValue(value.Value);
    }
}
