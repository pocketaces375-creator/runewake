using System.Linq;
using Godot;
using Runewake.Engine.World;

namespace Runewake.Client;

/// <summary>
/// FABLE-020: the Crossroads of Seals — the hub where the starting lands end
/// and the open world begins. Every open biome is a road from here; hidden
/// biomes join the list once a Gateway has been found. The Tower stands here too.
/// </summary>
public partial class CrossroadsScene : Control
{
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);

    public override void _Ready()
    {
        DuelScene.ExitTrace("arrived: CrossroadsScene");
        var vp = GetViewportRect().Size;
        var progress = WorldService.Progress;
        var atlas = WorldService.Atlas;

        var bg = new ColorRect { Color = new Color(0.05f, 0.045f, 0.035f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var fog = new TextureRect
        {
            Texture = new NoiseTexture2D { Width = 512, Height = 256, Seamless = true, Noise = new FastNoiseLite { Seed = 7, Frequency = 0.01f } },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Modulate = new Color(0.9f, 0.75f, 0.4f, 0.15f), MouseFilter = MouseFilterEnum.Ignore,
        };
        fog.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(fog);

        var title = new Label { Text = atlas.HubName, HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, 24), Size = new Vector2(vp.X, 90) };
        title.AddThemeFontOverride("font", ThemeTokens.GetCardNameFont(72));
        title.AddThemeFontSizeOverride("font_size", 72);
        title.AddThemeColorOverride("font_color", ThemeTokens.Gold);
        AddChild(title);
        var sub = new Label
        {
            Text = progress.CrossroadsOpen
                ? "Every road runs on forever. Every place you find, you find for everyone."
                : "Finish the starting lands to open the Crossroads.",
            HorizontalAlignment = HorizontalAlignment.Center, Position = new Vector2(0, 112), Size = new Vector2(vp.X, 44),
        };
        sub.AddThemeFontOverride("font", ThemeTokens.GetBodyFont(32));
        sub.AddThemeFontSizeOverride("font_size", 32);
        sub.AddThemeColorOverride("font_color", Parchment);
        AddChild(sub);

        var grid = new GridContainer { Columns = 4, Position = new Vector2(80, 190), Size = new Vector2(vp.X - 160, vp.Y - 330) };
        grid.AddThemeConstantOverride("h_separation", 26);
        grid.AddThemeConstantOverride("v_separation", 26);
        AddChild(grid);

        var areas = progress.CrossroadsAreas.Select(a => a.Biome)
            .Concat(progress.OpenedAreas.Select(a => a.Biome)).Distinct().ToList();
        foreach (var biomeId in areas)
        {
            var b = atlas.Biome(biomeId);
            // The deepest page of this biome the player can enter.
            var gen = WorldService.Generator;
            var deepest = new PageAddress(biomeId, 0, 0);
            var probe = deepest;
            for (int guard = 0; guard < 2000; guard++)
            {
                var next = gen.NextPage(probe);
                if (!progress.IsPageOpen(next)) break;
                probe = next;
            }
            deepest = probe;
            grid.AddChild(Card(b.Name, b.Hidden ? "A hidden land" : Strata(b.Strata),
                $"Area {deepest.Instance + 1} · Page {deepest.Page + 1}", Tint(b.Strata),
                () => { WorldService.CurrentPage = deepest; Go(WorldService.WorldMapScenePath); }));
        }
        grid.AddChild(Card("The Tower", "One hundred floors", "Floor 1 · The Rootgate", new Color(0.75f, 0.72f, 0.66f),
            () => Go(TowerScene.ScenePath)));

        var back = new Button { Text = "◀  Back to the map", Position = new Vector2(80, vp.Y - 120), Size = new Vector2(360, 84) };
        WorldMapScene.StyleButton(back);
        back.Pressed += () => Go(CampaignRun.MapScenePath);
        AddChild(back);

        if (WorldService.LastPage is PageAddress last && progress.IsPageOpen(last))
        {
            var cont = new Button { Text = $"Continue — {atlas.Biome(last.Biome).Name}, area {last.Instance + 1}", Position = new Vector2(vp.X - 900, vp.Y - 120), Size = new Vector2(820, 84) };
            WorldMapScene.StyleButton(cont, gold: true);
            cont.Pressed += () => { WorldService.CurrentPage = last; Go(WorldService.WorldMapScenePath); };
            AddChild(cont);
        }
    }

    private static string Strata(string s) => s switch
    {
        "VERDANT" => "Verdant", "EMBER" => "Ember", "TIDE" => "Tide", "HOLLOW" => "Hollow", "DAWN" => "Dawn", _ => s,
    };

    private static Color Tint(string strata) => strata switch
    {
        "VERDANT" => new Color(0.30f, 0.62f, 0.32f), "EMBER" => new Color(0.86f, 0.40f, 0.16f),
        "TIDE" => new Color(0.20f, 0.52f, 0.78f), "HOLLOW" => new Color(0.50f, 0.34f, 0.70f),
        "DAWN" => new Color(0.92f, 0.76f, 0.36f), _ => new Color(0.6f, 0.55f, 0.45f),
    };

    private Control Card(string name, string kind, string where, Color tint, System.Action onPress)
    {
        var btn = new Button { CustomMinimumSize = new Vector2(500, 270), FocusMode = FocusModeEnum.None };
        StyleBoxFlat Box(float a) => new()
        {
            BgColor = new Color(tint.R * 0.30f, tint.G * 0.30f, tint.B * 0.30f, a),
            BorderColor = new Color(tint.R, tint.G, tint.B, 0.9f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 8,
        };
        btn.AddThemeStyleboxOverride("normal", Box(0.85f));
        btn.AddThemeStyleboxOverride("hover", Box(1f));
        btn.AddThemeStyleboxOverride("pressed", Box(0.7f));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 8);
        btn.AddChild(col);
        foreach (var (text, size, font, color) in new[]
        {
            (kind.ToUpperInvariant(), 22, ThemeTokens.GetHeaderFont(22), new Color(0.8f, 0.75f, 0.62f)),
            (name, 44, ThemeTokens.GetCardNameFont(44), ThemeTokens.Gold),
            (where, 26, ThemeTokens.GetBodyFont(26), Parchment),
        })
        {
            var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore };
            l.AddThemeFontOverride("font", font); l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            col.AddChild(l);
        }
        btn.Pressed += () => { GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click"); onPress(); };
        return btn;
    }

    private void Go(string path)
    {
        DuelScene.ExitTrace($"crossroads → {path.GetFile()}");
        GetTree().ChangeSceneToFile(path);
    }
}
