using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Rotating card shop — spend RuneDust to buy new cards for your collection.
/// Shows ~6 cards at a time, priced by rarity:
///   Common 5, Uncommon 15, Rare 40, Mythic (RELIC) 120
/// Rotates per in-game day (ShopRotationDay counter).
/// Reachable from the Reliquary.
/// </summary>
public partial class CardShopScene : Control
{
    // ── Layout constants ──
    private const int CardColumns = 3;
    private const int CardRows = 2;
    private const int TotalOffered = CardColumns * CardRows; // 6

    // ── Pricing ──
    private static readonly Dictionary<Rarity, int> CardPrices = new()
    {
        { Rarity.COMMON, 5 },
        { Rarity.UNCOMMON, 15 },
        { Rarity.RARE, 40 },
        { Rarity.RELIC, 120 },
    };

    // ── UI ──
    private Label _titleLabel = default!;
    private Label _runeDustLabel = default!;
    private Button _backButton = default!;
    private Button _refreshButton = default!;
    private Label _shortfallLabel = default!;
    private Godot.Timer _shortfallTimer = default!;
    private GridContainer _cardGrid = default!;
    private Label _dayLabel = default!;

    // ── State ──
    private List<CardDef> _offeredCards = new();
    private bool _captureTriggered = false;
    private static readonly Color InsufficientColor = new(0.9f, 0.3f, 0.2f);
    private static readonly Color PriceColor = new(0.6f, 0.5f, 0.9f);
    private static readonly Color AffordColor = new(0.5f, 0.8f, 0.5f);

    private ProgressionState? Progression => CampaignContext.Progression;

    public override void _Ready()
    {
        AnchorLeft = 0; AnchorRight = 1;
        AnchorTop = 0; AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;

        BuildUI();
        RefreshShop();

        if (CampaignContext.CaptureShopScreenshot)
        {
            GD.Print("[CardShopScene] Capture mode active — will capture on first _Process");
        }
    }

    public override void _Process(double delta)
    {
        if (_captureTriggered) return;
        if (!CampaignContext.CaptureShopScreenshot) return;
        _captureTriggered = true;

        GD.Print("[CardShopScene] _Process capture triggered");
        var suffix = CampaignContext.WideCaptureMode ? "_wide" : "";
        var img = GetViewport().GetTexture().GetImage();
        if (img != null)
        {
            string path = $"/home/fictive/runewake-lane4/artifacts/captures/shop_test{suffix}.png";
            img.SavePng(path);
            DebugCapture.WriteLayoutJson(this, $"shop_test{suffix}");
            DebugCapture.DumpLayoutJSON($"shop_test{suffix}", this);
            GD.Print($"[CardShopScene] Saved {path}");
        }
        else
        {
            GD.PrintErr("[CardShopScene] Failed to capture: GetImage() returned null");
        }
        GetTree().Quit();
    }

    private void BuildUI()
    {
        // ── Background ──
        var bg = new ColorRect
        {
            Color = Color.FromHtml("#0A0806"),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(bg);

        // ── Top bar ──
        var topBar = new ColorRect
        {
            Color = new Color(0.1f, 0.08f, 0.06f, 0.85f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.08f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(topBar);

        var barLine = new ColorRect
        {
            Color = new Color(0.6f, 0.5f, 0.25f, 0.25f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.08f, AnchorBottom = 0.083f,
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(barLine);

        // ── Back button ──
        _backButton = new Button
        {
            Text = "< Back",
            AnchorLeft = 0.01f, AnchorRight = 0.10f,
            AnchorTop = 0.005f, AnchorBottom = 0.075f
        };
        _backButton.AddThemeFontSizeOverride("font_size", 12);
        _backButton.AddThemeColorOverride("font_color", Color.FromHtml("#CFC4AE"));
        _backButton.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        _backButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/reliquary/ReliquaryScene.tscn");
        };
        AddChild(_backButton);

        // ── Title ──
        _titleLabel = new Label
        {
            Text = "CARD SHOP",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.2f, AnchorRight = 0.6f,
            AnchorTop = 0.005f, AnchorBottom = 0.075f,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 24);
        _titleLabel.AddThemeColorOverride("font_color", Color.FromHtml("#D4B84C"));
        AddChild(_titleLabel);

        // ── RuneDust balance (top-right) ──
        _runeDustLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.70f, AnchorRight = 0.98f,
            AnchorTop = 0.005f, AnchorBottom = 0.075f,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        _runeDustLabel.AddThemeFontSizeOverride("font_size", 14);
        _runeDustLabel.AddThemeColorOverride("font_color", PriceColor);
        AddChild(_runeDustLabel);

        // ── Rotation day label ──
        _dayLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 0.6f, AnchorRight = 0.70f,
            AnchorTop = 0.005f, AnchorBottom = 0.075f,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        _dayLabel.AddThemeFontSizeOverride("font_size", 11);
        _dayLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.25f, 0.7f));
        AddChild(_dayLabel);

        // ── Shortfall feedback (hidden) ──
        _shortfallLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.085f, AnchorBottom = 0.12f,
            Visible = false,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        _shortfallLabel.AddThemeFontSizeOverride("font_size", 14);
        _shortfallLabel.AddThemeColorOverride("font_color", InsufficientColor);
        AddChild(_shortfallLabel);

        _shortfallTimer = new Godot.Timer
        {
            OneShot = true,
            WaitTime = 2.5f,
            Autostart = false
        };
        _shortfallTimer.Timeout += () => _shortfallLabel.Visible = false;
        AddChild(_shortfallTimer);

        // ── Refresh button ──
        _refreshButton = new Button
        {
            Text = "⟳ Refresh",
            AnchorLeft = 0.85f, AnchorRight = 0.98f,
            AnchorTop = 0.09f, AnchorBottom = 0.14f,
        };
        _refreshButton.AddThemeFontSizeOverride("font_size", 11);
        _refreshButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            RefreshShop();
        };
        AddChild(_refreshButton);

        // ── Card grid ──
        _cardGrid = new GridContainer
        {
            Columns = CardColumns,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.18f, AnchorBottom = 0.9f
        };
        _cardGrid.AddThemeConstantOverride("separation", 12);
        AddChild(_cardGrid);
    }

    private void RefreshShop()
    {
        if (Progression == null) return;

        // Increment rotation
        Progression.ShopRotationDay++;
        // Build offered cards from all registered cards
        BuildOffering();

        // Update labels
        int runeDust = Progression.RuneDust;
        _runeDustLabel.Text = $"Runes: {runeDust}";
        _dayLabel.Text = $"Day {Progression.ShopRotationDay}";

        // Rebuild grid
        RebuildGrid();
    }

    private void BuildOffering()
    {
        _offeredCards.Clear();
        int rotation = Progression?.ShopRotationDay ?? 0;

        var allCards = CardRegistry.GetAll();
        if (allCards.Count == 0) return;

        // Separate cards by rarity
        var byRarity = new Dictionary<Rarity, List<CardDef>>();
        foreach (var card in allCards)
        {
            if (!byRarity.ContainsKey(card.Rarity))
                byRarity[card.Rarity] = new List<CardDef>();
            byRarity[card.Rarity].Add(card);
        }

        // Use a seeded RNG from rotation day for determinism
        var rng = new System.Random(rotation);

        // Pick: 2 Common, 2 Uncommon, 1 Rare, 1 Mythic/RELIC
        var targets = new (Rarity rarity, int count)[]
        {
            (Rarity.COMMON, 2),
            (Rarity.UNCOMMON, 2),
            (Rarity.RARE, 1),
            (Rarity.RELIC, 1),
        };

        foreach (var (rarity, count) in targets)
        {
            var pool = byRarity.GetValueOrDefault(rarity, new List<CardDef>());
            if (pool.Count == 0) continue;

            var picked = pool.OrderBy(_ => rng.Next()).Take(count).ToList();
            _offeredCards.AddRange(picked);
        }

        // Shuffle the offering so rarities are mixed
        _offeredCards = _offeredCards.OrderBy(_ => rng.Next()).ToList();

        GD.Print($"[CardShopScene] Offering {_offeredCards.Count} cards (rotation {rotation})");
    }

    private void RebuildGrid()
    {
        foreach (var child in _cardGrid.GetChildren())
            child.QueueFree();

        foreach (var card in _offeredCards)
        {
            int price = CardPrices.GetValueOrDefault(card.Rarity, 0);
            AddCardSlot(card, price);
        }
    }

    private void AddCardSlot(CardDef card, int price)
    {
        var cardPanel = new ColorRect
        {
            Color = new Color(0.1f, 0.09f, 0.08f, 0.9f),
            CustomMinimumSize = new Vector2(240, 320),
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            MouseFilter = MouseFilterEnum.Stop
        };
        _cardGrid.AddChild(cardPanel);

        var vbox = new VBoxContainer
        {
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1,
        };
        cardPanel.AddChild(vbox);

        // Card name
        var nameLabel = new Label
        {
            Text = card.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = (Control.SizeFlags)3
        };
        nameLabel.AddThemeFontSizeOverride("font_size", 14);
        nameLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.72f, 0.35f));
        vbox.AddChild(nameLabel);

        // Rarity + cost badge
        var infoLabel = new Label
        {
            Text = $"{card.Rarity}  |  ATK {card.Attack}  VIG {card.Vigor}  Cost {card.Cost}",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = (Control.SizeFlags)3
        };
        infoLabel.AddThemeFontSizeOverride("font_size", 11);
        infoLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
        vbox.AddChild(infoLabel);

        // Spacer
        vbox.AddChild(new Control { SizeFlagsVertical = (Control.SizeFlags)3 });

        // Price + Buy button
        int currentDust = Progression?.RuneDust ?? 0;
        bool canAfford = currentDust >= price;

        var buyBtn = new Button
        {
            Text = canAfford ? $"Buy — {price} R" : $"Buy — {price} R (need {price - currentDust} more)",
            SizeFlagsHorizontal = (Control.SizeFlags)3,
            CustomMinimumSize = new Vector2(0, 36),
            Disabled = !canAfford
        };
        buyBtn.AddThemeFontSizeOverride("font_size", 12);
        if (canAfford)
            buyBtn.Modulate = AffordColor;
        else
            buyBtn.Modulate = new Color(0.3f, 0.3f, 0.3f);

        var captured = card;
        buyBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            BuyCard(captured, price);
        };
        vbox.AddChild(buyBtn);
    }

    private void BuyCard(CardDef card, int price)
    {
        if (Progression == null) return;

        if (Progression.SpendRuneDust(price, out var shortfall))
        {
            // Add card to collection
            Progression.AddCard(card.Id);

            // Refresh UI
            _runeDustLabel.Text = $"Runes: {Progression.RuneDust}";
            RebuildGrid();

            GD.Print($"[CardShopScene] Bought {card.Id} for {price} Runes");
        }
        else
        {
            // Show shortfall
            string msg = shortfall > 0
                ? $"Need {shortfall} more {(shortfall == 1 ? "Rune" : "Runes")} for {card.Name}."
                : $"Cannot afford {card.Name}.";
            ShowShortfall(msg);
            GD.Print($"[CardShopScene] Insufficient Runes for {card.Id}: need {price}, have {Progression.RuneDust}");
        }

        // Persist
        CampaignContext.SaveManager.Save();
    }

    private void ShowShortfall(string message)
    {
        _shortfallLabel.Text = message;
        _shortfallLabel.Visible = true;
        _shortfallTimer.Start();
    }

    // ════════════════════════════════════════════════════════════
    // CAPTURE HOOK
    // ════════════════════════════════════════════════════════════

    /// <summary>
    /// Set up shop state for debug capture. Called from DebugCapture when --capture=shop_test.
    /// </summary>
    public static void SetUpShopTest()
    {
        var progression = CampaignContext.Progression;
        // Give some RuneDust so the player can afford some cards but not all
        progression.RuneDust = 100;
        progression.ShopRotationDay = 7; // fixed rotation for deterministic capture
        GD.Print("[CardShopScene] SetUpShopTest: RuneDust=100, RotationDay=7");
    }
}