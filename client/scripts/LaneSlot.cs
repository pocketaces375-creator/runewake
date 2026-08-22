using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// A single lane slot on the board. Shows full-bleed card art with CardPlate
/// bottom plate (uniform name + stat layout) when occupied, or a visible empty
/// frame/border when empty so slots never read as voids (TASK-UI2).
/// Supports drag-and-drop for playing cards from hand, and tap selection
/// for attack targeting.
/// Now uses CardPlate for the uniform bottom plate with name and stat badges.
/// </summary>
public partial class LaneSlot : PanelContainer
{
    private CardPlate _cardPlate;
    private TextureRect _artRect;
    private Label _noArtLabel;
    private NodeState _state = NodeState.Empty;
    private InputController? _input;
    private Label _faceLabel;

    private float _cardWidth;
    private float _cardHeight;
    private string _currentCardId = "";

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

        SetEmpty();

        // CardPlate — uniform bottom plate with name and stat badges
        _cardPlate = new CardPlate();
        _cardPlate.Name = "CardPlate";
        var content = GetNode<Control>("Content");
        content.AddChild(_cardPlate);

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

        // CardPlate handles name + stat badges + plate background
        _cardPlate.Setup(name, attack, vigor, Strata.VERDANT, w, h);
        _cardPlate.Show();

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
        Modulate = Colors.White;
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
    /// Show a floating damage number (red) at this lane's position.
    /// </summary>
    public void ShowDamageNumber(int amount)
    {
        if (amount <= 0) return;
        var ftScene = GD.Load<PackedScene>("res://scenes/effects/FloatingText.tscn");
        var ft = ftScene.Instantiate<FloatingText>();
        GetParent().AddChild(ft);
        ft.ShowAt($"-{amount}", new Color(1, 0.2f, 0.2f), GlobalPosition + new Vector2(32, 0));
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
        Scale = new Vector2(0, 0);
        Modulate = new Color(2, 2, 2, 1);
        var tween = CreateTween();
        tween.SetParallel();
        tween.TweenProperty(this, "scale", new Vector2(1, 1), 0.3f)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        tween.TweenProperty(this, "modulate", new Color(1, 1, 1, 1), 0.2f);
    }

    public void PlayDeathEffect()
    {
        Modulate = new Color(1, 0.2f, 0.2f, 1);
        var tween = CreateTween();
        tween.TweenInterval(0.1f);
        tween.SetParallel();
        tween.TweenProperty(this, "modulate:a", 0.0f, 0.4f);
        tween.TweenProperty(this, "scale", new Vector2(0, 0), 0.4f)
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

    // ——— Tap handling ———

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(SignalName.LaneTapped, LaneIndex, _state == NodeState.Empty);
            GetViewport().SetInputAsHandled();
        }
    }
}