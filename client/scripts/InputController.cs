using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Manages the player input state machine for the duel scene.
/// Handles three input modes:
///   1. Drag card from hand to empty player lane → play card
///   2. Tap card in hand, then tap empty player lane → play card
///   3. Tap friendly creature, then tap enemy lane/creature → attack
/// Emits C# events for DuelScene to consume.
/// Engine is the sole authority on action legality — the client only
/// communicates intent and displays the result.
/// </summary>
public partial class InputController : Node
{
    // ——— Events ———

    /// <summary>Raised when the player drags or taps a card, then targets a lane.</summary>
    public event Action<string, int>? PlayCardRequested; // cardId, laneIndex

    /// <summary>Raised when the player confirms an attack (attacker lane → target lane).</summary>
    public event Action<int, int>? AttackRequested; // attackerLaneIndex, targetLaneIndex

    /// <summary>Raised when the player taps a friendly creature to select it for attack (before target chosen).</summary>
    public event Action<int>? CreatureSelectedForAttack; // attackerLaneIndex

    /// <summary>Raised when the player cancels a pending selection.</summary>
    public event Action? SelectionCancelled;

    // ——— State ———

    public enum InputState { Idle, SelectingAttacker, SelectingLane }

    public InputState State { get; private set; } = InputState.Idle;

    /// <summary>Index of the currently selected attacker lane, or -1.</summary>
    public int SelectedAttackerLane { get; private set; } = -1;

    /// <summary>Card ID selected for tap-to-summon, or null.</summary>
    public string? SelectedCardId { get; private set; }

    // ——— Public API ———

    /// <summary>
    /// Call when the player drags a card onto a lane slot (from HandCard._DropData).
    /// Always accepted by the controller — the engine validates cost and legality.
    /// </summary>
    public bool TryPlayCard(string cardId, int laneIndex)
    {
        if (string.IsNullOrEmpty(cardId) || laneIndex < 0 || laneIndex > 4)
        {
            GD.Print($"[INPUT_TRACE] TryPlayCard: REJECTED cardId='{cardId ?? "null"}' lane={laneIndex}");
            return false;
        }

        State = InputState.Idle;
        SelectedCardId = null;
        GD.Print($"[INPUT_TRACE] TryPlayCard: ACCEPTED cardId='{cardId}' lane={laneIndex}");
        PlayCardRequested?.Invoke(cardId, laneIndex);
        SelectionCancelled?.Invoke();
        return true;
    }

    /// <summary>
    /// Call when the player taps a card in hand in Idle state.
    /// Enters SelectingLane mode — next tap on an empty player lane summons.
    /// </summary>
    public bool SelectCardForPlay(string cardId)
    {
        if (string.IsNullOrEmpty(cardId))
        {
            GD.Print($"[INPUT_TRACE] SelectCardForPlay: REJECTED cardId='{cardId ?? "null"}'");
            return false;
        }

        State = InputState.SelectingLane;
        SelectedCardId = cardId;
        GD.Print($"[INPUT_TRACE] SelectCardForPlay: state=SelectingLane cardId='{cardId}'");
        return true;
    }

    /// <summary>
    /// Call when the player taps a lane while in SelectingLane state.
    /// Emits PlayCardRequested and resets to Idle.
    /// </summary>
    public bool SelectTargetLane(int laneIndex)
    {
        if (State != InputState.SelectingLane || SelectedCardId == null)
        {
            GD.Print($"[INPUT_TRACE] SelectTargetLane: REJECTED state={State} cardId='{SelectedCardId ?? "null"}' lane={laneIndex}");
            return false;
        }

        string cardId = SelectedCardId;
        State = InputState.Idle;
        SelectedCardId = null;
        GD.Print($"[INPUT_TRACE] SelectTargetLane: ACCEPTED cardId='{cardId}' lane={laneIndex}");
        PlayCardRequested?.Invoke(cardId, laneIndex);
        SelectionCancelled?.Invoke();
        return true;
    }

    /// <summary>
    /// Call when the player taps a friendly creature or its lane slot.
    /// Enters attacker-selection mode. Returns true if selection started.
    /// </summary>
    public bool SelectAttacker(int laneIndex)
    {
        if (laneIndex < 0 || laneIndex > 4)
            return false;

        State = InputState.SelectingAttacker;
        SelectedAttackerLane = laneIndex;
        CreatureSelectedForAttack?.Invoke(laneIndex);
        return true;
    }

    /// <summary>
    /// Call when the player taps a target lane (enemy) while in selecting-attacker mode.
    /// Emits AttackRequested and resets to Idle.
    /// </summary>
    public bool SelectAttackTarget(int targetLaneIndex)
    {
        if (State != InputState.SelectingAttacker)
            return false;

        int attackerLane = SelectedAttackerLane;
        State = InputState.Idle;
        SelectedAttackerLane = -1;
        AttackRequested?.Invoke(attackerLane, targetLaneIndex);
        SelectionCancelled?.Invoke();
        return true;
    }

    /// <summary>
    /// Call when the player taps empty space or cancels — resets selection state.
    /// </summary>
    public void CancelSelection()
    {
        if (State != InputState.Idle)
        {
            State = InputState.Idle;
            SelectedAttackerLane = -1;
            SelectedCardId = null;
            SelectionCancelled?.Invoke();
        }
    }

    /// <summary>
    /// Full reset — go back to idle state without emitting events.
    /// </summary>
    public void Reset()
    {
        State = InputState.Idle;
        SelectedAttackerLane = -1;
        SelectedCardId = null;
    }
}