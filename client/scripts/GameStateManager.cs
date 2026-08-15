using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.Engine;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Manages the lifecycle of the engine's GameState in the client.
/// Holds the current state, dispatches actions via Engine.Apply,
/// and provides structured data for the UI to render from.
/// </summary>
public partial class GameStateManager : Node
{
    private GameState _state = default!;

    /// <summary>The current game state. Read-only from outside.</summary>
    public GameState State => _state;

    /// <summary>True once Initialize() has been called and the game is running.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>True if the game has ended.</summary>
    public bool IsGameOver => _state?.IsGameOver ?? false;

    /// <summary>Index of the winning player, or -1 if game not over.</summary>
    public int WinnerIndex => _state?.WinnerIndex ?? -1;

    // ——— Signals for UI updates ———

    /// <summary>Raised after every state mutation. UI should re-render from State.</summary>
    public event Action? StateChanged;

    /// <summary>Raised when the game ends, with the winner's index.</summary>
    public event Action<int>? GameOver;

    public override void _ExitTree()
    {
        // Clear event delegates so no freed subscriber is ever invoked during
        // shutdown (signal 11 protection). Godot frees children in tree order,
        // but a timer or tween callback may fire during cleanup.
        StateChanged = null;
        GameOver = null;
    }

    // ——— Initialization ———

    /// <summary>
    /// Initialize a new game from a GameConfig.
    /// Card packs must have been loaded into CardRegistry beforehand.
    /// </summary>
    public void Initialize(GameConfig config)
    {
        _state = GameState.Initialize(config);
        IsInitialized = true;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Initialize a test game using example decks built from the content packs.
    /// </summary>
    public void InitializeTestGame(ulong seed = 42)
    {
        // Build two 30-card test decks from available cards
        var allCards = new List<string>
        {
            "vrd_c_root_warden",
            "vrd_c_verdant_sproutling",
            "vrd_c_thornbark_defender",
            "vrd_c_wildwood_stalker",
            "vrd_u_grove_healer",
            "vrd_u_canopy_archer",
            "vrd_u_elder_treant",
            "vrd_u_saphoof_charger",
            "vrd_r_bloomweaver",
            "vrd_r_undergrowth_eruption",
            "vrd_r_natures_renewal",
            "vrd_x_heartwood_relic",
            "emb_c_cinder_runner",
            "emb_c_ember_hound",
            "emb_c_flame_javelin",
            "emb_c_forgeguard_berserker",
            "emb_u_wildfire_adept",
            "emb_u_lava_serpent",
            "emb_u_searing_blast",
            "emb_u_cinderstorm_elemental",
            "emb_r_magma_forger",
            "emb_r_inferno_burst",
            "emb_r_phoenix_ash",
            "emb_x_the_last_ember",
            "tid_c_silt_reader",
            "tid_c_tidal_scholar",
            "tid_c_deep_one",
            "tid_c_abyssal_gaze",
            "tid_u_brine_witch",
            "tid_u_coral_guardian",
            "hol_c_gravewrit_thrall",
            "dwn_r_sealing_light",
            "dwn_c_dawn_warder",
            "hol_c_skeletal_reaver"
        };

        var config = new GameConfig
        {
            Seed = seed,
            ContentVersion = 1,
            Player0DeckIds = allCards.ToList(),
            Player1DeckIds = allCards.ToList()
        };

        Initialize(config);
    }

    // ——— Action dispatch ———

    /// <summary>
    /// Play a card from the player's hand to a lane.
    /// Returns ActionResult with success flag and error reason on failure.
    /// </summary>
    public ActionResult TryPlayCard(int playerIndex, string cardDefId, int laneIndex)
    {
        if (_state.IsGameOver)
            return Error("Game is already over.");

        if (_state.CurrentPlayerIndex != playerIndex)
            return Error("It's not your turn.");

        var player = _state.Players[playerIndex];
        var card = player.Hand.FirstOrDefault(c => c.CardDefId == cardDefId);
        if (card == null)
            return Error("Card not found in hand.");

        var def = CardRegistry.Get(cardDefId);
        if (def == null)
            return Error($"Card definition not found: {cardDefId}.");

        if (player.Attunement < card.Cost)
            return Error($"Not enough attunement: have {player.Attunement}, need {card.Cost}.");

        if (laneIndex < 0 || laneIndex > 4)
            return Error($"Invalid lane index: {laneIndex}.");

        var lane = player.Lanes[laneIndex];
        if (lane.Occupant is not null)
            return Error($"Lane {laneIndex + 1} is already occupied.");

        var action = new PlayCardAction
        {
            PlayerIndex = playerIndex,
            CardInstanceId = card.InstanceId,
            Cost = card.Cost,
            LaneIndex = laneIndex
        };

        try
        {
            _state = DuelEngine.Apply(_state, action);
            StateChanged?.Invoke();
            CheckGameOver();
            return Success();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameStateManager] PlayCard failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return Error(ex.Message);
        }
    }

    /// <summary>
    /// Attack with a creature from sourceLaneIndex, targeting the given lane.
    /// </summary>
    public ActionResult TryAttack(int playerIndex, int sourceLaneIndex, int targetLaneIndex)
    {
        if (_state.IsGameOver)
            return Error("Game is already over.");

        if (_state.CurrentPlayerIndex != playerIndex)
            return Error("It's not your turn.");

        var player = _state.Players[playerIndex];
        var sourceLane = player.Lanes[sourceLaneIndex];
        var attacker = sourceLane.Occupant;
        if (attacker == null)
            return Error($"No creature in lane {sourceLaneIndex + 1} to attack with.");

        if (attacker.IsExhausted)
            return Error("This creature is exhausted.");

        if (attacker.HasAttackedThisTurn)
            return Error("This creature has already attacked this turn.");

        if (sourceLaneIndex < 0 || sourceLaneIndex > 4 || targetLaneIndex < 0 || targetLaneIndex > 4)
            return Error("Invalid lane index.");

        var action = new AttackAction
        {
            PlayerIndex = playerIndex,
            SourceLane = sourceLaneIndex,
            TargetLane = targetLaneIndex
        };

        try
        {
            _state = DuelEngine.Apply(_state, action);
            StateChanged?.Invoke();
            CheckGameOver();
            return Success();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameStateManager] Attack failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return Error(ex.Message);
        }
    }

    /// <summary>
    /// Perform a mulligan for a player. Selected hand cards (by index in the
    /// player's hand list) are shuffled back into the deck and replacements
    /// are drawn. Passing an empty list declines the mulligan. Only one
    /// mulligan per player per game is permitted.
    /// </summary>
    public ActionResult PerformMulligan(int playerIndex, List<int> redrawIndices)
    {
        if (_state.IsGameOver)
            return Error("Game is already over.");

        var player = _state.Players[playerIndex];
        if (player.HasMulliganed)
            return Error("Already mulliganed.");

        // Sort descending so removal doesn't shift remaining indices
        var sorted = redrawIndices.OrderByDescending(i => i).ToList();

        // Validate indices
        if (sorted.Count > 0 && (sorted[0] >= player.Hand.Count || sorted[^1] < 0))
            return Error("Invalid hand index.");

        // Collect cards to redraw (by position in hand)
        var toRedraw = new List<CardInstance>();
        foreach (var idx in sorted)
        {
            if (idx < 0 || idx >= player.Hand.Count) continue;
            toRedraw.Add(player.Hand[idx]);
        }

        if (toRedraw.Count == 0)
        {
            // Declined mulligan — just mark it used
            player.HasMulliganed = true;
            StateChanged?.Invoke();
            return Success();
        }

        // Remove from hand (descending keeps earlier indices valid)
        foreach (var idx in sorted)
        {
            if (idx >= 0 && idx < player.Hand.Count)
            {
                player.Hand[idx].Zone = Zone.Deck;
                player.Hand.RemoveAt(idx);
            }
        }

        // Add back to deck and shuffle
        player.Deck.AddRange(toRedraw);
        GameState.Shuffle(player.Deck, _state.Rng, player.Deck.Count);

        // Draw replacements
        for (int i = 0; i < toRedraw.Count && player.Deck.Count > 0; i++)
        {
            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            card.Zone = Zone.Hand;
            player.Hand.Add(card);
        }

        player.HasMulliganed = true;
        StateChanged?.Invoke();
        return Success();
    }

    /// <summary>
    /// End the current player's turn.
    /// </summary>
    public ActionResult TryEndTurn()
    {
        if (_state.IsGameOver)
            return Error("Game is already over.");

        var action = new EndTurnAction
        {
            PlayerIndex = _state.CurrentPlayerIndex
        };

        try
        {
            // Log exhaustion BEFORE the turn end
            LogExhaustState("Before TryEndTurn");

            _state = DuelEngine.Apply(_state, action);

            // Log exhaustion AFTER the turn end (should show refresh)
            LogExhaustState("After TryEndTurn");

            StateChanged?.Invoke();
            CheckGameOver();
            return Success();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameStateManager] EndTurn failed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return Error(ex.Message);
        }
    }

    // ——— Helpers ———

    private static ActionResult Success() => new() { Success = true };
    private static ActionResult Error(string message) => new() { Success = false, ErrorMessage = message };

    // ——— Game over check ———

    private void CheckGameOver()
    {
        if (_state.IsGameOver)
        {
            GameOver?.Invoke(_state.WinnerIndex ?? -1);
        }
    }

    // ——— Query helpers for UI ———

    /// <summary>
    /// Get card information for the player's hand, ordered as they appear in hand.
    /// </summary>
    public List<HandCardInfo> GetHand(int playerIndex)
    {
        var player = _state.Players[playerIndex];
        var infos = new List<HandCardInfo>();
        foreach (var ci in player.Hand)
        {
            var def = CardRegistry.Get(ci.CardDefId);
            infos.Add(new HandCardInfo
            {
                CardDefId = ci.CardDefId,
                InstanceId = ci.InstanceId,
                Name = def?.Name ?? ci.CardDefId,
                Cost = ci.Cost,
                Strata = def?.Strata ?? Strata.VERDANT
            });
        }
        return infos;
    }

    /// <summary>
    /// Get lane occupant information for a player's board.
    /// Returns 5 entries (one per lane, occupier or null).
    /// </summary>
    public List<LaneInfo> GetLanes(int playerIndex)
    {
        var player = _state.Players[playerIndex];
        var infos = new List<LaneInfo>();
        foreach (var lane in player.Lanes)
        {
            if (lane.Occupant is CardInstance occ)
            {
                var def = CardRegistry.Get(occ.CardDefId);
                infos.Add(new LaneInfo
                {
                    LaneIndex = lane.Index,
                    IsEmpty = false,
                    Name = def?.Name ?? occ.CardDefId,
                    Attack = occ.CurrentAttack,
                    Vigor = occ.CurrentVigor,
                    Keywords = occ.EffectiveKeywords,
                    Controller = occ.Controller,
                    IsExhausted = occ.IsExhausted,
                    IsIdentified = occ.IsIdentified,
                    CardDefId = occ.CardDefId
                });
            }
            else
            {
                infos.Add(new LaneInfo
                {
                    LaneIndex = lane.Index,
                    IsEmpty = true
                });
            }
        }
        return infos;
    }

    /// <summary>
    /// Get player HUD info.
    /// </summary>
    public PlayerHudInfo GetPlayerHud(int playerIndex)
    {
        var p = _state.Players[playerIndex];
        return new PlayerHudInfo
        {
            Vigor = p.Vigor,
            MaxVigor = p.MaxVigor,
            Attunement = p.Attunement,
            AttunementMax = p.AttunementMax,
            DeckCount = p.Deck.Count,
            HandCount = p.Hand.Count
        };
    }

    public int CurrentPlayerIndex => _state.CurrentPlayerIndex;
    public int TurnNumber => _state.TurnNumber;

    /// <summary>
    /// Replaces the internal state wholesale and fires StateChanged.
    /// Used by the test hook to render the final frame of a batch-completed game.
    /// </summary>
    public void SetState(GameState state)
    {
        _state = state;
        IsInitialized = true;
        StateChanged?.Invoke();
        CheckGameOver();
    }

    /// <summary>
    /// Fire StateChanged from outside the class. Used by TutorialRunner to
    /// force a re-render after applying hand/attunement overrides.
    /// </summary>
    public void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Log all creatures' exhaustion state for debugging.
    /// </summary>
    private void LogExhaustState(string prefix)
    {
        if (_state == null) return;
        GD.Print($"=== {prefix} (Turn {_state.TurnNumber}, P{_state.CurrentPlayerIndex}) ===");
        for (int p = 0; p <= 1; p++)
        {
            var player = _state.Players[p];
            GD.Print($"  P{p} (Vigor={player.Vigor}, Attune={player.Attunement}/{player.AttunementMax}):");
            for (int i = 0; i < 5; i++)
            {
                var occ = player.Lanes[i].Occupant;
                if (occ != null)
                    GD.Print($"    Lane[{i}] {occ.CardDefId} A={occ.CurrentAttack} V={occ.CurrentVigor} Exh={occ.IsExhausted} Atk={occ.HasAttackedThisTurn} Sum={occ.SummonedThisTurn}");
                else
                    GD.Print($"    Lane[{i}] empty");
            }
        }
    }
}

// ——— Data transfer types for UI ———

public struct HandCardInfo
{
    public string CardDefId;
    public int InstanceId;
    public string Name;
    public int Cost;
    public Strata Strata;
}

public struct LaneInfo
{
    public int LaneIndex;
    public bool IsEmpty;
    public string Name;
    public int Attack;
    public int Vigor;
    public HashSet<string>? Keywords;
    public int Controller;
    public bool IsExhausted;
    public bool IsIdentified;
    public string CardDefId;
}

public struct PlayerHudInfo
{
    public int Vigor;
    public int MaxVigor;
    public int Attunement;
    public int AttunementMax;
    public int DeckCount;
    public int HandCount;
}

/// <summary>
/// Result of an action attempt. Success indicates the action was applied;
/// ErrorMessage provides a human-readable reason for failure.
/// </summary>
public struct ActionResult
{
    public bool Success;
    public string? ErrorMessage;
}