using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Manages the player input state machine for the duel scene.
/// Handles two input modes:
///   1. Drag card from hand to empty player lane → play card
///   2. Tap friendly creature → tap enemy lane/creature → attack
/// Emits C# events for DuelScene to consume.
/// </summary>
public partial class InputController : Node
{
    // ——— Events ———

    /// <summary>Raised when the player drags a card from hand onto a lane slot.</summary>
    public event Action<string, int>? PlayCardRequested; // cardId, laneIndex

    /// <summary>Raised when the player confirms an attack (attacker lane → target lane/face).</summary>
    public event Action<int, int>? AttackRequested; // attackerLaneIndex, targetLaneIndex (-1 = face)

    /// <summary>Raised when the player cancels a pending selection.</summary>
    public event Action? SelectionCancelled;

    // ——— State ———

    public enum InputState { Idle, SelectingAttacker }

    public InputState State { get; private set; } = InputState.Idle;

    /// <summary>Index of the currently selected attacker lane, or -1.</summary>
    public int SelectedAttackerLane { get; private set; } = -1;

    // ——— Public API ———

    /// <summary>
    /// Call when the player drags a card onto a lane slot (from HandCard._DropData).
    /// </summary>
    public bool TryPlayCard(string cardId, int laneIndex)
    {
        if (string.IsNullOrEmpty(cardId) || laneIndex < 0 || laneIndex > 4)
            return false;

        PlayCardRequested?.Invoke(cardId, laneIndex);
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
        return true;
    }

    /// <summary>
    /// Call when the player taps an empty/self lane or the hand area while in
    /// selecting-attacker mode — cancels the selection.
    /// </summary>
    public void CancelSelection()
    {
        if (State != InputState.Idle)
        {
            State = InputState.Idle;
            SelectedAttackerLane = -1;
            SelectionCancelled?.Invoke();
        }
    }

    /// <summary>
    /// Full reset — go back to idle state.
    /// </summary>
    public void Reset()
    {
        State = InputState.Idle;
        SelectedAttackerLane = -1;
    }
}