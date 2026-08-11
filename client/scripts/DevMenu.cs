using System;
using System.Linq;
using Godot;
using Runewake.Engine.Cards;
using Runewake.Engine.State;

namespace Runewake.Client;

/// <summary>
/// DEV-ONLY menu — accessible from the title screen via a small grey button.
/// Provides shortcuts for testing Phase 5 features without playing through
/// the entire campaign. REMOVE THIS FILE BEFORE RELEASE.
/// </summary>
public partial class DevMenu : Control
{
    private Label _statusLabel = default!;

    public override void _Ready()
    {
        AnchorLeft = 0; AnchorRight = 1;
        AnchorTop = 0; AnchorBottom = 1;
        MouseFilter = MouseFilterEnum.Stop;

        // Dim overlay
        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.7f),
            AnchorLeft = 0, AnchorRight = 1,
            AnchorTop = 0, AnchorBottom = 1
        };
        AddChild(dim);

        // Panel
        var panel = new ColorRect
        {
            Color = new Color(0.08f, 0.05f, 0.12f, 0.95f),
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.1f, AnchorBottom = 0.9f
        };
        AddChild(panel);

        // Title
        var title = new Label
        {
            Text = "DEV MENU — REMOVE BEFORE RELEASE",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.02f, AnchorBottom = 0.08f
        };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
        panel.AddChild(title);

        // Status label
        _statusLabel = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0.05f, AnchorRight = 0.95f,
            AnchorTop = 0.08f, AnchorBottom = 0.14f,
            AutowrapMode = TextServer.AutowrapMode.Word
        };
        _statusLabel.AddThemeFontSizeOverride("font_size", 12);
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.5f));
        panel.AddChild(_statusLabel);

        // Button container
        var vbox = new VBoxContainer
        {
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.15f, AnchorBottom = 0.85f
        };
        panel.AddChild(vbox);

        AddButton(vbox, "Jump to Warden Boss", () =>
        {
            var prog = CampaignContext.Progression;
            if (prog == null) { SetStatus("No progression loaded"); return; }

            // Unlock all nodes so the boss is reachable
            var regionJson = Godot.FileAccess.GetFileAsString("res://content/map/region_01.json");
            var region = MapLoader.LoadRegionFromString(regionJson);
            if (region != null)
            {
                foreach (var node in region.Nodes)
                    prog.ClearedNodes.Add(node.Id);
            }
            // Also clear the Warden (r1_n11) and WardenBoss (r1_n12) directly
            prog.ClearedNodes.Add("r1_n09");
            prog.ClearedNodes.Add("r1_n10");
            prog.ClearedNodes.Add("r1_n11");
            prog.ClearedNodes.Add("r1_n12");

            // Set the current encounter to the boss
            CampaignContext.CurrentNodeId = "r1_n12";

            // Give enough dig charges and shards to make it feel real
            prog.DigCharges += 10;
            prog.Shards += 500;

            // Save
            CampaignContext.SaveManager.Save();

            // Go straight to the duel
            GetTree().ChangeSceneToFile("res://scenes/duel/DuelScene.tscn");
        });

        AddButton(vbox, "Grant Dig Charges (+10)", () =>
        {
            var prog = CampaignContext.Progression;
            if (prog == null) { SetStatus("No progression"); return; }
            prog.DigCharges += 10;
            CampaignContext.SaveManager.Save();
            SetStatus("+10 dig charges granted. Saved.");
        });

        AddButton(vbox, "Grant Fragments (20 each)", () =>
        {
            var prog = CampaignContext.Progression;
            if (prog == null) { SetStatus("No progression"); return; }
            foreach (var strata in new[] { "verdant", "ember", "tide", "hollow", "dawn" })
                prog.AddFragments(strata, 20);
            CampaignContext.SaveManager.Save();
            SetStatus("+20 fragments per strata granted. Saved.");
        });

        AddButton(vbox, "Unlock All Nodes", () =>
        {
            var prog = CampaignContext.Progression;
            if (prog == null) { SetStatus("No progression"); return; }
            var regionJson = Godot.FileAccess.GetFileAsString("res://content/map/region_01.json");
            var region = MapLoader.LoadRegionFromString(regionJson);
            if (region == null) { SetStatus("No map loaded"); return; }
            foreach (var node in region.Nodes)
                prog.ClearedNodes.Add(node.Id);
            CampaignContext.SaveManager.Save();
            SetStatus("All nodes unlocked. Saved.");
        });

        AddButton(vbox, "Clear Save & Reset", () =>
        {
            CampaignContext.SaveManager.DeleteSave();
            SetStatus("Save deleted. Relaunch to start fresh.");
        });

        // Close button
        var closeBtn = new Button
        {
            Text = "Close Dev Menu",
            AnchorLeft = 0.2f, AnchorRight = 0.8f,
            AnchorTop = 0.88f, AnchorBottom = 0.97f
        };
        closeBtn.Pressed += () => QueueFree();
        panel.AddChild(closeBtn);
    }

    private static void AddButton(VBoxContainer vbox, string text, Action action)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 40),
            SizeFlagsHorizontal = (Control.SizeFlags)3
        };
        btn.AddThemeFontSizeOverride("font_size", 13);
        btn.Pressed += action;
        vbox.AddChild(btn);
    }

    private void SetStatus(string msg)
    {
        _statusLabel.Text = msg;
    }
}