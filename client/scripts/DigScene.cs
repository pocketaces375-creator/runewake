using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Dig site interaction scene — grid of soil tiles, tap to strike, reveal rewards.
/// Called from MapScene when the player selects a DIG node.
/// </summary>
public partial class DigScene : Control
{
    private DigSiteDef _siteDef = default!;
    private DigState _digState = default!;

    // Layout
    private const float TileSize = 72f;
    private const float TileGap = 8f;
    private static readonly Color SoilColor = new(0.35f, 0.25f, 0.15f);
    private static readonly Color RevealColor = new(0.65f, 0.55f, 0.35f);
    private static readonly Color ShardColor = new(0.95f, 0.85f, 0.2f);
    private static readonly Color FragmentColor = new(0.4f, 0.8f, 0.4f);
    private static readonly Color CodexColor = new(0.5f, 0.5f, 0.9f);
    private static readonly Color RelicColor = new(0.9f, 0.5f, 0.3f);
    private static readonly Color EmptyColor = new(0.5f, 0.45f, 0.35f);

    // UI controls
    private Label _titleLabel = default!;
    private Label _strikesLabel = default!;
    private Label _descLabel = default!;
    private Control _gridContainer = default!;
    private Label _resultLabel = default!;
    private Button _backButton = default!;
    private readonly List<TileButton> _tileButtons = new();
    private Button? _collectButton;

    public override void _Ready()
    {
        // Load the dig site from campaign context
        var siteId = CampaignContext.CurrentDigSiteId;
        if (siteId == null || !CampaignContext.DigSiteIndex.TryGetValue(siteId, out DigSiteDef? digSiteDef))
        {
            GD.PrintErr($"[DigScene] Unknown dig site: {siteId}");
            GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
            return;
        }
        _siteDef = digSiteDef!;

        _digState = DigState.FromDef(_siteDef);
        BuildUI();

        // Capture hook for --capture=dig_test[_wide]
        if (CampaignContext.AutoCaptureScreenshot && CampaignContext.CaptureDigScreenshot)
        {
            var timer = GetTree().CreateTimer(0.5f);
            timer.Timeout += () =>
            {
                var image = GetViewport().GetTexture().GetImage();
                if (image != null)
                {
                    string path = CampaignContext.WideCaptureMode
                        ? "/home/fictive/runewake-lane4/artifacts/captures/dig_test_wide.png"
                        : "/home/fictive/runewake-lane4/artifacts/captures/dig_test.png";
                    image.SavePng(path);
                    string baseName = CampaignContext.WideCaptureMode ? "dig_test_wide" : "dig_test";
                    DebugCapture.WriteLayoutJson(this, baseName);
                    GD.Print($"[DigScene] Captured to {path}");

                    // TASK-UI-LINT-1: Dump layout JSON
                    string digBasename = CampaignContext.WideCaptureMode ? "dig_test_wide" : "dig_test";
                    DebugCapture.DumpLayoutJSON(digBasename, this);
                }
                GetTree().Quit(0);
            };
        }
    }

    private void BuildUI()
    {
        // Background
        Color bgColor = new(0.15f, 0.12f, 0.08f);
        var bg = new ColorRect
        {
            Color = bgColor,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f
        };
        AddChild(bg);

        // Title
        _titleLabel = new Label
        {
            Text = _siteDef.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.08f
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 28);
        _titleLabel.Modulate = new Color(0.9f, 0.85f, 0.7f);
        AddChild(_titleLabel);

        // Description
        _descLabel = new Label
        {
            Text = _siteDef.Description ?? "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.08f, AnchorBottom = 0.16f,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _descLabel.AddThemeFontSizeOverride("font_size", 14);
        _descLabel.Modulate = new Color(0.6f, 0.55f, 0.4f);
        AddChild(_descLabel);

        // Stats row
        _strikesLabel = new Label
        {
            Text = $"Strikes: {_digState.StrikesRemaining}",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.16f, AnchorBottom = 0.22f
        };
        _strikesLabel.AddThemeFontSizeOverride("font_size", 18);
        _strikesLabel.Modulate = new Color(0.9f, 0.7f, 0.4f);
        AddChild(_strikesLabel);

        // Grid container
        _gridContainer = new Control
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0.22f, AnchorBottom = 0.75f
        };
        AddChild(_gridContainer);

        // Build tile grid
        float totalWidth = _siteDef.Cols * TileSize + (_siteDef.Cols - 1) * TileGap;
        float totalHeight = _siteDef.Rows * TileSize + (_siteDef.Rows - 1) * TileGap;
        float startX = (GetViewportRect().Size.X - totalWidth) / 2f;
        float startY = (GetViewportRect().Size.Y * 0.48f) - (totalHeight / 2f);

        for (int row = 0; row < _siteDef.Rows; row++)
        {
            for (int col = 0; col < _siteDef.Cols; col++)
            {
                int index = row * _siteDef.Cols + col;
                var tileBtn = new TileButton
                {
                    TileIndex = index,
                    Size = new Vector2(TileSize, TileSize),
                    Position = new Vector2(startX + col * (TileSize + TileGap),
                        startY + row * (TileSize + TileGap)),
                    TileColor = SoilColor,
                    Modulate = new Color(0.6f, 0.55f, 0.5f)
                };
                tileBtn.Pressed += () => OnTilePressed(tileBtn);
                _gridContainer.AddChild(tileBtn);
                _tileButtons.Add(tileBtn);
            }
        }

        // Result label
        _resultLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.75f, AnchorBottom = 0.85f,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _resultLabel.AddThemeFontSizeOverride("font_size", 16);
        _resultLabel.Modulate = new Color(0.9f, 0.85f, 0.8f);
        AddChild(_resultLabel);

        // Back button
        _backButton = new Button
        {
            Text = "Back to Map",
            AnchorLeft = 0.1f, AnchorRight = 0.45f,
            AnchorTop = 0.88f, AnchorBottom = 0.96f
        };
        _backButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            OnBackPressed();
        };
        AddChild(_backButton);

        // Collect button (hidden until complete)
        _collectButton = new Button
        {
            Text = "Collect Rewards",
            AnchorLeft = 0.55f, AnchorRight = 0.9f,
            AnchorTop = 0.88f, AnchorBottom = 0.96f,
            Visible = false
        };
        _collectButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            OnCollectPressed();
        };
        AddChild(_collectButton);
    }

    private void OnTilePressed(TileButton tileBtn)
    {
        if (_digState.IsComplete) return;

        var reward = _digState.ApplyStrike(tileBtn.TileIndex, _siteDef);
        if (reward == null) return;

        // Update tile visual
        tileBtn.TileColor = GetRewardColor(reward.Type);
        tileBtn.Modulate = new Color(0.85f, 0.85f, 0.9f);
        tileBtn.Disabled = true;

        // Draw reward icon
        string iconText = GetRewardIcon(reward.Type, reward.Value);
        tileBtn.Text = iconText;
        tileBtn.AddThemeFontSizeOverride("font_size", 20);

        // Update strikes display
        _strikesLabel.Text = $"Strikes: {_digState.StrikesRemaining}";

        // Show result
        string rewardLabel = GetRewardLabel(reward);
        _resultLabel.Text = rewardLabel;

        // Check completion
        if (_digState.IsComplete)
        {
            if (_digState.HeadlineClaimed && _collectButton != null)
            {
                string headlineLabel = GetHeadlineLabel(_siteDef);
                _resultLabel.Text += $"\n\n{headlineLabel}";
            }
            ShowComplete();
        }
    }

    private void ShowComplete()
    {
        if (_collectButton != null)
            _collectButton.Visible = true;

        // Disable all unrevealed tiles
        foreach (var btn in _tileButtons)
        {
            if (!btn.Disabled)
            {
                btn.Disabled = true;
                btn.Modulate = new Color(0.3f, 0.28f, 0.25f);
            }
        }
    }

    private void OnCollectPressed()
    {
        // Apply rewards to progression
        var prog = CampaignContext.Progression;
        foreach (var reward in _digState.RewardsEarned)
        {
            switch (reward.Type)
            {
                case DigRewardType.SHARD:
                    if (int.TryParse(reward.Value, out int shards))
                        prog.Shards += shards;
                    break;
                case DigRewardType.RUNE_FRAGMENT:
                    if (reward.Value != null)
                    {
                        var parts = reward.Value.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int fragCount))
                            prog.AddFragments(parts[0], fragCount);
                    }
                    break;
                case DigRewardType.CODEX_PAGE:
                    // Codex system is future — for now, acknowledge in log
                    GD.Print($"[DigScene] Codex page discovered: {reward.Value}");
                    break;
                case DigRewardType.RELIC:
                    if (reward.Value != null)
                        prog.AddCard(reward.Value);
                    break;
            }
        }

        // Save progression
        CampaignContext.SaveManager.Save();

        // Mark node as cleared
        if (CampaignContext.CurrentNodeId != null)
            prog.MarkNodeCleared(CampaignContext.CurrentNodeId);

        // Return to map
        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
    }

    private void OnBackPressed()
    {
        // No rewards — just go back
        GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
    }

    // ─── Visual helpers ───

    private static Color GetRewardColor(DigRewardType type) => type switch
    {
        DigRewardType.SHARD => ShardColor,
        DigRewardType.RUNE_FRAGMENT => FragmentColor,
        DigRewardType.CODEX_PAGE => CodexColor,
        DigRewardType.RELIC => RelicColor,
        DigRewardType.EMPTY => EmptyColor,
        _ => RevealColor
    };

    private static string GetRewardIcon(DigRewardType type, string? value) => type switch
    {
        DigRewardType.SHARD => "\u25C6",
        DigRewardType.RUNE_FRAGMENT => "\u25B2",
        DigRewardType.CODEX_PAGE => "\u2630",
        DigRewardType.RELIC => "\u2605",
        DigRewardType.EMPTY => "\u00B7",
        _ => "?"
    };

    private static string GetRewardLabel(DigRewardEntry reward) => reward.Type switch
    {
        DigRewardType.SHARD => $"+{reward.Value} Shards",
        DigRewardType.RUNE_FRAGMENT => reward.Value != null
            ? $"+Rune Fragments ({reward.Value})"
            : "Rune Fragments",
        DigRewardType.CODEX_PAGE => $"Codex: {reward.Value}",
        DigRewardType.RELIC => $"Relic found: {reward.Value}",
        DigRewardType.EMPTY => "Nothing here...",
        _ => "Unknown"
    };

    private static string GetHeadlineLabel(DigSiteDef site) =>
        $"Headline find! {site.HeadlineReward}";
}

/// <summary>
/// A clickable grid tile for the dig scene.
/// </summary>
public partial class TileButton : Button
{
    public int TileIndex { get; set; }
    public Color TileColor { get; set; } = Colors.Gray;

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, TileColor);
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, 2)), new Color(1f, 1f, 1f, 0.15f));
        DrawRect(new Rect2(r.Position, new Vector2(2, r.Size.Y)), new Color(1f, 1f, 1f, 0.15f));
    }
}