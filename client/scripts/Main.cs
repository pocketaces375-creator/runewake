using System.Collections.Generic;
using System.IO;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Title screen — entry point for the Runewake client.
/// Loads card packs, encounters, and save data on start.
/// </summary>
public partial class Main : Control
{
    private Button _startButton = default!;
    private Button _runeButton = default!;
    private Button _forgeButton = default!;
    private Label _statusLabel = default!;
    private bool _loading;

    public override void _Ready()
    {
        // Title label
        var title = new Label
        {
            Text = "RUNEWAKE",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0.6f
        };
        title.AddThemeFontSizeOverride("font_size", 64);
        AddChild(title);

        // Subtitle
        var subtitle = new Label
        {
            Text = "The Buried Age",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0.45f,
            AnchorBottom = 0.55f,
            AutoTranslate = false
        };
        subtitle.AddThemeFontSizeOverride("font_size", 24);
        subtitle.Modulate = new Color(0.7f, 0.7f, 0.8f);
        AddChild(subtitle);

        // Status label (loading feedback)
        _statusLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0.6f,
            AnchorBottom = 0.75f
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 16);
        _statusLabel.Modulate = new Color(0.5f, 0.5f, 0.6f);
        AddChild(_statusLabel);

        // Start Campaign button
        _startButton = new Button
        {
            Text = "Start Campaign",
            AnchorLeft = 0.35f,
            AnchorRight = 0.65f,
            AnchorTop = 0.75f,
            AnchorBottom = 0.85f,
            Disabled = true
        };
        _startButton.Pressed += OnStartCampaign;
        AddChild(_startButton);

        // Rune Page button
        var runeButton = new Button
        {
            Text = "Rune Page",
            AnchorLeft = 0.35f,
            AnchorRight = 0.65f,
            AnchorTop = 0.87f,
            AnchorBottom = 0.92f,
            Disabled = true
        };
        runeButton.Pressed += OnOpenRunePage;
        AddChild(runeButton);

        // Forge button
        var forgeButton = new Button
        {
            Text = "Rune Forge",
            AnchorLeft = 0.35f,
            AnchorRight = 0.65f,
            AnchorTop = 0.93f,
            AnchorBottom = 0.98f,
            Disabled = true
        };
        forgeButton.Pressed += OnOpenForge;
        AddChild(forgeButton);
        _forgeButton = forgeButton;

        // Begin loading
        Callable.From(LoadGameData).CallDeferred();

        // Store rune button reference for enabling after load
        _runeButton = runeButton;
    }

    private void LoadGameData()
    {
        _statusLabel.Text = "Loading cards...";

        // Load card packs into registry
        string contentDir = ProjectSettings.GlobalizePath("res://") + "../content/cards";
        var packs = new[] { "verdant.json", "ember.json", "tide.json", "hollow.json", "dawn.json" };
        foreach (var pack in packs)
        {
            string path = Path.Combine(contentDir, pack);
            var cards = CardLoader.LoadPack(path);
            CardRegistry.RegisterRange(cards);
        }

        _statusLabel.Text = "Loading encounters...";

        // Load encounter definitions
        CampaignContext.LoadEncounters();

        _statusLabel.Text = "Loading runes...";

        // Load rune definitions
        CampaignContext.LoadRunes();

        _statusLabel.Text = "Loading dig sites...";

        // Load dig site definitions
        CampaignContext.LoadDigSites();

        _statusLabel.Text = "Loading dig tools...";

        // Load dig tool definitions
        CampaignContext.LoadDigTools();

        _statusLabel.Text = "Loading relics...";

        // Load Lost Relic definitions
        CampaignContext.LoadLostRelics();

        _statusLabel.Text = "Loading save data...";

        // Initialize save manager
        CampaignContext.SaveManager.Initialize();

        // Build a default player deck from the full card pool (all cards × 1 copy)
        if (CampaignContext.Progression.Collection.Count > 0)
        {
            // Use the player's collection to build a deck
            var deck = new List<string>();
            foreach (var (cardId, count) in CampaignContext.Progression.Collection)
            {
                for (int i = 0; i < count && deck.Count < 30; i++)
                    deck.Add(cardId);
            }
            // Pad with known cards if collection is small
            while (deck.Count < 30)
                deck.Add("vrd_c_root_warden");
            CampaignContext.PlayerDeckIds = deck;
        }
        else
        {
            // First run — give a starter deck of the first 30 available cards
            var allCards = CardRegistry.GetAll();
            var deck = new List<string>();
            foreach (var card in allCards)
            {
                if (deck.Count >= 30) break;
                deck.Add(card.Id);
            }
            CampaignContext.PlayerDeckIds = deck;
            // Also grant the player 1 copy of each card for collection
            foreach (var card in allCards)
                CampaignContext.Progression.AddCard(card.Id);
            CampaignContext.SaveManager.Save();
        }

        _statusLabel.Text = "";
        _startButton.Disabled = false;
        _runeButton.Disabled = false;
        _forgeButton.Disabled = false;
    }

    private void OnStartCampaign()
    {
        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
    }

    private void OnOpenRunePage()
    {
        GetTree().ChangeSceneToFile("res://scenes/rune/RunePageScene.tscn");
    }

    private void OnOpenForge()
    {
        GetTree().ChangeSceneToFile("res://scenes/forge/ForgeScene.tscn");
    }
}