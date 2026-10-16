using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Runewake.Engine.Engine;

namespace Runewake.Engine.Coop;

/// <summary>
/// FABLE-020: one message on the expedition's Realtime channel ("exp:&lt;id&gt;").
///   t="move"  a player's move: seat, round, per-seat sequence number, action
///   t="hash"  after a round resolves: that phone's Expedition.Hash()
///   t="leave" a player disconnected (older builds; their End Turn is taken for them once)
///   t="concede" FABLE-COOP-1: a player gave up their board — in their move sequence (seq), so every
///               phone applies it in the same place relative to that player's moves
/// </summary>
public sealed class NetMessage
{
    [JsonPropertyName("t")] public string Type { get; set; } = "move";
    [JsonPropertyName("seat")] public int Seat { get; set; }
    [JsonPropertyName("round")] public int Round { get; set; }
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("action")] public JsonElement? Action { get; set; }
    [JsonPropertyName("hash")] public string? Hash { get; set; }

    private static readonly JsonSerializerOptions ActionOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new GameActionConverter() },
    };

    public static NetMessage Move(int seat, int round, int seq, GameAction a) => new()
    {
        Type = "move", Seat = seat, Round = round, Seq = seq,
        Action = JsonSerializer.SerializeToElement(a, ActionOpts),
    };

    public GameAction? ReadAction() => Action is JsonElement e ? e.Deserialize<GameAction>(ActionOpts) : null;

    public string ToJson() => JsonSerializer.Serialize(this);
    public static NetMessage? FromJson(string json)
    {
        try { return JsonSerializer.Deserialize<NetMessage>(json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// FABLE-020 / FABLE-062: keeps one phone's Expedition in step with everyone else's.
///
/// Every phone runs every board, and — because all boards share ONE enemy — every phone must apply
/// every move in the SAME order. That order is the server's: post_move gives each move a row id and
/// moves_since hands them back in id order, the phone's own moves included. So:
///   • Local(move) does not touch the real expedition. It checks the move against the predicted
///     expedition (the real one + this phone's unconfirmed moves), queues it and sends it.
///   • Receive(payload) applies moves strictly in the order they arrive from the server. When one of
///     this phone's own moves comes back, it is applied for real in its true place; if an ally's move
///     that landed first made it impossible (they killed the creature you were attacking), it is
///     dropped and <see cref="LocalMoveRejected"/> fires.
///   • <see cref="Predicted"/> is what this phone draws: instant for your own moves, correct for theirs.
/// After each round the phone publishes its hash; a different hash for the same round raises
/// <see cref="Desynced"/>.
///
/// Transport-free: the client wires <see cref="Outbound"/> to post_move and feeds every row from
/// moves_since (in id order, its own included) to <see cref="Receive"/>.
/// </summary>
public sealed class LockstepSession
{
    private readonly Expedition _exp;
    private readonly int _me;
    private int _mySeq;
    private readonly Dictionary<int, int> _nextSeq = new();                  // seat -> next expected seq
    private readonly List<NetMessage> _unconfirmed = new();                  // my moves sent, not yet back from the server
    private readonly Dictionary<int, ulong> _myHashes = new();                // round -> my hash
    private readonly Dictionary<(int round, int seat), ulong> _theirHashes = new();
    private Expedition? _predicted;
    private long _predictedFor = -1;

    public event Action<string>? Outbound;
    /// <summary>(round, seat whose hash differs)</summary>
    public event Action<int, int>? Desynced;
    public event Action<int, GameAction>? MoveApplied;
    /// <summary>FABLE-062: one of this phone's moves could not be applied in its real place (an ally's move came first).</summary>
    public event Action<GameAction, string>? LocalMoveRejected;

    public Expedition Expedition => _exp;
    public int LocalSeat => _me;
    public bool IsDesynced { get; private set; }
    /// <summary>FABLE-062: bumps whenever the shared picture changes for a reason other than my own prediction.</summary>
    public long ExternalVersion { get; private set; }
    public int Unconfirmed => _unconfirmed.Count;

    public LockstepSession(Expedition exp, int localSeat)
    {
        _exp = exp;
        _me = localSeat;
        foreach (var b in exp.Boards) _nextSeq[b.Seat.Seat] = 0;
    }

    /// <summary>The expedition as this phone should draw it: the real one plus my moves still in flight.</summary>
    public Expedition Predicted
    {
        get
        {
            if (_unconfirmed.Count == 0) return _exp;
            long key = _exp.Version * 1000 + _unconfirmed.Count;
            if (_predicted != null && _predictedFor == key) return _predicted;
            var p = _exp.Clone();
            foreach (var m in _unconfirmed) Replay(p, m, out _);
            _predicted = p; _predictedFor = key;
            return p;
        }
    }

    private bool Replay(Expedition x, NetMessage m, out string error)
    {
        error = "";
        if (m.Type == "concede") return x.Concede(m.Seat, m.Round, out error);
        var a = m.ReadAction();
        if (a == null) { error = "unreadable move"; return false; }
        return x.Submit(m.Seat, m.Round, a, out error);
    }

    /// <summary>The local player makes a move: checked against the prediction, then queued and sent.</summary>
    public bool Local(GameAction action, out string error)
    {
        var pred = Predicted;
        int round = pred.Round;
        var trial = pred.Clone();
        if (!trial.Submit(_me, round, action, out error)) return false;
        var msg = NetMessage.Move(_me, round, _mySeq++, action);
        _unconfirmed.Add(msg);
        _predicted = trial; _predictedFor = _exp.Version * 1000 + _unconfirmed.Count;
        Outbound?.Invoke(msg.ToJson());
        return true;
    }

    /// <summary>FABLE-COOP-1: the local player gives up their board — in their move sequence, so every phone applies it in the same place.</summary>
    public bool LocalConcede(out string error)
    {
        var pred = Predicted;
        var trial = pred.Clone();
        if (!trial.Concede(_me, pred.Round, out error)) return false;
        var msg = new NetMessage { Type = "concede", Seat = _me, Round = pred.Round, Seq = _mySeq++ };
        _unconfirmed.Add(msg);
        _predicted = trial; _predictedFor = _exp.Version * 1000 + _unconfirmed.Count;
        Outbound?.Invoke(msg.ToJson());
        return true;
    }

    /// <summary>A row from the server, in the server's order (this phone's own rows included).</summary>
    public void Receive(string json)
    {
        var m = NetMessage.FromJson(json);
        if (m == null) return;
        switch (m.Type)
        {
            case "move":
            case "concede":
                if (!_nextSeq.ContainsKey(m.Seat)) return;
                if (m.Seq < _nextSeq[m.Seat]) return;                       // a repeat
                _nextSeq[m.Seat] = m.Seq + 1;
                int before = _exp.Round;
                if (m.Seat == _me)
                {
                    int at = _unconfirmed.FindIndex(u => u.Seq == m.Seq);
                    if (at >= 0) _unconfirmed.RemoveAt(at);
                    if (!Replay(_exp, m, out var why))
                    {
                        // an ally's move landed first and made mine impossible: the server order wins
                        _predicted = null;
                        ExternalVersion++;
                        var a = m.ReadAction();
                        if (a != null) LocalMoveRejected?.Invoke(a, why);
                    }
                    else if (m.Type == "move" && m.ReadAction() is GameAction mine) MoveApplied?.Invoke(_me, mine);
                }
                else
                {
                    var a = m.Type == "move" ? m.ReadAction() : null;
                    if (Replay(_exp, m, out _) && a != null) MoveApplied?.Invoke(m.Seat, a);
                    _predicted = null;
                    ExternalVersion++;
                }
                if (_exp.Round != before) { _predicted = null; ExternalVersion++; }
                AfterApply(before);
                break;
            case "hash":
                if (m.Seat == _me) return;
                if (m.Hash != null && ulong.TryParse(m.Hash, out var h))
                {
                    _theirHashes[(m.Round, m.Seat)] = h;
                    Compare(m.Round, m.Seat);
                }
                break;
            case "leave":
                if (m.Seat == _me) return;
                // Treat as End Turn: the board stays, the round no longer waits for them.
                var b = _exp.Boards.FirstOrDefault(x => x.Seat.Seat == m.Seat);
                if (b != null && b.Active && !b.EndedTurn && b.State.CurrentPlayerIndex == 0)
                {
                    int r0 = _exp.Round;
                    _exp.Submit(m.Seat, _exp.Round, new EndTurnAction { PlayerIndex = 0 }, out _);
                    _predicted = null; ExternalVersion++;
                    AfterApply(r0);
                }
                break;
        }
    }

    private void AfterApply(int roundBefore)
    {
        if (_exp.Round == roundBefore && _exp.Outcome == ExpeditionOutcome.Running) return;
        if (_myHashes.ContainsKey(roundBefore)) return;
        ulong h = _exp.Round != roundBefore ? _exp.LastResolvedHash : _exp.DecisiveHash();
        _myHashes[roundBefore] = h;
        Outbound?.Invoke(new NetMessage { Type = "hash", Seat = _me, Round = roundBefore, Hash = h.ToString() }.ToJson());
        foreach (var seat in _exp.Boards.Select(b => b.Seat.Seat)) Compare(roundBefore, seat);
    }

    private void Compare(int round, int seat)
    {
        if (seat == _me) return;
        if (_myHashes.TryGetValue(round, out var mine) && _theirHashes.TryGetValue((round, seat), out var theirs) && mine != theirs)
        {
            IsDesynced = true;
            Desynced?.Invoke(round, seat);
        }
    }
}
