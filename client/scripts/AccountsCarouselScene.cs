using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Accounts carousel screen — rotating card picker for campaign accounts.
/// Shows one card per account (name, class portrait, progress), plus a
/// "New Account" card at the end. Tap to switch, tap New to create.
/// Delete with confirmation. Same rotating carousel style as ChooseYourPath.
/// </summary>
public partial class AccountsCarouselScene : Control
{
    // ── Carousel state ──
    private int _selectedIdx;
    private bool _dragging;
    private float _dragStartX;
    private float _dragOffset;

    // ── Layout ──
    private float _panelFullW = 220f;
    private float _panelFullH = 310f;
    private float _centerX;
    private float _viewportW;
    private float _viewportH;

    private const float ScaleStep = 0.22f;
    private const float MinScale = 0.45f;
    private const float BrightStep = 0.28f;
    private const float MinBright = 0.42f;
    private const float SpacingRatio = 0.68f;
    private const float OverlapMarginRatio = 0.12f;
    private const float TextMarginRatio = 0.10f;

    // ── UI references ──
    private Control _carouselSection;
    private readonly List<Control> _panelNodes = new();
    private readonly List<TextureRect> _panelPortraits = new();
    private ColorRect _titleBlock;
    private Button _leftArrow;
    private Button _rightArrow;
    private Button _backButton;
    private bool _layoutDone;
    private bool _captureMode;
    private int _capturePhase;

    // ── Data ──
    private readonly List<CampaignContext.CampaignProfile> _accounts = new();

    public override void _Ready()
    {
        var vp = GetViewportRect().Size;
        _viewportW = vp.X;
        _viewportH = vp.Y;

        // Card sizing
        float targetH = _viewportH * 0.55f;
        _panelFullH = targetH;
        _panelFullW = _panelFullH * 220f / 310f;
        _centerX = _viewportW / 2f;

        // Dark background
        var bg = new ColorRect { Color = new Color(0.06f, 0.04f, 0.02f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Hero art background (same as title)
        var heroArt = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = new Color(0.62f, 0.62f, 0.62f, 0.60f)
        };
        heroArt.SetAnchorsPreset(LayoutPreset.FullRect);
        string heroPath = "res://content/art/title/hero_art.png";
        if (ResourceLoader.Exists(heroPath))
            heroArt.Texture = GD.Load<Texture2D>(heroPath);
        else
            GD.Print("[ART-MISSING] title/hero_art.png");
        AddChild(heroArt);

        // Vignette
        var vignette = new ColorRect
        {
            Color = new Color(0.04f, 0.03f, 0.02f, 0.45f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        vignette.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(vignette);

        // ── Title ──
        BuildTitleBlock();

        // ── Carousel section ──
        BuildCarouselSection();

        // ── Back button ──
        BuildBackButton();

        // Load accounts
        LoadAccounts();

        // Build panels
        BuildCarouselPanels();

        // Initial render
        UpdateCarousel();

        // Capture hook
        if (CampaignContext.AutoCaptureScreenshot)
        {
            _captureMode = true;
            _capturePhase = 0;

            var timer = GetTree().CreateTimer(0.5f);
            timer.Timeout += () =>
            {
                GD.Print("[AccountsCarousel] Capturing accounts carousel");
                SaveCapture("accounts_carousel");
                GetTree().Quit();
            };
        }

        GD.Print("[AccountsCarousel] Ready — " + _accounts.Count + " accounts");
    }

    private void BuildTitleBlock()
    {
        _titleBlock = new ColorRect
        {
            Color = new Color(0.15f, 0.10f, 0.05f, 0.60f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        _titleBlock.SetAnchorsPreset(LayoutPreset.HcenterWide);
        _titleBlock.AnchorLeft = 0.25f;
        _titleBlock.AnchorRight = 0.75f;
        _titleBlock.AnchorTop = 0.04f;
        _titleBlock.AnchorBottom = 0.14f;
        AddChild(_titleBlock);

        var title = new Label
        {
            Text = "Accounts",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        ThemeTokens.ApplyHeaderFont(title, ThemeTokens.FontTitleScreen);
        title.Modulate = Color.FromHtml("#D4B84C");
        title.SetAnchorsPreset(LayoutPreset.HcenterWide);
        title.AnchorLeft = 0.25f;
        title.AnchorRight = 0.75f;
        title.AnchorTop = 0.04f;
        title.AnchorBottom = 0.14f;
        AddChild(title);

        var subtitle = new Label
        {
            Text = _accounts.Count + " saved",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        ThemeTokens.ApplyBodyFont(subtitle, ThemeTokens.FontSecondary);
        subtitle.Modulate = Color.FromHtml("#C8B88A");
        subtitle.SetAnchorsPreset(LayoutPreset.HcenterWide);
        subtitle.AnchorLeft = 0.30f;
        subtitle.AnchorRight = 0.70f;
        subtitle.AnchorTop = 0.14f;
        subtitle.AnchorBottom = 0.18f;
        AddChild(subtitle);
    }

    private void BuildCarouselSection()
    {
        _carouselSection = new Control
        {
            MouseFilter = MouseFilterEnum.Ignore
        };
        _carouselSection.SetAnchorsPreset(LayoutPreset.FullRect);
        _carouselSection.AnchorTop = 0.20f;
        _carouselSection.AnchorBottom = 0.85f;
        AddChild(_carouselSection);

        // Left arrow
        _leftArrow = new Button
        {
            Text = "<",
            CustomMinimumSize = new Vector2(60, 80),
            MouseFilter = MouseFilterEnum.Stop
        };
        _leftArrow.AddThemeFontSizeOverride("font_size", 36);
        _leftArrow.AddThemeColorOverride("font_color", Color.FromHtml("#D4B84C"));
        _leftArrow.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        _leftArrow.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _leftArrow.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        _leftArrow.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        _leftArrow.Position = new Vector2(20, _viewportH * 0.35f);
        _leftArrow.Pressed += () => { _selectedIdx = Mathf.Max(0, _selectedIdx - 1); UpdateCarousel(); };
        AddChild(_leftArrow);

        // Right arrow
        _rightArrow = new Button
        {
            Text = ">",
            CustomMinimumSize = new Vector2(60, 80),
            MouseFilter = MouseFilterEnum.Stop
        };
        _rightArrow.AddThemeFontSizeOverride("font_size", 36);
        _rightArrow.AddThemeColorOverride("font_color", Color.FromHtml("#D4B84C"));
        _rightArrow.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        _rightArrow.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        _rightArrow.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        _rightArrow.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        _rightArrow.Position = new Vector2(_viewportW - 80, _viewportH * 0.35f);
        _rightArrow.Pressed += () => { _selectedIdx = Mathf.Min(_panelNodes.Count - 1, _selectedIdx + 1); UpdateCarousel(); };
        AddChild(_rightArrow);
    }

    private void BuildBackButton()
    {
        _backButton = new Button
        {
            Text = "Back",
            CustomMinimumSize = new Vector2(160, ThemeTokens.MinButtonHeight),
        };
        _backButton.AddThemeFontSizeOverride("font_size", ThemeTokens.FontButtonPrimary);
        _backButton.AddThemeColorOverride("font_color", Color.FromHtml("#E8DCC8"));
        _backButton.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        _backButton.AddThemeColorOverride("font_pressed_color", Color.FromHtml("#B8A878"));
        var backNormal = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#3A3530"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        _backButton.AddThemeStyleboxOverride("normal", backNormal);
        _backButton.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#4A4540"),
            BorderColor = Color.FromHtml("#C9A84C"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        _backButton.AnchorLeft = 0.40f;
        _backButton.AnchorRight = 0.60f;
        _backButton.AnchorTop = 0.88f;
        _backButton.AnchorBottom = 0.94f;
        var labelFont = ThemeTokens.GetButtonFont(ThemeTokens.FontButtonPrimary);
        if (labelFont != null)
            _backButton.AddThemeFontOverride("font", labelFont);
        _backButton.Pressed += OnBack;
        AddChild(_backButton);
    }

    private void LoadAccounts()
    {
        _accounts.Clear();
        _accounts.AddRange(CampaignContext.Profiles);
    }

    private void BuildCarouselPanels()
    {
        // Clear old panels
        foreach (var p in _panelNodes)
            if (IsInstanceValid(p)) p.QueueFree();
        _panelNodes.Clear();
        _panelPortraits.Clear();

        int idx = 0;

        // Account cards
        foreach (var account in _accounts)
        {
            int capturedIdx = idx;
            var panel = BuildAccountPanel(account, capturedIdx);
            _panelNodes.Add(panel);
            _carouselSection.AddChild(panel);
            idx++;
        }

        // "New Account" card at the end
        var newPanel = BuildNewAccountPanel();
        _panelNodes.Add(newPanel);
        _carouselSection.AddChild(newPanel);

        _selectedIdx = Mathf.Clamp(_selectedIdx, 0, _panelNodes.Count - 1);

        // Update arrow visibility
        _leftArrow.Visible = _panelNodes.Count > 1;
        _rightArrow.Visible = _panelNodes.Count > 1;
    }

    private Control BuildAccountPanel(CampaignContext.CampaignProfile account, int idx)
    {
        string classId = account.ClassId;
        bool hasClass = !string.IsNullOrEmpty(classId);
        string className = hasClass
            ? char.ToUpper(classId[0]) + classId.Substring(1)
            : "New Campaign";
        string progress = hasClass
            ? (string.IsNullOrEmpty(account.MapProgress) ? "Region 1" : account.MapProgress)
            : "";

        var panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(220, 310),
        };

        var style = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#2A2520"),
            BorderColor = Color.FromHtml("#5A5048"),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        panel.AddChild(vbox);

        // Portrait
        if (hasClass)
        {
            string portraitPath = CampaignContext.GetClassPortraitPath(classId, account.PortraitVariant);
            if (ResourceLoader.Exists(portraitPath))
            {
                var portrait = new TextureRect
                {
                    Texture = ResourceLoader.Load<Texture2D>(portraitPath),
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(100, 120),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                };
                vbox.AddChild(portrait);
            }
        }
        else
        {
            // Empty slot indicator
            var placeholder = new ColorRect
            {
                Color = new Color(0.20f, 0.18f, 0.14f),
                CustomMinimumSize = new Vector2(80, 100),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            vbox.AddChild(placeholder);
        }

        // Name
        var nameLabel = new Label
        {
            Text = className,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        nameLabel.AddThemeFontSizeOverride("font_size", ThemeTokens.FontCardName);
        nameLabel.Modulate = Color.FromHtml("#E8DCC8");
        vbox.AddChild(nameLabel);

        // Progress
        if (!string.IsNullOrEmpty(progress))
        {
            var progLabel = new Label
            {
                Text = progress,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            };
            progLabel.AddThemeFontSizeOverride("font_size", 13);
            progLabel.Modulate = new Color(0.7f, 0.65f, 0.55f);
            vbox.AddChild(progLabel);
        }

        // Spacer
        vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

        // Buttons row
        var btnHbox = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        vbox.AddChild(btnHbox);

        // Select button
        var selectBtn = new Button
        {
            Text = hasClass ? "Select" : "Play",
            CustomMinimumSize = new Vector2(100, 36),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        selectBtn.AddThemeFontSizeOverride("font_size", 14);
        selectBtn.AddThemeColorOverride("font_color", Color.FromHtml("#D4B84C"));
        selectBtn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F0E8D0"));
        var selectNormal = new StyleBoxFlat
        {
            BgColor = new Color(0.3f, 0.25f, 0.1f, 0.5f),
            BorderColor = Color.FromHtml("#C9A84C"),
            BorderWidthLeft = 1, BorderWidthTop = 1,
            BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        };
        selectBtn.AddThemeStyleboxOverride("normal", selectNormal);
        int capturedIdx = idx;
        selectBtn.Pressed += () => OnAccountSelected(capturedIdx);
        btnHbox.AddChild(selectBtn);

        // Delete button (only for accounts with class)
        if (hasClass)
        {
            var deleteBtn = new Button
            {
                Text = "Delete",
                CustomMinimumSize = new Vector2(100, 36),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            deleteBtn.AddThemeFontSizeOverride("font_size", 14);
            deleteBtn.AddThemeColorOverride("font_color", new Color(0.8f, 0.3f, 0.2f));
            deleteBtn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.4f, 0.3f));
            var delNormal = new StyleBoxFlat
            {
                BgColor = new Color(0.3f, 0.1f, 0.05f, 0.3f),
                BorderColor = new Color(0.6f, 0.2f, 0.1f, 0.4f),
                BorderWidthLeft = 1, BorderWidthTop = 1,
                BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            };
            deleteBtn.AddThemeStyleboxOverride("normal", delNormal);
            deleteBtn.Pressed += () => OnDeleteAccount(capturedIdx);
            btnHbox.AddChild(deleteBtn);
        }

        // Make the whole panel tappable
        panel.GuiInput += (eventArgs) =>
        {
            if (eventArgs is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                OnAccountSelected(capturedIdx);
            }
        };

        return panel;
    }

    private Control BuildNewAccountPanel()
    {
        var panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(220, 310),
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.30f, 0.28f, 0.22f),
            BorderColor = new Color(0.6f, 0.55f, 0.40f),
            BorderWidthLeft = 2, BorderWidthTop = 2,
            BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            BorderBlend = true,
        };
        panel.AddThemeStyleboxOverride("panel", style);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.Fill,
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        panel.AddChild(vbox);

        // Spacer
        vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

        // Big "+"
        var plusLabel = new Label
        {
            Text = "+",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        plusLabel.AddThemeFontSizeOverride("font_size", 72);
        plusLabel.Modulate = Color.FromHtml("#D4B84C");
        vbox.AddChild(plusLabel);

        // Text
        var textLabel = new Label
        {
            Text = "New Account",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
        };
        textLabel.AddThemeFontSizeOverride("font_size", ThemeTokens.FontButtonPrimary);
        textLabel.Modulate = Color.FromHtml("#C8B88A");
        vbox.AddChild(textLabel);

        // Spacer
        vbox.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.Expand });

        // Tap handler
        panel.GuiInput += (eventArgs) =>
        {
            if (eventArgs is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                OnNewAccount();
            }
        };

        return panel;
    }

    private void OnAccountSelected(int accountIdx)
    {
        if (accountIdx < 0 || accountIdx >= _accounts.Count) return;

        var account = _accounts[accountIdx];
        if (string.IsNullOrEmpty(account.ClassId))
        {
            // Empty account — go to ChooseYourPath
            GD.Print("[AccountsCarousel] Empty account selected — starting new campaign");
            CampaignContext.AddOrUpdateProfile("", "");
            CampaignContext.ChosenClass = "";
            CampaignContext.ChosenTown = "";
            GetTree().ChangeSceneToFile("res://scenes/choose_path/ChooseYourPathScene.tscn");
            return;
        }

        // Switch to this account
        GD.Print("[AccountsCarousel] Switching to account " + accountIdx + ": " + account.ClassId);
        CampaignContext.ActiveProfileSlot = accountIdx;
        CampaignContext.SaveManager.SwitchSlot(accountIdx);
        CampaignContext.ChosenClass = account.ClassId;
        CampaignContext.ChosenTown = account.TownName ?? "";
        CampaignContext.PortraitVariant = account.PortraitVariant;

        // Navigate back to title screen
        GetTree().ChangeSceneToFile("res://scenes/main/MainScene.tscn");
    }

    private void OnNewAccount()
    {
        GD.Print("[AccountsCarousel] Creating new account");
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");

        // Create new empty profile — the slot index auto-assigns
        CampaignContext.AddOrUpdateProfile("", "");
        CampaignContext.ChosenClass = "";
        CampaignContext.ChosenTown = "";

        // Go to ChooseYourPath
        GetTree().ChangeSceneToFile("res://scenes/choose_path/ChooseYourPathScene.tscn");
    }

    private void OnDeleteAccount(int accountIdx)
    {
        if (accountIdx < 0 || accountIdx >= _accounts.Count) return;

        var account = _accounts[accountIdx];
        string className = string.IsNullOrEmpty(account.ClassId)
            ? "New Campaign"
            : char.ToUpper(account.ClassId[0]) + account.ClassId.Substring(1);

        var dialog = new AcceptDialog
        {
            DialogText = $"Delete {className}'s campaign? All progress in this slot will be lost forever.",
            OkButtonText = "Delete",
            Title = "Delete Account",
        };
        int capturedIdx = accountIdx;
        dialog.Confirmed += () =>
        {
            GD.Print("[AccountsCarousel] Deleting account " + capturedIdx);
            CampaignContext.DeleteProfile(capturedIdx);
            // Rebuild the carousel
            LoadAccounts();
            BuildCarouselPanels();
            UpdateCarousel();
        };
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void OnBack()
    {
        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
        GetTree().ChangeSceneToFile("res://scenes/main/MainScene.tscn");
    }

    // ═══════════════════════════════════════════════════
    // Carousel rendering
    // ═══════════════════════════════════════════════════

    private void UpdateCarousel()
    {
        int count = _panelNodes.Count;
        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            var panel = _panelNodes[i];
            if (!IsInstanceValid(panel)) continue;

            int dist = i - _selectedIdx;
            float absDist = Mathf.Abs(dist);

            // Scale: selected is 1.0, fades to MinScale
            float scale = Mathf.Max(MinScale, 1.0f - absDist * ScaleStep);
            // Brightness
            float bright = Mathf.Max(MinBright, 1.0f - absDist * BrightStep);

            // Position: offset from center
            float offsetX = dist * _panelFullW * SpacingRatio;
            if (dist > 0)
                offsetX += absDist * (_panelFullW * OverlapMarginRatio);

            float w = _panelFullW * scale;
            float h = _panelFullH * scale;

            float x = _centerX - w / 2f + offsetX;
            float y = (_carouselSection.GetViewportRect().Size.Y / 2f) - h / 2f + (_viewportH * 0.20f);

            panel.Position = new Vector2(x, y);
            panel.Size = new Vector2(w, h);
            panel.Modulate = new Color(bright, bright, bright, 1.0f);

            // Selected panel gets a gold border
            var style = panel.GetThemeStylebox("panel") as StyleBoxFlat;
            if (i == _selectedIdx)
            {
                if (style != null)
                    style.BorderColor = Color.FromHtml("#C9A84C");
            }
            else
            {
                if (style != null)
                    style.BorderColor = Color.FromHtml("#5A5048");
            }

            // Z order: selected on top
            if (i == _selectedIdx)
                _carouselSection.MoveChild(panel, _carouselSection.GetChildCount() - 1);
        }

        // Update arrows
        _leftArrow.Disabled = _selectedIdx <= 0;
        _rightArrow.Disabled = _selectedIdx >= count - 1;
    }

    private void SaveCapture(string basename)
    {
        var suffix = CampaignContext.WideCaptureMode ? "_wide" : "";
        var img = GetViewport().GetTexture().GetImage();
        if (img != null)
        {
            string path = ProjectPaths.Artifacts + $"/captures/{basename}{suffix}.png";
            img.SavePng(path);
            GD.Print($"[AccountsCarousel] Saved capture: {path}");
        }
    }

    private void RunTestCycle()
    {
        // Phase 0: initial empty carousel already captured
        // Phase 1: create a warrior account
        GD.Print("[AccountsCarousel] Test cycle: creating warrior account");
        CampaignContext.AddOrUpdateProfile("warrior", "Emberhold");
        CampaignContext.Progression.AddCard("vrd_c_root_warden");
        CampaignContext.Progression.AddCard("emb_c_ember_hound");
        CampaignContext.SaveManager.Save();

        var timer1 = GetTree().CreateTimer(0.5f);
        timer1.Timeout += () =>
        {
            LoadAccounts();
            BuildCarouselPanels();
            UpdateCarousel();
            SaveCapture("accounts_carousel_filled");

            // Phase 2: delete the account
            GD.Print("[AccountsCarousel] Test cycle: deleting account");
            CampaignContext.DeleteProfile(0);

            var timer2 = GetTree().CreateTimer(0.5f);
            timer2.Timeout += () =>
            {
                LoadAccounts();
                BuildCarouselPanels();
                UpdateCarousel();
                SaveCapture("accounts_carousel_deleted");
                GetTree().Quit();
            };
        };
    }
}