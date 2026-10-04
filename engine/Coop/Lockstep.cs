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
/// FABLE-020: keeps one phone's Expedition in step with everyone else's.
///
/// Every phone runs every board. A phone sends only its own player's moves;
/// it applies everyone's moves (its own immediately, others as they arrive) in
/// per-seat sequence order. Moves for a later round wait until this phone's
/// expedition gets there. After each round, the phone publishes its hash;
/// if another phone's hash for the same round differs, <see cref="Desynced"/>
/// is raised (the UI can then re-sync from the host's move log).
///
/// Transport-free: the client wires <see cref="Outbound"/> to the Realtime
/// socket and feeds received payloads to <see cref="Receive"/>.
/// </summary>
public sealed class LockstepSession
{
    private readonly Expedition _exp;
    private readonly int _me;
    private int _mySeq;
    private readonly Dictionary<int, int> _nextSeq = new();                  // seat -> next expected seq
    private readonly Dictionary<int, SortedDictionary<int, NetMessage>> _pending = new();
    private readonly Dictionary<int, ulong> _myHashes = new();                // round -> my hash
    private readonly Dictionary<(int round, int seat), ulong> _theirHashes = new();

    public event Action<string>? Outbound;
    /// <summary>(round, seat whose hash differs)</summary>
    public event Action<int, int>? Desynced;
    public event Action<int, GameAction>? MoveApplied;

    public Expedition Expedition => _exp;
    public int LocalSeat => _me;
    public bool IsDesynced { get; private set; }

    public LockstepSession(Expedition exp, int localSeat)
    {
        _exp = exp;
        _me = localSeat;
        foreach (var b in exp.Boards) { _nextSeq[b.Seat.Seat] = 0; _pending[b.Seat.Seat] = new(); }
    }

    /// <summary>The local player makes a move. Applied here at once and broadcast.</summary>
    public bool Local(GameAction action, out string error)
    {
        int round = _exp.Round;
        int before = round;
        if (!_exp.Submit(_me, round, action, out error)) return false;
        var msg = NetMessage.Move(_me, round, _mySeq++, action);
        _nextSeq[_me] = _mySeq;
        Outbound?.Invoke(msg.ToJson());
        MoveApplied?.Invoke(_me, action);
        AfterApply(before);
        Drain();
        return true;
    }

    /// <summary>
    /// FABLE-COOP-1: the local player gives up their board. Applied here at once and broadcast in
    /// this player's move sequence. The team fights on without them.
    /// </summary>
    public bool LocalConcede(out string error)
    {
        int round = _exp.Round;
        if (!_exp.Concede(_me, round, out error)) return false;
        var msg = new NetMessage { Type = "concede", Seat = _me, Round = round, Seq = _mySeq++ };
        _nextSeq[_me] = _mySeq;
        Outbound?.Invoke(msg.ToJson());
        AfterApply(round);
        Drain();
        return true;
    }

    /// <summary>A payload arrived from the channel.</summary>
    public void Receive(string json)
    {
        var m = NetMessage.FromJson(json);
        if (m == null || m.Seat == _me) return;
        switch (m.Type)
        {
            case "move":
            case "concede":
                if (!_pending.ContainsKey(m.Seat)) return;
                _pending[m.Seat][m.Seq] = m;
                Drain();
                break;
            case "hash":
                if (m.Hash != null && ulong.TryParse(m.Hash, out var h))
                {
                    _theirHashes[(m.Round, m.Seat)] = h;
                    Compare(m.Round, m.Seat);
                }
                break;
            case "leave":
                // Treat as End Turn for every remaining round: the board stays,
                // the round barrier no longer waits for them.
                var b = _exp.Boards.FirstOrDefault(x => x.Seat.Seat == m.Seat);
                if (b != null && b.Active && !b.EndedTurn && b.State.CurrentPlayerIndex == 0)
                    _exp.Submit(m.Seat, _exp.Round, new EndTurnAction { PlayerIndex = 0 }, out _);
                break;
        }
    }

    private void Drain()
    {
        bool progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (var (seat, queue) in _pending)
            {
                if (seat == _me) continue;
                while (queue.TryGetValue(_nextSeq[seat], out var m))
                {
                    if (m.Round > _exp.Round) break;            // their next round: wait for ours
                    queue.Remove(m.Seq);
                    _nextSeq[seat]++;
                    if (m.Type == "concede")
                    {
                        int r0 = _exp.Round;
                        _exp.Concede(seat, m.Round, out _);
                        AfterApply(r0);
                        progressed = true;
                        continue;
                    }
                    var a = m.ReadAction();
                    if (a == null) continue;
                    int before = _exp.Round;
                    if (m.Round == _exp.Round && _exp.Submit(seat, m.Round, a, out _))
                        MoveApplied?.Invoke(seat, a);
                    AfterApply(before);
                    progressed = true;
                }
            }
        }
    }

    private void AfterApply(int roundBefore)
    {
        if (_exp.Round == roundBefore && _exp.Outcome == ExpeditionOutcome.Running) return;
        // FABLE-COOP-1: a resolved round is compared by the hash taken as it resolved (see Expedition.Concede).
        // FABLE-DROP-1: decided mid-round → compare the deciding board(s) only (see Expedition.DecisiveHash)
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
