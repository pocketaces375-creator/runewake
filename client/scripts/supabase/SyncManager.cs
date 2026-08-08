using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;

namespace Runewake.Client;

/// <summary>
/// Godot Node that manages relic ledger sync lifecycle.
/// Added as a child of Main — runs startup sync once, then listens
/// for mint events from DuelScene.
/// All methods are safe to call when not configured (no-op).
/// </summary>
public partial class SyncManager : Node
{
    private RelicLedgerSync? _sync;
    private ProgressionState? _prog;
    private SaveManager? _save;
    private string? _accountId;

    private static readonly string AccountIdPath = "user://data/account_id.txt";

    /// <summary>
    /// Initialize with config and state references.
    /// Called once from Main.cs after LoadGameData().
    /// </summary>
    public void Initialize(SupabaseConfig config, ProgressionState prog, SaveManager save)
    {
        _sync = new RelicLedgerSync(config);
        _prog = prog;
        _save = save;
    }

    /// <summary>
    /// Run one startup sync cycle: get/create account ID, fetch server relics,
    /// merge into local progression, push local-only relics, save.
    /// Fire-and-forget — never awaits from main thread.
    /// </summary>
    public async Task RunStartupSync()
    {
        if (_sync == null || !_sync.IsConfigured || _prog == null || _save == null)
        {
            GD.Print("[SyncManager] Not configured — startup sync skipped.");
            return;
        }

        try
        {
            GD.Print("[SyncManager] Starting startup sync...");

            // 1. Get or create account ID
            var deviceId = GetOrCreateDeviceId();
            var accountId = await _sync.GetOrCreateAccountId(deviceId).ConfigureAwait(false);
            if (string.IsNullOrEmpty(accountId))
            {
                // Network failure — use local fallback UUID
                accountId = GetOrCreateLocalAccountId();
                GD.Print($"[SyncManager] Using local fallback account ID: {accountId}");
            }
            else
            {
                GD.Print($"[SyncManager] Server account ID: {accountId}");
            }
            _accountId = accountId;

            // 2. Fetch server relics and merge (add server-only relics not in local set)
            var serverRelics = await _sync.FetchRelics(accountId).ConfigureAwait(false);
            if (serverRelics.Count > 0)
            {
                var localIds = new HashSet<string>(
                    _prog.DiscoveredRelics.Select(r => r.RelicInstanceId));
                int mergedCount = 0;
                foreach (var relic in serverRelics)
                {
                    if (!localIds.Contains(relic.RelicInstanceId))
                    {
                        _prog.AddRelic(relic);
                        mergedCount++;
                    }
                }
                GD.Print($"[SyncManager] Merged {mergedCount} server-only relics.");
            }

            // 3. Push any local-only relics to server
            var localOnly = _prog.DiscoveredRelics
                .Where(r => !serverRelics.Any(s => s.RelicInstanceId == r.RelicInstanceId))
                .ToList();
            if (localOnly.Count > 0)
            {
                await _sync.SyncRelics(accountId, localOnly).ConfigureAwait(false);
                GD.Print($"[SyncManager] Pushed {localOnly.Count} local relics to server.");
            }

            // 4. Persist after merge
            _save.Save();
            GD.Print("[SyncManager] Startup sync complete.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SyncManager] Startup sync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by DuelScene after minting a new Lost Relic.
    /// Pushes the single new relic to the server immediately.
    /// Fire-and-forget — never blocks the UI.
    /// </summary>
    public async Task SyncOnRelicMint(LostRelicInstance relic)
    {
        if (_sync == null || !_sync.IsConfigured || _accountId == null || _prog == null || _save == null)
            return;

        try
        {
            GD.Print($"[SyncManager] Syncing newly minted relic {relic.RelicInstanceId}...");
            await _sync.SyncRelics(_accountId, new List<LostRelicInstance> { relic }).ConfigureAwait(false);
            _save.Save();
            GD.Print("[SyncManager] Mint sync complete.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SyncManager] Mint sync failed: {ex.Message}");
        }
    }

    // ——— Local helpers ———

    private static string GetOrCreateDeviceId()
    {
        // Try to read existing device ID from user://data/account_id.txt
        try
        {
            if (Godot.FileAccess.FileExists(AccountIdPath))
            {
                var existing = Godot.FileAccess.GetFileAsString(AccountIdPath)?.Trim();
                if (!string.IsNullOrEmpty(existing))
                    return existing;
            }
        }
        catch
        {
            // Ignore read failures
        }

        // Generate a new persistent device ID
        var newId = Guid.NewGuid().ToString();
        try
        {
            using var file = Godot.FileAccess.Open(AccountIdPath, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(newId);
        }
        catch
        {
            // Best-effort write
        }
        return newId;
    }

    private static string GetOrCreateLocalAccountId()
    {
        // Same file doubles as local fallback account ID
        return GetOrCreateDeviceId();
    }
}