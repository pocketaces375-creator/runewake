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
            "tid_u_coral_guardian"
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
    /// Returns true if the action was valid and applied.
    /// </summary>
    public bool TryPlayCard(int playerIndex, string cardDefId, int laneIndex)
    {
        if (_state.IsGameOver) return false;

        var player = _state.Players[playerIndex];
        var card = player.Hand.FirstOrDefault(c => c.CardDefId == cardDefId);
        if (card == null) return false;

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
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameStateManager] PlayCard failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Attack with a creature from sourceLaneIndex, targeting the opposing lane.
    /// </summary>
    public bool TryAttack(int playerIndex, int sourceLaneIndex, int targetLaneIndex)
    {
        if (_state.IsGameOver) return false;

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
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameStateManager] Attack failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// End the current player's turn.
    /// </summary>
    public bool TryEndTurn()
    {
        if (_state.IsGameOver) return false;

        var action = new EndTurnAction
        {
            PlayerIndex = _state.CurrentPlayerIndex
        };

        try
        {
            _state = DuelEngine.Apply(_state, action);
            StateChanged?.Invoke();
            CheckGameOver();
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GameStateManager] EndTurn failed: {ex.Message}");
            return false;
        }
    }

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
                Cost = ci.Cost
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
}

// ——— Data transfer types for UI ———

public struct HandCardInfo
{
    public string CardDefId;
    public int InstanceId;
    public string Name;
    public int Cost;
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