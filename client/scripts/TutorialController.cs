using System;
using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Godot Node that drives tutorial flow.
/// Wraps TutorialState and TutorialStepDefs, fires signals on step changes.
/// </summary>
public partial class TutorialController : Node
{
    private ProgressionState _prog = default!;
    private List<TutorialStepDef> _steps = new();
    private bool _initialized;

    /// <summary>
    /// Fired when the current step definition changes.
    /// The parameter is true if the step changed (not null; use GetCurrentDef() to read it).
    /// </summary>
    [Signal]
    public delegate void StepChangedEventHandler();

    /// <summary>
    /// Whether the tutorial should run on this session.
    /// </summary>
    public bool ShouldRunTutorial()
    {
        return _prog?.Tutorial != null
            && _prog.Tutorial.CurrentStep == TutorialStep.Lanes_SummonCreature
            && !_prog.Tutorial.IsComplete;
    }

    /// <summary>
    /// Start the tutorial by emitting the first step signal.
    /// </summary>
    public void StartTutorial()
    {
        if (IsActive)
        {
            EmitSignal(SignalName.StepChanged);
        }
    }

    /// <summary>
    /// Returns the tutorial config for the current step, or null if not in a tutorial duel.
    /// DuelScene checks this to override normal encounter-based initialization.
    /// </summary>
    public GameConfig? GetCurrentTutorialConfig()
    {
        if (!IsActive) return null;
        var step = CurrentStep;
        if (step != TutorialStep.Lanes_SummonCreature
            && step != TutorialStep.Lanes_Attack
            && step != TutorialStep.Lanes_EndTurn
            && step != TutorialStep.Excavate_PlayExcavate
            && step != TutorialStep.Excavate_BuryResolved)
            return null;
        return GetConfigForStep(step);
    }

    /// <summary>
    /// Initialize with progression state and step definitions.
    /// </summary>
    public void Initialize(ProgressionState prog, List<TutorialStepDef> steps)
    {
        _prog = prog;
        _steps = steps;
        _initialized = true;

        if (IsActive)
        {
            EmitSignal(SignalName.StepChanged);
        }
    }

    /// <summary>
    /// Whether the tutorial is currently active (not None and not Complete).
    /// </summary>
    public bool IsActive => _initialized
        && _prog.Tutorial != null
        && _prog.Tutorial.CurrentStep != TutorialStep.None
        && _prog.Tutorial.CurrentStep != TutorialStep.Complete
        && !_prog.Tutorial.IsComplete;

    /// <summary>
    /// Current step enum value.
    /// </summary>
    public TutorialStep CurrentStep => _prog?.Tutorial?.CurrentStep ?? TutorialStep.None;

    /// <summary>
    /// Get the TutorialStepDef for the current step.
    /// </summary>
    public TutorialStepDef? GetCurrentDef()
    {
        if (_prog?.Tutorial == null) return null;
        return _steps.Find(s => s.Step == _prog.Tutorial.CurrentStep);
    }

    /// <summary>
    /// Advance to the next step. Fires StepChanged.
    /// If advancing to Complete, marks IsComplete and fires StepChanged(null).
    /// </summary>
    public void Advance()
    {
        if (_prog?.Tutorial == null) return;

        var current = _prog.Tutorial.CurrentStep;
        var next = current switch
        {
            TutorialStep.None => TutorialStep.Lanes_SummonCreature,
            TutorialStep.Lanes_SummonCreature => TutorialStep.Lanes_Attack,
            TutorialStep.Lanes_Attack => TutorialStep.Lanes_EndTurn,
            TutorialStep.Lanes_EndTurn => TutorialStep.Excavate_PlayExcavate,
            TutorialStep.Excavate_PlayExcavate => TutorialStep.Excavate_BuryResolved,
            TutorialStep.Excavate_BuryResolved => TutorialStep.Runes_OpenRunePage,
            TutorialStep.Runes_OpenRunePage => TutorialStep.Runes_EquipRune,
            TutorialStep.Runes_EquipRune => TutorialStep.Complete,
            TutorialStep.Complete => TutorialStep.Complete,
            _ => TutorialStep.Complete,
        };

        _prog.Tutorial.CurrentStep = next;

        if (next == TutorialStep.Complete)
        {
            _prog.Tutorial.IsComplete = true;
            GD.Print("[TutorialController] Tutorial complete!");
        }
        else
        {
            var def = GetCurrentDef();
            GD.Print($"[TutorialController] Advanced to {next}");
        }
        EmitSignal(SignalName.StepChanged);
    }

    /// <summary>
    /// Get a GameConfig for a tutorial step duel.
    /// These are hard-coded minimal duels, not encounter-based.
    /// </summary>
    public GameConfig GetConfigForStep(TutorialStep step)
    {
        var playerDeck = new System.Collections.Generic.List<string>();
        var botDeck = new System.Collections.Generic.List<string>();

        // Use vrd_c_root_warden as the simplest existing card (1-cost 2/2 with GUARD)
        for (int i = 0; i < 30; i++)
            playerDeck.Add("vrd_c_root_warden");
        for (int i = 0; i < 30; i++)
            botDeck.Add("vrd_c_root_warden");

        if (step == TutorialStep.Excavate_PlayExcavate || step == TutorialStep.Excavate_BuryResolved)
        {
            // Place silt seeker at index 0 for the excavate duel
            if (playerDeck.Count > 0)
                playerDeck[0] = "tid_c_silt_seeker";
        }

        return new GameConfig
        {
            Seed = 42,
            ContentVersion = 1,
            Player0DeckIds = playerDeck,
            Player1DeckIds = botDeck,
        };
    }
}