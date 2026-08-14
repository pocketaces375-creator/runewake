using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// Stats gained per charge spent, used by FORGE ops.
/// JSON shape: { "attack": 1, "vigor": 1 }
/// </summary>
public sealed class PerChargeStats
{
    /// <summary>Attack granted per charge.</summary>
    [JsonPropertyName("attack")]
    public int Attack { get; set; }

    /// <summary>Vigor granted per charge.</summary>
    [JsonPropertyName("vigor")]
    public int Vigor { get; set; }
}