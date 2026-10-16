using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Coop;
using Runewake.Engine.Engine;
using Runewake.Engine.State;
using Runewake.Engine.Tower;
using Runewake.Engine.World;
using Runewake.Sim;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: the co-op expedition this phone is part of (raid or co-op fight).
///
/// It owns the engine Expedition and knows which seat is ours. Allies are
/// either AI Delvers on this phone (StartLocalRaid — playable today, and how
/// the tabs are exercised) or other players over Supabase Realtime (the
/// LockstepSession path: same Expedition, moves arrive from the channel).
/// DuelScene attaches a CoopOverlay when Current is set.
/// </summary>
public sealed class CoopSession
{
    public static CoopSession? Current { get; set; }

    public Expedition Expedition { get; }
    public int LocalSeat { get; }
    public string? RaidId { get; init; }
    public string Title { get; init; } = "";
    /// <summary>Seats played by AI on this phone.</summary>
    public HashSet<int> AiSeats { get; } = new();
    public LockstepSession? Network { get; init; }

    private static readonly GreedyBot Bot = new();

    public CoopSession(Expedition expedition, int localSeat)
    {
        Expedition = expedition;
        LocalSeat = localSeat;
    }

    /// <summary>
    /// FABLE-062: what this phone draws — the real expedition plus this phone's own moves still on their
    /// way to the server (so your plays show at once, and everyone else's show in the server's order).
    /// </summary>
    public Expedition View => Network?.Predicted ?? Expedition;

    /// <summary>Your board as drawn (shown as the enemy's turn once you have ended yours).</summary>
    public SeatBoard Local => View.Board(LocalSeat);
    public GameState LocalView => View.ViewState(LocalSeat);

    /// <summary>
    /// FABLE-062: bumps when the shared picture changed because of someone else — an ally's move (the one
    /// enemy is shared, so it changes your board too), the enemy's turn, or one of your moves being overtaken.
    /// </summary>
    public long ExternalVersion => Network?.ExternalVersion ?? _localExternal;
    private long _localExternal;

    /// <summary>The player's own move. Returns their board's new state (as drawn), or null if refused.</summary>
    public GameState? SubmitLocal(GameAction action)
    {
        bool ok = Network != null
            ? Network.Local(action, out var err)
            : Expedition.Submit(LocalSeat, Expedition.Round, action, out err);
        if (!ok) { GD.PrintErr($"[Coop] move refused: {err}"); return null; }
        return LocalView;
    }

    /// <summary>
    /// FABLE-062 (Trikzos: "name first — Trikzos or whatever it is — then class"): "Trikzos · Battlemage".
    /// A seat with no real name shows its class alone; an AI ally's name already carries its class.
    /// </summary>
    public static string SeatLabel(SeatConfig s)
    {
        string cls = Capital(s.ClassId);
        string name = (s.DisplayName ?? "").Trim();
        if (name.Length == 0 || name.Equals("Delver", StringComparison.OrdinalIgnoreCase) || name.Equals("You", StringComparison.OrdinalIgnoreCase))
            return cls.Length > 0 ? cls : "Delver";
        if (cls.Length == 0 || name.Contains(cls, StringComparison.OrdinalIgnoreCase)) return name;
        return $"{name} · {cls}";
    }

    /// <summary>
    /// FABLE-COOP-1: the player gives up their board. Allies fight on; over the network the concede
    /// goes out in this player's move sequence so every phone applies it in the same place.
    /// </summary>
    public bool Concede()
    {
        if (View.HasConceded(LocalSeat) || !Local.Active) return false;
        bool ok = Network != null
            ? Network.LocalConcede(out var err)
            : Expedition.Concede(LocalSeat, Expedition.Round, out err);
        if (!ok) GD.PrintErr($"[Coop] concede refused: {err}");
        return ok;
    }

    /// <summary>One AI move for one AI seat that still has to act this round. False = nothing to do.</summary>
    public bool StepAi()
    {
        foreach (var seat in AiSeats)
        {
            var b = Expedition.Board(seat);
            if (!b.Active || b.EndedTurn || b.State.CurrentPlayerIndex != 0) continue;
            var a = Bot.ChooseAction(b.State, 0) ?? new EndTurnAction { PlayerIndex = 0 };
            if (!Expedition.Submit(seat, Expedition.Round, a, out var err))
                Expedition.Submit(seat, Expedition.Round, new EndTurnAction { PlayerIndex = 0 }, out _);
            _localExternal++;      // an AI ally's move can change the shared enemy on your board too
            return true;
        }
        return false;
    }

    public static GameAction? EnemyPolicy(GameState s, int p) => Bot.ChooseAction(s, p);

    /// <summary>A Tower raid on this phone: you plus N AI allies against the floor boss's shared pool.</summary>
    public static void StartLocalRaid(TowerFloorDef floor, TowerPlaceDef boss, int allies)
    {
        var seats = new List<SeatConfig>
        {
            new()
            {
                Seat = 0, DisplayName = CampaignContext.SyncManager?.Username ?? "", ClassId = CampaignContext.ChosenClass ?? "",
                Deck = new List<string>(CampaignContext.PlayerDeckIds),
                Artifacts = ArtifactRegistry.PlayerLoadout(CampaignContext.ChosenClass ?? "", CampaignContext.Progression?.ActiveArtifacts),
            },
        };
        var starters = StarterDecks().Where(s => s.cls != CampaignContext.ChosenClass).ToList();
        var names = new[] { "Aldric", "Maren", "Tovi", "Sable" };
        for (int i = 0; i < allies && i < 4 && starters.Count > 0; i++)
        {
            var (cls, deck) = starters[i % starters.Count];
            seats.Add(new SeatConfig
            {
                Seat = i + 1, DisplayName = $"{names[i]} the {Capital(cls)}", ClassId = cls, Deck = deck,
                Artifacts = ArtifactRegistry.PlayerLoadout(cls, null),
            });
        }
        string bossClass = floor.Raid.BossClass ?? boss.Encounter!.Class ?? "";
        var cfg = new ExpeditionConfig
        {
            Kind = ExpeditionKind.Raid,
            WinRule = WinRule.SharedPool,
            Seed = (ulong)Random.Shared.NextInt64(),
            SharedPool = floor.Raid.PoolFor(seats.Count),
            MaxRounds = floor.Raid.MaxRounds,
            Encounter = boss.Encounter!,
            EncounterClass = bossClass,
            EncounterArtifacts = ArtifactRegistry.DefaultLoadoutFor(bossClass),
            Seats = seats,
        };
        var session = new CoopSession(new Expedition(cfg, EnemyPolicy), 0) { Title = boss.Name };
        foreach (var s in seats.Where(s => s.Seat != 0)) session.AiSeats.Add(s.Seat);
        Current = session;
        GD.Print($"[Coop] local raid: {boss.Name}, {seats.Count} Delver(s), pool {cfg.SharedPool}");
    }

    private static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static List<(string cls, List<string> deck)> StarterDecks()
    {
        var list = new List<(string, List<string>)>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://content/decks/starter_decks.json"));
            foreach (var s in doc.RootElement.GetProperty("starters").EnumerateArray())
                list.Add((s.GetProperty("class_id").GetString()!, s.GetProperty("cards").EnumerateArray().Select(x => x.GetString()!).ToList()));
        }
        catch (Exception ex) { GD.PrintErr($"[Coop] starter decks: {ex.Message}"); }
        return list;
    }
}
