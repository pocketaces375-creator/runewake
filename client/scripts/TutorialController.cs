using System;
using System.Collections.Generic;
using Godot;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Autoload controller for the tutorial system.
/// Manages the current tutorial step, provides tutorial game configs,
/// and handles step advancement.
/// </summary>
public partial class TutorialController : Node
{
    /// <summary>Current tutorial step. None = not in tutorial.</summary>
    public TutorialStep CurrentStep { get; private set; } = TutorialStep.None;

    /// <summary>True if the tutorial has been fully completed.</summary>
    public bool IsCompleted { get; private set; }

    /// <summary>True if a tutorial is active.</summary>
    public bool IsActive => CurrentStep > TutorialStep.None && CurrentStep < TutorialStep.Complete;

    /// <summary>Raised when the tutorial step changes. UI should re-render.</summary>
    public event Action<TutorialStep>? StepChanged;

    /// <summary>
    /// Raised when the tutorial is completed (all steps done).
    /// </summary>
    public event Action? Completed;

    public override void _Ready()
    {
        Name = "TutorialController";

        // Check if tutorial was already completed via save data
        var prog = CampaignContext.Progression;
        if (prog != null)
            IsCompleted = prog.Tutorial?.IsComplete ?? false;
    }

    /// <summary>
    /// Returns true if the tutorial has not been completed yet.
    /// </summary>
    public bool ShouldRunTutorial() => !IsCompleted;

    /// <summary>
    /// Start the tutorial by setting the first step and navigating to the duel scene.
    /// </summary>
    public void StartTutorial()
    {
        StartFirstDuel();
        GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
    }

    /// <summary>
    /// Start the first tutorial duel (lanes basics).
    /// </summary>
    public void StartFirstDuel()
    {
        CurrentStep = TutorialStep.Lanes_SummonCreature;
    }

    /// <summary>
    /// Force the tutorial to complete (skip). Sets step to Complete and
    /// marks IsCompleted so subsequent game starts skip the tutorial.
    /// </summary>
    public void ForceComplete()
    {
        CurrentStep = TutorialStep.Complete;
        IsCompleted = true;
        Completed?.Invoke();
        GD.Print("[TutorialController] Tutorial force-completed (skip).");
    }

    /// <summary>
    /// Advance to the next logical step. Called by DuelScene when
    /// the player performs the prompted action.
    /// </summary>
    public void Advance()
    {
        var next = CurrentStep switch
        {
            TutorialStep.Lanes_SummonCreature => TutorialStep.Lanes_Attack,
            TutorialStep.Lanes_Attack => TutorialStep.Lanes_EndTurn,
            TutorialStep.Lanes_EndTurn => TutorialStep.Complete,
            TutorialStep.Excavate_PlayExcavate => TutorialStep.Excavate_BuryResolved,
            TutorialStep.Excavate_BuryResolved => TutorialStep.Runes_OpenRunePage,
            TutorialStep.Runes_OpenRunePage => TutorialStep.Runes_EquipRune,
            TutorialStep.Runes_EquipRune => TutorialStep.Complete,
            _ => TutorialStep.Complete
        };

        CurrentStep = next;
        GD.Print($"[TutorialController] Advanced: {CurrentStep}");

        if (next == TutorialStep.Complete)
        {
            GD.Print("[TutorialController] Tutorial complete!");
            Completed?.Invoke();
        }

        StepChanged?.Invoke(next);
    }

    /// <summary>
    /// Get the tutorial hint text for the current step.
    /// These are imperative instructions — they tell the player what to tap next.
    /// DuelScene may override these with dynamic hints based on board state.
    /// </summary>
    public string GetCurrentHint()
    {
        return CurrentStep switch
        {
            TutorialStep.Lanes_SummonCreature =>
                "Tap a playable card to select it.",
            TutorialStep.Lanes_Attack =>
                "Tap your creature, then tap an empty enemy lane to attack!",
            TutorialStep.Lanes_EndTurn =>
                "Tap End Turn to pass. You'll gain more Attunement each turn.",
            TutorialStep.Excavate_PlayExcavate =>
                "Tap your Excavate card, then tap a lane to play it.",
            TutorialStep.Excavate_BuryResolved =>
                "Tap a card with a Bury effect to resolve it.",
            TutorialStep.Runes_OpenRunePage =>
                "Open the rune page to equip runes to your creatures.",
            TutorialStep.Runes_EquipRune =>
                "Select a rune and equip it to a creature on the board.",
            _ => ""
        };
    }

    /// <summary>
    /// Returns true if the current player has a creature on the board
    /// (any occupied lane on player's side). Used for dynamic hints.
    /// </summary>
    public bool PlayerHasCreature()
    {
        // This is a placeholder — DuelScene overrides hints dynamically via
        // board state checks. The controller itself doesn't know the board state.
        return false;
    }

    /// <summary>
    /// Get the GameConfig for the current tutorial duel.
    /// Returns null if not in a tutorial duel step.
    /// </summary>
    public GameConfig? GetCurrentTutorialConfig()
    {
        if (!IsActive) return null;
        return CurrentStep switch
        {
            TutorialStep.Lanes_SummonCreature or
            TutorialStep.Lanes_Attack or
            TutorialStep.Lanes_EndTurn => GetFirstDuelConfig(),
            _ => null
        };
    }

    /// <summary>
    /// Create a GameConfig for the first tutorial duel.
    /// Uses a small, curated deck with low-cost playable cards.
    /// </summary>
    private static GameConfig GetFirstDuelConfig()
    {
        // Curated 12-card creature deck for the tutorial.
        // At least 5 cards cost ≤1 so the opening hand almost always has a playable card.
        // Includes 2 SWIFT creatures so same-turn attacking is possible.
        var deck = new List<string>
        {
            "emb_c_ember_hound",        // cost 1, 2/1 SWIFT — ideal tutorial attacker
            "emb_c_ember_hound",        // duplicate
            "vrd_c_verdant_sproutling", // cost 1, 1/2 — cheap summon
            "vrd_c_verdant_sproutling", // duplicate
            "hol_c_skeletal_reaver",    // cost 1, 2/1 — cheap summon
            "vrd_c_wildwood_stalker",   // cost 2, 3/2 — mid option
            "tid_c_tidal_scholar",      // cost 2, 1/3 — mid option
            "emb_c_cinder_runner",      // cost 2, 3/1 SWIFT — backup swift
            "emb_c_forgeguard_berserker", // cost 3, 4/3 — heavy option
            "vrd_u_grove_healer",       // cost 3, 1/3 — heavy option
            "vrd_c_root_warden",        // cost 3, 2/4 GUARD — heavy option
        };

        return new GameConfig
        {
            Seed = 42,
            ContentVersion = 1,
            Player0DeckIds = deck,
            Player1DeckIds = deck
        };
    }
}