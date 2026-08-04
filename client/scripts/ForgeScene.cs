using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Rune Forge scene — spend 4 fragments to forge an unowned rune.
/// Shows fragment counts per strata, available rune pool, and forge results.
/// </summary>
public partial class ForgeScene : Control
{
    private static readonly Color[] StrataColors = new[]
    {
        new Color(0.2f, 0.7f, 0.2f),   // VERDANT
        new Color(0.8f, 0.3f, 0.1f),   // EMBER
        new Color(0.2f, 0.5f, 0.8f),   // TIDE
        new Color(0.6f, 0.2f, 0.6f),   // HOLLOW
        new Color(0.9f, 0.8f, 0.2f),   // DAWN
    };

    private static readonly string[] StrataNames = { "verdant", "ember", "tide", "hollow", "dawn" };

    private Label _fragmentsLabel = default!;
    private Label _resultLabel = default!;
    private Button _backButton = default!;
    private readonly List<Button> _forgeButtons = new();

    // Current forge recipes: strata → list of forgeable rune IDs
    private Dictionary<string, List<string>> _forgeRecipes = new();

    public override void _Ready()
    {
        BuildUI();
        LoadRecipes();
        UpdateDisplay();
    }

    private void LoadRecipes()
    {
        string contentDir = ProjectSettings.GlobalizePath("res://") + "../content/forge";
        var path = $"{contentDir}/recipes.json";
        try
        {
            var json = System.IO.File.ReadAllText(path);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement.GetProperty("recipes");
            _forgeRecipes = new Dictionary<string, List<string>>();
            foreach (var prop in root.EnumerateObject())
            {
                var ids = prop.Value.EnumerateArray().Select(e => e.GetString()!).ToList();
                _forgeRecipes[prop.Name] = ids;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ForgeScene] Failed to load recipes: {ex.Message}");
        }
    }

    private void BuildUI()
    {
        // Background
        var bg = new ColorRect
        {
            Color = new Color(0.08f, 0.06f, 0.1f),
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f
        };
        AddChild(bg);

        // Title
        var title = new Label
        {
            Text = "Rune Forge",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 0.08f
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.Modulate = new Color(0.9f, 0.85f, 0.7f);
        AddChild(title);

        // Instructions
        var instructions = new Label
        {
            Text = "Spend 4 fragments to forge a random unowned rune of that strata.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.08f, AnchorBottom = 0.14f
        };
        instructions.AddThemeFontSizeOverride("font_size", 14);
        instructions.Modulate = new Color(0.6f, 0.55f, 0.5f);
        AddChild(instructions);

        // Fragment counts + forge buttons (one per strata)
        float startY = 0.16f;
        float rowHeight = 0.13f;

        for (int i = 0; i < StrataNames.Length; i++)
        {
            int index = i; // capture for lambda
            string strata = StrataNames[i];
            float top = startY + i * rowHeight;
            float bottom = top + rowHeight;

            // Color bar
            var bar = new ColorRect
            {
                Color = StrataColors[i],
                Size = new Vector2(8, 0),
                AnchorLeft = 0.03f, AnchorRight = 0.07f,
                AnchorTop = top + 0.02f, AnchorBottom = bottom - 0.02f
            };
            AddChild(bar);

            // Strata name
            var nameLabel = new Label
            {
                Text = strata.ToUpper(),
                AnchorLeft = 0.09f, AnchorRight = 0.45f,
                AnchorTop = top, AnchorBottom = top + 0.05f
            };
            nameLabel.AddThemeFontSizeOverride("font_size", 16);
            nameLabel.Modulate = new Color(0.8f, 0.8f, 0.9f);
            AddChild(nameLabel);

            // Fragment count label (updated in UpdateDisplay)
            var fragLabel = new Label
            {
                Name = $"frag_{strata}",
                Text = "0 fragments",
                AnchorLeft = 0.09f, AnchorRight = 0.45f,
                AnchorTop = top + 0.05f, AnchorBottom = bottom
            };
            fragLabel.AddThemeFontSizeOverride("font_size", 13);
            fragLabel.Modulate = new Color(0.6f, 0.6f, 0.7f);
            AddChild(fragLabel);

            // Forge button
            var forgeBtn = new Button
            {
                Text = "Forge",
                AnchorLeft = 0.55f, AnchorRight = 0.85f,
                AnchorTop = top + 0.02f, AnchorBottom = bottom - 0.02f
            };
            forgeBtn.Pressed += () => OnForgePressed(strata);
            AddChild(forgeBtn);
            _forgeButtons.Add(forgeBtn);
        }

        // Result label
        _resultLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.82f, AnchorBottom = 0.9f
        };
        _resultLabel.AddThemeFontSizeOverride("font_size", 16);
        _resultLabel.Modulate = new Color(0.9f, 0.85f, 0.8f);
        AddChild(_resultLabel);

        // Back button
        _backButton = new Button
        {
            Text = "Back",
            AnchorLeft = 0.35f, AnchorRight = 0.65f,
            AnchorTop = 0.92f, AnchorBottom = 0.98f
        };
        _backButton.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main/Main.tscn");
        AddChild(_backButton);
    }

    private void UpdateDisplay()
    {
        var prog = CampaignContext.Progression;

        foreach (var strata in StrataNames)
        {
            int frags = prog.Fragments.TryGetValue(strata, out var f) ? f : 0;
            var fragLabel = GetNodeOrNull<Label>($"frag_{strata}");
            if (fragLabel != null)
                fragLabel.Text = $"{frags} fragments";
        }

        // Update forge button states
        for (int i = 0; i < StrataNames.Length; i++)
        {
            if (i < _forgeButtons.Count)
            {
                var btn = _forgeButtons[i];
                int frags = prog.Fragments.TryGetValue(StrataNames[i], out var f) ? f : 0;
                btn.Disabled = frags < ForgeSystem.FragmentsPerForge
                    || !ForgeSystem.CanForge(StrataNames[i], prog, _forgeRecipes);
            }
        }
    }

    private void OnForgePressed(string strata)
    {
        var prog = CampaignContext.Progression;

        var (result, runeId) = ForgeSystem.Forge(strata, prog, CampaignContext.RuneIndex, _forgeRecipes);

        switch (result)
        {
            case ForgeResult.Success:
                string runeName = "Unknown Rune";
                if (runeId != null && CampaignContext.RuneIndex.TryGetValue(runeId, out var runeDef))
                    runeName = runeDef.Name;
                _resultLabel.Text = $"Forged: {runeName}! {StrataNames.First(s => s == strata)} fragments: {prog.Fragments[strata]}";
                // Also equip it to the current rune page if there's room
                if (runeId != null && CampaignContext.RuneIndex.TryGetValue(runeId, out var rune))
                    CampaignContext.CurrentRunePage.Equip(rune);
                break;

            case ForgeResult.InsufficientFragments:
                _resultLabel.Text = "Not enough fragments. You need 4.";
                break;

            case ForgeResult.AllRunesOwned:
                _resultLabel.Text = "You already own all runes of this strata!";
                break;

            case ForgeResult.InvalidStrata:
                _resultLabel.Text = "Cannot forge runes of this type.";
                break;
        }

        CampaignContext.SaveManager.Save();
        UpdateDisplay();
    }
}