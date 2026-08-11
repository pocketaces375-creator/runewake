using Godot;
using Runewake.Engine.Cards;
using static ThemeTokens;

namespace Runewake.Client;

/// <summary>
/// A card in the player's hand, rendered as a framed card thumbnail.
/// Shows: cost badge (top-left), art region, card name, stats (bottom-right).
/// Root is a Button for click/drag handling.
/// VBoxContainer child fills the Button via anchors — no intermediate PanelContainer.
/// </summary>
public partial class HandCard : Button
{
    private Label _cardName;
    private Label _costLabel;
    private Label _statsLabel;
    private TextureRect _artRect;

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
        _cardName = GetNode<Label>("VBox/CardName");
        _artRect = GetNode<TextureRect>("VBox/ArtRect");
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

        // Style the card background to match CardFrame from before
        // Aged paper card face
        var cardStyle = new StyleBoxFlat
        {
            BgColor = Color.FromHtml("#332E28"),
            BorderWidthLeft = 0,
            BorderWidthTop = 0,
            BorderWidthRight = 0,
            BorderWidthBottom = 0,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 0,
            ContentMarginTop = 0,
            ContentMarginRight = 0,
            ContentMarginBottom = 0
        };
        AddThemeStyleboxOverride("normal", cardStyle);
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

        // Apply strata tint to card border via modulation
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
        AddThemeStyleboxOverride("normal", cardStyle);

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

                // === DEBUG ===
                GD.Print($"[HANDCARD DEBUG] {cardId} art loaded, texture={texture.GetSize()}");
                Callable.From(() =>
                {
                    var artSize = _artRect.Size;
                    var vboxSize = GetNode<Control>("VBox").Size;
                    var cardSize = Size;
                    GD.Print($"[HANDCARD DEBUG] Card.Size={cardSize}");
                    GD.Print($"[HANDCARD DEBUG] VBox.Size={vboxSize}");
                    GD.Print($"[HANDCARD DEBUG] ArtRect.Size={artSize}");
                    if (artSize.X > vboxSize.X || artSize.Y > vboxSize.Y)
                        GD.PrintErr($"[HANDCARD DEBUG] *** OUT OF BOUNDS delta={artSize - vboxSize}");
                    else
                        GD.Print($"[HANDCARD DEBUG] *** ArtRect FITS inside VBox OK");
                }).CallDeferred();

                return;
            }
        }
        _artRect.Texture = null;
    }
}