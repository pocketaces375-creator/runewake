using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// A card in the player's hand, rendered as a framed card thumbnail.
/// Root is PanelContainer — draws the "panel" theme style for card background and border.
/// Click/tap via GuiInput override, drag via _GetDragData override.
/// </summary>
public partial class HandCard : PanelContainer
{
    private Label _cardName;
    private Label _costLabel;
    private TextureRect _artRect;
    private PanelContainer _badgePanel;
    private ColorRect _desatOverlay;
    private Label _attackBadge;
    private Label _vigorBadge;
    private Label _noArtLabel;
    private bool _isHovered;

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
        _cardName = GetNode<Label>("Content/CardName");
        _artRect = GetNode<TextureRect>("Content/ArtTexture");
        _noArtLabel = GetNode<Label>("Content/NoArtLabel");
        _costLabel = GetNode<Label>("Content/CostBadge/CostLabel");

        ApplyHeaderFont(_cardName, FontLargeBody);
        _cardName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        ApplyBodyFont(_costLabel, FontLargeBody);

        _badgePanel = GetNode<PanelContainer>("Content/CostBadge");

        // High-contrast stat corner badges (FIX 3c) — attack bottom-left, vigor bottom-right
        _attackBadge = MakeStatBadge(new Color(0.72f, 0.18f, 0.10f));
        _vigorBadge = MakeStatBadge(new Color(0.20f, 0.55f, 0.30f));

        // Desaturation overlay for unplayable cards — global rule: NEVER black out.
        // A 30% gray veil desaturates the art while keeping it clearly visible.
        _desatOverlay = new ColorRect
        {
            Color = new Color(0.5f, 0.5f, 0.5f, 0.3f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        _desatOverlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        GetNode<Control>("Content").AddChild(_desatOverlay);
        // Keep the veil above the art but below the name/badge so text stays crisp
        var artIdx = GetNode("Content/ArtTexture").GetIndex();
        GetNode("Content").MoveChild(_desatOverlay, artIdx + 1);

        // Style cost badge
        ApplyBadgeStyle(Gold, Gold);

        // Hover enlarge (FIX 3d) — desktop pointer only; touch uses tap+detail popup
        MouseEntered += OnHoverEntered;
        MouseExited += OnHoverExited;

        // Card face style
        var cardStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#332E28"),
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
        AddThemeStyleboxOverride("panel", cardStyle);
    }

    public void SetCard(string cardId, string name, int cost, Strata strata)
    {
        CardId = cardId;
        CardName = name;
        CardCost = cost;
        CardStrata = strata;

        _cardName.Text = name;
        _costLabel.Text = cost.ToString();

        var def = CardRegistry.Get(cardId);
        CardAttack = def?.Attack;
        CardVigor = def?.Vigor;

        // Strata border
        var strataColor = StrataColor(strata);
        var cardStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#332E28"),
            BorderColor = strataColor.Darkened(0.4f),
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
        AddThemeStyleboxOverride("panel", cardStyle);

        // Fill corner stat badges
        UpdateStatBadges();

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
        _artRect.Texture = null;
        _noArtLabel.Visible = true;
        _noArtLabel.Text = _cardName.Text;
        GD.Print($"[HANDCARD] No art for {cardId} — placeholder shown");
        GD.Print($"[MISSING_ART] {cardId}");
    }

    /// <summary>
    /// Set whether this card is playable (cost <= available attunement).
    /// Playable = full brightness, gold cost badge.
    /// Unplayable = art visible with ≤30% desaturation, red cost badge.
    /// NEVER dim-to-black per global UI directive.
    /// </summary>
    public void SetPlayable(bool playable)
    {
        _desatOverlay.Visible = !playable;
        if (playable)
        {
            ApplyBadgeStyle(Gold, Gold);
            Modulate = Colors.White;
        }
        else
        {
            ApplyBadgeStyle(new Color(0.8f, 0.18f, 0.12f), new Color(0.95f, 0.3f, 0.2f));
            Modulate = Colors.White;
        }
    }

    /// <summary>
    /// Scale the card to a target height (px in viewport space), keeping the
    /// 110:168 aspect ratio. Fonts scale proportionally so stats stay ≥16px
    /// at 1080p (viewport 648 → card 190px → stat font 17px).
    /// </summary>
    public void ScaleTo(float targetHeight)
    {
        float aspect = 110f / 168f;
        CustomMinimumSize = new Vector2(targetHeight * aspect, targetHeight);
        Size = CustomMinimumSize;

        // Scale fonts proportionally from the base 168px design
        float scale = targetHeight / 168f;
        int nameSize = Mathf.Max(11, Mathf.RoundToInt(13 * scale));
        int costSize = Mathf.Max(16, Mathf.RoundToInt(18 * scale));
        _cardName.AddThemeFontSizeOverride("font_size", nameSize);
        _costLabel.AddThemeFontSizeOverride("font_size", costSize);

        // Hover pivot: bottom-center so card enlarges upward, not off-screen bottom
        PivotOffset = new Vector2(CustomMinimumSize.X / 2f, CustomMinimumSize.Y);

        // Reposition stat badges for the new size
        UpdateStatBadges();
    }

    // ——— Hand hover: enlarge ~1.8x, anchored above the hand (FIX 3d) ———

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

    /// <summary>
    /// Create a high-contrast corner stat badge (FIX 3c).
    /// </summary>
    private Label MakeStatBadge(Color accent)
    {
        var badge = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = ""
        };
        badge.AddThemeColorOverride("font_color", Colors.White);
        badge.AddThemeColorOverride("font_outline_color", Colors.Black);
        badge.AddThemeConstantOverride("outline_size", 4);
        var style = new StyleBoxFlat
        {
            BgColor = accent.Darkened(0.35f),
            BorderColor = accent,
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 4,
            ContentMarginTop = 1,
            ContentMarginRight = 4,
            ContentMarginBottom = 1
        };
        badge.AddThemeStyleboxOverride("normal", style);
        GetNode<Control>("Content").AddChild(badge);
        // Explicit positioning — opt out of Container layout so the PanelContainer
        // doesn't stretch labels full card width (the root cause of the green bar bug).
        badge.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        badge.SizeFlagsHorizontal = 0;
        badge.SizeFlagsVertical = 0;
        return badge;
    }

    /// <summary>
    /// Position + fill the stat corner badges after the card is sized.
    /// </summary>
    private void UpdateStatBadges()
    {
        if (_attackBadge == null || _vigorBadge == null) return;

        float h = CustomMinimumSize.Y;
        float w = CustomMinimumSize.X;
        float badgeSize = Mathf.Max(26f, h * 0.16f);

        bool hasAttack = CardAttack.HasValue;
        bool hasVigor = CardVigor.HasValue;

        _attackBadge.Visible = hasAttack;
        _vigorBadge.Visible = hasVigor;

        if (hasAttack)
        {
            _attackBadge.Text = CardAttack.Value.ToString();
            _attackBadge.Position = new Vector2(2, h - badgeSize - 2);
            _attackBadge.Size = new Vector2(badgeSize, badgeSize);
        }
        if (hasVigor)
        {
            _vigorBadge.Text = CardVigor.Value.ToString();
            _vigorBadge.Position = new Vector2(w - badgeSize - 2, h - badgeSize - 2);
            _vigorBadge.Size = new Vector2(badgeSize, badgeSize);
        }
    }

    /// <summary>
    /// Apply a border + text color to the cost badge.
    /// </summary>
    private void ApplyBadgeStyle(Color borderColor, Color textColor)
    {
        var style = new StyleBoxFlat
        {
            BgColor = BgVoid,
            BorderColor = borderColor,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3
        };
        _badgePanel.AddThemeStyleboxOverride("panel", style);
        _costLabel.AddThemeColorOverride("font_color", textColor);
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