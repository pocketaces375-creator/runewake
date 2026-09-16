using System;
using Godot;

namespace Runewake.Client;

/// <summary>The three lines of a refusal.</summary>
public readonly record struct DenyText(string Title, string Body, string Hint, bool PointAtEndTurn = false);

/// <summary>
/// FABLE-002: The "why can't I do that?" panel.
///
/// Every refused action in the duel (tapping a card you can't afford, a full
/// field, a taken lane, attacking with a resting creature, acting on the
/// opponent's turn...) goes through here instead of the tiny centre toast.
/// Each refusal has three lines the player actually reads:
///
///     TITLE      what happened, in three words or fewer
///     body       the concrete reason, with the real numbers
///     hint       the one thing to do next
///
/// The panel never blocks input (MouseFilter.Ignore everywhere) — it is
/// feedback, not a modal. It fades on its own. A new refusal replaces the
/// old one immediately.
///
/// <see cref="FromEngineMessage"/> turns the engine's ActionResult.ErrorMessage
/// strings into these three lines, so the engine remains the sole authority on
/// legality and this class only decides how to say it.
/// </summary>
public partial class DenyPopup : Control
{
    // ── Reference-height geometry (1080) ──
    private const float RefH = 1080f;
    private const float PanelW = 820f;
    private const float PadX = 34f;
    private const float PadY = 22f;
    private const int TitleSize = 36;
    private const int BodySize = 30;
    private const int HintSize = 25;
    private const float HoldSeconds = 2.8f;
    private const float FadeSeconds = 0.5f;

    private static readonly Color BgColor = new Color(0.086f, 0.071f, 0.055f, 0.96f);   // #16120E
    private static readonly Color BorderHard = new Color(0.66f, 0.23f, 0.16f);           // ember red
    private static readonly Color BorderSoft = new Color(0.79f, 0.66f, 0.30f);           // gold
    private static readonly Color TitleHard = new Color(0.95f, 0.45f, 0.35f);
    private static readonly Color TitleSoft = new Color(0.90f, 0.75f, 0.35f);
    private static readonly Color BodyColor = new Color(0.91f, 0.86f, 0.78f);            // parchment
    private static readonly Color HintColor = new Color(0.72f, 0.66f, 0.54f);

    private Panel _panel = default!;
    private StyleBoxFlat _style = default!;
    private Label _title = default!;
    private Label _body = default!;
    private Label _hint = default!;
    private Tween? _tween;
    private float _s = 1f;

    /// <summary>Optional: the End Turn button, pulsed when a hint says "press END TURN".</summary>
    public Button? EndTurnButton { get; set; }
    private Tween? _endTurnPulse;

    public override void _Ready()
    {
        Name = "DenyPopup";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 200;
        Visible = false;

        _s = GetViewportRect().Size.Y / RefH;

        _style = new StyleBoxFlat
        {
            BgColor = BgColor,
            BorderColor = BorderSoft,
            BorderWidthLeft = Mathf.RoundToInt(3 * _s),
            BorderWidthRight = Mathf.RoundToInt(3 * _s),
            BorderWidthTop = Mathf.RoundToInt(3 * _s),
            BorderWidthBottom = Mathf.RoundToInt(3 * _s),
            CornerRadiusTopLeft = Mathf.RoundToInt(8 * _s),
            CornerRadiusTopRight = Mathf.RoundToInt(8 * _s),
            CornerRadiusBottomLeft = Mathf.RoundToInt(8 * _s),
            CornerRadiusBottomRight = Mathf.RoundToInt(8 * _s),
            ShadowColor = new Color(0, 0, 0, 0.7f),
            ShadowSize = Mathf.RoundToInt(18 * _s),
            ShadowOffset = new Vector2(0, 6 * _s),
        };

        // FABLE-003: plain Panel, labels laid out by hand (UiText) so the panel is
        // exactly as tall as its three lines — no container min-size games.
        _panel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _panel.AddThemeStyleboxOverride("panel", _style);
        AddChild(_panel);

        _title = MakeLabel(ThemeTokens.GetHeaderFont(Mathf.RoundToInt(TitleSize * _s)), TitleSize, TitleSoft);
        _body = MakeLabel(ThemeTokens.GetButtonFont(Mathf.RoundToInt(BodySize * _s)), BodySize, BodyColor);
        _hint = MakeLabel(ThemeTokens.GetBodyFont(Mathf.RoundToInt(HintSize * _s)), HintSize, HintColor);
        _panel.AddChild(_title);
        _panel.AddChild(_body);
        _panel.AddChild(_hint);
    }

    private Label MakeLabel(Font? font, int refSize, Color color)
    {
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(refSize * _s));
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        l.AddThemeConstantOverride("shadow_offset_y", Mathf.RoundToInt(2 * _s));
        return l;
    }

    /// <summary>Show a refusal. Replaces whatever is currently shown.</summary>
    public void Show(DenyText text, bool hard = true)
    {
        if (_panel == null) return; // not ready yet

        _tween?.Kill();

        _title.Text = text.Title;
        _body.Text = text.Body;
        _hint.Text = text.Hint;
        _hint.Visible = !string.IsNullOrEmpty(text.Hint);

        _style.BorderColor = hard ? BorderHard : BorderSoft;
        _title.AddThemeColorOverride("font_color", hard ? TitleHard : TitleSoft);

        Visible = true;
        Modulate = new Color(1, 1, 1, 1);

        // Size to content, then centre over the lane band (upper-middle of the
        // screen — never over the hand, which is where the player is looking
        // when they get refused and where the next tap is going).
        Place();
        Callable.From(Place).CallDeferred();

        _tween = CreateTween();
        _tween.TweenInterval(HoldSeconds);
        _tween.TweenProperty(this, "modulate:a", 0f, FadeSeconds);
        _tween.TweenCallback(Callable.From(() => { Visible = false; }));

        if (text.PointAtEndTurn) PulseEndTurn();

        GD.Print($"[DENY] {text.Title} — {text.Body} — {text.Hint}");
    }

    private void Place()
    {
        var vp = GetViewportRect().Size;
        float w = Mathf.Round(Mathf.Min(PanelW * _s, vp.X * 0.7f));
        float padX = PadX * _s, gap = 8 * _s;
        float innerW = w - 2 * padX;
        float y = PadY * _s;
        float th = UiText.Fit(_title, padX, y, innerW);
        if (th > 0) y += th + gap;
        float bh = UiText.Fit(_body, padX, y, innerW);
        if (bh > 0) y += bh + gap;
        float hh = UiText.Fit(_hint, padX, y, innerW);
        if (hh > 0) y += hh;
        y += PadY * _s;
        var size = new Vector2(w, Mathf.Round(y));
        _panel.Size = size;
        _panel.Position = new Vector2(
            Mathf.Round(vp.X * 0.5f - size.X * 0.5f),
            Mathf.Round(vp.Y * 0.40f - size.Y * 0.5f));
        GD.Print($"[DENY] panel {size.X:0}x{size.Y:0}");
    }

    /// <summary>Hide immediately (e.g. when the player does something valid).</summary>
    public void HideNow()
    {
        _tween?.Kill();
        Visible = false;
    }

    private void PulseEndTurn()
    {
        var btn = EndTurnButton;
        if (btn == null || !IsInstanceValid(btn)) return;
        _endTurnPulse?.Kill();
        btn.Modulate = Colors.White;
        _endTurnPulse = CreateTween();
        _endTurnPulse.SetLoops(3);
        _endTurnPulse.TweenProperty(btn, "modulate", new Color(1.35f, 1.25f, 0.9f), 0.28f);
        _endTurnPulse.TweenProperty(btn, "modulate", Colors.White, 0.28f);
    }

    // ── Reason catalogue ──

    public static DenyText OpponentActing(string opponentName) => new(
        "NOT YET",
        $"{opponentName} is taking their turn.",
        "Wait for YOUR TURN to light up above End Turn.");

    public static DenyText NotEnoughAttunement(string cardName, int cost, int have) => new(
        "NOT ENOUGH ATTUNEMENT",
        $"{cardName} costs {cost}. You have {have}.",
        "Attunement refills — and grows by one — every turn. Press END TURN.",
        PointAtEndTurn: true);

    public static DenyText FieldFull() => new(
        "YOUR FIELD IS FULL",
        "All five of your lanes already hold a creature.",
        "Attack with the ones you have, or press END TURN.",
        PointAtEndTurn: true);

    public static DenyText LaneOccupied() => new(
        "LANE TAKEN",
        "A creature already stands in that lane.",
        "Tap one of your empty, glowing lanes instead.");

    public static DenyText LaneBuried() => new(
        "LANE BURIED",
        "Nothing can be played into a buried lane.",
        "Choose another lane.");

    public static DenyText EnemySide() => new(
        "THAT IS THE ENEMY'S SIDE",
        "Creatures are summoned into your own row — the lower one.",
        "Tap one of your glowing lanes.");

    public static DenyText Resting(string creatureName) => new(
        "RESTING",
        $"{creatureName} arrived this turn and cannot attack until your next turn.",
        "Creatures with SWIFT are the exception — they strike at once.");

    public static DenyText AlreadyAttacked(string creatureName) => new(
        "ALREADY ATTACKED",
        $"{creatureName} has attacked this turn. Each creature attacks once.",
        "Use another creature, or press END TURN.",
        PointAtEndTurn: true);

    public static DenyText WrongLane() => new(
        "WRONG LANE",
        "Creatures attack straight across — only the enemy lane directly opposite.",
        "Tap the lane facing your creature. An empty lane hits the enemy's Vigor.");

    public static DenyText Rooted(string creatureName) => new(
        "ROOTED",
        $"{creatureName} is Rooted and cannot attack.",
        "Choose another creature.");

    public static DenyText GameOver() => new(
        "THE DUEL IS OVER",
        "No more actions can be taken.",
        "");

    public static DenyText TutorialWants(string instruction) => new(
        "FOLLOW THE GLOW",
        instruction,
        "The glowing frame marks what the tutorial wants next.");

    public static DenyText Generic(string message) => new(
        "CAN'T DO THAT",
        message,
        "");

    /// <summary>
    /// Map an engine ActionResult.ErrorMessage to three readable lines.
    /// The engine's strings are the source of truth for legality; this just
    /// recognises them. Unknown strings fall back to showing the raw message.
    /// </summary>
    public static DenyText FromEngineMessage(string? message, string subjectName, string opponentName, int have = -1)
    {
        string m = (message ?? "").ToLowerInvariant();

        if (m.Contains("not your turn"))
            return OpponentActing(opponentName);
        if (m.Contains("game is already over"))
            return GameOver();
        if (m.Contains("not enough attunement"))
        {
            // "Not enough attunement: have X, need Y."
            int need = ExtractInt(m, "need ");
            int has = ExtractInt(m, "have ");
            if (has < 0) has = have;
            return NotEnoughAttunement(subjectName, need, Math.Max(0, has));
        }
        if (m.Contains("already occupied"))
            return LaneOccupied();
        if (m.Contains("buried"))
            return LaneBuried();
        if (m.Contains("exhausted"))
            return Resting(subjectName);
        if (m.Contains("already attacked"))
            return AlreadyAttacked(subjectName);
        if (m.Contains("invalid attack target"))
            return WrongLane();
        if (m.Contains("rooted"))
            return Rooted(subjectName);

        return Generic(message ?? "That action is not allowed right now.");
    }

    private static int ExtractInt(string text, string afterToken)
    {
        int i = text.IndexOf(afterToken, StringComparison.Ordinal);
        if (i < 0) return -1;
        i += afterToken.Length;
        int j = i;
        while (j < text.Length && char.IsDigit(text[j])) j++;
        return j > i && int.TryParse(text.AsSpan(i, j - i), out int v) ? v : -1;
    }
}
