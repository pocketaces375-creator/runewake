using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Art reference for a card. The AI pipeline writes the prompt and the
/// art pipeline fills in the asset URL after generation.
/// </summary>
public sealed class ArtDef
{
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("asset")]
    public string? Asset { get; set; }
}
