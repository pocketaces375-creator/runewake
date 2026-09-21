using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;
using Runewake.Persistence;

namespace Runewake.Client;

/// <summary>
/// The account + cloud-save brain of the client. FABLE-018.
///
/// What it does, in order, every launch (RunStartupSync):
///   1. Has a session on disk? Refresh it if it is near expiry.
///      None? Sign in ANONYMOUSLY. The player never sees this.
///   2. Fetch the cloud save. Compare it with when this install last synced
///      and whether the game has saved since. CloudSaveSync.Decide picks one
///      of: push ours / pull theirs / nothing / CONFLICT.
///   3. Conflict = both this phone and the cloud moved since we last spoke.
///      We do not guess. PendingConflict is set and the account panel asks.
///   4. Relics (the ledger) sync as before, now under the user's JWT.
///
/// After that: every SaveManager.Save() calls NotifySaved(), which marks the
/// install dirty and schedules a push ~6s later (coalesced, one in flight).
///
/// Everything is best-effort and offline-first. No network, wrong config,
/// dashboard toggle off — the game plays exactly as it always has, and the
/// account panel shows a one-line reason.
///
/// Threading: every await continues on the thread pool (ConfigureAwait
/// false), so this class touches NO nodes after an await. Anything that must
/// reach the scene tree goes through Emit(), which defers to the main thread.
/// </summary>
public partial class SyncManager : Node
{
    // ── signals (always emitted on the main thread) ───────────────────────
    [Signal] public delegate void StatusChangedEventHandler(string status);
    [Signal] public delegate void ConflictDetectedEventHandler();
    [Signal] public delegate void CloudSaveAppliedEventHandler();

    // ── config / services ─────────────────────────────────────────────────
    private SupabaseConfig _config = new();
    private SupabaseAuth? _auth;
    private CloudSaveSync? _cloud;
    private RelicLedgerSync? _relics;
    private SaveManager? _save;

    private const string SessionPath = "user://supabase_session.json";
    private const string PreviousSessionPath = "user://supabase_session.previous.json";
    private const string SyncMetaPath = "user://cloud_sync.json";
    private const string DeviceIdPath = "user://data/account_id.txt";   // pre-existing file, reused as device id
    private const string ProfilesPath = "user://profiles.json";
    private const string DecksPath = "user://decks.json";
    private const int MaxSlots = 3;

    // ── state ─────────────────────────────────────────────────────────────
    public SupabaseSession? Session { get; private set; }
    public bool IsConfigured => _config.IsConfigured;
    public bool IsSignedIn => Session?.IsValid == true;
    public bool IsLinked => Session != null && !Session.IsAnonymous && !string.IsNullOrEmpty(Session.Email);

    /// <summary>One line for the account panel: "Backed up · Guest 3F2A", "No connection", …</summary>
    public string Status { get; private set; } = "Starting…";

    /// <summary>Set when both sides moved. The panel reads it, the player picks, ResolveConflict clears it.</summary>
    public ConflictInfo? PendingConflict { get; private set; }

    public sealed class ConflictInfo
    {
        public CloudSaveSync.CloudSave Cloud { get; init; } = new();
        public string CloudSummary { get; init; } = "";
        public string LocalSummary { get; init; } = "";
    }

    private sealed class SyncMeta
    {
        public string? LastSyncedAt { get; set; }   // ISO-8601, server's updated_at
        public bool Dirty { get; set; }
        public string? LastError { get; set; }
    }
    private SyncMeta _meta = new();
    private string _deviceId = "";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _pushScheduled = 0;
    private DateTime _lastSaveUtc = DateTime.MinValue;

    // ═════════════════════════════════════════════════════════════════════
    //  Setup
    // ═════════════════════════════════════════════════════════════════════

    public void Initialize(SupabaseConfig config, ProgressionState prog, SaveManager save)
    {
        _config = config;
        _save = save;
        _auth = new SupabaseAuth(config);
        _cloud = new CloudSaveSync(config);
        _relics = new RelicLedgerSync(config);
        _deviceId = ReadOrCreateDeviceId();
        _meta = ReadMeta();
        Session = ReadSession();
        if (_relics != null && Session != null) _relics.AccessToken = Session.AccessToken;
        if (!config.IsConfigured) SetStatus("Accounts not configured in this build");
        else SetStatus(Session == null ? "Not signed in yet" : "Signed in · " + Session.DisplayLabel());
    }

    /// <summary>
    /// Config lives in res://supabase_config.json (baked into the APK by the
    /// build machine; gitignored) with user://supabase_config.json as a dev
    /// override. The anon key is SAFE to ship: Row Level Security is what
    /// protects data, the anon key only identifies the project.
    /// </summary>
    public static SupabaseConfig LoadConfig()
    {
        foreach (var path in new[] { "user://supabase_config.json", "res://supabase_config.json" })
        {
            try
            {
                if (!Godot.FileAccess.FileExists(path)) continue;
                var json = Godot.FileAccess.GetFileAsString(path);
                var cfg = JsonSerializer.Deserialize<SupabaseConfig>(json);
                if (cfg != null && cfg.IsConfigured)
                {
                    GD.Print($"[SyncManager] Supabase config from {path} (url={cfg.Url})");
                    return cfg;
                }
                GD.PrintErr($"[SyncManager] {path} exists but url/anon_key are empty");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SyncManager] Could not read {path}: {ex.Message}");
            }
        }
        GD.Print("[SyncManager] No Supabase config — accounts and cloud save disabled.");
        return new SupabaseConfig();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Startup
    // ═════════════════════════════════════════════════════════════════════

    public async Task RunStartupSync()
    {
        if (_auth == null || _cloud == null || _save == null || !_config.IsConfigured) return;
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            if (!await EnsureSession().ConfigureAwait(false)) return;

            var fetch = await _cloud.Fetch(Session!).ConfigureAwait(false);
            if (!fetch.Ok)
            {
                RecordError(fetch.Error);
                SetStatus("Could not reach cloud save — " + fetch.Error);
                return;
            }

            var lastSynced = ParseIso(_meta.LastSyncedAt);
            bool localEmpty = IsLocalEmpty();
            var decision = CloudSaveSync.Decide(fetch.Save, lastSynced, _meta.Dirty, localEmpty);
            GD.Print($"[SyncManager] startup decision={decision} cloud={(fetch.Save == null ? "none" : fetch.Save.UpdatedAt.ToString("u"))} lastSynced={_meta.LastSyncedAt ?? "never"} dirty={_meta.Dirty} localEmpty={localEmpty}");

            switch (decision)
            {
                case CloudSaveSync.Decision.PushLocal:
                case CloudSaveSync.Decision.PushLocalNewer:
                    await PushNow().ConfigureAwait(false);
                    break;
                case CloudSaveSync.Decision.PullCloud:
                    ApplyCloud(fetch.Save!);
                    break;
                case CloudSaveSync.Decision.Conflict:
                    PendingConflict = new ConflictInfo
                    {
                        Cloud = fetch.Save!,
                        CloudSummary = Summarize(fetch.Save!.Bundle) + $" · {Ago(fetch.Save.UpdatedAt)}"
                                       + (string.IsNullOrEmpty(fetch.Save.DeviceId) || fetch.Save.DeviceId == _deviceId ? "" : " · another device"),
                        LocalSummary = Summarize(BuildLocalBundle()) + " · this device",
                    };
                    SetStatus("Two saves found — choose one");
                    Emit(SignalName.ConflictDetected);
                    break;
                case CloudSaveSync.Decision.NoOp:
                    SetStatus("Backed up · " + Session!.DisplayLabel());
                    break;
            }

            await SyncRelicsWithLedger().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SyncManager] startup sync failed: {ex}");
            RecordError(ex.Message);
            SetStatus("Sync failed — " + ex.GetType().Name);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Get a usable session: refresh, or sign in anonymously. False = offline/failed, status set.</summary>
    private async Task<bool> EnsureSession()
    {
        if (_auth == null) return false;

        if (Session != null && Session.IsValid && !Session.NeedsRefresh())
            return true;

        if (Session != null && !string.IsNullOrEmpty(Session.RefreshToken))
        {
            var r = await _auth.Refresh(Session).ConfigureAwait(false);
            if (r.Ok) { AdoptSession(r.Session!); return true; }
            if (r.Status == 0) { SetStatus(r.Error); return false; }   // FABLE-019: carries the reason

            // The server rejected the refresh token outright: the session is
            // dead. For a GUEST we can just make a new guest — but their save
            // is under the old user id, so keep the old session file where
            // the account panel can find it. For a LINKED account, never
            // silently replace it: say so and let them sign back in.
            GD.PrintErr($"[SyncManager] refresh rejected ({r.Status}): {r.Error}");
            if (IsLinked)
            {
                SetStatus("Signed out — sign in again with " + Session.Email);
                return false;
            }
            ArchiveSession();
            Session = null;
        }

        var a = await _auth.SignInAnonymously().ConfigureAwait(false);
        if (!a.Ok)
        {
            SetStatus(a.Error);   // FABLE-019: "No connection — <reason>" when status 0
            RecordError(a.Error);
            return false;
        }
        AdoptSession(a.Session!);
        _meta.LastSyncedAt = null;   // a new user has never synced
        WriteMeta();
        GD.Print($"[SyncManager] new anonymous user {a.Session!.UserId}");
        return true;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Saving → pushing
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Called by SaveManager after every successful local save.</summary>
    public void NotifySaved()
    {
        _lastSaveUtc = DateTime.UtcNow;
        if (!_meta.Dirty) { _meta.Dirty = true; WriteMeta(); }
        if (!_config.IsConfigured || PendingConflict != null) return;
        if (Interlocked.Exchange(ref _pushScheduled, 1) == 1) return;
        _ = DebouncedPush();
    }

    private async Task DebouncedPush()
    {
        try
        {
            // Wait for a quiet moment: saves come in bursts (end of duel writes
            // three or four times). Push once, after the burst.
            while ((DateTime.UtcNow - _lastSaveUtc).TotalSeconds < 6)
                await Task.Delay(1500).ConfigureAwait(false);
            if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
            try
            {
                if (!await EnsureSession().ConfigureAwait(false)) return;
                await PushNow().ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }
        catch (Exception ex) { GD.PrintErr($"[SyncManager] push failed: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _pushScheduled, 0); }
    }

    /// <summary>Upload the whole local bundle. Caller holds the gate and has a session.</summary>
    private async Task PushNow()
    {
        if (_cloud == null || Session == null) return;
        var bundle = BuildLocalBundle();
        var r = await _cloud.Push(Session, bundle, _deviceId, AppVersion()).ConfigureAwait(false);
        if (r.Ok)
        {
            _meta.Dirty = false;
            _meta.LastSyncedAt = (r.UpdatedAt ?? DateTimeOffset.UtcNow).ToString("o");
            _meta.LastError = null;
            WriteMeta();
            SetStatus("Backed up · " + Session.DisplayLabel());
            GD.Print($"[SyncManager] pushed save ({bundle.Slots.Count} slot(s)) at {_meta.LastSyncedAt}");
        }
        else
        {
            RecordError(r.Error);
            SetStatus("Backup pending — " + r.Error);
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Conflict
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The player chose. true = take the cloud save, false = keep this phone's and overwrite the cloud.</summary>
    public async Task ResolveConflict(bool useCloud)
    {
        var c = PendingConflict;
        if (c == null) return;
        PendingConflict = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (useCloud) ApplyCloud(c.Cloud);
            else if (await EnsureSession().ConfigureAwait(false)) await PushNow().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Account panel API
    // ═════════════════════════════════════════════════════════════════════

    public Task<SupabaseAuth.AuthResult> LinkEmail(string email)
        => Session == null || _auth == null
            ? Task.FromResult(SupabaseAuth.AuthResult.Fail("Not signed in"))
            : _auth.LinkEmail(Session, email);

    public async Task<SupabaseAuth.AuthResult> ConfirmLinkEmail(string email, string code)
    {
        if (Session == null || _auth == null) return SupabaseAuth.AuthResult.Fail("Not signed in");
        var r = await _auth.ConfirmLinkEmail(Session, email, code).ConfigureAwait(false);
        if (r.Ok)
        {
            AdoptSession(r.Session!);
            SetStatus("Linked · " + r.Session!.DisplayLabel());
            // Make sure the cloud has the save under the now-recoverable account.
            _ = Task.Run(async () =>
            {
                if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
                try { await PushNow().ConfigureAwait(false); } finally { _gate.Release(); }
            });
        }
        return r;
    }

    public Task<SupabaseAuth.AuthResult> SendSignInCode(string email)
        => _auth == null ? Task.FromResult(SupabaseAuth.AuthResult.Fail("Not configured")) : _auth.SendSignInCode(email);

    /// <summary>
    /// Sign in as an existing account on this phone. The current guest session
    /// is archived (not destroyed) and the cloud save of the signed-in account
    /// is adopted — that is what "sign in on another phone" means.
    /// </summary>
    public async Task<SupabaseAuth.AuthResult> ConfirmSignInCode(string email, string code)
    {
        if (_auth == null || _cloud == null) return SupabaseAuth.AuthResult.Fail("Not configured");
        var r = await _auth.ConfirmSignInCode(email, code).ConfigureAwait(false);
        if (!r.Ok) return r;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Session != null && Session.UserId != r.Session!.UserId) ArchiveSession();
            AdoptSession(r.Session!);
            PendingConflict = null;
            var fetch = await _cloud.Fetch(Session!).ConfigureAwait(false);
            if (fetch.Ok && fetch.Save != null) ApplyCloud(fetch.Save);
            else if (fetch.Ok)
            {
                // Signed into an account that has no cloud save yet: this
                // phone's progress becomes its save.
                _meta.LastSyncedAt = null; _meta.Dirty = true; WriteMeta();
                await PushNow().ConfigureAwait(false);
            }
            else SetStatus("Signed in, but could not fetch save — " + fetch.Error);
        }
        finally { _gate.Release(); }
        return r;
    }

    /// <summary>Forget this phone's session. The cloud save stays; a new guest is made next launch.</summary>
    public async Task SignOut()
    {
        if (_auth != null && Session != null) await _auth.SignOut(Session).ConfigureAwait(false);
        ArchiveSession();
        Session = null;
        _meta = new SyncMeta { Dirty = !IsLocalEmpty() };
        WriteMeta();
        SetStatus("Signed out");
    }

    /// <summary>Force a sync now (account panel "Sync" button).</summary>
    public Task SyncNow() => RunStartupSync();

    // ═════════════════════════════════════════════════════════════════════
    //  Relic ledger (unchanged behaviour, now under the user's JWT)
    // ═════════════════════════════════════════════════════════════════════

    private async Task SyncRelicsWithLedger()
    {
        if (_relics == null || _save == null || Session == null) return;
        try
        {
            _relics.AccessToken = Session.AccessToken;
            var prog = _save.State;
            var server = await _relics.FetchRelics(Session.UserId).ConfigureAwait(false);
            var localIds = new HashSet<string>(prog.DiscoveredRelics.Select(r => r.RelicInstanceId));
            int merged = 0;
            foreach (var r in server) if (localIds.Add(r.RelicInstanceId)) { prog.AddRelic(r); merged++; }
            var localOnly = prog.DiscoveredRelics.Where(r => server.All(s => s.RelicInstanceId != r.RelicInstanceId)).ToList();
            if (localOnly.Count > 0) await _relics.SyncRelics(Session.UserId, localOnly).ConfigureAwait(false);
            if (merged > 0) _save.Save();
            GD.Print($"[SyncManager] relics: {merged} merged from server, {localOnly.Count} pushed");
        }
        catch (Exception ex) { GD.PrintErr($"[SyncManager] relic sync failed: {ex.Message}"); }
    }

    /// <summary>Called by DuelScene after minting. Kept for compatibility.</summary>
    public async Task SyncOnRelicMint(LostRelicInstance relic)
    {
        if (_relics == null || Session == null) return;
        try
        {
            _relics.AccessToken = Session.AccessToken;
            await _relics.SyncRelics(Session.UserId, new List<LostRelicInstance> { relic }).ConfigureAwait(false);
        }
        catch (Exception ex) { GD.PrintErr($"[SyncManager] mint sync failed: {ex.Message}"); }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Local ⇄ bundle
    // ═════════════════════════════════════════════════════════════════════

    private static string SlotDbPath(int slot)
        => Path.Combine(ProjectSettings.GlobalizePath("user://"), $"runewake_save_slot{slot}.db");

    /// <summary>Everything on this phone, as one bundle. Active slot from memory, the others from their db files.</summary>
    private CloudSaveBundle BuildLocalBundle()
    {
        var b = new CloudSaveBundle
        {
            ProfilesJson = ReadTextOrNull(ProfilesPath),
            DecksJson = ReadTextOrNull(DecksPath),
        };
        int active = CampaignContext.ActiveProfileSlot;
        for (int slot = 0; slot < MaxSlots; slot++)
        {
            try
            {
                if (slot == active && _save != null)
                    b.Slots[slot.ToString()] = ProgressionSnapshot.FromState(_save.State);
                else if (File.Exists(SlotDbPath(slot)))
                    b.Slots[slot.ToString()] = ProgressionSnapshot.FromState(new SaveRepository(SlotDbPath(slot)).Load());
            }
            catch (Exception ex) { GD.PrintErr($"[SyncManager] could not read slot {slot}: {ex.Message}"); }
        }
        return b;
    }

    /// <summary>Overwrite this phone with the cloud save, then tell Main to reload.</summary>
    private void ApplyCloud(CloudSaveSync.CloudSave cloud)
    {
        var b = cloud.Bundle;
        try
        {
            if (b.ProfilesJson != null) WriteText(ProfilesPath, b.ProfilesJson);
            if (b.DecksJson != null) WriteText(DecksPath, b.DecksJson);

            int active = CampaignContext.ActiveProfileSlot;
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                if (!b.Slots.TryGetValue(slot.ToString(), out var snap)) continue;
                if (slot == active && _save != null)
                {
                    snap.ApplyTo(_save.State);
                    _save.Save();                     // NotifySaved fires — harmless, meta is cleaned below
                }
                else
                {
                    var st = new ProgressionState();
                    snap.ApplyTo(st);
                    new SaveRepository(SlotDbPath(slot)).Save(st);
                }
            }

            _meta.Dirty = false;
            _meta.LastSyncedAt = cloud.UpdatedAt.ToString("o");
            _meta.LastError = null;
            WriteMeta();
            SetStatus("Restored from cloud · " + (Session?.DisplayLabel() ?? ""));
            GD.Print($"[SyncManager] applied cloud save from {cloud.UpdatedAt:u} ({b.Slots.Count} slot(s))");
            Emit(SignalName.CloudSaveApplied);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[SyncManager] applying cloud save failed: {ex}");
            RecordError(ex.Message);
            SetStatus("Could not apply cloud save — " + ex.GetType().Name);
        }
    }

    private bool IsLocalEmpty()
    {
        try { return BuildLocalBundle().IsEmptyProgress; } catch { return false; }
    }

    private static string Summarize(CloudSaveBundle b)
    {
        if (b.Slots.Count == 0) return "empty";
        var best = b.Slots.Values.OrderByDescending(s => s.DelverLevel).ThenByDescending(s => s.ClearedNodes.Count).First();
        int relics = b.Slots.Values.Sum(s => s.DiscoveredRelics.Count);
        return $"Level {best.DelverLevel} · {best.ClearedNodes.Count} nodes · {best.Shards} shards · {relics} relic{(relics == 1 ? "" : "s")}";
    }

    private static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.UtcNow - t;
        if (d.TotalMinutes < 2) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")} ago";
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Files
    // ═════════════════════════════════════════════════════════════════════

    private void AdoptSession(SupabaseSession s)
    {
        Session = s;
        if (_relics != null) _relics.AccessToken = s.AccessToken;
        try { WriteText(SessionPath, JsonSerializer.Serialize(s)); }
        catch (Exception ex) { GD.PrintErr($"[SyncManager] could not save session: {ex.Message}"); }
    }

    private static SupabaseSession? ReadSession()
    {
        try
        {
            var json = ReadTextOrNull(SessionPath);
            if (json == null) return null;
            var s = JsonSerializer.Deserialize<SupabaseSession>(json);
            return s != null && s.IsValid ? s : null;
        }
        catch { return null; }
    }

    /// <summary>Never delete a session file outright: a guest's save is only findable through its user id.</summary>
    private void ArchiveSession()
    {
        try
        {
            var json = ReadTextOrNull(SessionPath);
            if (json != null) WriteText(PreviousSessionPath, json);
            if (Godot.FileAccess.FileExists(SessionPath))
                DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SessionPath));
        }
        catch (Exception ex) { GD.PrintErr($"[SyncManager] archive session: {ex.Message}"); }
    }

    private static SyncMeta ReadMeta()
    {
        try
        {
            var json = ReadTextOrNull(SyncMetaPath);
            return json == null ? new SyncMeta() : (JsonSerializer.Deserialize<SyncMeta>(json) ?? new SyncMeta());
        }
        catch { return new SyncMeta(); }
    }

    private void WriteMeta()
    {
        try { WriteText(SyncMetaPath, JsonSerializer.Serialize(_meta)); }
        catch (Exception ex) { GD.PrintErr($"[SyncManager] could not write sync meta: {ex.Message}"); }
    }

    private void RecordError(string e) { _meta.LastError = e; WriteMeta(); }

    private static string ReadOrCreateDeviceId()
    {
        try
        {
            var existing = ReadTextOrNull(DeviceIdPath)?.Trim();
            if (!string.IsNullOrEmpty(existing)) return existing!;
        }
        catch { /* fall through */ }
        var id = Guid.NewGuid().ToString();
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("user://data"));
            WriteText(DeviceIdPath, id);
        }
        catch { /* best effort */ }
        return id;
    }

    private static string? ReadTextOrNull(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return null;
        return Godot.FileAccess.GetFileAsString(path);
    }

    private static void WriteText(string path, string text)
    {
        using var f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (f == null) throw new IOException($"cannot open {path} for write: {Godot.FileAccess.GetOpenError()}");
        f.StoreString(text);
    }

    private static string AppVersion()
        => ProjectSettings.GetSetting("application/config/version").AsString();

    private static DateTimeOffset? ParseIso(string? s)
        => string.IsNullOrEmpty(s)
            ? null
            : (DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t : null);

    // ═════════════════════════════════════════════════════════════════════
    //  Main-thread plumbing
    // ═════════════════════════════════════════════════════════════════════

    private void SetStatus(string s)
    {
        Status = s;
        Emit(SignalName.StatusChanged, s);
    }

    /// <summary>Signals must be emitted on the main thread; we are usually on the pool.</summary>
    private void Emit(StringName signal, params Variant[] args)
    {
        Callable.From(() =>
        {
            if (IsInsideTree()) EmitSignal(signal, args);
        }).CallDeferred();
    }
}
