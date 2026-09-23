using System;
using System.Collections.Generic;
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
    }

    public bool Submit(int seat, GameAction action, out string error)
    {
        error = "";
        if (State.IsGameOver) { error = "the duel is over"; return false; }
        if (seat is not (0 or 1)) { error = "seat must be 0 or 1"; return false; }
        if (State.CurrentPlayerIndex != seat) { error = "not your turn"; return false; }
        if (action.PlayerIndex != seat) { error = "you can only move your own side"; return false; }
        State = DuelEngine.Apply(State, action);
        Log.Add(action);
        return true;
    }

    public ulong Hash() => StateHash.Of(State);
}
