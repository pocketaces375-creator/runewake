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
    private Label _costLabel;
    private RootBoundBorder _rootBound;
    private bool _isHovered;

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

    [Signal]
    public delegate void PressedEventHandler();

    public override void _Ready()
    {
        _artRect = GetNode<TextureRect>("Content/ArtTexture");
        _noArtLabel = GetNode<Label>("Content/NoArtLabel");

        // Root-Bound 9-slice border overlay
        _rootBound = new RootBoundBorder();
        _rootBound.Name = "RootBoundBorder";
        AddChild(_rootBound);
        _rootBound.Setup(CustomMinimumSize.X, CustomMinimumSize.Y);

        // CardPlate — unified card frame: name band, stat rail
        _cardPlate = new CardPlate();
        _cardPlate.Name = "CardPlate";
        var content = GetNode<Control>("Content");
        content.AddChild(_cardPlate);

        // Cost rune — top-right inside Root-Bound border
        _costLabel = CardPlate.MakeCostRune(0, CustomMinimumSize.X, CustomMinimumSize.Y, out _);
        _costLabel.Name = "CostRune";
        content.AddChild(_costLabel);

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
        _rootBound.Setup(w, h);

        // CardPlate handles name band, stat rail
        _cardPlate.Setup(name, CardAttack, CardVigor, strata, w, h, cost);

        // Cost rune at top-right
        float costW = _costLabel.Size.X;
        if (cost > 0)
        {
            _costLabel.Visible = true;
            _costLabel.Text = cost.ToString();
            // Reposition for current card size
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
        else
        {
            _costLabel.Visible = false;
        }

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

        // Update RootBound border
        _rootBound.Setup(_cardWidth, _cardHeight);

        // Re-setup CardPlate with new dimensions
        _cardPlate.Setup(CardName, CardAttack, CardVigor, CardStrata, _cardWidth, _cardHeight, CardCost);

        // Reposition cost rune
        if (CardCost > 0 && _costLabel != null)
        {
            int bandPx = Mathf.Max(1, Mathf.RoundToInt(_cardWidth * 0.07f));
            float hexSize = _cardWidth * FrameHexSizeFraction;
            float hexX = _cardWidth - bandPx - hexSize - 2f;
            float hexY = bandPx + 2f;
            _costLabel.Position = new Vector2(hexX, hexY);
            _costLabel.Size = new Vector2(hexSize, hexSize);
            CardPlate.UpdateCostRuneStyle(_costLabel, hexSize);
            int costFontSize = Mathf.Max(11, Mathf.RoundToInt(hexSize * 0.5f));
            _costLabel.AddThemeFontSizeOverride("font_size", costFontSize);
        }

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
        tween.TweenProperty(this, "scale", new Vector2(1.8f, 1.8f), 0.15f)
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

    // ——— Click handling via GuiInput ———
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(SignalName.Pressed);
            GetViewport().SetInputAsHandled();
        }
    }

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