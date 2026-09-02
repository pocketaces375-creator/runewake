using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Rune page editor — view and edit a rune page before a duel.
/// Shows 9/9/9/3 slot layout with a budget bar.
/// Slots beyond the unlocked count require RuneDust to unlock.
/// Equipped runes can be upgraded with RuneDust for higher tiers.
/// </summary>
public partial class RunePageScene : Control
{
    private RunePage _page = new();
    private List<RuneDef> _availableRunes = new();
    private int _delverLevel = 1;

    // UI
    private Label _titleLabel = default!;
    private Label _budgetLabel = default!;
    private Label _runeDustLabel = default!;
    private ColorRect _budgetFill = default!;
    private ColorRect _budgetBack = default!;
    private GridContainer _offensiveGrid = default!;
    private GridContainer _defensiveGrid = default!;
    private GridContainer _utilityGrid = default!;
    private GridContainer _mythicGrid = default!;
    private Button _backButton = default!;
    private Control _runePicker = default!;
    private VBoxContainer _runeListBox = default!;
    private Label _shortfallLabel = default!;
    private Timer _shortfallTimer = default!;
    private List<RuneDef> _pickerRunes = new();
    private RuneSlotType _pickerSlotType;

    // Constants
    private static readonly Color SlotFillColor = new(0.08f, 0.08f, 0.18f);
    private static readonly Color SlotEmptyColor = new(0.12f, 0.12f, 0.25f);
    private static readonly Color SlotBorderColor = new(0.3f, 0.3f, 0.5f);
    private static readonly Color LockedSlotColor = new(0.05f, 0.05f, 0.10f);
    private static readonly Color BudgetGreen = new(0.2f, 0.8f, 0.2f);
    private static readonly Color BudgetYellow = new(0.9f, 0.8f, 0.2f);
    private static readonly Color BudgetRed = new(0.9f, 0.3f, 0.2f);
    private static readonly Color UpgradeColor = new(0.5f, 0.4f, 0.9f);
    private static readonly Color InsufficientColor = new(0.9f, 0.3f, 0.2f);

    public override void _Ready()
    {
        AnchorLeft = 0; AnchorRight = 1;
        AnchorTop = 0; AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Ignore;

        // Load available runes from the registry or campaign context
        LoadAvailableRunes();

        // Try to load existing page from campaign context
        if (CampaignContext.CurrentRunePage != null)
        {
            _page = CampaignContext.CurrentRunePage;
        }

        _delverLevel = CampaignContext.Progression?.DelverLevel ?? 1;

        BuildUI();
        RefreshUI();
    }

    private ProgressionState? Progression => CampaignContext.Progression;

    private void LoadAvailableRunes()
    {
        // Load from the starter runes file (via CampaignContext or direct load)
        try
        {
            string json = Godot.FileAccess.GetFileAsString("res://content/runes/starter_runes.json");
            if (!string.IsNullOrEmpty(json))
            {
                var pack = RuneLoader.LoadPackFromString(json);
                _availableRunes = pack.Runes;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RunePageScene] Failed to load runes: {ex.Message}");
        }
    }

    private void BuildUI()
    {
        // ── Background ──
        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.06f, 0.12f),
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        };
        AddChild(bg);

        // ── Title ──
        _titleLabel = new Label
        {
            Text = "RUNE PAGE",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.02f, AnchorBottom = 0.08f
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 22);
        _titleLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f));
        AddChild(_titleLabel);

        // ── RuneDust balance ──
        _runeDustLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            AnchorLeft = 0.7f, AnchorRight = 0.95f,
            AnchorTop = 0.02f, AnchorBottom = 0.08f,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        int runeDust = Progression?.RuneDust ?? 0;
        _runeDustLabel.Text = $"Runes: {runeDust}";
        _runeDustLabel.AddThemeFontSizeOverride("font_size", 14);
        _runeDustLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.5f, 0.9f));
        AddChild(_runeDustLabel);

        // ── Shortfall feedback label (hidden initially) ──
        _shortfallLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.08f, AnchorBottom = 0.12f,
            Visible = false,
            AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled
        };
        _shortfallLabel.AddThemeFontSizeOverride("font_size", 13);
        _shortfallLabel.AddThemeColorOverride("font_color", InsufficientColor);
        AddChild(_shortfallLabel);

        _shortfallTimer = new Timer
        {
            OneShot = true,
            WaitTime = 2.5f,
            AutoStart = false
        };
        _shortfallTimer.Timeout += () => _shortfallLabel.Visible = false;
        AddChild(_shortfallTimer);

        // ── Budget bar ──
        var budgetLabel = new Label
        {
            Text = "RP Budget",
            HorizontalAlignment = HorizontalAlignment.Left,
            AnchorLeft = 0.05f, AnchorRight = 0.45f,
            AnchorTop = 0.13f, AnchorBottom = 0.17f
        };
        budgetLabel.AddThemeFontSizeOverride("font_size", 14);
        budgetLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
        AddChild(budgetLabel);

        _budgetBack = new ColorRect
        {
            Color = new Color(0.15f, 0.15f, 0.25f),
            AnchorLeft = 0.05f, AnchorRight = 0.75f,
            AnchorTop = 0.18f, AnchorBottom = 0.22f
        };
        AddChild(_budgetBack);

        _budgetFill = new ColorRect
        {
            Color = BudgetGreen,
            AnchorLeft = 0.05f, AnchorRight = 0.05f,
            AnchorTop = 0.18f, AnchorBottom = 0.22f
        };
        AddChild(_budgetFill);

        _budgetLabel = new Label
        {
            Text = "0 / 12",
            HorizontalAlignment = HorizontalAlignment.Right,
            AnchorLeft = 0.75f, AnchorRight = 0.95f,
            AnchorTop = 0.18f, AnchorBottom = 0.22f
        };
        _budgetLabel.AddThemeFontSizeOverride("font_size", 12);
        _budgetLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        AddChild(_budgetLabel);

        // ── Section headers and grids ──
        float sectionTop = 0.26f;
        float sectionHeight = 0.17f;
        float gap = 0.02f;

        AddSection("OFFENSIVE (Marks)", 0, sectionTop, ref _offensiveGrid);
        AddSection("DEFENSIVE (Seals)", 1, sectionTop + sectionHeight + gap, ref _defensiveGrid);
        AddSection("UTILITY (Glyphs)", 2, sectionTop + (sectionHeight + gap) * 2, ref _utilityGrid);
        AddSection("MYTHIC (Sigils)", 3, sectionTop + (sectionHeight + gap) * 3, ref _mythicGrid);

        // ── Delver Level info ──
        var levelLabel = new Label
        {
            Text = $"Delver Level {_delverLevel} | Max Budget: {RunePage.GetBudgetForLevel(_delverLevel)} RP",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.94f, AnchorBottom = 0.98f
        };
        levelLabel.AddThemeFontSizeOverride("font_size", 12);
        levelLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.7f));
        AddChild(levelLabel);

        // ── Back button ──
        _backButton = new Button
        {
            Text = "Back",
            AnchorLeft = 0.02f, AnchorRight = 0.12f,
            AnchorTop = 0.02f, AnchorBottom = 0.07f
        };
        _backButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
        };
        AddChild(_backButton);

        // ── Save button ──
        var saveButton = new Button
        {
            Text = "Save & Close",
            AnchorLeft = 0.8f, AnchorRight = 0.98f,
            AnchorTop = 0.18f, AnchorBottom = 0.22f
        };
        saveButton.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            CampaignContext.SaveCurrentRunePage();
            CampaignContext.SaveManager.Save();
            GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
        };
        AddChild(saveButton);

        // ── Rune picker overlay (hidden initially) ──
        BuildRunePicker();
    }

    private void ShowShortfall(string message)
    {
        _shortfallLabel.Text = message;
        _shortfallLabel.Visible = true;
        _shortfallTimer.Start();
    }

    private void RefreshRuneDustLabel()
    {
        int runeDust = Progression?.RuneDust ?? 0;
        _runeDustLabel.Text = $"Runes: {runeDust}";
    }

    private void AddSection(string name, int index, float top, ref GridContainer grid)
    {
        var header = new Label
        {
            Text = name,
            HorizontalAlignment = HorizontalAlignment.Left,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = top, AnchorBottom = top + 0.03f
        };
        header.AddThemeFontSizeOverride("font_size", 12);
        header.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.8f));
        AddChild(header);

        int cols = index == 3 ? 3 : 9;
        grid = new GridContainer
        {
            Columns = cols,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = top + 0.03f, AnchorBottom = top + 0.17f
        };
        AddChild(grid);
    }

    private void RefreshUI()
    {
        int budget = RunePage.GetBudgetForLevel(_delverLevel);
        int used = _page.TotalCost;
        _budgetLabel.Text = $"{used} / {budget}";

        // Update budget bar
        float ratio = Math.Clamp((float)used / budget, 0f, 1f);
        float fullWidth = GetViewportRect().Size.X * 0.7f;
        _budgetFill.AnchorRight = 0.05f + ratio * 0.7f;
        _budgetFill.Color = ratio switch
        {
            > 0.85f => BudgetRed,
            > 0.65f => BudgetYellow,
            _ => BudgetGreen
        };

        RefreshRuneDustLabel();

        // Rebuild slot grids with unlock awareness
        RebuildGrid(_offensiveGrid, _page.OffensiveSlots, RuneSlotType.OFFENSIVE);
        RebuildGrid(_defensiveGrid, _page.DefensiveSlots, RuneSlotType.DEFENSIVE);
        RebuildGrid(_utilityGrid, _page.UtilitySlots, RuneSlotType.UTILITY);
        RebuildGrid(_mythicGrid, _page.MythicSlots, RuneSlotType.MYTHIC);
    }

    private int GetUnlockedSlotCount(RuneSlotType type)
    {
        return Progression?.GetUnlockedSlotCount(type) ?? 1;
    }

    private void RebuildGrid(GridContainer grid, RuneDef?[] slots, RuneSlotType slotType)
    {
        // Clear existing children
        foreach (var child in grid.GetChildren())
            child.QueueFree();

        int unlockedCount = GetUnlockedSlotCount(slotType);
        int maxSlots = RunePage.GetSlotCount(slotType);

        for (int i = 0; i < slots.Length; i++)
        {
            int idx = i;
            var slot = slots[i];

            if (i < unlockedCount)
            {
                // ── Available slot ──
                if (slot != null)
                {
                    // Equipped rune — show name, tier, and upgrade button
                    var container = new HBoxContainer
                    {
                        CustomMinimumSize = new Vector2(40, 36),
                        SizeFlagsHorizontal = (Control.SizeFlags)3
                    };

                    int runeTier = Progression?.GetRuneTier(slot.Id) ?? 1;

                    var runeBtn = new Button
                    {
                        Text = $"{slot.Name}\n[{slot.RpCost} RP] T{runeTier}",
                        SizeFlagsHorizontal = (Control.SizeFlags)3,
                        CustomMinimumSize = new Vector2(0, 36)
                    };
                    runeBtn.AddThemeFontSizeOverride("font_size", 8);
                    // Tapping an equipped rune unequips it
                    var capturedSlot = slot;
                    runeBtn.Pressed += () =>
                    {
                        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                        _page.Unequip(slotType, idx);
                        RefreshUI();
                    };
                    container.AddChild(runeBtn);

                    // Upgrade button (only if not max tier)
                    if (runeTier < 3)
                    {
                        int upgradeCost = RunePage.GetUpgradeCost(runeTier);
                        var upgradeBtn = new Button
                        {
                            Text = $"↑{upgradeCost}",
                            CustomMinimumSize = new Vector2(24, 36),
                            SizeFlagsHorizontal = (Control.SizeFlags)3
                        };
                        upgradeBtn.AddThemeFontSizeOverride("font_size", 7);
                        upgradeBtn.Modulate = UpgradeColor;

                        var capturedRuneId = slot.Id;
                        upgradeBtn.Pressed += () =>
                        {
                            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                            if (Progression == null) return;

                            var (success, cost, error) = Progression.UpgradeRune(capturedRuneId);
                            if (success)
                            {
                                RefreshRuneDustLabel();
                                // Refresh the whole page so the button updates
                                RefreshUI();
                            }
                            else
                            {
                                ShowShortfall(error ?? "Cannot upgrade.");
                            }
                        };
                        container.AddChild(upgradeBtn);
                    }

                    grid.AddChild(container);
                }
                else
                {
                    // Empty available slot — show "+" to open picker
                    var btn = new Button
                    {
                        Text = "+",
                        CustomMinimumSize = new Vector2(40, 36),
                        SizeFlagsHorizontal = (Control.SizeFlags)3
                    };
                    btn.AddThemeFontSizeOverride("font_size", 14);
                    btn.Modulate = new Color(0.4f, 0.4f, 0.6f);
                    btn.Pressed += () =>
                    {
                        GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                        ShowRunePicker(slotType);
                    };
                    grid.AddChild(btn);
                }
            }
            else if (i == unlockedCount && unlockedCount < maxSlots)
            {
                // ── Next purchasable slot ──
                int slotIndex = idx;
                int cost = RunePage.GetSlotUnlockCost(slotIndex);
                int currentRuneDust = Progression?.RuneDust ?? 0;
                bool canAfford = currentRuneDust >= cost && cost > 0;

                int displaySlot = slotIndex + 1; // 1-based for display
                string costText = cost > 0 ? $"{cost} R" : "FREE";
                var unlockBtn = new Button
                {
                    Text = cost > 0 ? $"Slot {displaySlot}\n🔓 {cost} R" : $"Slot {displaySlot}\n🔓 FREE",
                    CustomMinimumSize = new Vector2(40, 36),
                    SizeFlagsHorizontal = (Control.SizeFlags)3
                };
                unlockBtn.AddThemeFontSizeOverride("font_size", 8);

                if (cost > 0 && !canAfford)
                {
                    unlockBtn.Modulate = new Color(0.3f, 0.3f, 0.3f);
                }

                unlockBtn.Pressed += () =>
                {
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                    if (Progression == null) return;

                    var (success, spent, error) = Progression.UnlockNextSlot(slotType);
                    if (success)
                    {
                        RefreshRuneDustLabel();
                        RefreshUI();
                    }
                    else
                    {
                        if (spent > 0)
                        {
                            int shortfall = spent - (Progression?.RuneDust ?? 0);
                            if (shortfall > 0)
                                ShowShortfall($"Need {shortfall} more Rune{(shortfall == 1 ? "" : "s")} for slot {displaySlot}.");
                        }
                        else
                        {
                            ShowShortfall(error ?? "Cannot unlock.");
                        }
                    }
                };

                grid.AddChild(unlockBtn);
            }
            else
            {
                // ── Future locked slot (beyond the next purchasable one) ──
                int displaySlot = idx + 1;
                var lockedBtn = new Button
                {
                    Text = $"Slot {displaySlot}\n🔒",
                    CustomMinimumSize = new Vector2(40, 36),
                    SizeFlagsHorizontal = (Control.SizeFlags)3,
                    Disabled = true
                };
                lockedBtn.AddThemeFontSizeOverride("font_size", 8);
                lockedBtn.Modulate = new Color(0.25f, 0.25f, 0.35f);
                grid.AddChild(lockedBtn);
            }
        }
    }

    // ── Rune picker overlay ──

    private void BuildRunePicker()
    {
        _runePicker = new Control
        {
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1,
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };

        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.6f),
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        };
        _runePicker.AddChild(dim);

        var container = new Control
        {
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.15f, AnchorBottom = 0.85f
        };
        _runePicker.AddChild(container);

        var bg = new ColorRect
        {
            Color = new Color(0.06f, 0.06f, 0.15f, 0.95f),
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        };
        container.AddChild(bg);

        var title = new Label
        {
            Text = "SELECT RUNE",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.02f, AnchorBottom = 0.08f
        };
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f));
        container.AddChild(title);

        var closeBtn = new Button
        {
            Text = "Cancel",
            AnchorLeft = 0.05f, AnchorRight = 0.3f,
            AnchorTop = 0.02f, AnchorBottom = 0.08f
        };
        closeBtn.Pressed += () =>
        {
            GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
            _runePicker.Visible = false;
        };
        container.AddChild(closeBtn);

        // Scrollable rune list
        var scroll = new ScrollContainer
        {
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.1f, AnchorBottom = 0.9f
        };
        container.AddChild(scroll);

        _runeListBox = new VBoxContainer();
        _runeListBox.SizeFlagsHorizontal = (Control.SizeFlags)3;
        scroll.AddChild(_runeListBox);

        AddChild(_runePicker);
    }

    private void ShowRunePicker(RuneSlotType slotType)
    {
        _pickerSlotType = slotType;

        // Clear existing items
        foreach (var child in _runeListBox.GetChildren())
            child.QueueFree();

        // Filter runes by slot type
        var filtered = _availableRunes.Where(r => r.SlotType == slotType).ToList();

        foreach (var rune in filtered)
        {
            var btn = new Button
            {
                Text = $"[{rune.RpCost}RP] {rune.Name} — {rune.Description}",
                CustomMinimumSize = new Vector2(0, 32),
                SizeFlagsHorizontal = (Control.SizeFlags)3
            };
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.9f));

            var captured = rune;
            btn.Pressed += () =>
            {
                GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                _page.Equip(captured);
                _runePicker.Visible = false;
                RefreshUI();
            };
            _runeListBox.AddChild(btn);
        }

        _runePicker.Visible = true;
    }
}