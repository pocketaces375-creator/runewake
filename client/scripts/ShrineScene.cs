using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-030: the Shrine. A quiet place on the road where a Delver rests and takes ONE thing:
///
///   Rest       — +25 shards, no strings.
///   Offering   — give 15 shards, take a dig charge for a dig site ahead.
///   Blessing   — +4 Vigor in your next fight (spent when that fight starts).
///
/// One choice per shrine; the node is then cleared like any other. The three cards are built
/// like the Crossroads cards so the game keeps one visual language for "choose a road".
/// </summary>
public partial class ShrineScene : Control
{
    public const string ScenePath = "res://scenes/shrine/ShrineScene.tscn";
    private static readonly Color Parchment = new(0.91f, 0.86f, 0.78f);
    private bool _chosen;

    public override void _Ready() => SceneGuard.Build(this, "ShrineScene", ReadyBody, CampaignRun.MapScenePath, "Back to the map");

    private void ReadyBody()
    {
        var vp = GetViewportRect().Size;
        var prog = CampaignContext.Progression!;

        var bg = new ColorRect { Color = new Color(0.05f, 0.045f, 0.035f) };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var glow = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = Grad(new Color(0.95f, 0.78f, 0.40f, 0.30f), new Color(0.95f, 0.78f, 0.40f, 0f)),
                Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.35f), FillTo = new Vector2(0.5f, 1f), Width = 64, Height = 64,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore,
        };
        glow.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(glow);

        var kicker = Text("A QUIET PLACE", 0, 26, 28, ThemeTokens.GetHeaderFont(28), new Color(0.75f, 0.68f, 0.52f));
        var title = Text("The Shrine", 0, 60, 84, ThemeTokens.GetCardNameFont(84), ThemeTokens.Gold);
        Text("The road is long. Rest here, and take one thing with you.", 0, 160, 36, ThemeTokens.GetBodyFont(36), Parchment);
        var shards = Text($"Shards: {prog.Shards}   ·   Dig charges: {prog.DigCharges}", 0, 208, 28, ThemeTokens.GetBodyFont(28), new Color(0.7f, 0.65f, 0.55f));

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Position = new Vector2(0, 280), Size = new Vector2(vp.X, 300) };
        row.AddThemeConstantOverride("separation", 36);
        AddChild(row);

        row.AddChild(Card("Rest", "REST", "+25 shards", new Color(0.92f, 0.76f, 0.36f), () =>
        {
            prog.Shards += 25;
            Choose("You rest by the shrine. +25 shards.");
        }));
        bool canOffer = prog.Shards >= 15;
        row.AddChild(Card("Offering", "GIVE 15 SHARDS", canOffer ? "+1 dig charge" : "you need 15 shards", new Color(0.30f, 0.62f, 0.32f), () =>
        {
            if (!prog.SpendShards(15)) return;
            prog.DigCharges += 1;
            Choose("The shrine takes your offering. +1 dig charge.");
        }, enabled: canOffer));
        row.AddChild(Card("Blessing", "NEXT FIGHT", "+4 Vigor", new Color(0.20f, 0.52f, 0.78f), () =>
        {
            ShrineBlessing.Set(prog, "vigor", 4);
            Choose("The shrine's warmth stays with you. +4 Vigor in your next fight.");
        }));

        var back = new Button { Text = "◀  Back to the map", Position = new Vector2(80, vp.Y - 120), Size = new Vector2(360, 84) };
        WorldMapScene.StyleButton(back);
        back.Pressed += () => Leave("left without choosing");
        AddChild(back);

        MenuButtons.BreatheTitle(title);
        MenuButtons.RevealStagger(new Control[] { kicker, title, row, shards });
    }

    private void Choose(string line)
    {
        if (_chosen) return;
        _chosen = true;
        var prog = CampaignContext.Progression!;
        if (CampaignContext.CurrentNodeId is string id) prog.MarkNodeCleared(id);
        try { CampaignContext.SaveManager?.Save(); }
        catch (Exception ex) { GD.PrintErr($"[Shrine] save failed, continuing: {ex.Message}"); }
        GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("unlock");

        var note = Text(line, 0, GetViewportRect().Size.Y - 230, 34, ThemeTokens.GetBodyFont(34), ThemeTokens.Gold);
        note.Modulate = new Color(1, 1, 1, 0);
        note.CreateTween().TweenProperty(note, "modulate:a", 1f, 0.4f);
        GetTree().CreateTimer(1.6).Timeout += () => Leave("chose");
    }

    private void Leave(string why)
    {
        DuelScene.ExitTrace($"shrine: {why} → MapScene");
        GetTree().ChangeSceneToFile(CampaignRun.MapScenePath);
    }

    private Label Text(string text, float x, float y, int size, Font font, Color color)
    {
        var l = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Position = new Vector2(x, y), Size = new Vector2(GetViewportRect().Size.X - 2 * x, size + 16), MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        AddChild(l);
        return l;
    }

    private static Gradient Grad(Color a, Color b)
    {
        var g = new Gradient();
        g.SetColor(0, a);
        g.SetColor(1, b);
        return g;
    }

    private Control Card(string name, string kind, string what, Color tint, Action onPress, bool enabled = true)
    {
        var btn = new Button { CustomMinimumSize = new Vector2(520, 280), FocusMode = FocusModeEnum.None, Disabled = !enabled };
        StyleBoxFlat Box(float a) => new()
        {
            BgColor = new Color(tint.R * 0.30f, tint.G * 0.30f, tint.B * 0.30f, a),
            BorderColor = new Color(tint.R, tint.G, tint.B, enabled ? 0.9f : 0.35f),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 14, CornerRadiusTopRight = 14, CornerRadiusBottomLeft = 14, CornerRadiusBottomRight = 14,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 8,
        };
        btn.AddThemeStyleboxOverride("normal", Box(0.85f));
        btn.AddThemeStyleboxOverride("hover", Box(1f));
        btn.AddThemeStyleboxOverride("pressed", Box(0.7f));
        btn.AddThemeStyleboxOverride("disabled", Box(0.5f));
        btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        col.SetAnchorsPreset(LayoutPreset.FullRect);
        col.AddThemeConstantOverride("separation", 10);
        btn.AddChild(col);
        foreach (var (text, size, font, color) in new[]
        {
            (kind, 22, ThemeTokens.GetHeaderFont(22), new Color(0.8f, 0.75f, 0.62f)),
            (name, 48, ThemeTokens.GetCardNameFont(48), enabled ? ThemeTokens.Gold : new Color(0.6f, 0.55f, 0.45f)),
            (what, 28, ThemeTokens.GetBodyFont(28), Parchment),
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
}
