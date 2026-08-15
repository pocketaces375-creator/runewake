using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// DebugCapture autoload — enables deterministic screenshot captures for acceptance testing.
/// Activated via CLI arg: --capture=duel_test
/// Also handles --tutorial=<script_id> for TASK-TU2 headless tutorial runner.
/// Sets up a fixed game state: 4 hand cards (some with art, some without),
/// creatures on both board rows, partially spent attunement.
/// </summary>
public partial class DebugCapture : Node
{
    private bool _active = false;
    private bool _captureDone = false;
    private Node? _duelScene;

    public DebugCapture()
    {
        GD.Print("[DebugCapture] Constructor called");
    }

    public override void _EnterTree()
    {
        GD.Print("[DebugCapture] _EnterTree called");
    }

    public override void _Ready()
    {
        GD.Print("[DebugCapture] _Ready called");
        var args = OS.GetCmdlineArgs();
        var userArgs = OS.GetCmdlineUserArgs();
        if (userArgs != null && userArgs.Length > 0)
        {
            var combined = new string[args.Length + userArgs.Length];
            System.Array.Copy(args, combined, args.Length);
            System.Array.Copy(userArgs, 0, combined, args.Length, userArgs.Length);
            args = combined;
        }
        GD.Print($"[DebugCapture] Cmdline args: {string.Join(", ", args)}");

        // TASK-TU2: Handle --tutorial=<script_id> for headless tutorial runner
        string? tutorialScriptId = null;
        foreach (var arg in args)
        {
            if (arg == "--capture=duel_test")
            {
                _active = true;
                GD.Print("[DebugCapture] Capture mode enabled: --capture=duel_test");
            }
            if (arg.StartsWith("--tutorial="))
            {
                tutorialScriptId = arg.Substring("--tutorial=".Length);
            }
        }

        if (!string.IsNullOrEmpty(tutorialScriptId))
        {
            GD.Print($"[DebugCapture] Tutorial script mode: {tutorialScriptId}");
            SetUpTutorialEncounter(tutorialScriptId);
            return; // Tutorial script mode handles its own flow
        }

        if (_active)
        {
            SetUpTestEncounter();
        }
    }

    private void SetUpTestEncounter()
    {
        var deck = new List<string>
        {
            "tid_c_abyssal_gaze",
            "dwn_r_sealing_light",
            "emb_c_cinder_runner",
            "vrd_x_heartwood_relic",
            "vrd_c_root_warden", "vrd_c_root_warden", "vrd_c_root_warden",
            "vrd_c_verdant_sproutling", "vrd_c_verdant_sproutling",
            "vrd_c_thornbark_defender", "vrd_c_thornbark_defender",
            "dwn_c_dawn_warder", "dwn_c_dawn_warder",
            "emb_c_ember_hound", "emb_c_ember_hound",
            "vrd_r_bloomweaver",
            "hol_c_skeletal_reaver",
            "dwn_u_purifying_light",
            "emb_c_forgeguard_berserker",
            "vrd_c_verdant_sproutling",
            "emb_c_cinder_runner",
            "tid_c_tidal_scholar",
            "dwn_r_sealing_light",
            "hol_c_gravewrit_thrall",
            "tid_c_silt_reader",
            "emb_u_wildfire_adept",
            "tid_c_deep_one",
            "tid_u_brine_witch",
            "emb_u_lava_serpent",
            "vrd_u_grove_healer"
        };

        GD.Print($"[DebugCapture] Setting up test encounter with deck of {deck.Count} cards");

        CampaignContext.PlayerDeckIds = deck;
        CampaignContext.CurrentEncounter = new EncounterDef
        {
            Id = "debug_test",
            Name = "Debug Test",
            IsTutorial = false,
            Deck = deck,
            Portrait = "",
            DialogueIntro = [],
            DialogueOutro = [],
            ShardReward = 0,
            DigChargeReward = 0
        };
        CampaignContext.AutoCaptureScreenshot = true;
        CampaignContext.DebugSeed = 42;
        GD.Print("[DebugCapture] Set DebugSeed=42, AutoCaptureScreenshot=true");
    }

    /// <summary>
    /// TASK-TU2: Set up campaign context for headless tutorial run.
    /// The TutorialRunner in DuelScene will read CampaignContext.TutorialScriptId
    /// and take over the duel flow.
    /// </summary>
    private void SetUpTutorialEncounter(string scriptId)
    {
        // Set the flag that tells DuelScene to create a TutorialRunner
        CampaignContext.TutorialScriptId = scriptId;
        CampaignContext.AutoCaptureScreenshot = false;
        CampaignContext.DebugSeed = 42;

        GD.Print($"[DebugCapture] Tutorial mode set: {scriptId}");
    }

    public override void _Process(double delta)
    {
        if (!_active || _captureDone) return;

        if (_duelScene == null)
        {
            _duelScene = GetNodeOrNull<Node>("/root/DuelScene");
            if (_duelScene == null)
            {
                _duelScene = GetTree().CurrentScene;
                if (_duelScene != null && _duelScene.Name != "DuelScene")
                    _duelScene = null;
            }
        }
    }
}