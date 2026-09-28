using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.World;

namespace Runewake.Engine.Coop;

/// <summary>
/// FABLE-020: a 1v1 PvP duel between two people — the engine's native shape
/// (seat 0 = player 0, seat 1 = player 1), turn-based. Both phones build the
/// same starting state from the shared seed and apply the same moves in the
/// same order; only the player whose turn it is may move.
///
/// 2v2 (pick which enemy to attack; individual health; a team wins when one
/// of them wins) needs the engine to seat four players on one field. That is
/// the one rewrite multiplayer requires; it is NOT done here. Co-op and raids
/// deliberately avoid it by giving each player their own board (Expedition).
/// </summary>
public sealed class PvpDuel
{
    public GameState State { get; private set; }
    public List<GameAction> Log { get; } = new();

    public PvpDuel(ulong seed, SeatConfig seat0, SeatConfig seat1)
    {
        State = GameState.Initialize(new GameConfig
        {
            Seed = StableHash.Of(seed, "pvp"),
            Player0DeckIds = new List<string>(seat0.Deck),
            Player1DeckIds = new List<string>(seat1.Deck),
            Player0ArtifactIds = seat0.Artifacts,
            Player1ArtifactIds = seat1.Artifacts,
            Player0Class = seat0.ClassId,
            Player1Class = seat1.ClassId,
        });
        // FABLE-038: no mulligan screen online (the game turned it off in FABLE-026); both
        // phones must agree, so it is decided here rather than by a client setting.
        State.Players[0].HasMulliganed = true;
        State.Players[1].HasMulliganed = true;
    }

    public bool Submit(int seat, GameAction action, out string error)
    {
        error = "";
        if (State.IsGameOver) { error = "the duel is over"; return false; }
        if (seat is not (0 or 1)) { error = "seat must be 0 or 1"; return false; }
        if (State.CurrentPlayerIndex != seat) { error = "not your turn"; return false; }
        if (action.PlayerIndex != seat) { error = "you can only move your own side"; return false; }
        try { State = DuelEngine.Apply(State, action); }
        catch (Exception ex) { error = ex.Message; return false; }
        Log.Add(action);
        return true;
    }

    /// <summary>A seat gives up: the other seat wins. Both phones apply it the same way.</summary>
    public void Concede(int seat)
    {
        if (State.IsGameOver) return;
        State.IsGameOver = true;
        State.WinnerIndex = 1 - seat;
    }

    public ulong Hash() => StateHash.Of(State);
}

/// <summary>
/// FABLE-038: keeps one phone's PvpDuel in step with the other phone's.
///
/// Transport-free, like <see cref="LockstepSession"/> for expeditions: the local
/// player's moves are applied at once and handed to <see cref="Outbound"/> as
/// JSON; whatever the transport receives goes to <see cref="Receive"/>. Moves
/// carry a per-seat sequence number and are applied in order; a move for a seat
/// whose turn it is not yet (the other phone raced ahead of a message we have
/// not seen) waits in a queue. After every move each phone publishes its state
/// hash; a mismatch raises <see cref="Desynced"/>.
///
/// The transport (client OnlineMatch) polls the expedition's move log over
/// HTTPS; nothing here knows or cares.
/// </summary>
public sealed class PvpSession
{
    private readonly PvpDuel _duel;
    private readonly int _me;
    private int _mySeq;
    private readonly int[] _nextSeq = { 0, 0 };
    private readonly SortedDictionary<int, NetMessage>[] _pending = { new(), new() };
    private readonly Dictionary<int, ulong> _myHashes = new();          // after move count n
    private readonly Dictionary<(int count, int seat), ulong> _theirs = new();
    private int _applied;

    public event Action<string>? Outbound;
    /// <summary>The state changed because a REMOTE move (or concede / leave) was applied.</summary>
    public event Action? RemoteApplied;
    /// <summary>(move count, seat whose hash differs)</summary>
    public event Action<int, int>? Desynced;

    public PvpDuel Duel => _duel;
    public GameState State => _duel.State;
    public int LocalSeat => _me;
    public int Opponent => 1 - _me;
    public bool IsMyTurn => !State.IsGameOver && State.CurrentPlayerIndex == _me;
    public bool IsDesynced { get; private set; }
    /// <summary>FABLE-042: the two sides built different duels from the same lobby (different card
    /// data / versions). Known from the very first message, before anyone moves.</summary>
    public bool VersionMismatch { get; private set; }
    /// <summary>Set when the other seat sent "leave" (their app closed or they quit).</summary>
    public bool OpponentLeft { get; private set; }
    public int MovesApplied => _applied;

    public PvpSession(PvpDuel duel, int localSeat)
    {
        _duel = duel;
        _me = localSeat;
    }

    /// <summary>
    /// FABLE-042: say hello with the hash of the freshly built duel (move count 0). If the other
    /// side built something different — another version of the game, other card data — both
    /// know at once, instead of freezing mid-game when the first divergent rule fires.
    /// Older clients ignore it (they have no hash for count 0).
    /// </summary>
    public void Hello()
    {
        ulong h = _duel.Hash();
        _myHashes[0] = h;
        Send(new NetMessage { Type = "hash", Seat = _me, Round = 0, Hash = h.ToString() });
        Compare(0, Opponent);
    }

    /// <summary>The local player makes a move. Applied here at once and broadcast.</summary>
    public bool Local(GameAction action, out string error)
    {
        if (!_duel.Submit(_me, action, out error)) return false;
        Send(NetMessage.Move(_me, 0, _mySeq++, action));
        _nextSeq[_me] = _mySeq;
        AfterApply();
        Drain();
        return true;
    }

    /// <summary>The local player concedes. Broadcast, applied here at once.</summary>
    public void LocalConcede()
    {
        if (State.IsGameOver) return;
        _duel.Concede(_me);
        Send(new NetMessage { Type = "concede", Seat = _me, Seq = _mySeq++ });
        _nextSeq[_me] = _mySeq;
    }

    /// <summary>Tell the other phone we are gone (they win by default).</summary>
    public void LocalLeave()
    {
        Send(new NetMessage { Type = "leave", Seat = _me, Seq = _mySeq++ });
        _nextSeq[_me] = _mySeq;
    }

    /// <summary>The lobby says the other seat left (their app closed without a goodbye). They forfeit.</summary>
    public void OpponentAbandoned()
    {
        if (OpponentLeft) return;
        OpponentLeft = true;
        if (!_duel.State.IsGameOver) _duel.Concede(Opponent);
        RemoteApplied?.Invoke();
    }

    /// <summary>A payload arrived from the transport. Own messages echo back and are ignored.</summary>
    public void Receive(string json)
    {
        var m = NetMessage.FromJson(json);
        if (m == null || m.Seat == _me || m.Seat is not (0 or 1)) return;
        if (m.Type == "hash")
        {
            if (m.Hash != null && ulong.TryParse(m.Hash, out var h))
            {
                _theirs[(m.Round, m.Seat)] = h;
                Compare(m.Round, m.Seat);
            }
            return;
        }
        _pending[m.Seat][m.Seq] = m;
        Drain();
    }

    private void Drain()
    {
        int seat = Opponent;
        var queue = _pending[seat];
        while (queue.TryGetValue(_nextSeq[seat], out var m))
        {
            switch (m.Type)
            {
                case "move":
                    var a = m.ReadAction();
                    if (a == null) break;
                    if (_duel.State.CurrentPlayerIndex != seat && !_duel.State.IsGameOver)
                        return;                      // their move is ahead of ours: wait for our own turn to finish
                    if (!_duel.Submit(seat, a, out _))
                    {
                        // A move the engine refuses can only mean the two phones disagree on the state.
                        IsDesynced = true;
                        Desynced?.Invoke(_applied, seat);
                        break;
                    }
                    AfterApply();
                    RemoteApplied?.Invoke();
                    break;
                case "concede":
                    _duel.Concede(seat);
                    RemoteApplied?.Invoke();
                    break;
                case "leave":
                    OpponentLeft = true;
                    if (!_duel.State.IsGameOver) _duel.Concede(seat);
                    RemoteApplied?.Invoke();
                    break;
            }
            queue.Remove(m.Seq);
            _nextSeq[seat]++;
        }
    }

    private void AfterApply()
    {
        _applied++;
        ulong h = _duel.Hash();
        _myHashes[_applied] = h;
        Send(new NetMessage { Type = "hash", Seat = _me, Round = _applied, Hash = h.ToString() });
        Compare(_applied, Opponent);
    }

    private void Compare(int count, int seat)
    {
        if (_myHashes.TryGetValue(count, out var mine) && _theirs.TryGetValue((count, seat), out var theirs) && mine != theirs)
        {
            if (count == 0) VersionMismatch = true;
            IsDesynced = true;
            Desynced?.Invoke(count, seat);
        }
    }

    private void Send(NetMessage m) => Outbound?.Invoke(m.ToJson());
}
