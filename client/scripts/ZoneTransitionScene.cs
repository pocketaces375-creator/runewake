using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: the screen between zones.
///
/// Trikzos: "We should have continue chain through to the next maps. It should
/// say and clarify on the screen: 'you've reached a new zone with new monsters
/// and obstacles, continue?'"
///
/// CampaignRun.AdvanceAfterVictory has already armed the new zone's first duel
/// (CurrentNodeId / CurrentEncounter) before this scene loads, so Continue is a
/// plain scene change and the map button simply shows the new zone's map.
/// </summary>
public partial class ZoneTransitionScene : Control
{
    public const string ScenePath = "res://scenes/zone/ZoneTransitionScene.tscn";

    /// <summary>The zone's primary stratum as a colour wash (board skins are mostly "default").</summary>
    private static Color StrataColour(string? strata) => (strata ?? "").ToUpperInvariant() switch
    {
        "VERDANT" => new Color(0.30f, 0.62f, 0.32f),
        "EMBER" => new Color(0.86f, 0.40f, 0.16f),
        "TIDE" => new Color(0.20f, 0.52f, 0.78f),
        "HOLLOW" => new Color(0.50f, 0.34f, 0.70f),
        "DAWN" => new Color(0.92f, 0.76f, 0.36f),
        _ => new Color(0.6f, 0.55f, 0.45f),
    };

    public override void _Ready() => SceneGuard.Build(this, "ZoneTransitionScene", ReadyBody, CampaignRun.MapScenePath, "Back to the map");

    private void ReadyBody()
    {
        // A world page/area (ZoneInfo) or a campaign region (PendingZone).
        var info = CampaignContext.PendingZoneInfo;
        CampaignContext.PendingZoneInfo = null;
        var zone = CampaignContext.PendingZone;
        string zoneName = info?.Title ?? zone?.Name ?? "a new zone";
        var tint = StrataColour(info?.Strata ?? zone?.Strata);
        bool offerCrossroads = CampaignContext.CrossroadsJustOpened && info?.ContinuePath != WorldService.CrossroadsScenePath;
        CampaignContext.CrossroadsJustOpened = false;

        DuelScene.ExitTrace($"zone step 1: read pending zone ({zoneName}, crossroads offer {offerCrossroads})");
        var bg = new ColorRect { Color = new Color(0.05f, 0.045f, 0.035f), MouseFilter = MouseFilterEnum.Ignore };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // A soft wash of the new zone's colour rising from the floor.
        var grad = new Gradient();
        grad.SetColor(0, new Color(tint.R, tint.G, tint.B, 0.0f));
        grad.SetColor(1, new Color(tint.R * 0.7f, tint.G * 0.7f, tint.B * 0.7f, 0.65f));
        var wash = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = grad, FillFrom = new Vector2(0, 0), FillTo = new Vector2(0, 1), Width = 4, Height = 64 },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        wash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(wash);

        DuelScene.ExitTrace($"zone step 2: background and colour wash built");
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.AnchorLeft = 0.15f; col.AnchorRight = 0.85f;
        col.OffsetLeft = col.OffsetRight = 0;
        col.AddThemeConstantOverride("separation", 22);
        AddChild(col);

        Label L(string text, Font? font, int size, Color c)
        {
            var l = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            if (font != null) l.AddThemeFontOverride("font", font);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", c);
            l.AddThemeConstantOverride("outline_size", 3);
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
            col.AddChild(l);
            return l;
        }

        L(info?.Kicker ?? "A NEW ZONE", ThemeTokens.GetHeaderFont(30), 30, new Color(0.75f, 0.68f, 0.52f));
        var title = L(zoneName, ThemeTokens.GetCardNameFont(84), 84, ThemeTokens.Gold);
        L(info?.Line ?? "You've reached a new zone with new monsters and obstacles.", ThemeTokens.GetBodyFont(40), 40, new Color(0.91f, 0.86f, 0.78f));
        L("Continue?", ThemeTokens.GetBodyFont(40), 40, new Color(0.91f, 0.86f, 0.78f));
        if (offerCrossroads)
            L($"{WorldService.Atlas.HubName} has opened — the endless world is waiting.", ThemeTokens.GetBodyFont(30), 30, ThemeTokens.Gold);

        DuelScene.ExitTrace($"zone step 3: text built");
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 28);
        col.AddChild(row);

        Button Btn(string text)
        {
            var b = new Button { Text = text, CustomMinimumSize = new Vector2(360, 96) };
            b.AddThemeFontOverride("font", ThemeTokens.GetButtonFont(38));
            b.AddThemeFontSizeOverride("font_size", 38);
            b.AddThemeColorOverride("font_color", new Color(0.91f, 0.86f, 0.78f));
            b.AddThemeStyleboxOverride("normal", MenuButtons.Normal());
            b.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
            b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
            MenuButtons.Animate(b);
            row.AddChild(b);
            return b;
        }

        string firstFight = CampaignContext.CurrentEncounter?.Name ?? "";
        string goLabel = info != null ? info.ContinueLabel : (string.IsNullOrEmpty(firstFight) ? "Continue" : "Continue — " + firstFight);
        string goPath = info?.ContinuePath ?? CampaignRun.DuelScenePath;
        var go = Btn(goLabel);
        go.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        Button? cross = offerCrossroads ? Btn("The Crossroads") : null;
        var map = Btn("View the map");

        DuelScene.ExitTrace($"zone step 4: buttons built");
        bool left = false;
        void Leave(string path, string why)
        {
            if (left) return;
            left = true;
            DuelScene.ExitTrace($"zone transition: {why} → {path.GetFile()}");
            DuelScene.StartHangCheck(path);
            GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
            var err = GetTree().ChangeSceneToFile(path);
            if (err != Error.Ok)
            {
                DuelScene.ExitTrace($"zone transition: change failed ({err})");
                left = false;
            }
        }
        go.Pressed += () => Leave(goPath, goLabel);
        if (cross != null) cross.Pressed += () => Leave(WorldService.CrossroadsScenePath, "The Crossroads");
        map.Pressed += () => Leave(info?.ContinuePath == WorldService.WorldMapScenePath ? WorldService.WorldMapScenePath : CampaignRun.MapScenePath, "View the map");

        DuelScene.ExitTrace($"zone step 5: buttons wired");
        MenuButtons.BreatheTitle(title);
        MenuButtons.RevealStagger(cross != null ? new Control[] { title, go, cross, map } : new Control[] { title, go, map });
        DuelScene.ExitTrace("zone step 6: animations started");
    }
}
