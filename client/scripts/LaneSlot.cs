using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// A single lane slot on the board. Shows full-bleed card art with CardPlate
/// unified frame (Root-Bound border, name band, stat rail) when occupied,
/// or a visible empty frame/border when empty so slots never read as
/// voids (TASK-UI2).
/// Supports drag-and-drop for playing cards from hand, and tap selection
/// for attack targeting.
/// Root-Bound 9-slice border replaces the old gold two-layer StyleBoxFlat.
/// </summary>
public partial class LaneSlot : PanelContainer
{
    private CardPlate _cardPlate;
    private TextureRect _artRect;
    private Label _noArtLabel;
    private Label _costLabel;
    private RootBoundBorder _rootBound;
    private NodeState _state = NodeState.Empty;
    private InputController? _input;
    private Label _faceLabel;

    private float _cardWidth;
    private float _cardHeight;
    private string _currentCardId = "";
    private StyleBoxFlat? _emptySlotStyle;

    /// <summary>Warm-gold border color for empty slots.</summary>
    private static readonly Color EmptySlotBorder = Color.FromHtml("#C9A84C");
    /// <summary>Faint translucent stone tint for empty slot background.</summary>
    private static readonly Color EmptySlotBg = Color.FromHtml("#1A1816");

    /// <summary>
    /// Emitted when a card is dropped onto this lane slot.
    /// Parameters: cardId, laneIndex (this slot's index).
    /// </summary>
    [Signal]
    public delegate void CardDroppedEventHandler(string cardId, int laneIndex);

    /// <summary>
    /// Emitted when the player taps this lane slot.
    /// Parameters: laneIndex, isEmpty.
    /// </summary>
    [Signal]
    public delegate void LaneTappedEventHandler(int laneIndex, bool isEmpty);

    public enum NodeState { Empty, Occupied }

    /// <summary>Which row this lane belongs to: 0 = enemy, 1 = player.</summary>
    public int Row { get; set; }

    /// <summary>Lane index (0–4).</summary>
    public int LaneIndex { get; set; }

    public override void _Ready()
    {
        _artRect = GetNode<TextureRect>("Content/ArtTexture");
        _noArtLabel = GetNode<Label>("Content/NoArtLabel");

        // NoArtLabel — card name placeholder when art file is missing
        _noArtLabel.Visible = false;
        ApplyHeaderFont(_noArtLabel, FontLargeBody);

        // Empty slot style: warm-gold rounded keyline with translucent stone tint
        // Created BEFORE SetEmpty so the initial call applies it immediately.
        _emptySlotStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.09f, 0.07f, 0.10f), // faint translucent stone tint
            BorderColor = EmptySlotBorder,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 0,
            ContentMarginTop = 0,
            ContentMarginRight = 0,
            ContentMarginBottom = 0
        };

        SetEmpty();

        // Root-Bound 9-slice border overlay (hidden for empty slots)
        _rootBound = new RootBoundBorder();
        _rootBound.Name = "RootBoundBorder";
        AddChild(_rootBound);
        _rootBound.Setup(CustomMinimumSize.X, CustomMinimumSize.Y);
        _rootBound.Visible = false; // BOARD-MATCH-3: hidden on empty — gold keyline takes over

        // CardPlate — unified card frame: name band, stat rail
        _cardPlate = new CardPlate();
        _cardPlate.Name = "CardPlate";
        var content = GetNode<Control>("Content");
        content.AddChild(_cardPlate);

        // Cost rune — top-right inside Root-Bound border
        _costLabel = CardPlate.MakeCostRune(0, CustomMinimumSize.X, CustomMinimumSize.Y, out _);
        _costLabel.Name = "CostRune";
        content.AddChild(_costLabel);

        // Create the FACE attack target label (hidden by default)
        _faceLabel = new Label
        {
            Text = "→ FACE",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visible = false,
            Modulate = new Color(1, 0.85f, 0.2f, 1)
        };
        _faceLabel.AddThemeFontSizeOverride("font_size", 14);
        _faceLabel.MouseFilter = MouseFilterEnum.Ignore;
        _faceLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        content.AddChild(_faceLabel);

        // Connect touch area for touch input
        var touchArea = GetNodeOrNull<Control>("TouchArea");
        if (touchArea != null)
            touchArea.GuiInput += OnTouchAreaInput;

        // Load the stone texture for the lane slot background
        var slotBg = GetNodeOrNull<TextureRect>("SlotBg");
        if (slotBg != null)
        {
            var stoneTex = GD.Load<Texture2D>("res://assets/stone_board.png");
            if (stoneTex != null)
                slotBg.Texture = stoneTex;
        }
    }

    /// <summary>
    /// Handle taps from the expanded touch area overlay.
    /// </summary>
    private void OnTouchAreaInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(SignalName.LaneTapped, LaneIndex, _state == NodeState.Empty);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Set this lane slot to show card info with full-bleed art.
    /// </summary>
    public void SetCard(string cardDefId, string name, int attack, int vigor, bool isExhausted = false)
    {
        _currentCardId = cardDefId;
        _state = NodeState.Occupied;

        // Determine card dimensions from current size
        float w = CustomMinimumSize.X > 0 ? CustomMinimumSize.X : _cardWidth;
        float h = CustomMinimumSize.Y > 0 ? CustomMinimumSize.Y : _cardHeight;
        if (w <= 0) w = 106; if (h <= 0) h = 155;

        _cardWidth = w;
        _cardHeight = h;

        // Apply occupied card style (dark fill, no border — handled by RootBoundBorder)
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = FrameFill,
            BorderWidthLeft = 0, BorderWidthTop = 0,
            BorderWidthRight = 0, BorderWidthBottom = 0,
            ContentMarginLeft = 0, ContentMarginTop = 0,
            ContentMarginRight = 0, ContentMarginBottom = 0
        });

        // Get cost from card definition
        int cost = 0;
        var def = CardRegistry.Get(cardDefId);
        if (def != null) cost = def.Cost;

        // Update RootBound border (show on occupied)
        _rootBound.Setup(w, h);
        _rootBound.Visible = true;

        // CardPlate handles name, stat rail
        _cardPlate.Setup(name, attack, vigor, Strata.VERDANT, w, h, cost);
        _cardPlate.Show();

        // Cost rune at top-right
        if (cost > 0 && _costLabel != null)
        {
            _costLabel.Visible = true;
            _costLabel.Text = cost.ToString();
            int bandPx = Mathf.Max(1, Mathf.RoundToInt(w * 0.07f));
            float hexSize = w * FrameHexSizeFraction;
            float hexX = w - bandPx - hexSize - 2f;
            float hexY = bandPx + 2f;
            _costLabel.Position = new Vector2(hexX, hexY);
            _costLabel.Size = new Vector2(hexSize, hexSize);
            CardPlate.UpdateCostRuneStyle(_costLabel, hexSize);
            int costFontSize = Mathf.Max(11, Mathf.RoundToInt(hexSize * 0.5f));
            _costLabel.AddThemeFontSizeOverride("font_size", costFontSize);
        }
        else if (_costLabel != null)
        {
            _costLabel.Visible = false;
        }

        LoadArt(cardDefId);

        // Visual: exhausted creatures get subtle desaturation
        Modulate = isExhausted ? new Color(0.85f, 0.85f, 0.85f, 1f) : Colors.White;
    }

    private void LoadArt(string cardDefId)
    {
        string artPath = $"res://content/art/{cardDefId}.webp";
        if (ResourceLoader.Exists(artPath))
        {
            var texture = ResourceLoader.Load<Texture2D>(artPath);
            if (texture != null)
            {
                _artRect.Texture = texture;
                _noArtLabel.Visible = false;
                GD.Print($"[LANESLOT] {cardDefId} art via TextureRect, tex={texture.GetSize()}");
                return;
            }
        }
        _artRect.Texture = null;
        _noArtLabel.Visible = true;
        _noArtLabel.Text = _cardPlate == null ? nameof(_cardPlate) : "";
        GD.Print($"[LANESLOT] No art for {cardDefId} — NoArtLabel shown");
        GD.Print($"[MISSING_ART] {cardDefId}");
    }

    /// <summary>
    /// Clear this lane slot back to empty.
    /// </summary>
    public void SetEmpty()
    {
        _currentCardId = "";
        _state = NodeState.Empty;
        _artRect.Texture = null;
        _noArtLabel.Visible = false;
        if (_faceLabel != null)
            _faceLabel.Visible = false;
        if (_cardPlate != null)
            _cardPlate.Hide();
        if (_costLabel != null)
            _costLabel.Visible = false;
        Modulate = Colors.White;
        // Apply warm-gold keyline socket style for empty slots
        if (_emptySlotStyle != null)
            AddThemeStyleboxOverride("panel", _emptySlotStyle);
        // BOARD-MATCH-3: hide RootBound border so gold keyline is visible through empty slot
        if (_rootBound != null)
            _rootBound.Visible = false;
    }

    /// <summary>
    /// Scale the lane slot to a target height (px), keeping 13:19 portrait ratio.
    /// CardPlate repositions itself via Setup.
    /// </summary>
    public void ScaleTo(float targetHeight)
    {
        float aspect = 106f / 155f;
        _cardWidth = targetHeight * aspect;
        _cardHeight = targetHeight;
        CustomMinimumSize = new Vector2(_cardWidth, _cardHeight);
        Size = CustomMinimumSize;

        // Update RootBound border
        _rootBound.Setup(_cardWidth, _cardHeight);
    }

    /// <summary>
    /// Show visual feedback for being a valid attack target (highlight border).
    /// </summary>
    public void Highlight()
    {
        _faceLabel.Visible = false;
        Modulate = new Color(1, 1, 0.8f, 1);
    }

    /// <summary>
    /// Show that this empty lane is a valid face-attack target.
    /// </summary>
    public void HighlightAsFaceTarget()
    {
        Modulate = new Color(1, 0.9f, 0.4f, 1);
        _faceLabel.Visible = true;
    }

    /// <summary>
    /// Remove highlight effect.
    /// </summary>
    public void Unhighlight()
    {
        _faceLabel.Visible = false;
        Modulate = new Color(1, 1, 1, 1);
    }

    /// <summary>
    /// Show a floating damage number (stratum-coloured) at this lane's position.
    /// Uses Cinzel serif font for the dark fae ritual feel.
    /// </summary>
    public void ShowDamageNumber(int amount, Strata strata = Strata.VERDANT)
    {
        if (amount <= 0) return;
        if (CampaignContext.ReduceMotion)
        {
            // Skip animated floating text, just show a brief static label
            ShowStaticDamageLabel(amount, strata);
            return;
        }
        
        var ftScene = GD.Load<PackedScene>("res://scenes/effects/FloatingText.tscn");
        var ft = ftScene.Instantiate<FloatingText>();
        GetParent().AddChild(ft);
        
        // Stratum-coloured damage number — the spec says the number itself
        // takes on the character of the attacker's stratum
        Color textColor = ThemeTokens.StrataColor(strata);
        ft.ShowAt($"-{amount}", textColor, GlobalPosition + new Vector2(32, 0));
        
        // Apply Cinzel serif font for the ritual feel
        var headerFont = ThemeTokens.GetHeaderFont(22);
        if (headerFont != null)
            ft.AddThemeFontOverride("font", headerFont);
    }
    
    /// <summary>
    /// Non-animated fallback for reduce-motion mode.
    /// </summary>
    private void ShowStaticDamageLabel(int amount, Strata strata)
    {
        var label = new Label
        {
            Text = $"-{amount}",
            Modulate = ThemeTokens.StrataColor(strata),
            Position = GlobalPosition + new Vector2(32, 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        var headerFont = ThemeTokens.GetHeaderFont(18);
        if (headerFont != null)
            label.AddThemeFontOverride("font", headerFont);
        GetParent().AddChild(label);
        
        var tween = CreateTween();
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.4f);
        tween.TweenCallback(Callable.From(() => { if (label.IsInsideTree()) label.QueueFree(); }));
    }

    /// <summary>
    /// Show a floating heal number (green) at this lane's position.
    /// </summary>
    public void ShowHealNumber(int amount)
    {
        if (amount <= 0) return;
        var ftScene = GD.Load<PackedScene>("res://scenes/effects/FloatingText.tscn");
        var ft = ftScene.Instantiate<FloatingText>();
        GetParent().AddChild(ft);
        ft.ShowAt($"+{amount}", new Color(0.2f, 1, 0.2f), GlobalPosition + new Vector2(32, 0));
    }

    // ——— Animation effects ———

    public void PlaySummonEffect()
    {
        // Stone dust puff
        RitualEffects.PlayStoneDustPuff(this, CampaignContext.ReduceMotion);
        
        // Keep the existing scale-up animation for the card seating feel
        Scale = new Vector2(0, 0);
        Modulate = new Color(2, 2, 2, 1);
        var tween = CreateTween();
        tween.SetParallel();
        tween.TweenProperty(this, "scale", new Vector2(1, 1), 0.3f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate", new Color(1, 1, 1, 1), 0.2f);
    }

    public void PlayDeathEffect(Strata strata = Strata.VERDANT)
    {
        // Stratum-coloured crumbling death
        RitualEffects.PlayCrumblingDeath(this, strata, CampaignContext.ReduceMotion);
        
        // Keep the existing shrink + fade for structural cleanup
        var tween = CreateTween();
        tween.TweenInterval(0.1f);
        tween.SetParallel();
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.35f);
        tween.TweenProperty(this, "scale", new Vector2(0, 0), 0.35f)
            .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Back);
        tween.SetParallel(false);
        tween.TweenCallback(Callable.From(() =>
        {
            Modulate = new Color(1, 1, 1, 1);
            Scale = new Vector2(1, 1);
        }));
    }

    // ——— Drag-and-drop target ———

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (Row != 1 || _state == NodeState.Occupied)
            return false;
        if (data.VariantType != Variant.Type.Dictionary)
            return false;
        var dict = data.AsGodotDictionary();
        return dict.ContainsKey("type") && dict["type"].AsString() == "hand_card";
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var dict = data.AsGodotDictionary();
        string cardId = dict["card_id"].AsString();
        EmitSignal(SignalName.CardDropped, cardId, LaneIndex);
    }

    private readonly TapGuard _tap = new();

    // ——— Tap handling ———

    public override void _GuiInput(InputEvent @event)
    {
        // A tap on glass arrives twice — as a touch event and again as the mouse event Godot
        // emulates from it. TapGuard collapses the pair so one finger press is one press.
        if (_tap.Accept(@event))
        {
            EmitSignal(SignalName.LaneTapped, LaneIndex, _state == NodeState.Empty);
            GetViewport().SetInputAsHandled();
        }
    }
}