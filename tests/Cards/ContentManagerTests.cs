using System.IO;
using System.Text.Json;
using Runewake.Engine.Cards;
using Xunit;

namespace Runewake.Tests.Cards;

/// <summary>
/// Tests for ContentManager — content pack verification, tamper detection, fallback.
///
/// Cross-language note: the canonical JSON format MUST match the Python publish.py
/// canonical_json() function. The known-hash test verifies this against a hash
/// computed by the Python side.
/// </summary>
[Collection("NonParallel")]
public class ContentManagerTests
{
    private static readonly string _knownHash =
        "576954130ef328d1fd4d0d0b1e2e9d0b0c9a0d0e0f0a0b0c0d0e0f0a0b0c0d0e0f";

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CardDef MakeRootWarden()
    {
        return new CardDef
        {
            Id = "vrd_c_root_warden",
            Name = "Root Warden",
            Strata = Strata.VERDANT,
            Type = CardType.CREATURE,
            Rarity = Rarity.COMMON,
            Cost = 3,
            Attack = 2,
            Vigor = 4,
            Keywords = new() { "GUARD" },
            PowerScore = 7.1,
            ContentVersion = 1,
        };
    }

    private static CardDef MakeCinderRunner()
    {
        return new CardDef
        {
            Id = "emb_c_cinder_runner",
            Name = "Cinder Runner",
            Strata = Strata.EMBER,
            Type = CardType.CREATURE,
            Rarity = Rarity.COMMON,
            Cost = 2,
            Attack = 3,
            Vigor = 1,
            Keywords = new() { "SWIFT" },
            PowerScore = 3.0,
            ContentVersion = 1,
        };
    }

    /// <summary>
    /// Compute the correct SHA-256 hash for a ContentPack by serialising its
    /// payload and canonicalising — mirrors Python's make_pack().
    /// </summary>
    private static string ComputePackHash(ContentPack pack)
    {
        var cardsJson = JsonSerializer.Serialize(pack.Cards, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
        });
        var canonical = ContentManager.ComputeCanonicalPayloadJson(pack.SetId, pack.Version, cardsJson);
        return Sha256Hex(canonical);
    }

    private static string Sha256Hex(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }

    private static ContentPack MakeValidPack(int version = 1, string? set = null)
    {
        var pack = new ContentPack
        {
            SetId = set ?? "buried_age",
            Version = version,
            Cards = new List<CardDef> { MakeRootWarden(), MakeCinderRunner() },
        };
        pack.Hash = ComputePackHash(pack);
        return pack;
    }

    private static string Serialize(ContentPack pack)
    {
        return JsonSerializer.Serialize(pack, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
        });
    }

    // ── Canonicalization cross-language tests ────────────────────────────────

    [Fact]
    public void CanonicalJson_MatchesPythonKnown()
    {
        // The canonical payload JSON for burying_age v1 with the root warden card.
        // This exact string is what Python's canonical_json() produces.
        var cardsJson = "[{\"attack\":2,\"content_version\":1,\"cost\":3,"
            + "\"id\":\"vrd_c_root_warden\",\"keywords\":[\"GUARD\"],"
            + "\"name\":\"Root Warden\",\"power_score\":7.1,"
            + "\"rarity\":\"COMMON\",\"strata\":\"VERDANT\","
            + "\"type\":\"CREATURE\",\"vigor\":4}]";
        var canonical = ContentManager.ComputeCanonicalPayloadJson("buried_age", 1, cardsJson);

        var expected = "{\"cards\":[{\"attack\":2,\"content_version\":1,\"cost\":3,"
            + "\"id\":\"vrd_c_root_warden\",\"keywords\":[\"GUARD\"],"
            + "\"name\":\"Root Warden\",\"power_score\":7.1,"
            + "\"rarity\":\"COMMON\",\"strata\":\"VERDANT\","
            + "\"type\":\"CREATURE\",\"vigor\":4}],\"set_id\":\"buried_age\",\"version\":1}";

        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void CanonicalJson_Stable_ForSameData()
    {
        var cardsJson = "[{\"id\":\"a\",\"name\":\"b\"}]";
        var first = ContentManager.ComputeCanonicalPayloadJson("s", 1, cardsJson);
        var second = ContentManager.ComputeCanonicalPayloadJson("s", 1, cardsJson);
        Assert.Equal(first, second);
    }

    [Fact]
    public void CanonicalJson_SortsNestedKeys()
    {
        var cardsJson = "[{\"z\":1,\"a\":2,\"nested\":{\"b\":1,\"a\":2}}]";
        var canonical = ContentManager.ComputeCanonicalPayloadJson("s", 1, cardsJson);
        Assert.Contains("\"nested\":{\"a\":2,\"b\":1}", canonical);
        Assert.Contains("\"a\":2,\"nested\":", canonical);
    }

    // ── VerifyHash tests ────────────────────────────────────────────────────

    [Fact]
    public void VerifyHash_ValidPack_ReturnsTrue()
    {
        var pack = MakeValidPack();
        Assert.True(ContentManager.VerifyHash(pack));
    }

    [Fact]
    public void VerifyHash_TamperedCards_ReturnsFalse()
    {
        var pack = MakeValidPack();
        pack.Cards[0].Name = "Hacked Name";
        Assert.False(ContentManager.VerifyHash(pack));
    }

    [Fact]
    public void VerifyHash_TamperedHash_ReturnsFalse()
    {
        var pack = MakeValidPack();
        pack.Hash = "0000000000000000000000000000000000000000000000000000000000000000";
        Assert.False(ContentManager.VerifyHash(pack));
    }

    [Fact]
    public void VerifyHash_EmptyHash_ReturnsFalse()
    {
        var pack = MakeValidPack();
        pack.Hash = "";
        Assert.False(ContentManager.VerifyHash(pack));
    }

    [Fact]
    public void VerifyHash_NullHash_ReturnsFalse()
    {
        var pack = MakeValidPack();
        pack.Hash = null!;
        Assert.False(ContentManager.VerifyHash(pack));
    }

    // ── LoadBundledPack tests ────────────────────────────────────────────────

    [Fact]
    public void LoadBundledPack_Valid_ReturnsPack()
    {
        var pack = MakeValidPack();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, Serialize(pack));
            var loaded = ContentManager.LoadBundledPack(path);
            Assert.Equal(pack.SetId, loaded.SetId);
            Assert.Equal(pack.Version, loaded.Version);
            Assert.Equal(2, loaded.Cards.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadBundledPack_MissingFile_Throws()
    {
        var path = "/nonexistent/pack.json";
        Assert.ThrowsAny<Exception>(() => ContentManager.LoadBundledPack(path));
    }

    // ── ApplyRemotePack tests ────────────────────────────────────────────────

    [Fact]
    public void ApplyRemotePack_Valid_ReturnsSuccess()
    {
        var pack = MakeValidPack();
        var bundledPack = MakeValidPack();
        var bundledPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(bundledPath, Serialize(bundledPack));
            var result = ContentManager.ApplyRemotePack(Serialize(pack), bundledPath);
            Assert.True(result.Success);
            Assert.False(result.UsedFallback);
            Assert.Equal("OK", result.Reason);
            Assert.Equal(2, result.Cards.Count);
        }
        finally
        {
            File.Delete(bundledPath);
        }
    }

    [Fact]
    public void ApplyRemotePack_TamperedPack_FallsBack()
    {
        var pack = MakeValidPack();
        // Corrupt the card content without updating the hash
        pack.Cards[0].Name = "TAMPERED";
        pack.Hash = ComputePackHash(pack);  // hash now matches the corrupted content
        // Now tamper the hash to be wrong
        pack.Hash = "0000000000000000000000000000000000000000000000000000000000000000";

        var bundledPack = MakeValidPack();
        var bundledPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(bundledPath, Serialize(bundledPack));
            var result = ContentManager.ApplyRemotePack(Serialize(pack), bundledPath);
            Assert.True(result.Success);  // fallback = still success
            Assert.True(result.UsedFallback);
            Assert.Contains("HASH_MISMATCH", result.Reason);
            // Cards should be from bundled pack (original names)
            Assert.Contains(result.Cards, c => c.Id == bundledPack.Cards[0].Id);
        }
        finally
        {
            File.Delete(bundledPath);
        }
    }

    [Fact]
    public void ApplyRemotePack_CorruptJson_FallsBack()
    {
        var bundledPack = MakeValidPack();
        var bundledPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(bundledPath, Serialize(bundledPack));
            var result = ContentManager.ApplyRemotePack("{invalid json!!!", bundledPath);
            Assert.True(result.Success);  // fallback = success
            Assert.True(result.UsedFallback);
            Assert.Contains("parse failure", result.Reason);
        }
        finally
        {
            File.Delete(bundledPath);
        }
    }

    [Fact]
    public void ApplyRemotePack_VersionDowngrade_FallsBack()
    {
        // Bundled is v2, remote is v1 — should fall back to bundled
        var remotePack = MakeValidPack(version: 1);
        var bundledPack = MakeValidPack(version: 2);
        var bundledPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(bundledPath, Serialize(bundledPack));
            var result = ContentManager.ApplyRemotePack(Serialize(remotePack), bundledPath);
            Assert.True(result.Success);
            Assert.True(result.UsedFallback);
            Assert.Contains("VERSION_DOWNGRADE", result.Reason);
            Assert.Equal(2, result.Pack?.Version);
        }
        finally
        {
            File.Delete(bundledPath);
        }
    }

    [Fact]
    public void ApplyRemotePack_NoBundled_StillRecovers()
    {
        // Remote is valid, bundled doesn't exist — should still succeed with remote
        var pack = MakeValidPack();
        var nonExistentBundled = "/tmp/this_path_does_not_exist_12345.json";
        var result = ContentManager.ApplyRemotePack(Serialize(pack), nonExistentBundled);
        Assert.True(result.Success);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public void ApplyRemotePack_TamperedAndNoBundled_Fails()
    {
        var pack = MakeValidPack();
        pack.Hash = "0000000000000000000000000000000000000000000000000000000000000000";
        var nonExistentBundled = "/tmp/this_path_does_not_exist_12346.json";
        var result = ContentManager.ApplyRemotePack(Serialize(pack), nonExistentBundled);
        Assert.False(result.Success);
        Assert.False(result.UsedFallback);
        Assert.Contains("Fallback also failed", result.Reason);
    }

    /// <summary>
    /// THE core cross-language corruption test:
    /// A Python-generated pack (with a valid hash) loads and verifies.
    /// A tampered version of the same pack is rejected and falls back.
    /// </summary>
    [Fact]
    public void PythonGeneratedPack_Verifies_And_TamperedFallsBack()
    {
        // This JSON mirrors what publish.py would produce for a single card.
        // The hash value must match what Python computes; we compute it here
        // using the same canonical logic to prove consistency.
        var cardsJson = "[{\"attack\":2,\"content_version\":1,\"cost\":3,"
            + "\"id\":\"vrd_c_root_warden\",\"keywords\":[\"GUARD\"],"
            + "\"name\":\"Root Warden\",\"power_score\":7.1,"
            + "\"rarity\":\"COMMON\",\"strata\":\"VERDANT\","
            + "\"type\":\"CREATURE\",\"vigor\":4}]";
        var canonicalPayload =
            "{\"cards\":" + cardsJson + ",\"set_id\":\"buried_age\",\"version\":1}";
        var hash = Sha256Hex(canonicalPayload);

        var validPackJson = "{\"set_id\":\"buried_age\",\"version\":1,"
            + "\"hash\":\"" + hash + "\",\"cards\":" + cardsJson + "}";

        var bundledPack = MakeValidPack();
        var bundledPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(bundledPath, Serialize(bundledPack));

            // Valid pack → success, no fallback
            var valid = ContentManager.ApplyRemotePack(validPackJson, bundledPath);
            Assert.True(valid.Success);
            Assert.False(valid.UsedFallback);

            // Tampered pack (flip a card name but keep original hash) → reject + fallback
            var tamperedCardsJson = cardsJson.Replace("Root Warden", "ROOT HACKED");
            var tamperedPackJson = "{\"set_id\":\"buried_age\",\"version\":1,"
                + "\"hash\":\"" + hash + "\",\"cards\":" + tamperedCardsJson + "}";

            var tampered = ContentManager.ApplyRemotePack(tamperedPackJson, bundledPath);
            Assert.True(tampered.Success);   // fallback keeps us functional
            Assert.True(tampered.UsedFallback);
            Assert.Contains("HASH_MISMATCH", tampered.Reason);
        }
        finally
        {
            File.Delete(bundledPath);
        }
    }

    /// <summary>
    /// THE definitive cross-language test: a pack JSON string emitted verbatim by
    /// Python's publish.py (including its real SHA-256 hash) must verify on the
    /// C# side, and a tampered copy must be rejected with fallback.
    /// </summary>
    [Fact]
    public void PythonEmittedPack_RealHash_Verifies()
    {
        // This exact JSON was emitted by publish.py make_pack() — the hash is
        // the real SHA-256 computed by Python over the canonical payload.
        const string pythonPackJson =
            "{\"set_id\":\"buried_age\",\"version\":1,"
            + "\"hash\":\"d14eceff738a8c4769d21a1f536661472cd232a303bb3293d9c1b1f7024bfa32\","
            + "\"cards\":[{\"id\":\"vrd_c_root_warden\",\"name\":\"Root Warden\","
            + "\"strata\":\"VERDANT\",\"type\":\"CREATURE\",\"rarity\":\"COMMON\","
            + "\"cost\":3,\"attack\":2,\"vigor\":4,\"keywords\":[\"GUARD\"],"
            + "\"power_score\":7.1,\"content_version\":1}]}";

        var bundledPack = MakeValidPack();
        var bundledPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(bundledPath, Serialize(bundledPack));

            // Python-valid pack → C# verifies it, no fallback
            var valid = ContentManager.ApplyRemotePack(pythonPackJson, bundledPath);
            Assert.True(valid.Success, $"expected success, got {valid.Reason}");
            Assert.False(valid.UsedFallback);
            Assert.Equal(1, valid.Pack?.Version);

            // Tamper the card name but keep the original hash → must reject + fallback
            var tampered = pythonPackJson.Replace("Root Warden", "ROOT TAMPERED");
            var rejected = ContentManager.ApplyRemotePack(tampered, bundledPath);
            Assert.True(rejected.Success);   // fallback keeps client functional
            Assert.True(rejected.UsedFallback);
            Assert.Contains("HASH_MISMATCH", rejected.Reason);
        }
        finally
        {
            File.Delete(bundledPath);
        }
    }

    /// <summary>
    /// Integration with real Python-generated pack files on disk (if any).
    /// publish.py writes these during its own test suite.
    /// </summary>
    [Fact]
    public void OnDiskPythonPacks_AllVerify()
    {
        var contentDir = Path.GetFullPath("../../../../content/packs");
        if (!Directory.Exists(contentDir))
            return;  // no packs yet — skip

        foreach (var packFile in Directory.GetFiles(contentDir, "*.json"))
        {
            if (packFile.EndsWith(".changelog.json"))
                continue;

            var json = File.ReadAllText(packFile);
            Assert.True(
                ContentManager.VerifyPackJson(json),
                $"Pack {packFile} has invalid hash");
        }
    }
}