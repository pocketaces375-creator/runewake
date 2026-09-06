using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// Duel Arena screen — pick any saved deck and fight an AI-piloted opponent
/// drawn from the pool (all class starters, every encounter deck, and the
/// Region 2 decks), seeded, with a win/loss ledger in the save and a
/// RuneDust reward per win (10 normal, 25 against a Warden deck).
/// </summary>
public partial class ArenaScene : Control
{
    // ── Top bar ──
    private Button _backButton = default!;
    private Label _titleLabel = default!;
    private Label _recordLabel = default!;

    // ── Opponent panel ──
    private Label _opponentTitle = default!;
    private Label _opponentName = default!;
    private Label _opponentLabel = default!;
    private Button _rerollButton = default!;

    // ── Your decks ──
    private VBoxContainer _deckListContainer = default!;

    // ── Fight / back ──
    private Button _fightButton = default!;

    // ── State ──
    private readonly List<CampaignContext.DeckProfile> _playerDecks = new();
    private CampaignContext.DeckProfile? _selectedDeck;
    private EncounterDef _currentOpponent = default!;
    private SeededRng _arenaRng = default!;
    private readonly List<EncounterDef> _opponentPool = new();
    private bool _duelInProgress;

    // ── Button style helpers ──
    private static StyleBoxFlat MakeBtnNormal(Color bg, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 10, ContentMarginTop = 4,
            ContentMarginRight = 10, ContentMarginBottom = 4
        };
    }

    private static StyleBoxFlat MakeBtnHover(Color bg, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 10, ContentMarginTop = 4,
            ContentMarginRight = 10, ContentMarginBottom = 4
        };
    }

    private void StyleButton(Button btn, float fontSize = 12, bool goldText = true)
    {
        btn.AddThemeFontSizeOverride("font_size", (int)fontSize);
        var fc = goldText ? new Color(0.95f, 0.88f, 0.65f, 1f) : new Color(0.8f, 0.75f, 0.6f, 1f);
        var fd = new Color(0.4f, 0.35f, 0.25f, 0.5f);
        btn.AddThemeColorOverride("font_color", fc);
        btn.AddThemeColorOverride("font_disabled_color", fd);
        btn.AddThemeStyleboxOverride("normal", MakeBtnNormal(
            new Color(0.2f, 0.15f, 0.1f, 1f), new Color(0.7f, 0.6f, 0.3f, 1f)));
        btn.AddThemeStyleboxOverride("hover", MakeBtnHover(
            new Color(0.3f, 0.22f, 0.14f, 1f), new Color(0.9f, 0.78f, 0.45f, 1f)));
        btn.AddThemeStyleboxOverride("pressed", MakeBtnHover(
            new Color(0.3f, 0.22f, 0.14f, 1f), new Color(0.9f, 0.78f, 0.45f, 1f)));
    }

    public override void _Ready()
    {
        CampaignContext.IsArenaDuel = false;

        // ── Build background ──
        var backdrop = new ColorRect
        {
            Color = new Color(0.07f, 0.055f, 0.04f, 1f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(backdrop);

        // ── Load player decks ──
        CampaignContext.LoadDeckLibrary();
        _playerDecks.Clear();
        foreach (var d in CampaignContext.DeckLibrary)
        {
            if (d.Cards != null && d.Cards.Count >= DeckRules.MinSize)
                _playerDecks.Add(d);
        }

        // ── Build opponent pool ──
        BuildOpponentPool();

        // ── Seed the arena RNG ──
        _arenaRng = new SeededRng(
            CampaignContext.ArenaSeed > 0
                ? CampaignContext.ArenaSeed
                : (ulong)(GD.Randi() & 0x7FFFFFFF));
        CampaignContext.ArenaSeed = _arenaRng.NextU64();

        // ── Top bar ──
        BuildTopBar();

        // ── Opponent panel (left) ──
        BuildOpponentPanel();

        // ── Deck list (center-right) ──
        BuildDeckList();

        // ── Fight button (bottom) ──
        BuildFightButton();

        // ── Pick first opponent ──
        RerollOpponent();
    }

    // ════════════════════════════════════════════════════════════════
    // OPPONENT POOL
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Build the opponent pool from all class starters, every encounter
    /// deck, and the Region 2 decks specifically.
    /// </summary>
    private void BuildOpponentPool()
    {
        _opponentPool.Clear();

        // 1. Class starters — turn each starter deck into an EncounterDef
        CampaignContext.LoadStarterDecks();
        foreach (var (cid, starter) in CampaignContext.StarterDeckIndex)
        {
            string className = char.ToUpper(cid[0]) + cid.Substring(1);
            _opponentPool.Add(new EncounterDef
            {
                Id = $"arena_starter_{cid}",
                Name = $"{starter.DeckName} ({className})",
                Deck = new List<string>(starter.Cards),
            });
        }

        // 2. Every encounter deck from all regions
        foreach (var (encId, enc) in CampaignContext.EncounterIndex)
        {
            if (enc.Deck == null || enc.Deck.Count < DeckRules.MinSize) continue;
            // Skip tutorial encounters — they have tutor tokens
            if (enc.IsTutorial) continue;

            bool isWarden = encId.Contains("warden") || encId.Contains("boss")
                || enc.Modifier == "ELITE" || enc.Modifier == "WARDEN"
                || !string.IsNullOrEmpty(enc.OpeningRule);

            _opponentPool.Add(new EncounterDef
            {
                Id = $"arena_{encId}",
                Name = enc.Name,
                Deck = new List<string>(enc.Deck),
                OpeningRule = enc.OpeningRule,
                Modifier = enc.Modifier,
            });
        }

        GD.Print($"[ArenaScene] Opponent pool: {_opponentPool.Count} opponents");
    }

    // ════════════════════════════════════════════════════════════════
    // UI BUILDERS
    // ════════════════════════════════════════════════════════════════

    private void BuildTopBar()
    {
        var topBar = new ColorRect
        {
            Color = new Color(0.1f, 0.08f, 0.06f, 0.85f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.055f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(topBar);

        var barLine = new ColorRect
        {
            Color = new Color(0.6f, 0.5f, 0.25f, 0.25f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.055f, AnchorBottom = 0.057f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(barLine);

        // Back button
        _backButton = new Button
        {
            Text = "< Title",
            AnchorLeft = 0.01f, AnchorRight = 0.12f,
            AnchorTop = 0.002f, AnchorBottom = 0.053f
        };
        StyleButton(_backButton, 11, goldText: true);
        _backButton.Pressed += () => {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        };
        AddChild(_backButton);

        // Title
        _titleLabel = new Label
        {
            Text = "DUEL ARENA",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.25f, AnchorRight = 0.75f,
            AnchorTop = 0.002f, AnchorBottom = 0.053f
        };
        ThemeTokens.ApplyHeaderFont(_titleLabel, ThemeTokens.FontSecondary);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.65f, 1f));
        AddChild(_titleLabel);

        // Win/loss record
        UpdateRecordLabel();
        _recordLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.78f, AnchorRight = 0.98f,
            AnchorTop = 0.002f, AnchorBottom = 0.053f
        };
        _recordLabel.AddThemeFontSizeOverride("font_size", 13);
        _recordLabel.Modulate = new Color(0.85f, 0.72f, 0.35f, 0.8f);
        AddChild(_recordLabel);
    }

    private void UpdateRecordLabel()
    {
        var prog = CampaignContext.Progression;
        _recordLabel.Text = $"W: {prog.ArenaWins}  L: {prog.ArenaLosses}";
    }

    private void BuildOpponentPanel()
    {
        // Opponent panel container — left side
        var panel = new PanelContainer
        {
            AnchorLeft = 0.02f, AnchorRight = 0.35f,
            AnchorTop = 0.07f, AnchorBottom = 0.85f,
        };
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.10f, 0.07f, 0.9f),
            BorderColor = new Color(0.5f, 0.4f, 0.2f, 0.5f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginTop = 8,
            ContentMarginRight = 12, ContentMarginBottom = 8
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill
        };
        panel.AddChild(vbox);

        // "Opponent" header
        _opponentTitle = new Label
        {
            Text = "OPPONENT",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        ThemeTokens.ApplyHeaderFont(_opponentTitle, ThemeTokens.FontSecondary);
        _opponentTitle.AddThemeColorOverride("font_color", new Color(0.9f, 0.82f, 0.55f, 1f));
        vbox.AddChild(_opponentTitle);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // Opponent name
        _opponentName = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _opponentName.AddThemeFontSizeOverride("font_size", 18);
        _opponentName.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.75f, 1f));
        vbox.AddChild(_opponentName);

        // Warden badge
        _opponentLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        _opponentLabel.AddThemeFontSizeOverride("font_size", 12);
        vbox.AddChild(_opponentLabel);

        vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

        // Reward info
        var rewardLabel = new Label
        {
            Text = "Reward on win: 10 RuneDust",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        rewardLabel.AddThemeFontSizeOverride("font_size", 12);
        rewardLabel.Modulate = new Color(0.7f, 0.65f, 0.5f, 0.8f);
        vbox.AddChild(rewardLabel);

        // Warden bonus
        var wardenBonusLabel = new Label
        {
            Text = "Warden bonus: +15 RuneDust",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        wardenBonusLabel.AddThemeFontSizeOverride("font_size", 11);
        wardenBonusLabel.Modulate = new Color(0.8f, 0.5f, 0.25f, 0.7f);
        vbox.AddChild(wardenBonusLabel);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });

        // Reroll button
        _rerollButton = new Button
        {
            Text = "New Opponent",
            SizeFlagsHorizontal = Control.SizeFlags.Fill | Control.SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(0, 36)
        };
        StyleButton(_rerollButton, 12, goldText: true);
        _rerollButton.Pressed += OnRerollPressed;
        vbox.AddChild(_rerollButton);
    }

    private void BuildDeckList()
    {
        // Deck list container — center-right, scrollable
        var deckSection = new PanelContainer
        {
            AnchorLeft = 0.37f, AnchorRight = 0.98f,
            AnchorTop = 0.07f, AnchorBottom = 0.60f,
        };
        var deckSectionStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.08f, 0.06f, 0.8f),
            BorderColor = new Color(0.4f, 0.35f, 0.15f, 0.4f),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8, ContentMarginTop = 4,
            ContentMarginRight = 8, ContentMarginBottom = 4
        };
        deckSection.AddThemeStyleboxOverride("panel", deckSectionStyle);
        AddChild(deckSection);

        var outerVbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill
        };
        deckSection.AddChild(outerVbox);

        var headerLabel = new Label
        {
            Text = "YOUR DECKS",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        ThemeTokens.ApplyHeaderFont(headerLabel, ThemeTokens.FontSecondary);
        headerLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.82f, 0.55f, 1f));
        outerVbox.AddChild(headerLabel);

        var scrollContainer = new ScrollContainer
        {
            SizeFlagsVertical = Control.SizeFlags.Expand | Control.SizeFlags.Fill,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        outerVbox.AddChild(scrollContainer);

        _deckListContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill
        };
        scrollContainer.AddChild(_deckListContainer);

        // RuneDust display below deck list
        var runeDustLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
        };
        runeDustLabel.AddThemeFontSizeOverride("font_size", 12);
        runeDustLabel.Modulate = new Color(0.7f, 0.6f, 0.3f, 0.8f);
        outerVbox.AddChild(runeDustLabel);

        // Populate deck list
        RefreshDeckList();
    }

    private void RefreshDeckList()
    {
        // Clear existing children
        foreach (var child in _deckListContainer.GetChildren())
        {
            _deckListContainer.RemoveChild(child);
            child.QueueFree();
        }

        if (_playerDecks.Count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No saved decks found.\nCreate one in the Deck Builder.",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = Control.SizeFlags.Fill,
                SizeFlagsVertical = Control.SizeFlags.Expand,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            emptyLabel.AddThemeFontSizeOverride("font_size", 14);
            emptyLabel.Modulate = new Color(0.5f, 0.45f, 0.35f, 0.7f);
            _deckListContainer.AddChild(emptyLabel);
            return;
        }

        foreach (var deck in _playerDecks)
        {
            bool isSelected = _selectedDeck != null && _selectedDeck.DeckId == deck.DeckId;

            var hbox = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.Fill,
                CustomMinimumSize = new Vector2(0, 32)
            };

            // Color indicator for selected deck
            var indicator = new ColorRect
            {
                Color = isSelected
                    ? new Color(0.9f, 0.78f, 0.35f, 1f)
                    : new Color(0.2f, 0.18f, 0.14f, 1f),
                CustomMinimumSize = new Vector2(4, 0),
                SizeFlagsVertical = Control.SizeFlags.Fill,
                SizeFlagsHorizontal = 0
            };
            hbox.AddChild(indicator);

            // Deck name button
            var nameBtn = new Button
            {
                Text = deck.Name,
                SizeFlagsHorizontal = Control.SizeFlags.Expand | Control.SizeFlags.Fill,
                SizeFlagsVertical = Control.SizeFlags.Fill,
            };
            string capturedDeckId = deck.DeckId;
            var capturedDeck = deck;
            nameBtn.Pressed += () => {
                _selectedDeck = capturedDeck;
                RefreshDeckList();
                UpdateFightButton();
            };
            nameBtn.AddThemeFontSizeOverride("font_size", 12);
            nameBtn.AddThemeColorOverride("font_color",
                isSelected ? new Color(0.95f, 0.88f, 0.65f, 1f) : new Color(0.8f, 0.75f, 0.6f, 0.9f));
            nameBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = isSelected ? new Color(0.25f, 0.2f, 0.12f, 1f) : new Color(0.15f, 0.12f, 0.08f, 1f),
                BorderColor = isSelected ? new Color(0.7f, 0.6f, 0.3f, 0.6f) : new Color(0.3f, 0.25f, 0.1f, 0.3f),
                BorderWidthLeft = 0, BorderWidthTop = 0,
                BorderWidthRight = 0, BorderWidthBottom = 1,
                ContentMarginLeft = 6, ContentMarginTop = 2,
                ContentMarginRight = 6, ContentMarginBottom = 2
            });
            // Card count
            var countLabel = new Label
            {
                Text = $"{deck.Cards?.Count ?? 0}",
                VerticalAlignment = VerticalAlignment.Center,
                CustomMinimumSize = new Vector2(36, 0)
            };
            countLabel.AddThemeFontSizeOverride("font_size", 10);
            countLabel.Modulate = new Color(0.6f, 0.55f, 0.4f, 0.7f);
            hbox.AddChild(countLabel);

            _deckListContainer.AddChild(hbox);
        }
    }

    private void BuildFightButton()
    {
        var bottomBar = new ColorRect
        {
            Color = new Color(0.1f, 0.08f, 0.06f, 0.85f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.92f, AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(bottomBar);

        _fightButton = new Button
        {
            Text = "FIGHT!",
            AnchorLeft = 0.35f, AnchorRight = 0.65f,
            AnchorTop = 0.935f, AnchorBottom = 0.985f,
            Disabled = true
        };
        _fightButton.AddThemeFontSizeOverride("font_size", ThemeTokens.FontButtonPrimary);
        _fightButton.AddThemeColorOverride("font_color", new Color(0.95f, 0.88f, 0.65f, 1f));
        _fightButton.AddThemeColorOverride("font_disabled_color", new Color(0.4f, 0.35f, 0.25f, 0.5f));
        var fightNormal = new StyleBoxFlat
        {
            BgColor = new Color(0.3f, 0.15f, 0.05f, 1f),
            BorderColor = new Color(0.7f, 0.4f, 0.1f, 1f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        };
        _fightButton.AddThemeStyleboxOverride("normal", fightNormal);
        _fightButton.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = new Color(0.4f, 0.2f, 0.08f, 1f),
            BorderColor = new Color(0.9f, 0.5f, 0.15f, 1f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        });
        _fightButton.Pressed += OnFightPressed;
        AddChild(_fightButton);
    }

    private void UpdateFightButton()
    {
        _fightButton.Disabled = _selectedDeck == null || _currentOpponent == null;
        _fightButton.Text = _selectedDeck != null ? "FIGHT!" : "Select a deck above";
    }

    // ════════════════════════════════════════════════════════════════
    // OPPONENT SELECTION
    // ════════════════════════════════════════════════════════════════

    private void RerollOpponent()
    {
        if (_opponentPool.Count == 0) return;

        int idx = (int)(_arenaRng.NextU64() % (ulong)_opponentPool.Count);
        _currentOpponent = _opponentPool[idx];

        bool isWarden = _currentOpponent.Id.Contains("warden") || _currentOpponent.Id.Contains("boss")
            || !string.IsNullOrEmpty(_currentOpponent.OpeningRule)
            || _currentOpponent.Modifier == "ELITE" || _currentOpponent.Modifier == "WARDEN";

        // Update UI
        _opponentName.Text = _currentOpponent.Name;

        var deckSize = _currentOpponent.Deck.Count;
        _opponentLabel.Text = isWarden
            ? "WARDEN — 25 RuneDust reward"
            : $"{deckSize} cards — 10 RuneDust reward";
        _opponentLabel.Modulate = isWarden
            ? new Color(0.9f, 0.55f, 0.2f, 1f)
            : new Color(0.7f, 0.65f, 0.5f, 0.8f);

        UpdateFightButton();
    }

    // ════════════════════════════════════════════════════════════════
    // FIGHT
    // ════════════════════════════════════════════════════════════════

    private void OnRerollPressed()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        RerollOpponent();
    }

    private void OnFightPressed()
    {
        if (_selectedDeck == null || _currentOpponent == null) return;

        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        // Set the player's deck from the selected saved deck
        CampaignContext.PlayerDeckIds = new List<string>(_selectedDeck.Cards);

        // Build an encounter def for the arena opponent
        bool isWarden = _currentOpponent.Id.Contains("warden") || _currentOpponent.Id.Contains("boss")
            || !string.IsNullOrEmpty(_currentOpponent.OpeningRule)
            || _currentOpponent.Modifier == "ELITE" || _currentOpponent.Modifier == "WARDEN";

        var arenaEnc = new EncounterDef
        {
            Id = _currentOpponent.Id,
            Name = _currentOpponent.Name,
            Deck = new List<string>(_currentOpponent.Deck),
        };

        CampaignContext.ArenaEncounter = arenaEnc;
        CampaignContext.CurrentEncounter = arenaEnc;
        CampaignContext.IsArenaDuel = true;
        CampaignContext.ArenaOpponentName = _currentOpponent.Name;
        CampaignContext.IsWardenOpponent = isWarden;
        CampaignContext.CurrentNodeId = "arena_duel";

        // Clear campaign-style encounter rewards (we don't want those for arena)
        // The DuelScene OnGameOver will detect IsArenaDuel and handle RuneDust instead.

        GD.Print($"[ArenaScene] Starting arena duel: {_selectedDeck.Name} vs {_currentOpponent.Name} (warden={isWarden})");
        GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
    }

    /// <summary>
    /// Static entry: after an arena duel ends, navigate back here.
    /// Called from DuelScene when IsArenaDuel is true.
    /// </summary>
    public static void ReturnFromDuel()
    {
        CampaignContext.IsArenaDuel = false;
        CampaignContext.ArenaEncounter = null;
        // Reload scene will re-build with fresh stats
    }
}