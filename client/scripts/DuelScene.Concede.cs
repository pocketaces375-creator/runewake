using Godot;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// FABLE-035: a way out of a duel mid-game. Trikzos: "We should have a button to exit duels
/// mid game, an option in the corner that says forfeit/surrender/concede."
///
/// A quiet plate in the top-left corner (the one corner the board leaves empty). Tapping it
/// asks once — conceding is a loss and the fight's rewards are forfeited — and a confirmed
/// concede ends the duel through the engine's own game-over, so the player lands on the same
/// defeat screen as any loss (Try again / Back to the map), and every exit path, save and
/// telemetry hook that already works for a loss works for this.
///
/// Not offered in the guided first duel (it has its own "Skip tutorial") or on a co-op board
/// (the expedition decides the outcome for everyone at that table).
/// </summary>
public partial class DuelScene
{
    private Button? _concedeBtn;
    private Control? _concedeConfirm;
    private bool _conceded;

    private void BuildConcedeButton()
    {
        if (_isTutorialScriptMode || CoopSession.Current != null) return;
        float s = GetViewportRect().Size.Y / 1080f;
        _concedeBtn = new Button
        {
            Name = "ConcedeButton", Text = "⚑  Concede", FocusMode = FocusModeEnum.None,
            Position = new Vector2(22f * s, 20f * s), Size = new Vector2(196f * s, 60f * s),
            CustomMinimumSize = new Vector2(196f * s, 60f * s),
            MouseFilter = MouseFilterEnum.Stop, ZIndex = 60,
        };
        _concedeBtn.AddThemeStyleboxOverride("normal", MenuButtons.QuietNormal());
        _concedeBtn.AddThemeStyleboxOverride("hover", MenuButtons.Hover());
        _concedeBtn.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
        _concedeBtn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        _concedeBtn.AddThemeFontOverride("font", GetButtonFont((int)(24 * s)));
        _concedeBtn.AddThemeFontSizeOverride("font_size", (int)(24 * s));
        _concedeBtn.AddThemeColorOverride("font_color", Color.FromHtml("#CDBE9C"));
        _concedeBtn.AddThemeColorOverride("font_hover_color", Color.FromHtml("#F2DFA6"));
        _concedeBtn.Pressed += () =>
        {
            GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click");
            ShowConcedeConfirm();
        };
        MenuButtons.Animate(_concedeBtn);
        AddChild(_concedeBtn);
    }

    private void ShowConcedeConfirm()
    {
        if (_concedeConfirm != null || _gsm == null || _gsm.IsGameOver) return;
        var vp = GetViewportRect().Size;
        float s = vp.Y / 1080f;

        var root = new Control { Name = "ConcedeConfirm", ZIndex = 900, MouseFilter = MouseFilterEnum.Stop };
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.62f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        // A tap outside the panel is "never mind".
        dim.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } or InputEventScreenTouch { Pressed: false })
                CloseConcedeConfirm();
        };
        root.AddChild(dim);

        float pw = 760f * s, ph = 360f * s;
        var panel = new PanelContainer
        {
            Position = new Vector2((vp.X - pw) / 2f, (vp.Y - ph) / 2f), Size = new Vector2(pw, ph),
            CustomMinimumSize = new Vector2(pw, ph), MouseFilter = MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.075f, 0.066f, 0.055f, 0.97f), BorderColor = new Color(Ember.R, Ember.G, Ember.B, 0.9f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16, CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16,
            ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 18,
            ContentMarginLeft = 40 * s, ContentMarginRight = 40 * s, ContentMarginTop = 30 * s, ContentMarginBottom = 30 * s,
        });
        root.AddChild(panel);

        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", (int)(14 * s));
        panel.AddChild(col);

        Label L(string text, Font font, int size, Color color)
        {
            var l = new Label
            {
                Text = text, HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore,
            };
            l.AddThemeFontOverride("font", font);
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", color);
            col.AddChild(l);
            return l;
        }
        L("Concede the duel?", GetCardNameFont((int)(52 * s)), (int)(52 * s), Color.FromHtml("#E4C7A0"));
        L("It counts as a loss, and this fight's rewards are forfeited. You can try it again straight away.",
            GetBodyFont((int)(28 * s)), (int)(28 * s), TextSecondary);

        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", (int)(26 * s));
        col.AddChild(row);

        Button Plate(string text, bool danger)
        {
            var b = new Button
            {
                Text = text, FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Stop,
                CustomMinimumSize = new Vector2(290f * s, 88f * s),
            };
            b.AddThemeStyleboxOverride("normal", danger ? MenuButtons.QuietNormal() : MenuButtons.PrimaryNormal());
            b.AddThemeStyleboxOverride("hover", danger ? MenuButtons.Hover() : MenuButtons.PrimaryHover());
            b.AddThemeStyleboxOverride("pressed", MenuButtons.Pressed());
            b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            b.AddThemeFontOverride("font", GetButtonFont((int)(32 * s)));
            b.AddThemeFontSizeOverride("font_size", (int)(32 * s));
            b.AddThemeColorOverride("font_color", danger ? Color.FromHtml("#E08B78") : Color.FromHtml("#F2DFA6"));
            MenuButtons.Animate(b);
            row.AddChild(b);
            return b;
        }
        var keep = Plate("Keep fighting", false);
        var yes = Plate("Concede", true);
        keep.Pressed += () => { GetNodeOrNull<AudioManager>("/root/AudioManager")?.PlaySfx("click"); CloseConcedeConfirm(); };
        yes.Pressed += Concede;

        AddChild(root);
        _concedeConfirm = root;
        MenuButtons.RevealStagger(new Control[] { panel }, 0f);
    }

    private void CloseConcedeConfirm()
    {
        _concedeConfirm?.QueueFree();
        _concedeConfirm = null;
    }

    /// <summary>End the duel as a loss for the player, through the engine's own game-over.</summary>
    private void Concede()
    {
        CloseConcedeConfirm();
        if (_gsm == null || _gsm.IsGameOver) return;
        _conceded = true;
        ExitTrace("concede: player conceded the duel");
        if (_concedeBtn != null) _concedeBtn.Visible = false;
        _gsm.Concede(0);
    }
}
