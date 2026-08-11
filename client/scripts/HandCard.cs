using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// A card in the player's hand, rendered as a framed card thumbnail.
/// Shows: cost badge (top-left), art region placeholder, card name,
/// attack/vigor stats (bottom-right), and a strata-colored frame border.
/// </summary>
public partial class HandCard : Button
{
    private Label _cardName;
    private Label _costLabel;
    private Label _statsLabel;
    private TextureRect _artRect;
    private PanelContainer _cardFrame;

    /// <summary>Card's unique identifier from the engine.</summary>
    public string CardId { get; private set; } = "";

    /// <summary>Card's display name.</summary>
    public string CardName { get; private set; } = "";

    /// <summary>Attunement cost to play this card.</summary>
    public int CardCost { get; private set; }

    /// <summary>Card's strata for color coding.</summary>
    public Strata CardStrata { get; private set; }

    /// <summary>Card's attack value (null for non-creatures).</summary>
    public int? CardAttack { get; private set; }

    /// <summary>Card's vigor value (null for non-creatures).</summary>
    public int? CardVigor { get; private set; }

    public override void _Ready()
    {
        _cardFrame = GetNode<PanelContainer>("CardFrame");
        _cardName = GetNode<Label>("CardFrame/VBox/CardName");
        _artRect = GetNode<TextureRect>("CardFrame/VBox/ArtRect");
        _statsLabel = GetNode<Label>("CardFrame/VBox/BottomRow/StatsLabel");
        _costLabel = GetNode<Label>("CostBadge/CostLabel");

        // Apply fonts
        ApplyHeaderFont(_cardName, FontLargeBody);
        ApplyBodyFont(_statsLabel, FontSmall);
        ApplyBodyFont(_costLabel, FontLargeBody);

        // Style the cost badge — dark fill + tarnished gold border
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
    }

    /// <summary>
    /// Configure this hand card widget with card data.
    /// Looks up the CardDef from CardRegistry to get attack/vigor and type.
    /// </summary>
    public void SetCard(string cardId, string name, int cost, Strata strata)
    {
        CardId = cardId;
        CardName = name;
        CardCost = cost;
        CardStrata = strata;

        _cardName.Text = name;
        _costLabel.Text = cost.ToString();

        // Look up card definition for attack/vigor
        var def = CardRegistry.Get(cardId);
        CardAttack = def?.Attack;
        CardVigor = def?.Vigor;

        // Show stats for creatures, hide for non-creatures
        bool isCreature = CardAttack.HasValue && CardVigor.HasValue;
        _statsLabel.Text = isCreature ? $"{CardAttack}/{CardVigor}" : "";
        _statsLabel.Visible = isCreature;

        // Build frame border with strata color
        ApplyFrameStyle(strata);

        // Load card art from WebP (runtime-loadable from content/art/)
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
                return;
            }
            GD.PrintErr($"[HandCard] ResourceLoader returned null for {artPath}");
        }
        // No art available — leave TextureRect empty (dark background shows through)
        _artRect.Texture = null;
    }

    /// <summary>
    /// Apply a neutral worn-metal frame style to the CardFrame.
    /// No strata coloring — the frame is a subtle weathered border.
    /// </summary>
    private void ApplyFrameStyle(Strata strata)
    {
        // Aged paper card face — warmer, lighter than CardFace
        var style = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#332E28"),
            BorderColor = BorderStandard,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 2,
            ContentMarginTop = 2,
            ContentMarginRight = 2,
            ContentMarginBottom = 2
        };

        // Strata-tinted inner glow on the border
        var strataColor = StrataColor(strata);
        style.BorderColor = strataColor.Darkened(0.4f);

        _cardFrame.AddThemeStyleboxOverride("panel", style);
    }

    // ——— Drag-and-drop support ———

    public override Variant _GetDragData(Vector2 atPosition)
    {
        // Create drag preview — a semi-transparent copy of this card
        var preview = new Label();
        preview.Text = CardName;
        preview.Size = new Vector2(80, 24);
        preview.Modulate = new Color(1, 1, 1, 0.7f);
        SetDragPreview(preview);

        // Return card data for the drop target
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