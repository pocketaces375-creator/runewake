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
/// Tap an empty slot to browse available runes, tap an equipped rune to unequip.
/// </summary>
public partial class RunePageScene : Control
{
    private RunePage _page = new();
    private List<RuneDef> _availableRunes = new();
    private int _delverLevel = 1;

    // UI
    private Label _titleLabel = default!;
    private Label _budgetLabel = default!;
    private ColorRect _budgetFill = default!;
    private ColorRect _budgetBack = default!;
    private GridContainer _offensiveGrid = default!;
    private GridContainer _defensiveGrid = default!;
    private GridContainer _utilityGrid = default!;
    private GridContainer _mythicGrid = default!;
    private Button _backButton = default!;
    private Control _runePicker = default!;
    private List<RuneDef> _pickerRunes = new();
    private RuneSlotType _pickerSlotType;

    // Constants
    private static readonly Color SlotFillColor = new(0.08f, 0.08f, 0.18f);
    private static readonly Color SlotEmptyColor = new(0.12f, 0.12f, 0.25f);
    private static readonly Color SlotBorderColor = new(0.3f, 0.3f, 0.5f);
    private static readonly Color BudgetGreen = new(0.2f, 0.8f, 0.2f);
    private static readonly Color BudgetYellow = new(0.9f, 0.8f, 0.2f);
    private static readonly Color BudgetRed = new(0.9f, 0.3f, 0.2f);

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

    private void LoadAvailableRunes()
    {
        // Load from the starter runes file (via CampaignContext or direct load)
        // For now, load from the embedded file
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

        // ── Budget bar ──
        var budgetLabel = new Label
        {
            Text = "RP Budget",
            HorizontalAlignment = HorizontalAlignment.Left,
            AnchorLeft = 0.05f, AnchorRight = 0.45f,
            AnchorTop = 0.09f, AnchorBottom = 0.13f
        };
        budgetLabel.AddThemeFontSizeOverride("font_size", 14);
        budgetLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
        AddChild(budgetLabel);

        _budgetBack = new ColorRect
        {
            Color = new Color(0.15f, 0.15f, 0.25f),
            AnchorLeft = 0.05f, AnchorRight = 0.75f,
            AnchorTop = 0.14f, AnchorBottom = 0.18f
        };
        AddChild(_budgetBack);

        _budgetFill = new ColorRect
        {
            Color = BudgetGreen,
            AnchorLeft = 0.05f, AnchorRight = 0.05f, // starts at zero — updated in RefreshUI
            AnchorTop = 0.14f, AnchorBottom = 0.18f
        };
        AddChild(_budgetFill);

        _budgetLabel = new Label
        {
            Text = "0 / 12",
            HorizontalAlignment = HorizontalAlignment.Right,
            AnchorLeft = 0.75f, AnchorRight = 0.95f,
            AnchorTop = 0.14f, AnchorBottom = 0.18f
        };
        _budgetLabel.AddThemeFontSizeOverride("font_size", 12);
        _budgetLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
        AddChild(_budgetLabel);

        // ── Section headers and grids ──
        float sectionTop = 0.2f;
        float sectionHeight = 0.18f;
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
            AnchorTop = 0.14f, AnchorBottom = 0.18f
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

        int cols = index == 3 ? 3 : 9; // Mythic has 3 slots per row, others 9
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

        // Rebuild slot grids
        RebuildGrid(_offensiveGrid, _page.OffensiveSlots, RuneSlotType.OFFENSIVE);
        RebuildGrid(_defensiveGrid, _page.DefensiveSlots, RuneSlotType.DEFENSIVE);
        RebuildGrid(_utilityGrid, _page.UtilitySlots, RuneSlotType.UTILITY);
        RebuildGrid(_mythicGrid, _page.MythicSlots, RuneSlotType.MYTHIC);
    }

    private void RebuildGrid(GridContainer grid, RuneDef?[] slots, RuneSlotType slotType)
    {
        // Clear existing children
        foreach (var child in grid.GetChildren())
            child.QueueFree();

        for (int i = 0; i < slots.Length; i++)
        {
            int idx = i;
            var slot = slots[i];
            var btn = new Button
            {
                CustomMinimumSize = new Vector2(40, 36),
                SizeFlagsHorizontal = (Control.SizeFlags)3 // expand
            };

            if (slot != null)
            {
                btn.Text = $"{slot.Name}\n[{slot.RpCost} RP]";
                btn.AddThemeFontSizeOverride("font_size", 9);
                // Tapping an equipped rune unequips it
                btn.Pressed += () =>
                {
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                    _page.Unequip(slotType, idx);
                    RefreshUI();
                };
            }
            else
            {
                btn.Text = "+";
                btn.AddThemeFontSizeOverride("font_size", 14);
                btn.Modulate = new Color(0.4f, 0.4f, 0.6f);
                // Tapping an empty slot opens the rune picker
                btn.Pressed += () =>
                {
                    GetNode<AudioManager>("/root/AudioManager").PlaySfx("click");
                    ShowRunePicker(slotType);
                };
            }

            grid.AddChild(btn);
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

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = (Control.SizeFlags)3; // expand
        scroll.AddChild(vbox);

        // Store reference to vbox for dynamic population
        _runePicker.SetMeta("rune_list", vbox);

        AddChild(_runePicker);
    }

    private void ShowRunePicker(RuneSlotType slotType)
    {
        _pickerSlotType = slotType;

        var vbox = _runePicker.GetMeta("rune_list").As<Godot.Collections.Dictionary>() is null
            ? null
            : _runePicker.GetNodeOrNull<VBoxContainer>(_runePicker.GetMeta("rune_list").ToString() ?? ".");

        // Find the vbox manually
        var scrollContainer = _runePicker.GetNodeOrNull<ScrollContainer>(".");
        VBoxContainer? listBox = null;
        if (scrollContainer != null)
        {
            foreach (var child in scrollContainer.GetChildren())
            {
                if (child is VBoxContainer vb)
                {
                    listBox = vb;
                    break;
                }
            }
        }
        if (listBox == null) return;

        // Clear existing items
        foreach (var child in listBox.GetChildren())
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
            listBox.AddChild(btn);
        }

        _runePicker.Visible = true;
    }
}