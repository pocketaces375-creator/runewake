using System;
using System.Collections.Generic;
using System.Linq;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.World;

namespace Runewake.Engine.Coop;

// ═══════════════════════════════════════════════════════════════════════════
// FABLE-020 — co-op expeditions and Tower raids.
//
// Trikzos's design, confirmed:
//   * Up to 5 players. Each fights THEIR OWN copy of the enemy on their own
//     board (so the duel engine stays 1-v-1).
//   * Everyone acts at the same time. A round ends when every player still
//     standing has tapped End Turn; then every board's enemy takes its turn.
//     No "would you like to respond" — the game leans on traps, not reactions.
//   * Individual health. A player knocked out is out; the others fight on.
//   * The team shares the result.
//   * The tabs on the side of the screen show each ally's board — every phone
//     simulates every board from the same moves, so any tab is always live.
//
// Win rules:
//   SharedPool     raids / bosses: one health pool shared by every board. Damage
//                  dealt to the enemy on ANY board comes off the pool; at the end
//                  of each round every board's enemy is set to what is left.
//   AnySeatWins    "if one wins they all win": first board to beat its enemy
//                  wins for the team.
//   AllSeatsMustWin everyone still standing must beat their own copy.
// The team loses when every player is knocked out (or the round limit passes).
//
// Everything here is deterministic: same config + same moves = same result on
// every phone. That is the whole netcode — phones exchange moves, not state,
// and compare StateHash each round to catch a desync.
// ═══════════════════════════════════════════════════════════════════════════

public enum ExpeditionKind { Coop, Raid }
public enum WinRule { SharedPool, AnySeatWins, AllSeatsMustWin }
public enum ExpeditionOutcome { Running, Victory, Defeat }

public sealed class SeatConfig
{
    public int Seat { get; init; }
    public string DisplayName { get; init; } = "";
    public string ClassId { get; init; } = "";
    public List<string> Deck { get; init; } = new();
    public string[] Artifacts { get; init; } = Array.Empty<string>();
}

public sealed class ExpeditionConfig
{
    public ExpeditionKind Kind { get; init; } = ExpeditionKind.Coop;
    public WinRule WinRule { get; init; } = WinRule.AnySeatWins;
    /// <summary>The shared seed from start_expedition. Every board's seed derives from it.</summary>
    public ulong Seed { get; init; }
    public List<SeatConfig> Seats { get; init; } = new();
    /// <summary>The enemy every seat faces a copy of.</summary>
    public EncounterDef Encounter { get; init; } = new();
    public string[] EncounterArtifacts { get; init; } = Array.Empty<string>();
    public string EncounterClass { get; init; } = "";
    /// <summary>SharedPool only: the boss's total health across all boards.</summary>
    public int SharedPool { get; init; }
    /// <summary>Safety net so a stalemate cannot run forever.</summary>
    public int MaxRounds { get; init; } = 60;
    /// <summary>
    /// Mark every board as already mulliganed. A mulligan is not a move (it edits
    /// the hand directly), so in lockstep it would desync phones; expeditions
    /// start from the dealt hand.
    /// </summary>
    public bool SkipMulligan { get; init; } = true;
}

public sealed class SeatBoard
{
    public SeatConfig Seat { get; init; } = new();
    public GameState State { get; internal set; } = null!;
    /// <summary>This player's End Turn is in for the current round.</summary>
    public bool EndedTurn { get; internal set; }
    /// <summary>Knocked out: their Vigor hit 0.</summary>
    public bool IsOut => State.IsGameOver && State.WinnerIndex == 1;
    /// <summary>They beat their copy of the enemy.</summary>
    public bool Won => State.IsGameOver && State.WinnerIndex == 0;
    public bool Active => !State.IsGameOver;
    /// <summary>Moves applied this round, for replay/verification.</summary>
    public int ActionsThisRound { get; internal set; }
}

public sealed class Expedition
{
    private readonly ExpeditionConfig _cfg;
    private readonly Func<GameState, int, GameAction?> _enemy;
    private readonly List<SeatBoard> _boards = new();
    /// <summary>Each board's enemy Vigor when the round began — the pool is charged for everything below it.</summary>
    private readonly Dictionary<int, int> _enemyVigorAtRoundStart = new();

    public int Round { get; private set; } = 1;
    public int? PoolRemaining { get; private set; }
    public int PoolMax => _cfg.SharedPool;
    public ExpeditionOutcome Outcome { get; private set; } = ExpeditionOutcome.Running;
    public IReadOnlyList<SeatBoard> Boards => _boards;
    public ExpeditionConfig Config => _cfg;
    /// <summary>Every accepted move, in order: (round, seat, action). Enough to replay or verify.</summary>
    public List<(int round, int seat, GameAction action)> Log { get; } = new();

    /// <param name="enemyPolicy">The enemy AI. MUST be deterministic and identical on every phone (GreedyBot is).</param>
    public Expedition(ExpeditionConfig cfg, Func<GameState, int, GameAction?> enemyPolicy)
    {
        if (cfg.Seats.Count is < 1 or > 5) throw new ArgumentException("an expedition has 1–5 players");
        if (cfg.Seats.Select(s => s.Seat).Distinct().Count() != cfg.Seats.Count) throw new ArgumentException("duplicate seat");
        if (cfg.WinRule == WinRule.SharedPool && cfg.SharedPool <= 0) throw new ArgumentException("SharedPool needs a pool");
        _cfg = cfg;
        _enemy = enemyPolicy;

        foreach (var seat in cfg.Seats.OrderBy(s => s.Seat))
        {
            var state = GameState.Initialize(new GameConfig
            {
                Seed = StableHash.Of(cfg.Seed, "seat", seat.Seat),
                Player0DeckIds = new List<string>(seat.Deck),
                Player1DeckIds = new List<string>(cfg.Encounter.Deck),
                Player0ArtifactIds = seat.Artifacts,
                Player0Class = seat.ClassId,
                Player1ArtifactIds = cfg.EncounterArtifacts,
                Player1Class = cfg.EncounterClass,
                OpeningRule = cfg.Encounter.OpeningRule,
                Player1StartingVigor = cfg.WinRule == WinRule.SharedPool ? cfg.SharedPool : cfg.Encounter.EnemyVigor,
                Player1BonusAttunement = cfg.Encounter.EnemyBonusAttunement,
                BossRules = cfg.Encounter.BossRules,
            });
            if (cfg.SkipMulligan) { state.Players[0].HasMulliganed = true; state.Players[1].HasMulliganed = true; }
            _boards.Add(new SeatBoard { Seat = seat, State = state });
        }
        if (cfg.WinRule == WinRule.SharedPool) PoolRemaining = cfg.SharedPool;
        foreach (var b in _boards) _enemyVigorAtRoundStart[b.Seat.Seat] = b.State.Players[1].Vigor;
    }

    public SeatBoard Board(int seat) => _boards.First(b => b.Seat.Seat == seat);

    /// <summary>
    /// Apply one player's move on their own board. Returns false (with a reason)
    /// if the move is not allowed right now — the network layer must drop it.
    /// </summary>
    public bool Submit(int seat, int round, GameAction action, out string error)
    {
        error = "";
        if (Outcome != ExpeditionOutcome.Running) { error = "expedition is over"; return false; }
        if (round != Round) { error = $"move for round {round}, expedition is on round {Round}"; return false; }
        var board = _boards.FirstOrDefault(b => b.Seat.Seat == seat);
        if (board == null) { error = $"no seat {seat}"; return false; }
        if (!board.Active) { error = "that player is out of the fight"; return false; }
        if (board.EndedTurn) { error = "that player already ended their turn this round"; return false; }
        if (action.PlayerIndex != 0) { error = "players act as player 0 on their own board"; return false; }
        if (board.State.CurrentPlayerIndex != 0) { error = "not the player's turn on that board"; return false; }

        board.State = DuelEngine.Apply(board.State, action);
        board.ActionsThisRound++;
        Log.Add((Round, seat, action));

        if (action is EndTurnAction || board.State.IsGameOver)
            board.EndedTurn = true;

        // A board that just won or lost may settle the whole expedition.
        CheckOutcome();
        if (Outcome == ExpeditionOutcome.Running && _boards.Where(b => b.Active).All(b => b.EndedTurn))
            ResolveRound();
        return true;
    }

    /// <summary>Everyone still standing has ended their turn: every enemy acts, the pool settles.</summary>
    private void ResolveRound()
    {
        foreach (var b in _boards.Where(b => b.Active))
        {
            int guard = 0;
            while (!b.State.IsGameOver && b.State.CurrentPlayerIndex == 1 && guard++ < 400)
            {
                var a = _enemy(b.State, 1) ?? new EndTurnAction { PlayerIndex = 1 };
                b.State = DuelEngine.Apply(b.State, a);
            }
            if (!b.State.IsGameOver && b.State.CurrentPlayerIndex == 1)
                b.State = DuelEngine.Apply(b.State, new EndTurnAction { PlayerIndex = 1 });
        }

        if (_cfg.WinRule == WinRule.SharedPool && PoolRemaining is int pool)
        {
            // Damage (net of any healing) done to the enemy on every board this
            // round — the players' own turns AND anything during the enemy's
            // turn — comes off the one shared pool; then every board shows it.
            int dealt = 0;
            foreach (var b in _boards)
                dealt += Math.Max(0, _enemyVigorAtRoundStart[b.Seat.Seat] - Math.Max(0, b.State.Players[1].Vigor));
            pool = Math.Max(0, pool - dealt);
            PoolRemaining = pool;
            foreach (var b in _boards.Where(b => !b.State.IsGameOver || b.State.WinnerIndex == 0))
            {
                var s = b.State.Clone();
                s.Players[1].Vigor = pool;
                if (pool > 0 && s.IsGameOver && s.WinnerIndex == 0) { s.IsGameOver = false; s.WinnerIndex = null; }
                b.State = s;
            }
        }

        CheckOutcome();
        Round++;
        foreach (var b in _boards)
        {
            b.EndedTurn = !b.Active;
            b.ActionsThisRound = 0;
            _enemyVigorAtRoundStart[b.Seat.Seat] = b.State.Players[1].Vigor;
        }
        if (Outcome == ExpeditionOutcome.Running && Round > _cfg.MaxRounds) Outcome = ExpeditionOutcome.Defeat;
    }

    private void CheckOutcome()
    {
        if (Outcome != ExpeditionOutcome.Running) return;
        if (_boards.All(b => b.IsOut)) { Outcome = ExpeditionOutcome.Defeat; return; }
        switch (_cfg.WinRule)
        {
            case WinRule.SharedPool:
                if (PoolRemaining is int p && (p <= 0 || _boards.Any(b => b.Won))) { Outcome = ExpeditionOutcome.Victory; PoolRemaining = 0; }
                break;
            case WinRule.AnySeatWins:
                if (_boards.Any(b => b.Won)) Outcome = ExpeditionOutcome.Victory;
                break;
            case WinRule.AllSeatsMustWin:
                if (_boards.Where(b => !b.IsOut).All(b => b.Won) && _boards.Any(b => b.Won)) Outcome = ExpeditionOutcome.Victory;
                break;
        }
    }

    /// <summary>One number for the whole expedition. Phones compare it every round to catch a desync.</summary>
    public ulong Hash()
    {
        var parts = new List<object> { Round, PoolRemaining ?? -1, (int)Outcome };
        foreach (var b in _boards) parts.Add(StateHash.Of(b.State));
        return StableHash.Of(parts.ToArray());
    }
}
