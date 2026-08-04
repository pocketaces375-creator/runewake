using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Rune page editor screen — equips runes into 9/9/9/3 slots with RP budget bar.
/// Programmatic UI (no .tscn dependencies).
/// </summary>
public partial class RunePageScene : Control
{
    private Button _backButton = default!;
    private Button _saveButton = default!;
    private Label _budgetLabel = default!;
    private ProgressBar _budgetBar = default!;
    private VBoxContainer _slotsArea = default!;
    private Panel _detailPanel = default!;
    private Label _detailName = default!;
    private Label _detailDesc = default!;
    private Label _detailEffect = default!;
    private Label _detailCost = default!;
    private Button _detailUnequip = default!;
    private Label _feedbackLabel = default!;

    // Picker overlay
    private Panel _pickerOverlay = default!;
    private VBoxContainer _pickerList = default!;
    private LineEdit _pickerSearch = default!;

    private RuneSlotType _selectedSlotType;
    private int _selectedSlotIndex = -1;
    private RuneDef? _selectedRune;

    private static readonly (RuneSlotType type, string label, int count)[] SlotGroups = new[]
    {
        (RuneSlotType.OFFENSIVE, "Offensive", 9),
        (RuneSlotType.DEFENSIVE, "Defensive", 9),
        (RuneSlotType.UTILITY, "Utility", 9),
        (RuneSlotType.MYTHIC, "Mythic", 3)
    };

    private readonly Dictionary<(RuneSlotType, int), Button> _slotButtons = new();

    public override void _Ready()
    {
        BuildUI();
        RefreshAll();
    }

    private void BuildUI()
    {
        // Background
        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.08f, 0.12f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f
        };
        AddChild(bg);

        // Top bar
        _backButton = new Button
        {
            Text = "< Title",
            AnchorLeft = 0f, AnchorRight = 0.1f,
            AnchorTop = 0f, AnchorBottom = 0.05f
        };
        _backButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        AddChild(_backButton);

        _saveButton = new Button
        {
            Text = "Save",
            AnchorLeft = 0.7f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.05f
        };
        _saveButton.Pressed += OnSave;
        AddChild(_saveButton);

        var title = new Label
        {
            Text = "Rune Page",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.2f, AnchorRight = 0.7f,
            AnchorTop = 0f, AnchorBottom = 0.05f,
            VerticalAlignment = VerticalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        AddChild(title);

        // Feedback label
        _feedbackLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.2f, AnchorRight = 0.7f,
            AnchorTop = 0.05f, AnchorBottom = 0.08f
        };
        _feedbackLabel.AddThemeFontSizeOverride("font_size", 12);
        _feedbackLabel.Modulate = new Color(0.4f, 1, 0.4f);
        AddChild(_feedbackLabel);

        // Slots area (left 60%)
        _slotsArea = new VBoxContainer
        {
            AnchorLeft = 0.02f, AnchorRight = 0.6f,
            AnchorTop = 0.09f, AnchorBottom = 0.85f
        };
        AddChild(_slotsArea);

        // Detail panel (right 38%)
        _detailPanel = new Panel();
        _detailPanel.AnchorLeft = 0.62f;
        _detailPanel.AnchorRight = 0.98f;
        _detailPanel.AnchorTop = 0.09f;
        _detailPanel.AnchorBottom = 0.85f;
        AddChild(_detailPanel);

        var detailVbox = new VBoxContainer();
        detailVbox.AnchorLeft = 0f; detailVbox.AnchorRight = 1f;
        detailVbox.AnchorTop = 0f; detailVbox.AnchorBottom = 1f;
        detailVbox.AddThemeConstantOverride("separation", 6);
        _detailPanel.AddChild(detailVbox);

        _detailName = new Label();
        _detailName.AddThemeFontSizeOverride("font_size", 18);
        detailVbox.AddChild(_detailName);

        _detailDesc = new Label { Modulate = new Color(0.7f, 0.7f, 0.8f), AutowrapMode = TextServer.AutowrapMode.Word };
        _detailDesc.AddThemeFontSizeOverride("font_size", 12);
        detailVbox.AddChild(_detailDesc);

        _detailEffect = new Label { AutowrapMode = TextServer.AutowrapMode.Word };
        _detailEffect.AddThemeFontSizeOverride("font_size", 11);
        detailVbox.AddChild(_detailEffect);

        _detailCost = new Label();
        _detailCost.AddThemeFontSizeOverride("font_size", 14);
        _detailCost.Modulate = new Color(1, 0.8f, 0.4f);
        detailVbox.AddChild(_detailCost);

        _detailUnequip = new Button { Text = "Unequip" };
        _detailUnequip.Pressed += OnUnequipDetail;
        detailVbox.AddChild(_detailUnequip);

        detailVbox.AddChild(new Control()); // spacer

        // Budget bar (bottom)
        _budgetBar = new ProgressBar
        {
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.87f, AnchorBottom = 0.91f,
            ShowPercentage = false
        };
        AddChild(_budgetBar);

        _budgetLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.91f, AnchorBottom = 0.94f,
            VerticalAlignment = VerticalAlignment.Center
        };
        _budgetLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_budgetLabel);

        // Build slot buttons
        foreach (var (type, label, count) in SlotGroups)
        {
            var sectionLabel = new Label
            {
                Text = $"{label} ({count})",
                ThemeTypeVariation = "HeaderLabel",
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            sectionLabel.AddThemeFontSizeOverride("font_size", 14);
            sectionLabel.Modulate = new Color(0.8f, 0.8f, 0.9f);
            _slotsArea.AddChild(sectionLabel);

            var grid = new GridContainer
            {
                Columns = 3,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            for (int i = 0; i < count; i++)
            {
                int slotIdx = i;
                var btn = new Button
                {
                    Text = "—",
                    CustomMinimumSize = new Vector2(90, 36),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    TooltipText = $"Slot {i + 1}"
                };
                btn.Pressed += () => OnSlotClicked(type, slotIdx);
                grid.AddChild(btn);
                _slotButtons[(type, i)] = btn;
            }
            _slotsArea.AddChild(grid);
        }

        // Picker overlay (hidden by default)
        _pickerOverlay = new Panel();
        _pickerOverlay.AnchorLeft = 0.1f; _pickerOverlay.AnchorRight = 0.9f;
        _pickerOverlay.AnchorTop = 0.1f; _pickerOverlay.AnchorBottom = 0.9f;
        _pickerOverlay.Hide();
        AddChild(_pickerOverlay);

        var pickerVbox = new VBoxContainer();
        pickerVbox.AnchorLeft = 0f; pickerVbox.AnchorRight = 1f;
        pickerVbox.AnchorTop = 0f; pickerVbox.AnchorBottom = 1f;
        pickerVbox.AddThemeConstantOverride("separation", 4);
        _pickerOverlay.AddChild(pickerVbox);

        var pickerTitle = new Label
        {
            Text = "Select a Rune",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        pickerTitle.AddThemeFontSizeOverride("font_size", 18);
        pickerVbox.AddChild(pickerTitle);

        _pickerSearch = new LineEdit
        {
            PlaceholderText = "Search runes...",
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _pickerSearch.TextChanged += _ => RebuildPickerList();
        pickerVbox.AddChild(_pickerSearch);

        var pickerScroll = new ScrollContainer();
        pickerScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        pickerVbox.AddChild(pickerScroll);

        _pickerList = new VBoxContainer();
        _pickerList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pickerScroll.AddChild(_pickerList);

        var closeButton = new Button
        {
            Text = "Cancel",
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        closeButton.Pressed += () => _pickerOverlay.Hide();
        pickerVbox.AddChild(closeButton);
    }

    private void OnSlotClicked(RuneSlotType type, int slotIndex)
    {
        var rune = CampaignContext.CurrentRunePage.GetSlot(type, slotIndex);
        if (rune != null)
        {
            // Already equipped — show detail
            ShowRuneDetail(rune, type, slotIndex);
        }
        else
        {
            // Empty slot — show picker
            _selectedSlotType = type;
            _selectedSlotIndex = slotIndex;
            _pickerSearch.Text = "";
            RebuildPickerList();
            _pickerOverlay.Show();
        }
    }

    private void RebuildPickerList()
    {
        // Clear old items
        foreach (var child in _pickerList.GetChildren())
            child.QueueFree();

        string filter = _pickerSearch.Text.ToLowerInvariant();

        // Get all runes of the selected slot type, filter by search
        var candidates = CampaignContext.RuneIndex.Values
            .Where(r => r.SlotType == _selectedSlotType)
            .Where(r => r.Name.ToLowerInvariant().Contains(filter) || r.Id.ToLowerInvariant().Contains(filter))
            .ToList();

        if (candidates.Count == 0)
        {
            var lbl = new Label { Text = "No runes found." };
            _pickerList.AddChild(lbl);
            return;
        }

        foreach (var rune in candidates)
        {
            var btn = new Button();
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            string strataStr = rune.Strata?.ToString() ?? "";
            string costStr = $"{rune.Cost} RP";
            btn.Text = $"{rune.Name}  [{costStr}]  {strataStr}";
            btn.TooltipText = rune.Description;

            // Check if already equipped (grey out if so)
            if (IsRuneEquipped(rune.Id))
                btn.Modulate = new Color(0.5f, 0.5f, 0.5f);

            btn.Pressed += () =>
            {
                OnPickerSelect(rune);
                _pickerOverlay.Hide();
            };
            _pickerList.AddChild(btn);
        }
    }

    private void OnPickerSelect(RuneDef rune)
    {
        var page = CampaignContext.CurrentRunePage;
        if (page.Equip(rune))
        {
            RefreshAll();
            ShowFeedback($"Equipped {rune.Name}");
        }
        else
        {
            ShowFeedback($"Cannot equip {rune.Name} — budget or slot full", false);
        }
    }

    private void OnUnequipDetail()
    {
        if (_selectedRune == null) return;
        var page = CampaignContext.CurrentRunePage;
        if (page.Unequip(_selectedSlotType, _selectedSlotIndex))
        {
            ShowFeedback($"Unequipped {_selectedRune.Name}");
            _selectedRune = null;
            ClearDetail();
            RefreshAll();
        }
    }

    private void ShowRuneDetail(RuneDef rune, RuneSlotType type, int slotIndex)
    {
        _selectedRune = rune;
        _selectedSlotType = type;
        _selectedSlotIndex = slotIndex;

        _detailName.Text = rune.Name;
        _detailDesc.Text = rune.Description;
        _detailCost.Text = $"{rune.Cost} RP  |  {rune.SlotType}";

        string effectStr = $"Trigger: {rune.Ability.Trigger}";
        if (rune.Ability.Condition != null)
            effectStr += $"\nCondition: {rune.Ability.Condition.Op}";
        foreach (var eff in rune.Ability.Effects)
        {
            effectStr += $"\n  {eff.Op}";
            if (eff.Amount.HasValue) effectStr += $" ({eff.Amount})";
            if (eff.Target != null) effectStr += $" → {eff.Target.Scope}";
        }
        _detailEffect.Text = effectStr;

        _detailUnequip.Show();
    }

    private void ClearDetail()
    {
        _detailName.Text = "";
        _detailDesc.Text = "";
        _detailEffect.Text = "";
        _detailCost.Text = "";
        _detailUnequip.Hide();
    }

    private void RefreshAll()
    {
        var page = CampaignContext.CurrentRunePage;

        // Refresh slot buttons
        foreach (var (type, _, _) in SlotGroups)
        {
            for (int i = 0; i < GetSlotCount(type); i++)
            {
                var rune = page.GetSlot(type, i);
                if (_slotButtons.TryGetValue((type, i), out var btn))
                {
                    if (rune != null)
                    {
                        btn.Text = $"{rune.Name}\n[{rune.Cost} RP]";
                        btn.Modulate = new Color(0.8f, 0.9f, 1f);
                    }
                    else
                    {
                        btn.Text = "—";
                        btn.Modulate = new Color(0.5f, 0.5f, 0.5f);
                    }
                }
            }
        }

        // Budget bar
        int total = page.TotalCost;
        _budgetBar.MaxValue = RunePage.MaxBudget;
        _budgetBar.Value = total;
        _budgetLabel.Text = $"RP: {total}/{RunePage.MaxBudget}";

        // Color budget bar
        float ratio = (float)total / RunePage.MaxBudget;
        _budgetBar.Modulate = ratio switch
        {
            < 0.5f => new Color(0.2f, 0.8f, 0.2f),    // green
            < 0.8f => new Color(0.8f, 0.8f, 0.2f),    // yellow
            _ => new Color(0.9f, 0.3f, 0.2f)          // red
        };

        ClearDetail();
    }

    private void OnSave()
    {
        CampaignContext.SaveManager.Save();
        ShowFeedback("Rune page saved!");
    }

    private void ShowFeedback(string msg, bool success = true)
    {
        _feedbackLabel.Text = msg;
        _feedbackLabel.Modulate = success
            ? new Color(0.4f, 1, 0.4f)
            : new Color(1, 0.4f, 0.4f);

        // Auto-clear after 2 seconds
        var timer = new Godot.Timer();
        timer.OneShot = true;
        timer.WaitTime = 2.0;
        timer.Timeout += () => _feedbackLabel.Text = "";
        AddChild(timer);
        timer.Start();
    }

    private bool IsRuneEquipped(string runeId)
    {
        return CampaignContext.CurrentRunePage.GetAllEquipped().Any(r => r.Id == runeId);
    }

    private static int GetSlotCount(RuneSlotType type) => type switch
    {
        RuneSlotType.OFFENSIVE => 9,
        RuneSlotType.DEFENSIVE => 9,
        RuneSlotType.UTILITY => 9,
        RuneSlotType.MYTHIC => 3,
        _ => 0
    };
}

/// <summary>
/// Extension method to retrieve a rune from a specific slot.
/// </summary>
internal static class RunePageExtensions
{
    public static RuneDef? GetSlot(this RunePage page, RuneSlotType type, int index)
    {
        var slots = type switch
        {
            RuneSlotType.OFFENSIVE => page.OffensiveSlots,
            RuneSlotType.DEFENSIVE => page.DefensiveSlots,
            RuneSlotType.UTILITY => page.UtilitySlots,
            RuneSlotType.MYTHIC => page.MythicSlots,
            _ => null
        };
        if (slots == null || index < 0 || index >= slots.Length) return null;
        return slots[index];
    }
}