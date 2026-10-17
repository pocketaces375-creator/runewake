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
// FABLE-062 (Trikzos: "We keep our own boards — but the enemy is only given one board and dialed up to
// fight x number of players"):
//   * ONE enemy for the whole team. Every player keeps their own board (own lanes, hand, deck, Vigor),
//     and every board faces the SAME enemy side — same creatures, same Vigor, same hand. A creature one
//     player kills is gone for everyone; a creature the enemy summons is there for everyone.
//   * The enemy is scaled to the team: each enemy creature's Attack and Vigor, and the enemy's own Vigor,
//     are multiplied by 1.75 / 2.5 / 3.15 / 3.6 for 2 / 3 / 4 / 5 players (ScaleFor). It takes its one
//     turn a round against ONE player — the focus rotates through everyone still standing. Tuned with
//     the bots so a team wins a little more often than one player alone, as asked.
//   * Every phone applies every move in the SAME order (the server's), so all phones show the same
//     enemy at all times; LockstepSession predicts your own moves so play stays instant.
//
// The rest of Trikzos's original design still holds:
//   * Up to 5 players (the duel engine stays 1-v-1: each board is you vs. the shared enemy).
//   * Everyone acts at the same time. A round ends when every player still
//     standing has tapped End Turn; then the enemy takes its turn (against the focus player).
//     No "would you like to respond" — the game leans on traps, not reactions.
//   * Individual health. A player knocked out is out; the others fight on.
//   * The team shares the result.
//   * The tabs on the side of the screen show each ally's board — every phone
//     simulates every board from the same moves, so any tab is always live.
//
// Win rules (FABLE-062): there is one enemy, so the team wins when it falls and loses when every player
// is knocked out (or the round limit passes). WinRule only sizes the enemy's Vigor now:
//   SharedPool     raids: the floor's pool (already sized for the team) is the enemy's Vigor.
//   AnySeatWins /
//   AllSeatsMustWin the encounter's Vigor × the team multiplier.
//
// Everything here is deterministic: same config + same moves = same result on
// every phone. That is the whole netcode — phones exchange moves, not state, apply them in the
// server's one order (FABLE-062: with a shared enemy, order across players matters), and compare
// StateHash each round to catch a desync.
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

    /// <summary>
    /// FABLE-054: a seat from what a lobby member posted. The posted deck list carries the deck's two artifact
    /// picks as extra "artf_" entries (no server change needed); they come off the deck here, and the seat
    /// brings them if they're a legal pair for the class — else the class's Forge defaults. Both phones and the
    /// PC bot build seats through this, so they always agree.
    /// </summary>
    public static SeatConfig FromPosted(int seat, string displayName, string classId, IEnumerable<string> posted)
    {
        var list = posted.ToList();
        var picks = list.Where(IsArtifactEntry).ToList();
        return new SeatConfig
        {
            Seat = seat, DisplayName = displayName, ClassId = classId,
            Deck = list.Where(id => !IsArtifactEntry(id)).ToList(),
            Artifacts = Runewake.Engine.Cards.ArtifactRegistry.PlayerLoadout(classId, picks),
        };
    }

    /// <summary>The list a phone posts: its deck, then its two artifact picks.</summary>
    public static List<string> ToPosted(IEnumerable<string> deck, IEnumerable<string>? artifacts) =>
        deck.Where(id => !IsArtifactEntry(id)).Concat(artifacts ?? Enumerable.Empty<string>()).ToList();

    public static bool IsArtifactEntry(string id) => id.StartsWith("artf_", StringComparison.Ordinal);
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
    /// <summary>FABLE-062: how much stronger the one enemy gets per extra player (creature stats and Vigor).</summary>
    public double EnemyScalePerExtraPlayer { get; init; } = 0.75;
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
    /// <summary>This player's End Turn is in for the current round (applied when the round resolves).</summary>
    public bool EndedTurn { get; internal set; }
    /// <summary>Knocked out: their Vigor hit 0.</summary>
    public bool IsOut => State.IsGameOver && State.WinnerIndex == 1;
    /// <summary>They beat the enemy (on their board).</summary>
    public bool Won => State.IsGameOver && State.WinnerIndex == 0;
    public bool Active => !State.IsGameOver;
    /// <summary>FABLE-COOP-1: they gave up this board (it counts as out).</summary>
    public bool Conceded { get; internal set; }
    /// <summary>Moves applied this round, for replay/verification.</summary>
    public int ActionsThisRound { get; internal set; }

    internal SeatBoard Copy() => new()
    {
        Seat = Seat, State = State.Clone(), EndedTurn = EndedTurn, Conceded = Conceded, ActionsThisRound = ActionsThisRound,
    };
}

public sealed class Expedition
{
    /// <summary>The one enemy's cards get instance ids from here (each board's own cards start at 1).</summary>
    public const int EnemyIdBase = 100_000;
    /// <summary>Anything created during the fight (tokens, copies) gets an id from here, unique across every board.</summary>
    public const int SharedIdBase = 200_000;

    private readonly ExpeditionConfig _cfg;
    private readonly Func<GameState, int, GameAction?> _enemyAi;
    private readonly List<SeatBoard> _boards = new();
    /// <summary>FABLE-062: THE enemy. Every board's Players[1] is a copy of this.</summary>
    private PlayerState _enemy = null!;
    /// <summary>The enemy's Prey mark points at one board's creature, so it is kept per board.</summary>
    private Dictionary<int, int?> _enemyPrey = new();
    /// <summary>Enemy cards already scaled to the team (by instance id).</summary>
    private HashSet<int> _scaled = new();
    private int _nextId = SharedIdBase;
    /// <summary>FABLE-COOP-1: seats that gave up after already ending this round's turn — they drop out as the round resolves.</summary>
    private SortedSet<int> _concedeAtRoundEnd = new();

    public int Round { get; private set; } = 1;
    public int? PoolRemaining { get; private set; }
    public int PoolMax { get; private set; }
    public ExpeditionOutcome Outcome { get; private set; } = ExpeditionOutcome.Running;
    public IReadOnlyList<SeatBoard> Boards => _boards;
    public ExpeditionConfig Config => _cfg;
    /// <summary>Every accepted move, in order: (round, seat, action). Enough to replay or verify.</summary>
    public List<(int round, int seat, GameAction action)> Log { get; private set; } = new();
    /// <summary>FABLE-062: the multiplier on the enemy for this many players.</summary>
    public double EnemyScale { get; }
    /// <summary>FABLE-062: the seat the enemy took its last turn against (-1 before the first round resolves).</summary>
    public int LastFocusSeat { get; private set; } = -1;
    /// <summary>Bumped on every change, so a screen knows when to redraw.</summary>
    public long Version { get; private set; }
    /// <summary>The shared enemy's Vigor right now.</summary>
    public int EnemyVigor => _enemy.Vigor;
    public int EnemyMaxVigor => _enemy.MaxVigor;

    /// <param name="enemyPolicy">The enemy AI. MUST be deterministic and identical on every phone (GreedyBot is).</param>
    public Expedition(ExpeditionConfig cfg, Func<GameState, int, GameAction?> enemyPolicy)
    {
        if (cfg.Seats.Count is < 1 or > 5) throw new ArgumentException("an expedition has 1–5 players");
        if (cfg.Seats.Select(s => s.Seat).Distinct().Count() != cfg.Seats.Count) throw new ArgumentException("duplicate seat");
        if (cfg.WinRule == WinRule.SharedPool && cfg.SharedPool <= 0) throw new ArgumentException("SharedPool needs a pool");
        _cfg = cfg;
        _enemyAi = enemyPolicy;
        EnemyScale = ScaleFor(cfg.Seats.Count, cfg.EnemyScalePerExtraPlayer);
        int enemyVigor = cfg.WinRule == WinRule.SharedPool
            ? cfg.SharedPool                                                  // a raid pool is already sized for the team
            : Math.Max(1, (int)Math.Round((cfg.Encounter.EnemyVigor ?? 25) * EnemyScale, MidpointRounding.AwayFromZero));

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
                Player1StartingVigor = enemyVigor,
                Player1BonusAttunement = cfg.Encounter.EnemyBonusAttunement,
                BossRules = cfg.Encounter.BossRules,
            });
            if (cfg.SkipMulligan) { state.Players[0].HasMulliganed = true; state.Players[1].HasMulliganed = true; }
            _boards.Add(new SeatBoard { Seat = seat, State = state });
            _enemyPrey[seat.Seat] = null;
        }

        // The enemy comes from the expedition's own seed (not any one seat's), under its own id range, scaled once.
        var enemySource = GameState.Initialize(new GameConfig
        {
            Seed = StableHash.Of(cfg.Seed, "enemy"),
            Player0DeckIds = new List<string>(cfg.Encounter.Deck),
            Player1DeckIds = new List<string>(cfg.Encounter.Deck),
            Player1ArtifactIds = cfg.EncounterArtifacts,
            Player1Class = cfg.EncounterClass,
            OpeningRule = cfg.Encounter.OpeningRule,
            Player1StartingVigor = enemyVigor,
            Player1BonusAttunement = cfg.Encounter.EnemyBonusAttunement,
            BossRules = cfg.Encounter.BossRules,
        });
        _enemy = Renumber(enemySource.Players[1], EnemyIdBase);
        _enemy.HasMulliganed = true;
        ScaleNewEnemyCards(_enemy);
        foreach (var b in _boards) Install(b);
        PoolMax = enemyVigor;
        if (cfg.WinRule == WinRule.SharedPool) PoolRemaining = _enemy.Vigor;
    }

    private Expedition(Expedition o)
    {
        _cfg = o._cfg; _enemyAi = o._enemyAi; EnemyScale = o.EnemyScale;
        _boards = o._boards.Select(b => b.Copy()).ToList();
        _enemy = o._enemy.Clone();
        _enemyPrey = new Dictionary<int, int?>(o._enemyPrey);
        _scaled = new HashSet<int>(o._scaled);
        _nextId = o._nextId;
        _concedeAtRoundEnd = new SortedSet<int>(o._concedeAtRoundEnd);
        Round = o.Round; PoolRemaining = o.PoolRemaining; PoolMax = o.PoolMax; Outcome = o.Outcome;
        Log = new List<(int, int, GameAction)>(o.Log);
        LastFocusSeat = o.LastFocusSeat; Version = o.Version; LastResolvedHash = o.LastResolvedHash;
    }

    /// <summary>FABLE-062: a full copy — LockstepSession plays the local player's unconfirmed moves on one.</summary>
    public Expedition Clone() => new(this);

    public SeatBoard Board(int seat) => _boards.First(b => b.Seat.Seat == seat);

    /// <summary>
    /// What a screen should draw for a seat: its board — shown as the enemy's turn once that player has
    /// ended theirs (their End Turn is only applied when the round resolves).
    /// </summary>
    public GameState ViewState(int seat)
    {
        var b = Board(seat);
        if (!b.EndedTurn || !b.Active || b.State.CurrentPlayerIndex != 0) return b.State;
        var v = b.State.Clone();
        v.CurrentPlayerIndex = 1;
        return v;
    }

    /// <summary>
    /// Apply one player's move on their own board. Returns false (with a reason)
    /// if the move is not allowed right now — the network layer must drop it.
    /// </summary>
    public bool Submit(int seat, int round, GameAction action, out string error)
    {
        error = "";
        // FABLE-DROP-1: a move made in the round the expedition was decided is still applied (to its own
        // board — nothing resolves any more), so every phone ends with the same boards.
        if (Outcome != ExpeditionOutcome.Running && round != Round) { error = "expedition is over"; return false; }
        if (round != Round) { error = $"move for round {round}, expedition is on round {Round}"; return false; }
        var board = _boards.FirstOrDefault(b => b.Seat.Seat == seat);
        if (board == null) { error = $"no seat {seat}"; return false; }
        if (!board.Active) { error = "that player is out of the fight"; return false; }
        if (board.EndedTurn) { error = "that player already ended their turn this round"; return false; }
        if (action.PlayerIndex != 0) { error = "players act as player 0 on their own board"; return false; }
        if (board.State.CurrentPlayerIndex != 0) { error = "not the player's turn on that board"; return false; }

        if (action is EndTurnAction)
        {
            // FABLE-062: held until the round resolves — the enemy's turn starts for everyone at once.
            board.EndedTurn = true;
        }
        else
        {
            try { Apply(board, action); }
            catch (Exception ex) { error = "the engine refused that move: " + ex.Message; return false; }
            if (board.State.IsGameOver) board.EndedTurn = true;
        }
        board.ActionsThisRound++;
        Log.Add((Round, seat, action));
        Version++;

        CheckOutcome();
        if (Outcome == ExpeditionOutcome.Running && _boards.Where(b => b.Active).All(b => b.EndedTurn))
            ResolveRound();
        return true;
    }

    /// <summary>
    /// FABLE-COOP-1: a player gives up their board. Their board is out (a loss for them, exactly
    /// like being knocked out); the team fights on, and loses only if nobody is left standing.
    /// A concede made after ending this round's turn takes effect as the round resolves, after the
    /// enemy acts — so every phone lands in the same place whatever it heard first.
    /// </summary>
    public bool Concede(int seat, int round, out string error)
    {
        error = "";
        var board = _boards.FirstOrDefault(b => b.Seat.Seat == seat);
        if (board == null) { error = $"no seat {seat}"; return false; }
        if (round > Round) { error = $"concede for round {round}, expedition is on round {Round}"; return false; }
        if (!board.Active) { error = "that player is already out of the fight"; return false; }
        Version++;
        if (round == Round && board.EndedTurn && Outcome == ExpeditionOutcome.Running)
        {
            _concedeAtRoundEnd.Add(seat);
            return true;
        }
        DropOut(board);
        CheckOutcome();
        if (Outcome == ExpeditionOutcome.Running && _boards.Any(b => b.Active) && _boards.Where(b => b.Active).All(b => b.EndedTurn))
            ResolveRound();
        return true;
    }

    /// <summary>FABLE-COOP-1: true once this seat has given up (applied, or waiting for the round to resolve).</summary>
    public bool HasConceded(int seat) => _concedeAtRoundEnd.Contains(seat) || _boards.Any(b => b.Seat.Seat == seat && b.Conceded);

    private static void DropOut(SeatBoard board)
    {
        var s = board.State.Clone();
        s.IsGameOver = true;
        s.WinnerIndex = 1;
        board.State = s;
        board.EndedTurn = true;
        board.Conceded = true;
    }

    // ── the one enemy ────────────────────────────────────────────────────

    /// <summary>Give a board the current enemy (its own Prey mark), on the shared id counter.</summary>
    private void Install(SeatBoard b)
    {
        var s = b.State.Clone();
        var e = _enemy.Clone();
        e.PreyTargetId = _enemyPrey.TryGetValue(b.Seat.Seat, out var prey) ? prey : null;
        s.Players[1] = e;
        s.NextInstanceId = Math.Max(s.NextInstanceId, _nextId);
        Auras.Recompute(s);
        b.State = s;
    }

    /// <summary>One engine action on one board; whatever it did to the enemy becomes THE enemy, on every board.</summary>
    private void Apply(SeatBoard b, GameAction action)
    {
        Install(b);
        b.State = Chain(b.State, action);
        Capture(b);
    }

    /// <summary>
    /// FABLE-064: one engine action whose story (its combat events) is added after the board's earlier ones,
    /// so a phone that only sees the board once the round resolves still gets every event of the round.
    /// </summary>
    private static GameState Chain(GameState before, GameAction action)
    {
        var prior = before.Events.ToList();
        var after = DuelEngine.Apply(before, action);
        if (prior.Count > 0)
        {
            after.Events.InsertRange(0, prior);
            if (after.Events.Count > Fx.Keep) after.Events.RemoveRange(0, after.Events.Count - Fx.Keep);
        }
        return after;
    }

    private void Capture(SeatBoard b)
    {
        _nextId = Math.Max(_nextId, b.State.NextInstanceId);
        var e = b.State.Players[1];
        _enemyPrey[b.Seat.Seat] = e.PreyTargetId;
        ScaleNewEnemyCards(e);
        _enemy = e.Clone();
        _enemy.PreyTargetId = null;
        foreach (var other in _boards) if (other != b) Install(other);
        if (_cfg.WinRule == WinRule.SharedPool) PoolRemaining = Math.Max(0, _enemy.Vigor);
    }

    /// <summary>FABLE-062: the team-sized enemy — every enemy creature card is scaled once, when first seen.</summary>
    private void ScaleNewEnemyCards(PlayerState e)
    {
        IEnumerable<CardInstance> All() => e.Deck.Concat(e.Hand).Concat(e.Discard).Concat(e.Barrow)
            .Concat(e.Lanes.Where(l => l.Occupant != null).Select(l => l.Occupant!)).Concat(e.UnearthQueue);
        foreach (var c in All())
        {
            if (!_scaled.Add(c.InstanceId)) continue;
            if (EnemyScale <= 1.0 || c.StolenFrom >= 0) continue;                 // a stolen player creature stays as it was
            if (c.BaseAttack > 0) c.BaseAttack = Scale(c.BaseAttack);
            if (c.BaseVigor > 0) c.BaseVigor = Scale(c.BaseVigor);
        }
    }

    /// <summary>
    /// FABLE-062: the team multiplier — 1 + k·(players − 1), tapering past three players (a big team's
    /// extra bodies are worth a little less each). With k = 0.75: 1 · 1.75 · 2.5 · 3.15 · 3.6. Tuned with
    /// the bots so a team wins a little more often than one player alone.
    /// </summary>
    public static double ScaleFor(int players, double perExtra)
    {
        int n = Math.Max(1, players);
        return 1.0 + Math.Max(0, perExtra) * (n - 1) - 0.1 * Math.Pow(Math.Max(0, n - 3), 2);
    }

    private int Scale(int v) => Math.Max(v, (int)Math.Round(v * EnemyScale, MidpointRounding.AwayFromZero));

    /// <summary>A copy of a player's cards under fresh instance ids starting at <paramref name="baseId"/>.</summary>
    private static PlayerState Renumber(PlayerState p, int baseId)
    {
        var e = p.Clone();
        int next = baseId;
        void Remap(List<CardInstance> list) { for (int i = 0; i < list.Count; i++) list[i] = list[i].CloneWithId(next++); }
        Remap(e.Deck); Remap(e.Hand); Remap(e.Discard); Remap(e.Barrow); Remap(e.UnearthQueue); Remap(e.RuneTokens);
        foreach (var lane in e.Lanes) if (lane.Occupant != null) lane.Occupant = lane.Occupant.CloneWithId(next++);
        foreach (var slot in e.ArtifactSlots) if (slot.Occupant != null) slot.Occupant = slot.Occupant.CloneWithId(next++);
        e.PreyTargetId = null;
        return e;
    }

    /// <summary>
    /// Everyone still standing has ended their turn: their End Turns go in, the enemy takes its ONE turn
    /// against the focus player, and every board ends up facing the same enemy again.
    /// </summary>
    private void ResolveRound()
    {
        var active = _boards.Where(b => b.Active).OrderBy(b => b.Seat.Seat).ToList();
        if (active.Count > 0)
        {
            var focus = active[(Round - 1) % active.Count];
            LastFocusSeat = focus.Seat.Seat;

            // 1. the focus board: the player's turn ends, the enemy plays its whole turn here
            Apply(focus, new EndTurnAction { PlayerIndex = 0 });
            int guard = 0;
            while (!focus.State.IsGameOver && focus.State.CurrentPlayerIndex == 1 && guard++ < 400)
            {
                var a = _enemyAi(focus.State, 1) ?? new EndTurnAction { PlayerIndex = 1 };
                try { Apply(focus, a); }
                catch (Exception) { Apply(focus, new EndTurnAction { PlayerIndex = 1 }); }
            }
            if (!focus.State.IsGameOver && focus.State.CurrentPlayerIndex == 1)
                Apply(focus, new EndTurnAction { PlayerIndex = 1 });

            // 2. every other board: its turn ends and its next turn begins; the enemy did its acting above
            var after = _enemy.Clone();
            foreach (var b in active.Where(b => b != focus && b.Active))
            {
                Install(b);
                var s = Chain(b.State, new EndTurnAction { PlayerIndex = 0 });
                if (!s.IsGameOver && s.CurrentPlayerIndex == 1) s = Chain(s, new EndTurnAction { PlayerIndex = 1 });
                _nextId = Math.Max(_nextId, s.NextInstanceId);
                b.State = s;
            }
            _enemy = after;                    // the enemy's own side is the focus board's; nothing else touched it
            foreach (var b in _boards) Install(b);
            if (_cfg.WinRule == WinRule.SharedPool) PoolRemaining = Math.Max(0, _enemy.Vigor);
        }

        // FABLE-COOP-1: the round's hash is taken HERE — after the enemy acted, before anyone who gave up
        // mid-round drops out — so a phone that hears of such a concede late still agrees.
        LastResolvedHash = Hash();

        foreach (var seat in _concedeAtRoundEnd)
        {
            var b = _boards.First(x => x.Seat.Seat == seat);
            if (b.Active) DropOut(b);
        }
        _concedeAtRoundEnd.Clear();

        CheckOutcome();
        Round++;
        foreach (var b in _boards)
        {
            b.EndedTurn = !b.Active;
            b.ActionsThisRound = 0;
        }
        Version++;
        if (Outcome == ExpeditionOutcome.Running && Round > _cfg.MaxRounds) Outcome = ExpeditionOutcome.Defeat;
    }

    private void CheckOutcome()
    {
        if (Outcome != ExpeditionOutcome.Running) return;
        // FABLE-062: one enemy — when it falls, the team wins, whichever board landed the blow
        if (_enemy.Vigor <= 0 || _boards.Any(b => b.Won))
        {
            Outcome = ExpeditionOutcome.Victory;
            if (PoolRemaining != null) PoolRemaining = 0;
            return;
        }
        if (_boards.All(b => b.IsOut)) Outcome = ExpeditionOutcome.Defeat;
    }

    /// <summary>FABLE-COOP-1: <see cref="Hash"/> as the last round resolved (see ResolveRound). Lockstep compares this per round.</summary>
    public ulong LastResolvedHash { get; private set; }

    /// <summary>
    /// FABLE-DROP-1: the hash phones compare when the fight is decided in the middle of a round. With one
    /// enemy and one order of moves every board is the same on every phone, so it is the whole hash.
    /// </summary>
    public ulong DecisiveHash() => Hash();

    /// <summary>One number for the whole expedition. Phones compare it every round to catch a desync.</summary>
    public ulong Hash()
    {
        var parts = new List<object> { Round, PoolRemaining ?? -1, (int)Outcome, _enemy.Vigor };
        foreach (var b in _boards) { parts.Add(StateHash.Of(b.State)); parts.Add(b.EndedTurn); }
        return StableHash.Of(parts.ToArray());
    }
}
