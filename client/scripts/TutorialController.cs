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
    /// </summary>
    public string GetCurrentHint()
    {
        return CurrentStep switch
        {
            TutorialStep.Lanes_SummonCreature =>
                "Tap a card in your hand, then tap an empty lane to summon it to the board.",
            TutorialStep.Lanes_Attack =>
                "Tap one of your creatures, then tap an enemy lane to attack. Empty lanes deal damage to the enemy's face!",
            TutorialStep.Lanes_EndTurn =>
                "Tap the 'End Turn' button in the bottom-right corner to pass to the enemy.",
            TutorialStep.Excavate_PlayExcavate =>
                "Excavate cards let you dig into the earth. Tap your Excavate card, then tap a lane to play it.",
            TutorialStep.Excavate_BuryResolved =>
                "The Excavate card buried a token! Tap a card with a Bury effect to resolve it.",
            TutorialStep.Runes_OpenRunePage =>
                "Open the rune page to equip runes to your creatures.",
            TutorialStep.Runes_EquipRune =>
                "Select a rune from your collection and equip it to a creature on the board.",
            _ => ""
        };
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
        // Curated 15-card deck with cheap, playable cards for the tutorial
        var deck = new List<string>
        {
            "vrd_c_root_warden",     // Cost 2, 3/4 — solid blocker
            "vrd_c_verdant_sproutling", // Cost 1, 2/2 — cheap summon
            "vrd_c_thornbark_defender", // Cost 2, 2/5
            "vrd_u_grove_healer",    // Cost 3, 3/3
            "emb_c_cinder_runner",   // Cost 1, 2/1 — cheap attacker
            "emb_c_ember_hound",     // Cost 2, 3/2
            "tid_c_silt_reader",     // Cost 1, 1/3
            "tid_c_tidal_scholar",   // Cost 2, 2/3
            "vrd_c_root_warden",     // Duplicate so hand has options
            "emb_c_ember_hound",     // Duplicate
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