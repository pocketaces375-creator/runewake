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
        bool reliquaryMode = false;
        foreach (var arg in args)
        {
            if (arg == "--capture=duel_test")
            {
                _active = true;
                GD.Print("[DebugCapture] Capture mode enabled: --capture=duel_test");
            }
            if (arg == "--capture=input_smoke_test")
            {
                _active = true;
                CampaignContext.InputSmokeTest = true;
                GD.Print("[DebugCapture] Input smoke test enabled: --capture=input_smoke_test");
            }
            if (arg == "--capture=loop_smoke_test")
            {
                // Don't set _active = true — loop smoke test handles everything via
                // its own autoload and natural scene navigation, not through SetUpTestEncounter.
                CampaignContext.LoopSmokeTest = true;
                CampaignContext.BotDuelTest = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Loop smoke test enabled: --capture=loop_smoke_test");
                // Clear save database for fresh campaign start
                // Path: ~/.local/share/godot/app_userdata/Runewake/
                string saveDir = System.IO.Path.Combine(
                    System.Environment.GetEnvironmentVariable("HOME") ?? "/home/fictive",
                    ".local", "share", "godot", "app_userdata", "Runewake");
                if (System.IO.Directory.Exists(saveDir))
                {
                    foreach (var f in System.IO.Directory.GetFiles(saveDir, "*.json"))
                    {
                        System.IO.File.Delete(f);
                        GD.Print($"[DebugCapture] Deleted save file: {f}");
                    }
                }
                else
                {
                    GD.Print($"[DebugCapture] Save directory not found: {saveDir}");
                }
            }
            if (arg == "--capture=bot_duel")
            {
                _active = true;
                CampaignContext.BotDuelTest = true;
                GD.Print("[DebugCapture] BOT-FIX-1: bot duel harness enabled: --capture=bot_duel");
            }
            if (arg == "--capture=bot_duel_tut")
            {
                _active = true;
                CampaignContext.BotDuelTest = true;
                CampaignContext.BotDuelTutorialVariant = true;
                GD.Print("[DebugCapture] BOT-FIX-1: bot duel harness (Wayfarer tutorial variant) enabled");
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
            if (arg == "--capture=duel_test_safe")
            {
                _active = true;
                CampaignContext.DebugSafeAreaMode = true;
                GD.Print("[DebugCapture] Safe-area capture mode enabled: --capture=duel_test_safe");
            }
            if (arg == "--capture=duel_test_r2")
            {
                _active = true;
                CampaignContext.R2CardScale = true;
                CampaignContext.CurrentRegionSkinId = "ember";
                GD.Print("[DebugCapture] R2 card scale variant enabled: --capture=duel_test_r2 — ember skin active");
            }
            if (arg == "--capture=deck_test")
            {
                deckBuilderMode = true;
                GD.Print("[DebugCapture] Capture mode enabled: --capture=deck_test");
            }
            if (arg == "--capture=deck_test_phone")
            {
                deckBuilderMode = true;
                CampaignContext.PhoneCaptureMode = true;
                GD.Print("[DebugCapture] Phone capture mode enabled: --capture=deck_test_phone");
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
            if (arg == "--capture=title_test_wide")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureTitleTestScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Title screen wide capture mode enabled: --capture=title_test_wide");
            }
            if (arg == "--capture=map_test")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureMapScreenshot = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Map screen capture mode enabled: --capture=map_test");
            }
            if (arg == "--capture=map_test_wide")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureMapScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Map screen wide capture mode enabled: --capture=map_test_wide");
            }
            if (arg == "--capture=map_test_r2")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureMapScreenshot = true;
                CampaignContext.CaptureMapR2Screenshot = true;
                CampaignContext.CurrentRegionId = "region_02";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Map screen R2 (ember skin) capture mode enabled: --capture=map_test_r2");
            }
            if (arg == "--capture=map_test_r2_wide")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureMapScreenshot = true;
                CampaignContext.CaptureMapR2Screenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.CurrentRegionId = "region_02";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Map screen R2 wide (ember skin) capture mode enabled: --capture=map_test_r2_wide");
            }
            if (arg == "--capture=map_test_r3")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureMapScreenshot = true;
                CampaignContext.ForceRegionId = "region_03";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Map screen R3 (Tide) capture mode enabled: --capture=map_test_r3");
            }
            if (arg == "--capture=map_test_r4")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureMapScreenshot = true;
                CampaignContext.ForceRegionId = "region_04";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Map screen R4 (Dawn) capture mode enabled: --capture=map_test_r4");
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
            if (arg == "--capture=victory_flow_test")
            {
                _active = true;
                CampaignContext.CaptureVictoryOverlay = true;
                CampaignContext.FlowTestAfterOverlay = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Victory flow test enabled: --capture=victory_flow_test — will auto-navigate to map after capture");
            }
            if (arg == "--capture=victory_flow_test_wide")
            {
                _active = true;
                CampaignContext.CaptureVictoryOverlay = true;
                CampaignContext.FlowTestAfterOverlay = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Victory flow test (wide) enabled: --capture=victory_flow_test_wide");
            }
            if (arg == "--capture=defeat_flow_test")
            {
                _active = true;
                CampaignContext.CaptureDefeatOverlay = true;
                CampaignContext.FlowTestAfterOverlay = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Defeat flow test enabled: --capture=defeat_flow_test — will auto-navigate to map after capture");
            }
            if (arg == "--capture=defeat_flow_test_wide")
            {
                _active = true;
                CampaignContext.CaptureDefeatOverlay = true;
                CampaignContext.FlowTestAfterOverlay = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Defeat flow test (wide) enabled: --capture=defeat_flow_test_wide");
            }
            if (arg == "--capture=choose_path")
            {
                CampaignContext.CaptureChoosePathScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] ChoosePath capture mode enabled: --capture=choose_path");
            }
            if (arg == "--capture=choose_path_wide")
            {
                CampaignContext.CaptureChoosePathScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] ChoosePath wide capture mode enabled: --capture=choose_path_wide");
            }
            if (arg == "--capture=settings_test")
            {
                CampaignContext.CaptureSettingsScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Settings capture mode enabled: --capture=settings_test");
            }
            if (arg == "--capture=settings_test_wide")
            {
                CampaignContext.CaptureSettingsScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Settings wide capture mode enabled: --capture=settings_test_wide");
            }
            if (arg == "--capture=crash_test")
            {
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CrashTestMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Crash test capture mode enabled: --capture=crash_test");
            }
            if (arg == "--capture=dig_test")
            {
                CampaignContext.CaptureDigScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.DebugSeed = 42;
                // Set up a test dig site so the scene can render
                var digSite = new Runewake.Engine.Cards.DigSiteDef
                {
                    Id = "test_dig_site",
                    Name = "The Earthen Maw",
                    Description = "A dark crevice in the earth, humming with ancient energy.",
                    Rows = 4,
                    Cols = 4,
                    Strikes = 3
                };
                CampaignContext.DigSiteIndex.Clear();
                CampaignContext.DigSiteIndex["test_dig_site"] = digSite;
                CampaignContext.CurrentDigSiteId = "test_dig_site";
                GD.Print("[DebugCapture] Dig capture mode enabled: --capture=dig_test");
            }
            if (arg == "--capture=dig_test_wide")
            {
                CampaignContext.CaptureDigScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                var digSite = new Runewake.Engine.Cards.DigSiteDef
                {
                    Id = "test_dig_site",
                    Name = "The Earthen Maw",
                    Description = "A dark crevice in the earth, humming with ancient energy.",
                    Rows = 4,
                    Cols = 4,
                    Strikes = 3
                };
                CampaignContext.DigSiteIndex.Clear();
                CampaignContext.DigSiteIndex["test_dig_site"] = digSite;
                CampaignContext.CurrentDigSiteId = "test_dig_site";
                GD.Print("[DebugCapture] Dig wide capture mode enabled: --capture=dig_test_wide");
            }
            if (arg == "--capture=reliquary_test")
            {
                reliquaryMode = true;
                CampaignContext.CaptureReliquaryScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureOverrideStrataIdx = 2; // EMBER for visual variety
                CampaignContext.CaptureReliquaryBasename = "reliquary_test";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Reliquary capture mode enabled: --capture=reliquary_test");
            }
            if (arg == "--capture=reliquary_test_wide")
            {
                reliquaryMode = true;
                CampaignContext.CaptureReliquaryScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.CaptureOverrideStrataIdx = 2; // EMBER for visual variety
                CampaignContext.CaptureReliquaryBasename = "reliquary_test_wide";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Reliquary wide capture mode enabled: --capture=reliquary_test_wide");
            }
            if (arg == "--capture=reliquary_test_all")
            {
                reliquaryMode = true;
                CampaignContext.CaptureReliquaryScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.CaptureOverrideStrataIdx = 0; // ALL — unfiltered
                CampaignContext.CaptureReliquaryBasename = "reliquary_test_all";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Reliquary all-cards capture mode enabled: --capture=reliquary_test_all");
            }
            if (arg == "--capture=reliquary_test_all_wide")
            {
                reliquaryMode = true;
                CampaignContext.CaptureReliquaryScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.CaptureOverrideStrataIdx = 0; // ALL — unfiltered
                CampaignContext.CaptureReliquaryBasename = "reliquary_test_all_wide";
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Reliquary all-cards wide capture mode enabled: --capture=reliquary_test_all_wide");
            }
            if (arg == "--capture=shop_test")
            {
                CampaignContext.CaptureShopScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Shop capture mode enabled: --capture=shop_test");
            }
            if (arg == "--capture=shop_test_wide")
            {
                CampaignContext.CaptureShopScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Shop wide capture mode enabled: --capture=shop_test_wide");
            }
            if (arg == "--capture=slots_test")
            {
                CampaignContext.CaptureSlotPickerScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.SlotPickerTestMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Slot picker capture mode enabled: --capture=slots_test");
            }
            if (arg == "--capture=slots_test_wide")
            {
                CampaignContext.CaptureSlotPickerScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.SlotPickerTestMode = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Slot picker wide capture mode enabled: --capture=slots_test_wide");
            }
            if (arg == "--capture=accounts_carousel")
            {
                CampaignContext.CaptureAccountsCarouselScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Accounts carousel capture mode enabled: --capture=accounts_carousel");
            }
            if (arg == "--capture=accounts_carousel_wide")
            {
                CampaignContext.CaptureAccountsCarouselScreenshot = true;
                CampaignContext.AutoCaptureScreenshot = true;
                CampaignContext.WideCaptureMode = true;
                CampaignContext.DebugSeed = 42;
                GD.Print("[DebugCapture] Accounts carousel wide capture mode enabled: --capture=accounts_carousel_wide");
            }
            if (arg == "--capture=map_loop_soak")
            {
                _active = true;
                CampaignContext.SoakActive = true;
                CampaignContext.BotDuelTest = true;
                GD.Print("[DebugCapture] Soak loop test enabled: --capture=map_loop_soak");
            }
            if (arg.StartsWith("--soak-seed="))
            {
                var seedStr = arg.Substring("--soak-seed=".Length);
                if (ulong.TryParse(seedStr, out var seed))
                {
                    CampaignContext.SoakSeed = seed;
                    CampaignContext.DebugSeed = seed;
                    CampaignContext.SoakSeedStr = seedStr;
                    GD.Print($"[DebugCapture] Soak seed set: {seed}");
                }
            }
            if (arg.StartsWith("--soak-phase="))
            {
                var phase = arg.Substring("--soak-phase=".Length);
                CampaignContext.SoakPhaseLabel = phase;
                GD.Print($"[DebugCapture] Soak phase set: {phase}");
                if (phase == "save_quit")
                {
                    CampaignContext.SoakMaxNodes = 4;
                    GD.Print("[DebugCapture] Save/quit phase: will quit after 4 nodes");
                }
                else if (phase == "defeat_test")
                {
                    CampaignContext.SoakDefeatPhase = true;
                    CampaignContext.SoakStopAfterRetry = true;
                    GD.Print("[DebugCapture] Defeat test phase: will test defeat->retry flow");
                }
            }
            if (arg.StartsWith("--soak-force-region="))
            {
                var regionId = arg.Substring("--soak-force-region=".Length);
                CampaignContext.ForceRegionId = regionId;
                GD.Print($"[DebugCapture] Soak forced region: {regionId}");
            }
            if (arg.StartsWith("--tutorial="))
            {
                tutorialScriptId = arg.Substring("--tutorial=".Length);
            }
        }

        if (CampaignContext.SoakActive)
        {
            _active = true;
            CampaignContext.AutoCaptureScreenshot = true;
            GD.Print("[DebugCapture] Soak mode: AutoCaptureScreenshot=true, BotDuelTest=true");
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

        if (_active && !CampaignContext.SoakActive)
        {
            SetUpTestEncounter();

            // BOT-FIX-1: Wayfarer variant — enemy runs 30 Thorn Sprout tokens with
            // the tutorial flag on, exactly like campaign node 1.
            if (CampaignContext.BotDuelTutorialVariant && CampaignContext.CurrentEncounter != null)
            {
                var tokens = new List<string>();
                for (int i = 0; i < 30; i++) tokens.Add("tut_opponent_token");
                CampaignContext.CurrentEncounter.Deck = tokens;
                CampaignContext.CurrentEncounter.IsTutorial = true;
                GD.Print("[DebugCapture] BOT-FIX-1: encounter deck overridden to 30x tut_opponent_token, IsTutorial=true");
            }
        }

        if (reliquaryMode)
        {
            GD.Print("[DebugCapture] Reliquary capture mode — populating collection");
            SetUpReliquaryTest();
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

        // Register a test card with a long name to verify two-line auto-fit wrapping
        var longNameCard = new CardDef
        {
            Id = "test_long_name_wrapper",
            Name = "The Undying Root of the Fallow Reach",
            Set = "debug",
            Strata = Strata.VERDANT,
            Type = CardType.CREATURE,
            Rarity = Rarity.COMMON,
            Cost = 5,
            Attack = 4,
            Vigor = 6
        };
        CardRegistry.Register(longNameCard);
        // Insert 10 copies at front to guarantee the long-name card appears in the hand
        for (int i = 0; i < 10; i++)
            deck.Insert(0, "test_long_name_wrapper");
        GD.Print("[DebugCapture] Registered test_long_name_wrapper for two-line name wrap verification");

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
            FragmentReward = "ember:1",
            // TASK-DROPS-UI-1: Fixed drops for capture — rate 1.0 guarantees both drop
            Drops = new List<DropEntry>
            {
                new DropEntry { CardId = "vrd_c_root_warden", Rate = 1.0 },  // pre-seeded → +1
                new DropEntry { CardId = "emb_c_cinder_runner", Rate = 1.0 }, // not pre-seeded → NEW
                new DropEntry { CardId = "dwn_c_dawn_warder", Rate = 1.0 },   // not pre-seeded → NEW
            }
        };

        // TASK-DROPS-UI-1: Pre-seed collection so one drop shows "+1" (already owned)
        // and the others show "NEW" (first copy ever received)
        CampaignContext.Progression.Collection.Clear();
        CampaignContext.Progression.Collection["vrd_c_root_warden"] = 1;
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

        // Set HOLLOW filter active (index 4) so capture shows a non-ALL selected state
        CampaignContext.CaptureOverrideStrataIdx = 4;

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
        // IMPORTANT: Must be true so Main.cs navigates to the duel scene in capture mode
        CampaignContext.AutoCaptureScreenshot = true;
        CampaignContext.DebugSeed = 42;

        // Set _active = true to keep the main loop processing while the
        // tutorial runs headless (TutorialRunner drives the flow via timers).
        _active = true;

        GD.Print($"[DebugCapture] Tutorial mode set: {scriptId} (active=true)");
    }

    /// <summary>
    /// Reliquary capture mode: populate collection with 13 owned cards
    /// (1 marked NEW — not yet seen), leave the rest unowned.
    /// </summary>
    private void SetUpReliquaryTest()
    {
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

        // Own 13 cards spanning multiple strata
        var ownedIds = new List<string>
        {
            // VERDANT (4)
            "vrd_c_root_warden", "vrd_c_verdant_sproutling", "vrd_c_thornbark_defender", "vrd_r_bloomweaver",
            // EMBER (3)
            "emb_c_cinder_runner", "emb_c_ember_hound", "emb_u_wildfire_adept",
            // TIDE (2)
            "tid_c_tidal_scholar", "tid_c_deep_one",
            // HOLLOW (2) — one NEW (not seen)
            "hol_c_skeletal_reaver", "hol_c_gravewrit_thrall",
            // DAWN (2)
            "dwn_c_dawn_warder", "dwn_c_sunblade_recruit",
        };

        CampaignContext.Progression.Collection.Clear();
        foreach (var id in ownedIds)
            CampaignContext.Progression.Collection[id] = 1;

        // Mark all owned cards as seen EXCEPT the first HOLLOW card (hol_c_skeletal_reaver)
        // so it shows a NEW badge — this satisfies the acceptance criteria
        foreach (var id in ownedIds)
        {
            // Skip the first HOLLOW card to keep it as NEW
            if (id == "hol_c_skeletal_reaver")
                continue;
            CampaignContext.Progression.MarkCardSeen(id);
        }

        // Give the first owned card 2 copies to show "x2" owned count badge
        CampaignContext.Progression.Collection["vrd_c_root_warden"] = 2;

        // Set EMBER strata filter via CaptureOverrideStrataIdx (already set to 2 in the handler)
        // This ensures some owned AND unowned cards are visible in the default view

        GD.Print($"[DebugCapture] Reliquary test: {ownedIds.Count} owned cards, 1 NEW, {allCards.Count - ownedIds.Count} unowned");
    }

    /// <summary>
    /// Walk the scene tree and write a layout.json describing every visible Control.
    /// Called after each DebugCapture screenshot is saved.
    /// </summary>
    public static void WriteLayoutJson(Node root, string captureBaseName)
    {
        var projectDir = ProjectSettings.GlobalizePath("res://");
        var rootDir = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(projectDir));
        var captureDir = System.IO.Path.Combine(rootDir!, "artifacts", "captures");
        var path = System.IO.Path.Combine(captureDir, $"{captureBaseName}.layout.json");
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);

        var entries = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
        WalkControls(root, "", entries);

        var vp = root.GetViewport();
        var vpSize = vp != null ? vp.GetVisibleRect().Size : Vector2.Zero;
        var safeArea = DisplayServer.GetDisplaySafeArea();
        // BOARD-DEVICE-1: Override safe area in layout JSON when simulating Android insets
        if (CampaignContext.DebugSafeAreaMode)
        {
            float simulatedSafeBottom = vpSize.Y - 48f;
            safeArea = new Rect2I(0, 32, (int)vpSize.X, (int)(simulatedSafeBottom - 32f));
        }
        else
        {
            // On headless the safe area may be smaller than the viewport — use viewport bounds
            safeArea = new Rect2I(0, 0, (int)vpSize.X, (int)vpSize.Y);
        }

        var data = new System.Collections.Generic.Dictionary<string, object>
        {
            ["viewport_width"] = vpSize.X,
            ["viewport_height"] = vpSize.Y,
            ["safe_area_x"] = safeArea.Position.X,
            ["safe_area_y"] = safeArea.Position.Y,
            ["safe_area_w"] = safeArea.Size.X,
            ["safe_area_h"] = safeArea.Size.Y,
            ["controls"] = entries
        };

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        if (file != null)
        {
            file.StoreString(System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"[DebugCapture] Layout JSON written: {path}");
        }
        else
        {
            GD.PrintErr($"[DebugCapture] Failed to write layout JSON: {path}");
        }
    }

    private static void WalkControls(Node node, string parentPath, System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> entries)
    {
        var control = node as Control;
        if (control == null)
        {
            // Walk children even for non-Controls (e.g. Node2D, Panel)
            foreach (var child in node.GetChildren())
                WalkControls(child, parentPath, entries);
            return;
        }

        // Skip invisible controls
        if (!control.Visible)
        {
            foreach (var child in node.GetChildren())
                WalkControls(child, parentPath, entries);
            return;
        }

        string nodePath = string.IsNullOrEmpty(parentPath)
            ? control.Name
            : $"{parentPath}/{control.Name}";

        var rect = control.GetGlobalRect();
        var entry = new System.Collections.Generic.Dictionary<string, object>
        {
            ["path"] = nodePath,
            ["class"] = control.GetType().Name,
            ["x"] = (int)rect.Position.X,
            ["y"] = (int)rect.Position.Y,
            ["w"] = (int)rect.Size.X,
            ["h"] = (int)rect.Size.Y,
            ["mouse_filter"] = (int)control.MouseFilter
        };

        // For TextureRects, record if texture is non-null
        if (control is TextureRect texRect)
        {
            entry["texture_non_null"] = texRect.Texture != null;
        }

        // For Labels, record font_size from theme override
        if (control is Label label)
        {
            int fontSize = label.GetThemeFontSize("font_size");
            entry["font_size"] = fontSize;
        }

        entries.Add(entry);

        foreach (var child in node.GetChildren())
            WalkControls(child, nodePath, entries);
    }

    public override void _Process(double delta)
    {
        if (!_active || _captureDone) return;

        // Flow test: after overlay capture, stop processing — scene change to MapScene is handled
        // by the capture timer's deferred call. The MapScene capture hook takes over from there.
        if (CampaignContext.FlowTestAfterOverlay && CampaignContext.CaptureFlowTestMap)
        {
            _captureDone = true;
            return;
        }

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

    /// <summary>
    /// Walk the scene tree rooted at rootNode and write a .layout.json for ui_lint.py.
    /// Captures: every visible Control's node path, class, global rect, mouse_filter,
    /// and for TextureRects whether the texture is non-null. Also includes viewport size
    /// and display safe area.
    /// </summary>
    public static void DumpLayoutJSON(string captureName, Node rootNode)
    {
        try
        {
            var viewport = rootNode.GetViewport();
            var vpSize = viewport.GetVisibleRect().Size;
            var safeArea = DisplayServer.GetDisplaySafeArea();
            // BOARD-DEVICE-1: Override safe area in layout JSON when simulating Android insets
            if (CampaignContext.DebugSafeAreaMode)
            {
                float simulatedSafeBottom = vpSize.Y - 48f;
                safeArea = new Rect2I(0, 32, (int)vpSize.X, (int)(simulatedSafeBottom - 32f));
            }
            else
            {
                // On headless the safe area may be smaller than the viewport — use viewport bounds
                safeArea = new Rect2I(0, 0, (int)vpSize.X, (int)vpSize.Y);
            }
            var projectDir2 = ProjectSettings.GlobalizePath("res://");
            var rootDir2 = System.IO.Path.GetDirectoryName(System.IO.Path.TrimEndingDirectorySeparator(projectDir2));
            var captureDir = System.IO.Path.Combine(rootDir2!, "artifacts", "captures");
            System.IO.Directory.CreateDirectory(captureDir);
            var layoutPath = System.IO.Path.Combine(captureDir, $"{captureName}.layout.json");

            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"capture\": \"{captureName}\",\n");
            sb.Append($"  \"viewport\": {{\"width\": {vpSize.X:F0}, \"height\": {vpSize.Y:F0}}},\n");
            sb.Append($"  \"safe_area\": {{\"x\": {safeArea.Position.X:F0}, \"y\": {safeArea.Position.Y:F0}, \"w\": {safeArea.Size.X:F0}, \"h\": {safeArea.Size.Y:F0}}},\n");
            sb.Append("  \"controls\": [\n");

            var controls = new System.Collections.Generic.List<string>();
            DumpNodeRecursive(rootNode, "", controls);

            for (int i = 0; i < controls.Count; i++)
            {
                sb.Append(controls[i]);
                if (i < controls.Count - 1)
                    sb.Append(",");
                sb.Append("\n");
            }

            sb.Append("  ]\n");
            sb.Append("}\n");

            using (var writer = new System.IO.StreamWriter(layoutPath))
                writer.Write(sb.ToString());

            GD.Print($"[LAYOUT] {layoutPath} written ({controls.Count} controls)");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[LAYOUT] Failed to dump layout: {ex.Message}");
        }
    }

    private static void DumpNodeRecursive(Node node, string parentPath, System.Collections.Generic.List<string> controls)
    {
        var control = node as Control;
        if (control != null)
        {
            if (!control.Visible)
                return; // skip invisible controls — they don't affect visual layout

            var gp = control.GetScreenTransform().Origin;
            var sz = control.Size;
            var path = string.IsNullOrEmpty(parentPath) ? node.Name.ToString() : parentPath + "/" + node.Name.ToString();
            var cls = node.GetType().Name;
            var mf = control.MouseFilter.ToString();

            var entry = new System.Text.StringBuilder();
            entry.Append("    {");
            entry.Append($"\"path\": \"{EscapeJson(path)}\", ");
            entry.Append($"\"class\": \"{EscapeJson(cls)}\", ");
            entry.Append($"\"rect\": {{\"x\": {gp.X:F1}, \"y\": {gp.Y:F1}, \"w\": {sz.X:F1}, \"h\": {sz.Y:F1}}}, ");
            entry.Append($"\"mouse_filter\": \"{mf}\"");

            var tr = node as TextureRect;
            if (tr != null)
            {
                bool hasTex = tr.Texture != null;
                entry.Append($", \"has_texture\": {(hasTex ? "true" : "false")}");
            }

            entry.Append("}");
            controls.Add(entry.ToString());
        }

        foreach (Node child in node.GetChildren())
            DumpNodeRecursive(child, string.IsNullOrEmpty(parentPath) ? node.Name.ToString() : parentPath + "/" + node.Name.ToString(), controls);
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }
}