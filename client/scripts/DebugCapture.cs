using System.Collections.Generic;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// DebugCapture autoload — enables deterministic screenshot captures for acceptance testing.
/// Activated via CLI arg: --capture=duel_test
/// Supports --resolution=WxH to override window resolution (e.g. --resolution=390x844 for portrait phone).
/// Also handles --tutorial=<<script_id>> for TASK-TU2 headless tutorial runner.
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
        bool deckBuilderMode = false;
        bool titleDeckMode = false;
        foreach (var arg in args)
        {
            if (arg == "--capture=duel_test")
            {
                _active = true;
                GD.Print("[DebugCapture] Capture mode enabled: --capture=duel_test");
            }
            if (arg == "--capture=duel_test_wide")
            {
                _active = true;
                CampaignContext.WideCaptureMode = true;
                // Viewport resize is done via project.godot swap in the shell script wrapper.
                // This flag tells DuelScene.cs to write to duel_test_wide.png/meta.json.
                GD.Print("[DebugCapture] Wide capture mode enabled: --capture=duel_test_wide");
            }
            if (arg == "--capture=duel_test_align")
            {
                _active = true;
                CampaignContext.DebugAlignMode = true;
                GD.Print("[DebugCapture] Align capture mode enabled: --capture=duel_test_align");
            }
            if (arg == "--capture=deck_test")
            {
                deckBuilderMode = true;
                GD.Print("[DebugCapture] Capture mode enabled: --capture=deck_test");
            }
            if (arg == "--capture=title_deck")
            {
                titleDeckMode = true;
                GD.Print("[DebugCapture] Capture mode enabled: --capture=title_deck");
            }
            if (arg == "--capture=title_test")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureTitleTestScreenshot = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Title screen capture mode enabled: --capture=title_test");
            }
            if (arg == "--capture=map_test")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureMapScreenshot = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Map screen capture mode enabled: --capture=map_test");
            }
            if (arg == "--capture=victory_overlay")
            {
                _active = true;
                CampaignContext.CaptureVictoryOverlay = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Victory overlay capture mode enabled: --capture=victory_overlay");
            }
            if (arg == "--capture=victory_overlay_wide")
            {
                _active = true;
                CampaignContext.CaptureVictoryOverlay = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Victory overlay (wide) capture mode enabled: --capture=victory_overlay_wide");
            }
            if (arg == "--capture=defeat_overlay")
            {
                _active = true;
                CampaignContext.CaptureDefeatOverlay = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Defeat overlay capture mode enabled: --capture=defeat_overlay");
            }
            if (arg == "--capture=defeat_overlay_wide")
            {
                _active = true;
                CampaignContext.CaptureDefeatOverlay = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Defeat overlay (wide) capture mode enabled: --capture=defeat_overlay_wide");
            }
            if (arg.StartsWith("--tutorial="))
            {
                tutorialScriptId = arg.Substring("--tutorial=".Length);
            }
        }

        if (deckBuilderMode)
        {
            GD.Print("[DebugCapture] Deck builder capture mode — setting up test deck state");
            SetUpDeckBuilderTest();
            return;
        }

        if (titleDeckMode)
        {
            GD.Print("[DebugCapture] Title+Deck capture mode — showing title screen then navigating to deck builder");
            SetUpTitleDeckTest();
            return;
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
            Name = "The Wayfarer",
            IsTutorial = false,
            Deck = deck,
            Portrait = "",
            DialogueIntro = [],
            DialogueOutro = ["A worthy challenge...", "The ember burns brighter within you."],
            ShardReward = 15,
            DigChargeReward = 2,
            FragmentReward = "ember:1"
        };
        CampaignContext.AutoCaptureScreenshot = true;
        CampaignContext.DebugSeed = 42;
        GD.Print("[DebugCapture] Set DebugSeed=42, AutoCaptureScreenshot=true");

        // For victory/defeat overlay captures: the duel will auto-end via
        // DuelScene._Ready() which checks CaptureVictoryOverlay/DefeatOverlay.
        // The encounter name "The Wayfarer" will appear in the overlay headline.
    }

    /// <summary>
    /// Deck builder capture mode: populate collection with all cards, create a
    /// 31-card deck (one duplicate forced to trigger DK1 validation error),
    /// then navigate to the deck builder scene for capture.
    /// </summary>
    private void SetUpDeckBuilderTest()
    {
        GD.Print("[DebugCapture] Deck builder test setup — loading all cards into collection");

        // Load all card packs into collection
        var allCards = new List<CardDef>();
        var packs = new[] {
            "res://content/cards/verdant.json", "res://content/cards/ember.json",
            "res://content/cards/tide.json", "res://content/cards/hollow.json",
            "res://content/cards/dawn.json"
        };
        foreach (var pack in packs)
        {
            string json = Godot.FileAccess.GetFileAsString(pack);
            allCards.AddRange(CardLoader.LoadPackFromString(json));
        }

        // Add all cards to progression collection (one copy each)
        CampaignContext.Progression.Collection.Clear();
        foreach (var card in allCards)
            CampaignContext.Progression.Collection[card.Id] = 1;

        // Build a 31-card deck with one intentional duplicate to trigger DK1 validation error
        // 30 unique cards + 1 duplicate = 31 total, but the duplicate triggers singleton error
        var deck = new List<string>
        {
            "vrd_c_root_warden",
            "vrd_c_verdant_sproutling",
            "vrd_c_thornbark_defender",
            "vrd_r_bloomweaver",
            "vrd_u_grove_healer",
            "vrd_x_heartwood_relic",
            "vrd_c_wildwood_stalker",
            "vrd_u_canopy_archer",
            "vrd_u_saphoof_charger",
            "vrd_u_elder_treant",
            "emb_c_ember_hound",
            "emb_c_cinder_runner",
            "emb_c_forgeguard_berserker",
            "emb_u_wildfire_adept",
            "emb_u_lava_serpent",
            "tid_c_tidal_scholar",
            "tid_c_deep_one",
            "tid_c_silt_reader",
            "tid_u_brine_witch",
            "hol_c_skeletal_reaver",
            "hol_c_gravewrit_thrall",
            "hol_c_ossuary_guard",
            "dwn_r_sealing_light",
            "dwn_c_dawn_warder",
            "dwn_c_sunblade_recruit",
            "dwn_u_purifying_light",
            "dwn_c_golden_retainer",
            "dwn_c_dawnbreaker_charger",
            "dwn_u_steadfast_bulwark",
            "tid_c_abyssal_gaze",
            "vrd_c_root_warden"   // intentional duplicate → DK1 error: "duplicate: Root Warden"
        };
        CampaignContext.Progression.DeckCardIds.Clear();
        CampaignContext.Progression.DeckCardIds.AddRange(deck);

        GD.Print($"[DebugCapture] Deck builder test: {deck.Count} cards loaded, one duplicate forced");

        // Set auto-capture flag to navigate to deck builder
        CampaignContext.AutoCaptureScreenshot = true;
        CampaignContext.CaptureDeckBuilderScreenshot = true;
        CampaignContext.DebugSeed = 42;
    }

    /// <summary>
    /// Title+Deck capture mode: set up deck card data, then set capture flags so
    /// Main.cs first captures the title screen (with Decks button), then navigates
    /// to the deck builder for the tome capture.
    /// </summary>
    private void SetUpTitleDeckTest()
    {
        var allCards = new List<CardDef>();
        var packs = new[] {
            "res://content/cards/verdant.json", "res://content/cards/ember.json",
            "res://content/cards/tide.json", "res://content/cards/hollow.json",
            "res://content/cards/dawn.json"
        };
        foreach (var pack in packs)
        {
            string json = Godot.FileAccess.GetFileAsString(pack);
            allCards.AddRange(CardLoader.LoadPackFromString(json));
        }

        CampaignContext.Progression.Collection.Clear();
        foreach (var card in allCards)
            CampaignContext.Progression.Collection[card.Id] = 1;

        var deck = new List<string>
        {
            "vrd_c_root_warden",
            "vrd_c_verdant_sproutling",
            "vrd_c_thornbark_defender",
            "vrd_r_bloomweaver",
            "vrd_u_grove_healer",
            "vrd_x_heartwood_relic",
            "vrd_c_wildwood_stalker",
            "vrd_u_canopy_archer",
            "vrd_u_saphoof_charger",
            "vrd_u_elder_treant",
            "emb_c_ember_hound",
            "emb_c_cinder_runner",
            "emb_c_forgeguard_berserker",
            "emb_u_wildfire_adept",
            "emb_u_lava_serpent",
            "tid_c_tidal_scholar",
            "tid_c_deep_one",
            "tid_c_silt_reader",
            "tid_u_brine_witch",
            "hol_c_skeletal_reaver",
            "hol_c_gravewrit_thrall",
            "hol_c_ossuary_guard",
            "dwn_r_sealing_light",
            "dwn_c_dawn_warder",
            "dwn_c_sunblade_recruit",
            "dwn_u_purifying_light",
            "dwn_c_golden_retainer",
            "dwn_c_dawnbreaker_charger",
            "dwn_u_steadfast_bulwark",
            "tid_c_abyssal_gaze",
            "vrd_c_root_warden"
        };
        CampaignContext.Progression.DeckCardIds.Clear();
        CampaignContext.Progression.DeckCardIds.AddRange(deck);

        CampaignContext.AutoCaptureScreenshot = true;
        CampaignContext.CaptureTitleDeckScreenshot = true;
        CampaignContext.DebugSeed = 42;

        GD.Print($"[DebugCapture] Title+Deck test: {deck.Count} cards loaded");
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