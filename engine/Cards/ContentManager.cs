using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Runewake.Engine.Cards;

/// <summary>
/// A versioned, integrity-checked content pack.
/// Matches the JSON format produced by the Python publish module.
/// </summary>
public sealed class ContentPack
{
    [JsonPropertyName("set_id")]
    public string SetId { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("cards")]
    public List<CardDef> Cards { get; set; } = new();
}

/// <summary>
/// Result of attempting to load/apply a content pack.
/// Used so callers can distinguish success from fallback vs. total failure.
/// </summary>
public sealed class ContentPackResult
{
    /// <summary>The resolved pack (may be the fallback pack on hash mismatch).</summary>
    public ContentPack? Pack { get; init; }

    /// <summary>True if the requested pack loaded and verified successfully.</summary>
    public bool Success { get; init; }

    /// <summary>True if the fallback pack was used instead of the requested one.</summary>
    public bool UsedFallback { get; init; }

    /// <summary>Human-readable reason for fallback or failure.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The cards extracted from the resolved pack.</summary>
    public List<CardDef> Cards => Pack?.Cards ?? new();
}

/// <summary>
/// Manages loading, verifying, and falling back for versioned content packs.
///
/// The canonical JSON serialisation in this class MUST produce byte-identical
/// output to the Python <c>canonical_json()</c> function in <c>publish.py</c>
/// so that SHA-256 hashes computed on either side match.
///
/// Integrity verification is performed over the RAW received JSON (never over
/// deserialised CardDef objects) so that the exact bytes the server hashed are
/// the exact bytes the client re-hashes.
/// </summary>
public static class ContentManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Load a bundled (trusted) pack from a file path.
    /// Bundled packs are shipped with the client and are expected to be valid.
    /// </summary>
    public static ContentPack LoadBundledPack(string path)
    {
        var json = File.ReadAllText(path);
        var pack = JsonSerializer.Deserialize<ContentPack>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Bundled pack deserialised to null.");
        return pack;
    }

    /// <summary>
    /// Attempt to load a remote (downloaded) pack with hash verification.
    /// On hash mismatch or parse failure, falls back to the bundled pack.
    /// </summary>
    /// <param name="remoteJson">The downloaded pack JSON content.</param>
    /// <param name="bundledPath">Path to the bundled fallback pack file.</param>
    /// <returns>A ContentPackResult with the resolved pack and status info.</returns>
    public static ContentPackResult ApplyRemotePack(string remoteJson, string bundledPath)
    {
        // Try to parse the remote pack
        ContentPack? remotePack;
        try
        {
            remotePack = JsonSerializer.Deserialize<ContentPack>(remoteJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return FallbackToBundled(bundledPath, $"Remote pack parse failure: {ex.Message}");
        }

        if (remotePack is null)
        {
            return FallbackToBundled(bundledPath, "Remote pack deserialised to null.");
        }

        // Verify hash over the RAW json (exact bytes received)
        if (!VerifyPackJson(remoteJson))
        {
            return FallbackToBundled(bundledPath,
                "HASH_MISMATCH: remote pack content does not match declared hash.");
        }

        // Version sanity — prevent downgrade
        try
        {
            var bundled = LoadBundledPack(bundledPath);
            if (remotePack.Version < bundled.Version)
            {
                return FallbackToBundled(bundledPath,
                    $"VERSION_DOWNGRADE: remote v{remotePack.Version} < bundled v{bundled.Version}");
            }
        }
        catch
        {
            // No bundled pack available — proceed with remote
        }

        return new ContentPackResult
        {
            Pack = remotePack,
            Success = true,
            UsedFallback = false,
            Reason = "OK",
        };
    }

    /// <summary>
    /// Verify that a pack's declared SHA-256 hash matches its content.
    /// The pack is verified against its raw JSON string, so the exact bytes
    /// received are the exact bytes hashed.
    /// </summary>
    public static bool VerifyHash(ContentPack pack)
    {
        if (string.IsNullOrEmpty(pack.Hash))
            return false;
        var json = JsonSerializer.Serialize(pack, JsonOptions);
        return VerifyPackJson(json);
    }

    /// <summary>
    /// Verify that a pack JSON string's declared hash matches its content.
    /// Recomputes the canonical SHA-256 over {set_id, version, cards} from the
    /// raw JSON and compares to the declared 'hash' field.
    /// </summary>
    public static bool VerifyPackJson(string packJson)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(packJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is not JsonObject obj)
            return false;

        var declaredHash = obj["hash"]?.GetValue<string>() ?? string.Empty;
        if (string.IsNullOrEmpty(declaredHash))
            return false;

        // Rebuild payload exactly as Python does: {set_id, version, cards}
        var payload = new JsonObject
        {
            ["set_id"] = obj["set_id"]?.DeepClone(),
            ["version"] = obj["version"]?.DeepClone(),
            ["cards"] = obj["cards"]?.DeepClone(),
        };

        var canonical = Canonicalize(payload);
        var computed = Sha256Hex(canonical);
        return string.Equals(computed, declaredHash, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compute the canonical JSON string of a pack payload {set_id, version, cards}.
    /// Exposed for testing cross-language consistency with Python.
    /// </summary>
    public static string ComputeCanonicalPayloadJson(string setId, int version, string cardsJson)
    {
        var cardsNode = JsonNode.Parse(cardsJson);
        var payload = new JsonObject
        {
            ["set_id"] = setId,
            ["version"] = version,
            ["cards"] = cardsNode,
        };
        return Canonicalize(payload);
    }

    /// <summary>
    /// Recursively build a canonical JSON string from a JsonNode tree.
    /// Rules:
    ///   - Objects: keys sorted by ordinal (like Python sort_keys=True)
    ///   - Arrays: compact []
    ///   - Strings/numbers/bools: compact, no extra escaping (like ensure_ascii=False)
    ///   - null: "null"
    /// </summary>
    public static string Canonicalize(JsonNode? node)
    {
        if (node is null)
            return "null";

        if (node is JsonObject obj)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            var first = true;
            foreach (var kvp in obj.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append('"');
                sb.Append(kvp.Key);
                sb.Append('"');
                sb.Append(':');
                sb.Append(Canonicalize(kvp.Value));
            }
            sb.Append('}');
            return sb.ToString();
        }

        if (node is JsonArray arr)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            var first = true;
            foreach (var item in arr)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                sb.Append(Canonicalize(item));
            }
            sb.Append(']');
            return sb.ToString();
        }

        if (node is JsonValue val)
        {
            // ToJsonString with UnsafeRelaxedJsonEscaping produces compact
            // JSON scalars that match Python's json.dumps output.
            return val.ToJsonString(new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }

        return "null";
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }

    private static ContentPackResult FallbackToBundled(string bundledPath, string reason)
    {
        try
        {
            var bundled = LoadBundledPack(bundledPath);
            return new ContentPackResult
            {
                Pack = bundled,
                Success = true,
                UsedFallback = true,
                Reason = reason,
            };
        }
        catch (Exception ex)
        {
            return new ContentPackResult
            {
                Pack = null,
                Success = false,
                UsedFallback = false,
                Reason = $"Fallback also failed: {ex.Message}",
            };
        }
    }
}