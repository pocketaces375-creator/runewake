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
    private Label _statsLabel;
    private FixedArtRect _artRect;

    /// <summary>Card's unique identifier from the engine.</summary>
    public string CardId { get; private set; } = "";
    public string CardName { get; private set; } = "";
    public int CardCost { get; private set; }
    public Strata CardStrata { get; private set; }
    public int? CardAttack { get; private set; }
    public int? CardVigor { get; private set; }

    /// <summary>ArtRect for verification.</summary>
    public Control ArtRectNode => _artRect;

    [Signal]
    public delegate void PressedEventHandler();

    public override void _Ready()
    {
        _cardName = GetNode<Label>("VBox/CardName");
        _artRect = GetNode<FixedArtRect>("VBox/ArtRect");
        _statsLabel = GetNode<Label>("VBox/BottomRow/StatsLabel");
        _costLabel = GetNode<Label>("CostBadge/CostLabel");

        ApplyHeaderFont(_cardName, FontLargeBody);
        ApplyBodyFont(_statsLabel, FontSmall);
        ApplyBodyFont(_costLabel, FontLargeBody);

        // Style cost badge
        var badgeStyle = new StyleBoxFlat
        {
            BgColor = BgVoid,
            BorderColor = Gold,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3
        };
        GetNode<PanelContainer>("CostBadge").AddThemeStyleboxOverride("panel", badgeStyle);

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

        bool isCreature = CardAttack.HasValue && CardVigor.HasValue;
        _statsLabel.Text = isCreature ? $"{CardAttack}/{CardVigor}" : "";
        _statsLabel.Visible = isCreature;

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

        LoadArt(cardId);
    }

    private void LoadArt(string cardId)
    {
        // Remove any previous sprite child from FixedArtRect
        foreach (var child in _artRect.GetChildren())
            _artRect.RemoveChild(child);

        string artPath = $"res://content/art/{cardId}.webp";
        if (ResourceLoader.Exists(artPath))
        {
            var texture = ResourceLoader.Load<Texture2D>(artPath);
            if (texture != null)
            {
                // Use TextureRect — the FixedArtRect's clip_contents keeps it bounded.
                // TextureRect renders in Control layer (on top of card bg).
                var tr = new TextureRect();
                tr.Texture = texture;
                tr.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                tr.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                tr.AnchorRight = 1.0f;
                tr.AnchorBottom = 1.0f;
                tr.MouseFilter = MouseFilterEnum.Ignore;
                _artRect.AddChild(tr);

                GD.Print($"[HANDCARD] {cardId} art via TextureRect, tex={texture.GetSize()}");
                return;
            }
        }
        GD.Print($"[HANDCARD] No art for {cardId}");
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