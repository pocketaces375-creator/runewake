using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// Forge scene — convert rune fragments into runes.
/// Each strata has a set of forgeable runes. 4 fragments of a strata forge 1 rune.
/// </summary>
public partial class ForgeScene : Control
{
    private ForgeRecipeBook _recipes = new();
    private readonly List<(string strata, int count)> _fragmentList = new();
    private Label _fragmentLabel = default!;
    private VBoxContainer _runeList = default!;

    public override void _Ready()
    {
        AnchorLeft = 0; AnchorRight = 1;
        AnchorTop = 0; AnchorBottom = 1;

        LoadRecipes();
        GatherFragments();
        BuildUI();
    }

    private void LoadRecipes()
    {
        try
        {
            string json = Godot.FileAccess.GetFileAsString("res://content/forge/recipes.json");
            if (!string.IsNullOrEmpty(json))
                _recipes = ForgeLoader.LoadPackFromString(json);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ForgeScene] Failed to load recipes: {ex.Message}");
        }
    }

    private void GatherFragments()
    {
        var prog = CampaignContext.Progression;
        if (prog?.Fragments == null) return;

        foreach (var kv in prog.Fragments)
        {
            if (kv.Value > 0)
                _fragmentList.Add((kv.Key, kv.Value));
        }
    }

    private void BuildUI()
    {
        // Background
        var bg = new ColorRect
        {
            Color = new Color(0.06f, 0.06f, 0.15f),
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        };
        AddChild(bg);

        // Title
        var title = new Label
        {
            Text = "FORGE",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.02f, AnchorBottom = 0.08f
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f));
        AddChild(title);

        // Fragment display
        _fragmentLabel = new Label
        {
            Text = "Fragments: " + string.Join(", ", _fragmentList.Select(f => $"{f.strata}: {f.count}")),
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.08f, AnchorBottom = 0.12f,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _fragmentLabel.AddThemeFontSizeOverride("font_size", 12);
        _fragmentLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
        AddChild(_fragmentLabel);

        // Back button
        var backBtn = new Button
        {
            Text = "Back",
            AnchorLeft = 0.02f, AnchorRight = 0.12f,
            AnchorTop = 0.02f, AnchorBottom = 0.07f
        };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/map/MapScene.tscn");
        AddChild(backBtn);

        // Scrollable rune list
        var scroll = new ScrollContainer
        {
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.14f, AnchorBottom = 0.92f
        };
        AddChild(scroll);

        _runeList = new VBoxContainer();
        _runeList.SizeFlagsHorizontal = (Control.SizeFlags)3;
        scroll.AddChild(_runeList);

        PopulateRuneList();
    }

    private void PopulateRuneList()
    {
        foreach (var child in _runeList.GetChildren())
            child.QueueFree();

        var prog = CampaignContext.Progression;
        if (prog == null) return;

        foreach (var kv in _recipes.Recipes)
        {
            string strata = kv.Key;
            var runeIds = kv.Value;
            int fragCount = prog.Fragments.GetValueOrDefault(strata, 0);

            // Section header
            var header = new Label
            {
                Text = $"{strata.ToUpper()} ({fragCount}/4 fragments)",
                AnchorLeft = 0, AnchorRight = 1
            };
            header.AddThemeFontSizeOverride("font_size", 14);
            header.AddThemeColorOverride("font_color", strata switch
            {
                "verdant" => new Color(0.3f, 0.9f, 0.3f),
                "ember" => new Color(0.9f, 0.4f, 0.2f),
                "tide" => new Color(0.3f, 0.5f, 0.9f),
                "hollow" => new Color(0.6f, 0.3f, 0.7f),
                "dawn" => new Color(0.9f, 0.8f, 0.3f),
                _ => new Color(0.7f, 0.7f, 0.8f)
            });
            _runeList.AddChild(header);

            bool canForge = fragCount >= 4;

            foreach (var runeId in runeIds)
            {
                var rune = FindRuneDef(runeId);
                if (rune == null) continue;

                bool alreadyOwned = prog.OwnedRuneIds.Contains(runeId);
                bool available = canForge && !alreadyOwned;

                var btn = new Button
                {
                    Text = $"[{rune.RpCost}RP] {rune.Name} — {rune.Description}" +
                           (alreadyOwned ? " (OWNED)" : ""),
                    CustomMinimumSize = new Vector2(0, 36),
                    SizeFlagsHorizontal = (Control.SizeFlags)3,
                    Disabled = !available
                };
                btn.AddThemeFontSizeOverride("font_size", 11);

                if (available)
                {
                    var capturedRuneId = runeId;
                    var capturedStrata = strata;
                    btn.Pressed += () =>
                    {
                        // Forge the rune: spend 4 fragments, add to owned
                        prog.AddFragments(capturedStrata, -4);
                        prog.AddOwnedRune(capturedRuneId);
                        CampaignContext.SaveManager.Save();
                        // Refresh
                        GatherFragments();
                        PopulateRuneList();
                        _fragmentLabel.Text = "Fragments: " + string.Join(", ",
                            _fragmentList.Select(f => $"{f.strata}: {f.count}"));
                    };
                }

                _runeList.AddChild(btn);
            }
        }
    }

    private RuneDef? FindRuneDef(string runeId)
    {
        // Try loading from starter_runes.json embedded
        try
        {
            string json = Godot.FileAccess.GetFileAsString("res://content/runes/starter_runes.json");
            if (!string.IsNullOrEmpty(json))
            {
                var pack = RuneLoader.LoadPackFromString(json);
                return pack.Runes.FirstOrDefault(r => r.Id == runeId);
            }
        }
        catch { }
        return null;
    }
}