using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Coop;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Supabase;

namespace Runewake.Client;

/// <summary>
/// FABLE-038: the online match this phone is in — a PvP duel or a co-op expedition with
/// real people — and the wire between the engine session and Supabase.
///
/// The wire is a poll. Every <see cref="PollSeconds"/> the phone asks the server for moves it
/// has not seen (moves_since, cursor = the last row id) and posts any of its own that are
/// still queued (post_move, in sequence). Turn-based play is happy with that; it needs no
/// sockets, and it works through Godot's own HTTP on Android like every other call.
///
/// Threading, the FABLE-019d rule: nothing after an await touches a node. HTTP replies land
/// in a queue; <see cref="Pump"/>, called from the overlay's _Process on the main thread,
/// applies them to the engine session and the GameStateManager.
/// </summary>
public sealed class OnlineMatch
{
    public static OnlineMatch? Current { get; set; }

    public string ExpeditionId { get; }
    public string Kind { get; }                 // pvp1v1 | coop
    public int LocalSeat { get; }
    public string MyName { get; }
    public string OpponentName { get; }
    public PvpSession? Pvp { get; }
    public CoopSession? Coop { get; }
    public GameState? State => Pvp?.State ?? Coop?.Local.State;

    public double PollSeconds { get; set; } = 1.0;
    public string Status { get; private set; } = "Connecting…";
    public bool OpponentGone { get; private set; }
    public bool Reported { get; private set; }
    public DateTime LastContact { get; private set; } = DateTime.UtcNow;

    private readonly OnlinePlaySync _sync;
    private readonly SupabaseSession _session;
    private readonly ConcurrentQueue<string> _outbound = new();      // JSON payloads, in order
    private readonly ConcurrentQueue<string> _inbound = new();       // JSON payloads from the server
    private readonly ConcurrentQueue<string> _errors = new();
    private int _nextSeq;
    private long _cursor;
    private int _inFlight;
    private double _clock;
    private double _presenceClock;
    private int _opponentSecondsAgo;
    private volatile bool _opponentLeftFlag;
    private int _failStreak;             // FABLE-042: consecutive failed exchanges
    private volatile string _lastError = "";
    /// <summary>FABLE-042: the match can't go on (versions differ or the two phones disagree).</summary>
    public bool Broken => Pvp != null && Pvp.IsDesynced;
    public string BrokenReason => Pvp == null ? "" : Pvp.VersionMismatch
        ? $"Your game and {OpponentName}'s don't match — different versions or card data. Update both to the newest build, then play again."
        : $"Your game and {OpponentName}'s disagree about what happened, so the match can't continue. Leave and start a new one — and send Fable the log.";

    private OnlineMatch(string id, string kind, int seat, string me, string them, OnlinePlaySync sync, SupabaseSession session,
                        PvpSession? pvp, CoopSession? coop)
    {
        ExpeditionId = id; Kind = kind; LocalSeat = seat; MyName = me; OpponentName = them;
        _sync = sync; _session = session; Pvp = pvp; Coop = coop;
        if (pvp != null) pvp.Outbound += Enqueue;
        if (coop?.Network != null) coop.Network.Outbound += Enqueue;
    }

    // ── building a match from a started lobby ───────────────────────────────

    /// <summary>Both phones build the identical duel from the lobby's seed and seats.</summary>
    public static OnlineMatch? FromLobby(OnlinePlaySync.Lobby lobby, OnlinePlaySync sync, SupabaseSession session)
    {
        var mine = lobby.Mine;
        if (mine == null || lobby.Seed is not long seed) return null;
        var seats = lobby.Members.Where(m => m.Status != "left").OrderBy(m => m.Seat).Select(ToSeat).ToList();
        string them = string.Join(", ", lobby.Members.Where(m => !m.Me && m.Status != "left").Select(m => m.DisplayName));
        if (string.IsNullOrEmpty(them)) them = "your opponent";

        if (lobby.Kind == "pvp1v1")
        {
            if (seats.Count < 2) return null;
            var duel = new PvpDuel((ulong)seed, seats[0], seats[1]);
            var pvp = new PvpSession(duel, mine.Seat);
            var match = new OnlineMatch(lobby.Id, lobby.Kind, mine.Seat, mine.DisplayName, them, sync, session, pvp, null);
            pvp.Hello();   // FABLE-042: compare the freshly built duels before anyone moves
            return match;
        }

        // co-op: everyone fights their own copy of the target encounter; first to win wins for all
        if (!CampaignContext.EncounterIndex.TryGetValue(lobby.Target, out var enc)) return null;
        string bossClass = enc.Class ?? "";
        string[] bossArts = enc.Artifacts is { Count: > 0 } ? enc.Artifacts.ToArray()
            : ArtifactRegistry.OpponentLoadout(enc.Class, seats[0].ClassId, seats[0].Artifacts, enc.Id, (ulong)seed).Artifacts;
        if (string.IsNullOrEmpty(bossClass))
            bossClass = ArtifactRegistry.OpponentLoadout(enc.Class, seats[0].ClassId, seats[0].Artifacts, enc.Id, (ulong)seed).ClassId;
        var cfg = new ExpeditionConfig
        {
            Kind = ExpeditionKind.Coop, WinRule = WinRule.AnySeatWins, Seed = (ulong)seed,
            Encounter = enc, EncounterClass = bossClass, EncounterArtifacts = bossArts, Seats = seats,
        };
        var exp = new Expedition(cfg, CoopSession.EnemyPolicy);
        var net = new LockstepSession(exp, mine.Seat);
        var coop = new CoopSession(exp, mine.Seat) { Network = net, Title = enc.Name };
        CoopSession.Current = coop;
        return new OnlineMatch(lobby.Id, lobby.Kind, mine.Seat, mine.DisplayName, them, sync, session, null, coop);
    }

    private static SeatConfig ToSeat(OnlinePlaySync.Member m) => new()
    {
        Seat = m.Seat, DisplayName = m.DisplayName, ClassId = m.ClassId,
        Deck = new List<string>(m.Deck),
        Artifacts = ArtifactRegistry.DefaultLoadoutFor(m.ClassId),
    };

    // ── the local player ────────────────────────────────────────────────────

    /// <summary>A move from this phone (canonical PlayerIndex). Returns the new state, or null if refused.</summary>
    public GameState? SubmitLocal(GameAction action)
    {
        if (Pvp != null)
        {
            if (!Pvp.Local(action, out var err)) { GD.PrintErr($"[Online] move refused: {err}"); return null; }
            return Pvp.State;
        }
        return Coop?.SubmitLocal(action);
    }

    /// <summary>FABLE-COOP-1: send what is queued now (a co-op concede) instead of on the next poll.</summary>
    public void FlushOutbound() => FlushNow();

    public void ConcedeLocal()
    {
        Pvp?.LocalConcede();
        FlushNow();
    }

    /// <summary>
    /// Called when the duel scene is left: say goodbye, report the result if we know it.
    /// FABLE-COOP-1: in co-op, leaving mid-fight gives up this player's board, so the allies'
    /// round no longer waits for a phone that has gone. The goodbye is sent AFTER everything
    /// queued (the concede included) has reached the server — the per-frame pump stops with
    /// the scene, so a flush that lost the race with an exchange in flight used to drop it.
    /// </summary>
    public void LeaveMatch()
    {
        if (Pvp != null && !Pvp.State.IsGameOver) Pvp.LocalLeave();
        if (Coop != null && Coop.Expedition.Outcome == ExpeditionOutcome.Running) Coop.Concede();
        _ = FinalFlushThenLeave();
        if (Current == this) Current = null;
        if (Coop != null && CoopSession.Current == Coop) CoopSession.Current = null;
    }

    /// <summary>No nodes touched (FABLE-019d): waits out an exchange in flight, sends the rest, then leaves.</summary>
    private async Task FinalFlushThenLeave()
    {
        try
        {
            for (int i = 0; i < 3 && !_outbound.IsEmpty; i++)
            {
                int wait = 0;
                while (System.Threading.Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0 && wait++ < 50)
                    await Task.Delay(100).ConfigureAwait(false);
                if (wait > 50) break;
                await Exchange(false).ConfigureAwait(false);   // releases _inFlight when done
            }
        }
        catch (Exception ex) { GD.PrintErr($"[Online] final flush: {ex.Message}"); }
        await _sync.Leave(_session, ExpeditionId).ConfigureAwait(false);
    }

    public void ReportResult(bool iWon)
    {
        if (Reported) return;
        Reported = true;
        _ = _sync.Finish(_session, ExpeditionId, new { winner_seat = iWon ? LocalSeat : 1 - LocalSeat, reported_by = LocalSeat, moves = Pvp?.MovesApplied ?? 0 });
    }

    private void Enqueue(string json) => _outbound.Enqueue(json);
    private static string Short(string e) => string.IsNullOrEmpty(e) ? "no answer" : e.Length > 60 ? e.Substring(0, 60) + "…" : e;

    // ── the wire ────────────────────────────────────────────────────────────

    /// <summary>Main thread, every frame: send what is queued, poll on the clock, apply what arrived.</summary>
    public void Pump(double delta)
    {
        _clock += delta;
        _presenceClock += delta;
        bool myTurn = Pvp?.IsMyTurn ?? false;
        double period = myTurn ? Math.Max(PollSeconds, 2.0) : PollSeconds;
        if (_clock >= period || !_outbound.IsEmpty) { _clock = 0; FlushNow(); }

        while (_inbound.TryDequeue(out var json))
        {
            Pvp?.Receive(json);
            Coop?.Network?.Receive(json);
        }
        while (_errors.TryDequeue(out var e))
        {
            GD.PrintErr("[Online] " + e);
            // the exit trace is what Trikzos screenshots; one line per trouble, not one per second
            if (_failStreak <= 1 || e.Contains("refused") || e.Contains("threw")) DuelScene.ExitTrace("[Online] " + e);
        }
        if (_opponentLeftFlag && Pvp != null && !Pvp.OpponentLeft) Pvp.OpponentAbandoned();

        if (Pvp != null)
        {
            if (Pvp.OpponentLeft) Status = $"{OpponentName} left the duel";
            else if (Pvp.VersionMismatch) Status = "Games don't match — update both";
            else if (Pvp.IsDesynced) Status = "Out of step with the other phone";
            else if (_failStreak >= 3) Status = $"Can't reach the server — retrying ({_lastError})";
            else if (Pvp.State.IsGameOver) Status = "";
            else if (Pvp.IsMyTurn) Status = "Your turn";
            else Status = _opponentSecondsAgo > 30 ? $"{OpponentName} — no word for {_opponentSecondsAgo}s" : $"Waiting for {OpponentName}…";
        }
        else if (Coop != null)
        {
            Status = Coop.Expedition.HasConceded(LocalSeat) ? "You left the fight — your allies fight on"
                   : Coop.Local.EndedTurn ? "Waiting for your allies…" : "Your move";
        }
        OpponentGone = _opponentSecondsAgo > 90;
    }

    private void FlushNow()
    {
        if (System.Threading.Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
        bool presence = _presenceClock > 8;
        if (presence) _presenceClock = 0;
        _ = Exchange(presence);
    }

    private async Task Exchange(bool presence)
    {
        try
        {
            // 1. post everything queued, in order; stop at the first failure and retry next time
            while (_outbound.TryPeek(out var json))
            {
                var r = await _sync.PostMove(_session, ExpeditionId, _nextSeq, json).ConfigureAwait(false);
                if (!r.ok)
                {
                    if (r.error.Contains("sequence gap") || r.error.Contains("expedition is"))
                        _errors.Enqueue($"post_move refused: {r.error}");
                    else
                        _errors.Enqueue($"post_move failed (will retry): {r.error}");
                    _failStreak++; _lastError = Short(r.error);
                    break;
                }
                _outbound.TryDequeue(out _);
                _nextSeq++;
                LastContact = DateTime.UtcNow;
            }

            // 2. fetch what is new
            var m = await _sync.MovesSince(_session, ExpeditionId, _cursor).ConfigureAwait(false);
            if (m.ok)
            {
                LastContact = DateTime.UtcNow;
                if (_outbound.IsEmpty) _failStreak = 0;
                foreach (var row in m.rows)
                {
                    _cursor = Math.Max(_cursor, row.Id);
                    if (row.Seat == LocalSeat) continue;
                    _inbound.Enqueue(row.Payload);
                }
            }
            else { _errors.Enqueue($"moves_since failed: {m.error}"); _failStreak++; _lastError = Short(m.error); }

            // 3. now and then, who is still here
            if (presence)
            {
                var l = await _sync.GetLobby(_session, ExpeditionId).ConfigureAwait(false);
                if (l.ok && l.lobby != null)
                {
                    var other = l.lobby.Members.Where(x => !x.Me).ToList();
                    _opponentSecondsAgo = other.Count == 0 ? 0 : other.Min(x => x.SecondsAgo);
                    if (other.Count > 0 && other.All(x => x.Status == "left")) _opponentLeftFlag = true;
                }
            }
        }
        catch (Exception ex) { _errors.Enqueue($"exchange threw: {ex.GetType().Name}: {ex.Message}"); _failStreak++; _lastError = ex.GetType().Name; }
        finally { System.Threading.Interlocked.Exchange(ref _inFlight, 0); }
    }
}
