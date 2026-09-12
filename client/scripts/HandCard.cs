using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// A card in the player's hand, rendered as a framed card thumbnail.
/// Root is PanelContainer — background fill only (border is now RootBoundBorder).
/// Click/tap via GuiInput override, drag via _GetDragData override.
/// Uses CardPlate for the unified card frame: name band, stat rail.
/// Cost rune is at top-right (Root-Bound corner motif owns the top-left).
/// </summary>
public partial class HandCard : PanelContainer
{
    private CardPlate _cardPlate;
    private TextureRect _artRect;
    private ColorRect _desatOverlay;
    private Label _noArtLabel;
    private bool _isHovered;
    private bool _selected;

    private StyleBoxFlat? _selectedStyle;

    private float _cardWidth;
    private float _cardHeight;

    /// <summary>Card's unique identifier from the engine.</summary>
    public string CardId { get; private set; } = "";
    public string CardName { get; private set; } = "";
    public int CardCost { get; private set; }
    public Strata CardStrata { get; private set; }
    public int? CardAttack { get; private set; }
    public int? CardVigor { get; private set; }

    public Control ArtRectNode => _artRect;

    // TASK-CARD-TEXT-1: Long-press for rules slab
    private Godot.Timer? _holdTimer;
    private bool _isLongPressing;
    private const float LongPressThreshold = 0.25f;
    /// <summary>DuelScene hooks this to show the rules slab with the card's CardDef.</summary>
    public Action<CardDef?>? LongPressStarted;
    /// <summary>DuelScene hooks this to hide the rules slab.</summary>
    public Action? LongPressEnded;

    [Signal]
    public delegate void PressedEventHandler();

    public override void _Ready()
    {
        _artRect = GetNode<TextureRect>("Content/ArtTexture");
        _noArtLabel = GetNode<Label>("Content/NoArtLabel");

        // CardPlate now paints the full template (frame, name band, stats)
        _cardPlate = new CardPlate();
        _cardPlate.Name = "CardPlate";
        var content = GetNode<Control>("Content");
        content.AddChild(_cardPlate);

        // Desaturation overlay for unplayable cards — global rule: NEVER black out.
        _desatOverlay = new ColorRect
        {
            Color = new Color(0.5f, 0.5f, 0.5f, 0.3f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _desatOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(_desatOverlay);
        var artIdx = GetNode("Content/ArtTexture").GetIndex();
        content.MoveChild(_desatOverlay, artIdx + 1);

        // Hover enlarge — desktop pointer only; touch uses tap+detail popup
        MouseEntered += OnHoverEntered;
        MouseExited += OnHoverExited;

        // Card face background — dark fill, no border (RootBoundBorder handles that)
        var cardStyle = new StyleBoxFlat
        {
            BgColor = FrameFill,
            BorderWidthLeft = 0,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = 0,
            ContentMarginLeft = 0,
            ContentMarginTop = 0,
            ContentMarginRight = 0,
            ContentMarginBottom = 0
        };
        AddThemeStyleboxOverride("panel", cardStyle);
    }

    public void SetCard(string cardId, string name, int cost, Strata strata)
    {
        CardId = cardId;
        CardName = name;
        CardCost = cost;
        CardStrata = strata;

        var def = CardRegistry.Get(cardId);
        CardAttack = def?.Attack;
        CardVigor = def?.Vigor;

        // Background fill (no border — RootBoundBorder handles that)
        var cardStyle = new StyleBoxFlat
        {
            BgColor = FrameFill,
            BorderWidthLeft = 0,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = 0,
            ContentMarginLeft = 0,
            ContentMarginTop = 0,
            ContentMarginRight = 0,
            ContentMarginBottom = 0
        };
        AddThemeStyleboxOverride("panel", cardStyle);

        // Update RootBound border for current size
        float w = CustomMinimumSize.X > 0 ? CustomMinimumSize.X : _cardWidth;
        float h = CustomMinimumSize.Y > 0 ? CustomMinimumSize.Y : _cardHeight;
        if (w <= 0) w = 104; if (h <= 0) h = 152;

        // CardPlate shows baked card + stat numerals
        _cardPlate.Setup(CardId, CardAttack, CardVigor, w, h, cost, artTexture: _artRect.Texture);

        LoadArt(cardId);
    }

    private void LoadArt(string cardId)
    {
        string artPath = $"res://content/art/{cardId}.webp";
        if (ResourceLoader.Exists(artPath))
        {
            var texture = ResourceLoader.Load<Texture2D>(artPath);
            if (texture != null)
            {
                _artRect.Texture = texture;
                _noArtLabel.Visible = false;
                GD.Print($"[HANDCARD] {cardId} art via TextureRect, tex={texture.GetSize()}");
                return;
            }
        }
        // No card art — show centered placeholder label only
        _artRect.Texture = null;
        _noArtLabel.Visible = true;
        _noArtLabel.Text = CardName;
        GD.Print($"[HANDCARD] No art for {cardId} — placeholder shown");
        GD.Print($"[MISSING_ART] {cardId}");
    }

    /// <summary>
    /// Set whether this card is playable (cost <= available attunement).
    /// </summary>
    public void SetPlayable(bool playable)
    {
        _desatOverlay.Visible = !playable;
        Modulate = Colors.White;
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        if (selected)
        {
            // Gold border glow for selection
            if (_selectedStyle == null)
            {
                _selectedStyle = new StyleBoxFlat
                {
                    BgColor = ThemeTokens.FrameFill,
                    BorderColor = ThemeTokens.Gold,
                    BorderWidthLeft = 3, BorderWidthTop = 3,
                    BorderWidthRight = 3, BorderWidthBottom = 3,
                    CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                    CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                    ContentMarginLeft = 0, ContentMarginTop = 0,
                    ContentMarginRight = 0, ContentMarginBottom = 0
                };
            }
            AddThemeStyleboxOverride("panel", _selectedStyle);
            ZIndex = 10;
            var tween = CreateTween();
            tween.TweenProperty(this, "position:y", -20f, 0.12f)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            tween.Parallel();
            tween.TweenProperty(this, "modulate", new Color(1.15f, 1.1f, 1.0f, 1), 0.12f);
        }
        else
        {
            // Restore normal card style
            var cardStyle = new StyleBoxFlat
            {
                BgColor = ThemeTokens.FrameFill,
                BorderWidthLeft = 0, BorderWidthTop = 0,
                BorderWidthRight = 0, BorderWidthBottom = 0,
                ContentMarginLeft = 0, ContentMarginTop = 0,
                ContentMarginRight = 0, ContentMarginBottom = 0
            };
            AddThemeStyleboxOverride("panel", cardStyle);
            var tween = CreateTween();
            tween.TweenProperty(this, "position:y", 0f, 0.1f)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
            tween.Parallel();
            tween.TweenProperty(this, "modulate", Colors.White, 0.1f);
            tween.TweenCallback(Callable.From(() => ZIndex = 1));
        }
    }

    /// <summary>
    /// Scale the card to a target height (px in viewport space), keeping the
    /// 104:152 aspect ratio. CardPlate repositions itself via Setup.
    /// </summary>
    public void ScaleTo(float targetHeight)
    {
        float aspect = 104f / 152f;
        _cardWidth = targetHeight * aspect;
        _cardHeight = targetHeight;
        CustomMinimumSize = new Vector2(_cardWidth, _cardHeight);
        Size = CustomMinimumSize;

        // Re-setup CardPlate with new dimensions
        _cardPlate.Setup(CardId, CardAttack, CardVigor, _cardWidth, _cardHeight, CardCost, artTexture: _artRect.Texture);

        // Hover pivot: bottom-center so card enlarges upward
        PivotOffset = new Vector2(CustomMinimumSize.X / 2f, CustomMinimumSize.Y);
    }

    // ——— Hand hover: enlarge ~1.8x, anchored above the hand ———

    private void OnHoverEntered()
    {
        if (_isHovered) return;
        _isHovered = true;
        ZIndex = 10;
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1.3f, 1.3f), 0.15f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
    }

    private void OnHoverExited()
    {
        _isHovered = false;
        var tween = CreateTween();
        tween.TweenProperty(this, "scale", new Vector2(1f, 1f), 0.12f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        tween.TweenCallback(Callable.From(() => ZIndex = 0));
    }

    private readonly TapGuard _tap = new();

    // ——— Long-press timer management ———
    private void StartHoldTimer()
    {
        StopHoldTimer();
        _holdTimer = new Godot.Timer();
        _holdTimer.OneShot = true;
        _holdTimer.WaitTime = LongPressThreshold;
        _holdTimer.Timeout += OnHoldTimerFired;
        AddChild(_holdTimer);
        _holdTimer.Start();
    }

    private void StopHoldTimer()
    {
        if (_holdTimer != null && IsInstanceValid(_holdTimer))
        {
            _holdTimer.Stop();
            _holdTimer.QueueFree();
            _holdTimer = null;
        }
    }

    private void OnHoldTimerFired()
    {
        // 250ms elapsed — this is a long-press
        _isLongPressing = true;
        var def = CardRegistry.Get(CardId);
        LongPressStarted?.Invoke(def);
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Clean up timer when node exits tree.</summary>
    public override void _ExitTree()
    {
        StopHoldTimer();
        base._ExitTree();
    }

    private bool IsReleaseEvent(InputEvent @event)
    {
        return (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            || (@event is InputEventScreenTouch st && !st.Pressed);
    }

    // ——— Click/touch handling via GuiInput ———
    public override void _GuiInput(InputEvent @event)
    {
        // Check for release first — hides slab or cancels timer
        if (IsReleaseEvent(@event))
        {
            if (_isLongPressing)
            {
                // Release after long-press — hide the slab
                _isLongPressing = false;
                LongPressEnded?.Invoke();
                GetViewport().SetInputAsHandled();
                return;
            }
            // Early release before 250ms — fire the normal tap
            StopHoldTimer();
            EmitSignal(SignalName.Pressed);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Press events: use TapGuard to deduplicate touch+mouse pairs
        bool accepted = _tap.Accept(@event);
        GD.Print($"[HANDCARD_TOUCH] _GuiInput: event={@event.GetType().Name}, " +
            $"pressed={(@event is InputEventScreenTouch t ? t.Pressed : @event is InputEventMouseButton m ? m.Pressed : false)}, " +
            $"card={CardName}, accepted={accepted}");
        if (accepted)
        {
            // Start the hold timer — if it fires in 250ms, it's a long-press
            StartHoldTimer();
            // NOTE: intentionally NOT calling SetInputAsHandled() here.
            // Godot initiates drag-from-press when the press event propagates;
            // marking it handled cancels the drag before _GetDragData fires.
            // The release branches below still call SetInputAsHandled.
        }
    }

    // /// <summary>
    // /// OLD _GuiInput — replaced by TASK-CARD-TEXT-1 long-press handler above
    // /// </summary>
    // public override void _GuiInput(InputEvent @event)
    // {
    //     bool accepted = _tap.Accept(@event);
    //     GD.Print($"[HANDCARD_TOUCH] _GuiInput: event={@event.GetType().Name}, " +
    //         $"pressed={(@event is InputEventScreenTouch t ? t.Pressed : @event is InputEventMouseButton m ? m.Pressed : false)}, " +
    //         $"card={CardName}, accepted={accepted}");
    //     if (accepted)
    //     {
    //         EmitSignal(SignalName.Pressed);
    //         GetViewport().SetInputAsHandled();
    //     }
    // }

    // ——— Drag-and-drop ———
    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new Label();
        preview.Text = CardName;
        preview.Size = new Vector2(80, 24);
        preview.Modulate = new Color(1, 1, 1, 0.7f);
        SetDragPreview(preview);

        var data = new Godot.Collections.Dictionary
        {
            ["type"] = "hand_card",
            ["card_id"] = CardId,
            ["card_name"] = CardName,
            ["card_cost"] = CardCost
        };
        return data;
    }
}