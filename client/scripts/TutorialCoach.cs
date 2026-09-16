using System;
using System.Collections.Generic;
using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-002: The guided-duel coach. Three ways of talking to the player:
///
///   Prompt    — non-modal instruction card + pulsing frames around the things
///               to tap. Shown BEFORE the action. The board stays live; the
///               player does the thing. ("Tap the Student, then the glowing lane.")
///   Note      — modal consequence card with one Continue button. Shown AFTER
///               the action. ("It has SWIFT, so it can attack at once.")
///   Narration — small non-modal line during the opponent's scripted turn.
///               ("The Wayfarer summons a Thorn Sprout.")
///
/// The prompt card is placed by <see cref="AnchorProvider"/> — the runner hands
/// it the union of the enemy's three leftmost lane slots, which the guided script
/// never uses, so the card is large, readable and never covers anything that
/// matters. If no anchor is available it falls back to the top-left corner.
///
/// Highlight frames are re-read from the live targets every frame, so they
/// follow hand re-flows and late layout passes instead of freezing where a
/// node was when the prompt appeared.
/// </summary>
public partial class TutorialCoach : Control
{
    public event Action? SkipRequested;

    /// <summary>Where the prompt / narration card should sit (global rect). Null = fallback.</summary>
    public Func<Rect2?>? AnchorProvider { get; set; }

    // ── Reference geometry (1080) ──
    private const float RefH = 1080f;
    private const int PromptTitleSize = 27;
    private const int PromptBodySize = 27;
    private const int SkipSize = 21;
    private const int NarrationSize = 27;
    private const int NoteTitleSize = 34;
    private const int NoteBodySize = 30;
    private const int NoteButtonSize = 34;
    private const float NoteW = 940f;
    private const float FrameThickness = 4f;
    private const float FrameMargin = 8f;

    private static readonly Color CardBg = new Color(0.086f, 0.071f, 0.055f, 0.94f);
    private static readonly Color CardBorder = new Color(0.79f, 0.66f, 0.30f);
    private static readonly Color TitleColor = new Color(0.90f, 0.75f, 0.35f);
    private static readonly Color BodyColor = new Color(0.91f, 0.86f, 0.78f);
    private static readonly Color SkipColor = new Color(0.62f, 0.56f, 0.46f);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color FramePeak = new Color(0.98f, 0.80f, 0.30f, 0.95f);
    private static readonly Color FrameTrough = new Color(0.98f, 0.80f, 0.30f, 0.30f);

    private float _s = 1f;
    private float _t;

    // Layout constants (reference px)
    private const float PadX = 26f;
    private const float PadTop = 18f;
    private const float PadBottom = 16f;
    private const float Gap = 10f;

    // Prompt
    private Panel _prompt = default!;
    private Label _promptTitle = default!;
    private Label _promptBody = default!;
    private Button _skipBtn = default!;

    // Narration
    private Panel _narration = default!;
    private Label _narrationBody = default!;
    private Tween? _narrationTween;

    // Note (modal)
    private ColorRect _dim = default!;
    private Panel _note = default!;
    private Label _noteTitle = default!;
    private Label _noteBody = default!;
    private Button _continueBtn = default!;
    private Action? _onContinue;

    // Highlights — re-resolved from the provider every 0.2s because DuelScene
    // rebuilds the hand (new HandCard nodes) on every state change.
    private Func<List<Control>>? _targetProvider;
    private float _sinceResolve;
    private readonly List<Control> _targets = new();
    private readonly List<PanelContainer> _frames = new();
    private readonly List<StyleBoxFlat> _frameStyles = new();

    public bool IsNoteOpen => _note != null && _note.Visible;

    public override void _Ready()
    {
        Name = "TutorialCoach";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        ZIndex = 150;
        _s = GetViewportRect().Size.Y / RefH;

        // ── Prompt card (plain Panel, children laid out by hand — see UiText) ──
        _prompt = MakeCard(MouseFilterEnum.Stop);
        _promptTitle = MakeLabel(ThemeTokens.GetHeaderFont(Px(PromptTitleSize)), PromptTitleSize, TitleColor, HorizontalAlignment.Left);
        _promptBody = MakeLabel(ThemeTokens.GetButtonFont(Px(PromptBodySize)), PromptBodySize, BodyColor, HorizontalAlignment.Left);
        _prompt.AddChild(_promptTitle);
        _prompt.AddChild(_promptBody);
        _skipBtn = new Button { Text = "Skip tutorial", Flat = true, FocusMode = FocusModeEnum.None };
        var skipFont = ThemeTokens.GetBodyFont(Px(SkipSize));
        if (skipFont != null) _skipBtn.AddThemeFontOverride("font", skipFont);
        _skipBtn.AddThemeFontSizeOverride("font_size", Px(SkipSize));
        _skipBtn.AddThemeColorOverride("font_color", SkipColor);
        _skipBtn.AddThemeColorOverride("font_hover_color", TitleColor);
        _skipBtn.AddThemeColorOverride("font_pressed_color", TitleColor);
        _skipBtn.Pressed += () => { GD.Print("[TutorialCoach] skip requested"); SkipRequested?.Invoke(); };
        _prompt.AddChild(_skipBtn);
        AddChild(_prompt);
        _prompt.Visible = false;

        // ── Narration card ──
        _narration = MakeCard(MouseFilterEnum.Ignore);
        _narrationBody = MakeLabel(ThemeTokens.GetButtonFont(Px(NarrationSize)), NarrationSize, BodyColor, HorizontalAlignment.Left);
        _narration.AddChild(_narrationBody);
        AddChild(_narration);
        _narration.Visible = false;

        // ── Modal note ──
        _dim = new ColorRect { Color = DimColor, MouseFilter = MouseFilterEnum.Stop };
        _dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_dim);
        _dim.Visible = false;

        _note = MakeCard(MouseFilterEnum.Stop);
        _noteTitle = MakeLabel(ThemeTokens.GetHeaderFont(Px(NoteTitleSize)), NoteTitleSize, TitleColor, HorizontalAlignment.Center);
        _noteBody = MakeLabel(ThemeTokens.GetButtonFont(Px(NoteBodySize)), NoteBodySize, BodyColor, HorizontalAlignment.Center);
        _note.AddChild(_noteTitle);
        _note.AddChild(_noteBody);
        _continueBtn = new Button { Text = "Continue", FocusMode = FocusModeEnum.None };
        var btnFont = ThemeTokens.GetHeaderFont(Px(NoteButtonSize));
        if (btnFont != null) _continueBtn.AddThemeFontOverride("font", btnFont);
        _continueBtn.AddThemeFontSizeOverride("font_size", Px(NoteButtonSize));
        _continueBtn.AddThemeColorOverride("font_color", new Color(0.16f, 0.12f, 0.03f));
        _continueBtn.AddThemeColorOverride("font_hover_color", new Color(0.16f, 0.12f, 0.03f));
        _continueBtn.AddThemeColorOverride("font_pressed_color", new Color(0.16f, 0.12f, 0.03f));
        var btnStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.89f, 0.70f, 0.24f),
            CornerRadiusTopLeft = Px(6), CornerRadiusTopRight = Px(6),
            CornerRadiusBottomLeft = Px(6), CornerRadiusBottomRight = Px(6),
        };
        _continueBtn.AddThemeStyleboxOverride("normal", btnStyle);
        _continueBtn.AddThemeStyleboxOverride("hover", btnStyle);
        _continueBtn.AddThemeStyleboxOverride("pressed", btnStyle);
        _continueBtn.AddThemeStyleboxOverride("focus", btnStyle);
        _continueBtn.Pressed += OnContinuePressed;
        _note.AddChild(_continueBtn);
        AddChild(_note);
        _note.Visible = false;
    }

    private int Px(float refPx) => Mathf.Max(1, Mathf.RoundToInt(refPx * _s));

    private Panel MakeCard(MouseFilterEnum filter)
    {
        var style = new StyleBoxFlat
        {
            BgColor = CardBg,
            BorderColor = CardBorder,
            BorderWidthLeft = Px(3), BorderWidthRight = Px(3), BorderWidthTop = Px(3), BorderWidthBottom = Px(3),
            CornerRadiusTopLeft = Px(8), CornerRadiusTopRight = Px(8),
            CornerRadiusBottomLeft = Px(8), CornerRadiusBottomRight = Px(8),
            ShadowColor = new Color(0, 0, 0, 0.7f),
            ShadowSize = Px(16),
            ShadowOffset = new Vector2(0, 6 * _s),
        };
        var panel = new Panel { MouseFilter = filter };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private Label MakeLabel(Font? font, int refSize, Color color, HorizontalAlignment align)
    {
        var l = new Label
        {
            HorizontalAlignment = align,
            VerticalAlignment = VerticalAlignment.Top,
            AutowrapMode = TextServer.AutowrapMode.Word,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (font != null) l.AddThemeFontOverride("font", font);
        l.AddThemeFontSizeOverride("font_size", Px(refSize));
        l.AddThemeColorOverride("font_color", color);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        l.AddThemeConstantOverride("shadow_offset_y", Px(2));
        l.AddThemeConstantOverride("line_spacing", Px(4));
        return l;
    }

    // ── Placement ──

    private Rect2 ResolveAnchor()
    {
        var vp = GetViewportRect().Size;
        Rect2? a = null;
        try { a = AnchorProvider?.Invoke(); } catch (Exception ex) { GD.PrintErr($"[TutorialCoach] anchor provider threw: {ex.Message}"); }
        if (a is Rect2 r && r.Size.X >= 320 * _s && r.Size.Y >= 120 * _s)
            return r;
        // Fallback: top-left block under the enemy strip
        return new Rect2(24 * _s, 110 * _s, Mathf.Min(560 * _s, vp.X * 0.36f), 320 * _s);
    }

    /// <summary>Prompt card: title / body / skip link, exactly as tall as that content.</summary>
    private void LayoutPrompt()
    {
        var anchor = ResolveAnchor();
        float w = Mathf.Round(anchor.Size.X);
        float padX = PadX * _s, gap = Gap * _s;
        float innerW = w - 2 * padX;
        float y = PadTop * _s;

        float th = UiText.Fit(_promptTitle, padX, y, innerW);
        if (th > 0) y += th + gap;
        float bh = UiText.Fit(_promptBody, padX, y, innerW);
        if (bh > 0) y += bh + gap * 0.6f;
        if (_skipBtn.Visible)
        {
            var bs = _skipBtn.GetCombinedMinimumSize();
            _skipBtn.Size = bs;
            _skipBtn.Position = new Vector2(w - padX - bs.X, y);
            y += bs.Y;
        }
        y += PadBottom * _s;

        _prompt.Size = new Vector2(w, Mathf.Round(y));
        _prompt.Position = new Vector2(Mathf.Round(anchor.Position.X), Mathf.Round(anchor.Position.Y));
    }

    /// <summary>Narration: one body line block, same anchor, as tall as the text.</summary>
    private void LayoutNarration()
    {
        var anchor = ResolveAnchor();
        float w = Mathf.Round(anchor.Size.X);
        float padX = PadX * _s;
        float y = PadTop * _s;
        y += UiText.Fit(_narrationBody, padX, y, w - 2 * padX);
        y += PadBottom * _s;
        _narration.Size = new Vector2(w, Mathf.Round(y));
        _narration.Position = new Vector2(Mathf.Round(anchor.Position.X), Mathf.Round(anchor.Position.Y));
    }

    /// <summary>Modal note: centred, width capped, height from content + Continue.</summary>
    private void LayoutNote()
    {
        var vp = GetViewportRect().Size;
        float w = Mathf.Round(Mathf.Min(NoteW * _s, vp.X * 0.8f));
        float padX = PadX * _s, gap = Gap * _s;
        float innerW = w - 2 * padX;
        float y = PadTop * _s;

        float th = UiText.Fit(_noteTitle, padX, y, innerW);
        if (th > 0) y += th + gap;
        float bh = UiText.Fit(_noteBody, padX, y, innerW);
        if (bh > 0) y += bh + gap * 1.6f;
        float btnH = Mathf.Round(84 * _s);
        _continueBtn.Position = new Vector2(padX, y);
        _continueBtn.Size = new Vector2(innerW, btnH);
        y += btnH + PadBottom * _s;

        _note.Size = new Vector2(w, Mathf.Round(y));
        _note.Position = new Vector2(Mathf.Round(vp.X * 0.5f - w * 0.5f), Mathf.Round(vp.Y * 0.42f - _note.Size.Y * 0.5f));
    }

    // ── Public API ──

    /// <summary>
    /// Non-modal instruction with pulsing frames around the controls returned by
    /// <paramref name="targets"/>. The provider is re-run periodically so frames
    /// survive hand rebuilds.
    /// </summary>
    public void ShowPrompt(string title, string text, Func<List<Control>> targets, bool showSkip = true)
    {
        HideNarration();
        _promptTitle.Text = title ?? "";
        _promptTitle.Visible = !string.IsNullOrEmpty(title);
        _promptBody.Text = text ?? "";
        _skipBtn.Visible = showSkip;
        _targetProvider = targets;
        _sinceResolve = 999f;
        ResolveTargetsIfDue(0f);
        _prompt.Visible = true;
        LayoutPrompt();
        Callable.From(LayoutPrompt).CallDeferred(); // once more after the anchor slots settle
        GD.Print($"[TutorialCoach] PROMPT '{title}': {text} ({_targets.Count} targets) card={_prompt.Size.X:0}x{_prompt.Size.Y:0}");
    }

    public void HidePrompt()
    {
        _prompt.Visible = false;
        _targetProvider = null;
        SetTargets(new List<Control>());
    }

    private void ResolveTargetsIfDue(float delta)
    {
        _sinceResolve += delta;
        if (_targetProvider == null || _sinceResolve < 0.2f) return;
        _sinceResolve = 0f;
        List<Control> fresh;
        try { fresh = _targetProvider() ?? new List<Control>(); }
        catch (Exception ex) { GD.PrintErr($"[TutorialCoach] target provider threw: {ex.Message}"); return; }
        fresh.RemoveAll(c => c == null || !IsInstanceValid(c));
        bool same = fresh.Count == _targets.Count;
        for (int i = 0; same && i < fresh.Count; i++)
            if (!ReferenceEquals(fresh[i], _targets[i])) same = false;
        if (!same) SetTargets(fresh);
    }

    /// <summary>Modal consequence note. Blocks the board until Continue.</summary>
    public void ShowNote(string title, string text, Action? onContinue)
    {
        HidePrompt();
        HideNarration();
        _onContinue = onContinue;
        _noteTitle.Text = title ?? "";
        _noteTitle.Visible = !string.IsNullOrEmpty(title);
        _noteBody.Text = text ?? "";
        _dim.Visible = true;
        _note.Visible = true;
        MoveChild(_dim, GetChildCount() - 1);
        MoveChild(_note, GetChildCount() - 1);
        LayoutNote();
        Callable.From(LayoutNote).CallDeferred();
        GD.Print($"[TutorialCoach] NOTE '{title}': {text} card={_note.Size.X:0}x{_note.Size.Y:0}");
    }

    /// <summary>Programmatic Continue (headless, or skip while a note is open).</summary>
    public void DismissNote()
    {
        if (!_note.Visible) return;
        OnContinuePressed();
    }

    private void OnContinuePressed()
    {
        _dim.Visible = false;
        _note.Visible = false;
        var cb = _onContinue;
        _onContinue = null;
        cb?.Invoke();
    }

    /// <summary>Non-modal one-liner during the opponent's scripted turn. Fades on its own.</summary>
    public void ShowNarration(string text, float holdSeconds = 3.2f)
    {
        _narrationTween?.Kill();
        _narrationBody.Text = text ?? "";
        _narration.Modulate = Colors.White;
        _narration.Visible = true;
        LayoutNarration();
        Callable.From(LayoutNarration).CallDeferred();
        _narrationTween = CreateTween();
        _narrationTween.TweenInterval(holdSeconds);
        _narrationTween.TweenProperty(_narration, "modulate:a", 0f, 0.5f);
        _narrationTween.TweenCallback(Callable.From(() => { _narration.Visible = false; }));
        GD.Print($"[TutorialCoach] NARRATION: {text}");
    }

    public void HideNarration()
    {
        _narrationTween?.Kill();
        _narration.Visible = false;
    }

    public void HideAll()
    {
        HidePrompt();
        HideNarration();
        _dim.Visible = false;
        _note.Visible = false;
        _onContinue = null;
    }

    // ── Highlight frames ──

    private void SetTargets(List<Control> targets)
    {
        foreach (var f in _frames) if (IsInstanceValid(f)) f.QueueFree();
        _frames.Clear();
        _frameStyles.Clear();
        _targets.Clear();
        if (targets == null) return;
        foreach (var t in targets)
        {
            if (t == null || !IsInstanceValid(t)) continue;
            _targets.Add(t);
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),
                BorderColor = FramePeak,
                BorderWidthLeft = Px(FrameThickness), BorderWidthRight = Px(FrameThickness),
                BorderWidthTop = Px(FrameThickness), BorderWidthBottom = Px(FrameThickness),
                CornerRadiusTopLeft = Px(6), CornerRadiusTopRight = Px(6),
                CornerRadiusBottomLeft = Px(6), CornerRadiusBottomRight = Px(6),
                ShadowColor = new Color(0.98f, 0.80f, 0.30f, 0.35f),
                ShadowSize = Px(10),
            };
            var frame = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 5 };
            frame.AddThemeStyleboxOverride("panel", style);
            AddChild(frame);
            _frames.Add(frame);
            _frameStyles.Add(style);
        }
        UpdateFrames(0f);
    }

    public override void _Process(double delta)
    {
        ResolveTargetsIfDue((float)delta);
        // Keep the prompt card glued to its anchor (lanes can shift on re-layout).
        if (_prompt.Visible && _sinceResolve == 0f) LayoutPrompt();
        if (_frames.Count == 0) return;
        _t += (float)delta;
        UpdateFrames(_t);
    }

    private void UpdateFrames(float t)
    {
        // Triangle pulse, 0.9s period
        float k = Mathf.Abs(((t / 0.9f) % 1f) * 2f - 1f);
        var col = FrameTrough.Lerp(FramePeak, k);
        float m = FrameMargin * _s;
        for (int i = 0; i < _frames.Count; i++)
        {
            var target = i < _targets.Count ? _targets[i] : null;
            var frame = _frames[i];
            if (target == null || !IsInstanceValid(target) || !target.IsInsideTree() || !IsInstanceValid(frame))
            {
                if (IsInstanceValid(frame)) frame.Visible = false;
                continue;
            }
            var r = target.GetGlobalRect();
            if (r.Size.X <= 0 || r.Size.Y <= 0) { frame.Visible = false; continue; }
            frame.Visible = true;
            frame.Position = new Vector2(r.Position.X - m, r.Position.Y - m);
            frame.Size = new Vector2(r.Size.X + 2 * m, r.Size.Y + 2 * m);
            _frameStyles[i].BorderColor = col;
        }
    }

    public override void _ExitTree()
    {
        _narrationTween?.Kill();
        SkipRequested = null;
    }
}
