using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Xunit;

namespace Runewake.Tests.Supabase;

/// <summary>
/// Tests for RelicLedgerSync and sync merge logic.
/// All tests use mock HttpMessageHandler — no network access.
/// </summary>
public class RelicLedgerSyncTests
{
    private static SupabaseConfig EmptyConfig => new();
    private static SupabaseConfig ConfiguredConfig => new()
    {
        Url = "https://test.supabase.co",
        AnonKey = "test-anon-key"
    };

    // ——— IsConfigured ———

    [Fact]
    public void IsConfigured_EmptyUrl_ReturnsFalse()
    {
        var sync = new RelicLedgerSync(EmptyConfig);
        Assert.False(sync.IsConfigured);
    }

    [Fact]
    public void IsConfigured_WithUrl_ReturnsTrue()
    {
        var sync = new RelicLedgerSync(ConfiguredConfig);
        Assert.True(sync.IsConfigured);
    }

    // ——— GetOrCreateAccountId ———

    [Fact]
    public async Task GetOrCreateAccountId_NetworkFailure_ReturnsNull()
    {
        var handler = new MockHttpHandler(
            _ => throw new HttpRequestException("Network error"));
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var result = await sync.GetOrCreateAccountId("test-device");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrCreateAccountId_NotConfigured_ReturnsNull()
    {
        var sync = new RelicLedgerSync(EmptyConfig);
        var result = await sync.GetOrCreateAccountId("test-device");
        Assert.Null(result);
    }

    // ——— SyncRelics ———

    [Fact]
    public async Task SyncRelics_NotConfigured_ReturnsImmediately()
    {
        int callCount = 0;
        var handler = new MockHttpHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(EmptyConfig, http);

        await sync.SyncRelics("account-1", new List<LostRelicInstance>
        {
            new() { RelicInstanceId = "r1", CardId = "c1" }
        });

        Assert.Equal(0, callCount); // No HTTP calls made
    }

    [Fact]
    public async Task SyncRelics_BatchesCorrectly_50PerBatch()
    {
        int callCount = 0;
        var handler = new MockHttpHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var relics = new List<LostRelicInstance>();
        for (int i = 0; i < 110; i++)
        {
            relics.Add(new LostRelicInstance
            {
                RelicInstanceId = $"r{i:D3}",
                CardId = $"c{i}"
            });
        }

        await sync.SyncRelics("account-1", relics);

        Assert.Equal(3, callCount); // 50 + 50 + 10
    }

    [Fact]
    public async Task SyncRelics_EmptyList_MakesNoCalls()
    {
        int callCount = 0;
        var handler = new MockHttpHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        await sync.SyncRelics("account-1", new List<LostRelicInstance>());

        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task SyncRelics_NetworkFailure_DoesNotThrow()
    {
        var handler = new MockHttpHandler(
            _ => throw new HttpRequestException("Timeout"));
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var relics = new List<LostRelicInstance>
        {
            new() { RelicInstanceId = "r1", CardId = "c1" }
        };

        // Should not throw
        await sync.SyncRelics("account-1", relics);
    }

    // ——— FetchRelics ———

    [Fact]
    public async Task FetchRelics_EmptyResponse_ReturnsEmptyList()
    {
        var handler = new MockHttpHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
            };
        });
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var result = await sync.FetchRelics("account-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchRelics_MalformedJson_ReturnsEmptyList()
    {
        var handler = new MockHttpHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", System.Text.Encoding.UTF8, "application/json")
            };
        });
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var result = await sync.FetchRelics("account-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchRelics_NetworkFailure_ReturnsEmptyList()
    {
        var handler = new MockHttpHandler(
            _ => throw new HttpRequestException("Timeout"));
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var result = await sync.FetchRelics("account-1");

        Assert.Empty(result);
    }

    [Fact]
    public async Task FetchRelics_NotConfigured_ReturnsEmptyList()
    {
        var sync = new RelicLedgerSync(EmptyConfig);
        var result = await sync.FetchRelics("account-1");
        Assert.Empty(result);
    }

    // ——— Merge logic (same semantics as SyncManager.RunStartupSync) ———

    [Fact]
    public async Task FetchRelics_ReturnsDeserializedRelics()
    {
        var serverJson = @"[
            {
                ""relic_instance_id"": ""rel-a"",
                ""card_id"": ""relic_aelins_seal"",
                ""acquirer_name"": ""Trikzos"",
                ""acquired_at"": ""2026-08-07"",
                ""site"": ""The Fallow Reach"",
                ""discovery_index"": 1,
                ""engraving_style"": ""verdant_gold""
            },
            {
                ""relic_instance_id"": ""rel-b"",
                ""card_id"": ""relic_ember_crown"",
                ""acquirer_name"": ""Trikzos"",
                ""acquired_at"": ""2026-08-08"",
                ""site"": ""Ember Depths"",
                ""discovery_index"": 2,
                ""engraving_style"": ""ember_iron""
            }
        ]";
        var handler = new MockHttpHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(serverJson, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var relics = await sync.FetchRelics("account-1");

        Assert.Equal(2, relics.Count);
        Assert.Equal("rel-a", relics[0].RelicInstanceId);
        Assert.Equal("relic_aelins_seal", relics[0].CardId);
        Assert.Equal("Trikzos", relics[0].AcquirerName);
        Assert.Equal(1, relics[0].DiscoveryIndex);
        Assert.Equal("rel-b", relics[1].RelicInstanceId);
    }

    [Fact]
    public async Task MergeServerRelics_SkipsExistingIds()
    {
        // Simulate the merge logic from SyncManager.RunStartupSync
        var prog = new ProgressionState();

        // Local has relic A
        prog.AddRelic(new LostRelicInstance
        {
            RelicInstanceId = "rel-a",
            CardId = "relic_aelins_seal",
            AcquirerName = "Test",
            AcquiredAt = "2026-08-07",
            Site = "The Fallow Reach",
            DiscoveryIndex = 1,
            EngravingStyle = "verdant_gold"
        });

        // Server returns relics A (existing) + B (new)
        var serverJson = @"[
            {
                ""relic_instance_id"": ""rel-a"",
                ""card_id"": ""relic_aelins_seal"",
                ""acquirer_name"": ""Test"",
                ""acquired_at"": ""2026-08-07"",
                ""site"": ""The Fallow Reach"",
                ""discovery_index"": 1,
                ""engraving_style"": ""verdant_gold""
            },
            {
                ""relic_instance_id"": ""rel-b"",
                ""card_id"": ""relic_ember_crown"",
                ""acquirer_name"": ""Test"",
                ""acquired_at"": ""2026-08-08"",
                ""site"": ""Ember Depths"",
                ""discovery_index"": 2,
                ""engraving_style"": ""ember_iron""
            }
        ]";

        var handler = new MockHttpHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(serverJson, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var http = new HttpClient(handler);
        var sync = new RelicLedgerSync(ConfiguredConfig, http);

        var serverRelics = await sync.FetchRelics("account-1");

        // Merge logic (same as SyncManager.RunStartupSync)
        var localIds = new HashSet<string>(prog.DiscoveredRelics.Select(r => r.RelicInstanceId));
        foreach (var relic in serverRelics)
        {
            if (!localIds.Contains(relic.RelicInstanceId))
            {
                prog.AddRelic(relic);
            }
        }

        // Assert: prog has exactly 2 relics (A + B), no duplicates
        Assert.Equal(2, prog.DiscoveredRelics.Count);
        Assert.Contains(prog.DiscoveredRelics, r => r.RelicInstanceId == "rel-a");
        Assert.Contains(prog.DiscoveredRelics, r => r.RelicInstanceId == "rel-b");
    }
}

// ——— Mock HTTP handler ———

internal class MockHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}