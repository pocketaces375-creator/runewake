using Godot;
using Runewake.Engine.Cards;

namespace Runewake.Client;

/// <summary>
/// Full card view component driven entirely by CardDef data.
/// Displays name, cost, art, type/strata line, keywords, rules text,
/// flavor, and attack/vigor stats. Used as the detail card view.
/// </summary>
public partial class CardView : PanelContainer
{
    private Label _nameLabel;
    private Label _costLabel;
    private TextureRect _artRect;
    private Label _typeLine;
    private Label _keywordLine;
    private Label _rulesLabel;
    private Label _flavorLabel;
    private Label _attackLabel;
    private Label _vigorLabel;
    private Control _statsPanel;

    /// <summary>The CardDef this view currently displays, or null.</summary>
    public CardDef? CurrentCard { get; private set; }

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Margin/VBox/Header/NameLabel");
        _costLabel = GetNode<Label>("Margin/VBox/Header/CostLabel");
        _artRect = GetNode<TextureRect>("Margin/VBox/ArtRect");
        _typeLine = GetNode<Label>("Margin/VBox/TypeLine");
        _keywordLine = GetNode<Label>("Margin/VBox/KeywordLine");
        _rulesLabel = GetNode<Label>("Margin/VBox/RulesLabel");
        _flavorLabel = GetNode<Label>("Margin/VBox/FlavorLabel");
        _attackLabel = GetNode<Label>("Margin/VBox/StatsPanel/AttackLabel");
        _vigorLabel = GetNode<Label>("Margin/VBox/StatsPanel/VigorLabel");
        _statsPanel = GetNode<Control>("Margin/VBox/StatsPanel");

        Clear();
    }

    /// <summary>
    /// Populate all card fields from a CardDef.
    /// </summary>
    public void SetCard(CardDef card)
    {
        CurrentCard = card;

        // Name
        _nameLabel.Text = card.Name;

        // Cost
        _costLabel.Text = card.Cost.ToString();

        // Type line: "Creature · Verdant · Common"
        _typeLine.Text = $"{FormatCardType(card.Type)} · {FormatStrata(card.Strata)} · {FormatRarity(card.Rarity)}";

        // Keywords
        string keywords = card.Keywords.Count > 0
            ? string.Join(", ", card.Keywords.Select(FormatKeyword))
            : "";
        _keywordLine.Text = keywords;
        _keywordLine.Visible = keywords.Length > 0;

        // Rules text (ability text only — stats and flavor are rendered separately)
        string rules = RulesTextRenderer.RenderAbilityTextOnly(card);
        _rulesLabel.Text = rules;
        _rulesLabel.Visible = rules.Length > 0;

        // Flavor
        string flavor = card.Flavor != null ? $"\"{card.Flavor}\"" : "";
        _flavorLabel.Text = flavor;
        _flavorLabel.Visible = flavor.Length > 0;

        // Stats — only for creatures and tokens
        bool hasStats = card.Type is CardType.CREATURE or CardType.TOKEN;
        _statsPanel.Visible = hasStats;
        if (hasStats)
        {
            _attackLabel.Text = (card.Attack ?? 0).ToString();
            _vigorLabel.Text = (card.Vigor ?? 0).ToString();
        }

        // Art
        SetArt(card);

        // Strata border color
        SetStrataStyle(card.Strata);
    }

    /// <summary>
    /// Clear the card view to placeholder state.
    /// </summary>
    public void Clear()
    {
        CurrentCard = null;
        _nameLabel.Text = "?";
        _costLabel.Text = "?";
        _typeLine.Text = "?";
        _keywordLine.Visible = false;
        _rulesLabel.Visible = false;
        _flavorLabel.Visible = false;
        _statsPanel.Visible = false;
        ClearArt();
        SetStrataStyle(Strata.VERDANT);
    }

    // ——— Private helpers ———

    private void SetArt(CardDef card)
    {
        // Try loading from asset path
        if (card.Art?.Asset != null && ResourceLoader.Exists(card.Art.Asset, nameof(Texture2D)))
        {
            var texture = ResourceLoader.Load<Texture2D>(card.Art.Asset);
            if (texture != null)
            {
                _artRect.Texture = texture;
                ClearArtChildren();
                return;
            }
        }

        // Fallback: strata-colored placeholder
        SetArtPlaceholder(card.Strata);
    }

    private void SetArtPlaceholder(Strata strata)
    {
        ClearArtChildren();
        _artRect.Texture = null;

        var rect = new ColorRect();
        rect.Color = strata switch
        {
            Strata.VERDANT => new Color(0.1f, 0.5f, 0.2f, 0.6f),
            Strata.EMBER => new Color(0.7f, 0.2f, 0.1f, 0.6f),
            Strata.TIDE => new Color(0.1f, 0.3f, 0.6f, 0.6f),
            Strata.HOLLOW => new Color(0.3f, 0.1f, 0.3f, 0.6f),
            Strata.DAWN => new Color(0.7f, 0.6f, 0.1f, 0.6f),
            _ => new Color(0.3f, 0.3f, 0.3f, 0.6f)
        };
        rect.Size = _artRect.Size;
        _artRect.AddChild(rect);
    }

    private void ClearArt()
    {
        ClearArtChildren();
        _artRect.Texture = null;
    }

    private void ClearArtChildren()
    {
        foreach (var child in _artRect.GetChildren())
            child.QueueFree();
    }

    private void SetStrataStyle(Strata strata)
    {
        var borderColor = strata switch
        {
            Strata.VERDANT => new Color(0.2f, 0.7f, 0.3f),
            Strata.EMBER => new Color(0.9f, 0.3f, 0.1f),
            Strata.TIDE => new Color(0.2f, 0.5f, 0.8f),
            Strata.HOLLOW => new Color(0.5f, 0.2f, 0.5f),
            Strata.DAWN => new Color(0.9f, 0.8f, 0.2f),
            _ => new Color(0.5f, 0.5f, 0.5f)
        };

        // Apply strata border color via a StyleBoxFlat
        var style = new StyleBoxFlat();
        style.BgColor = new Color(0.08f, 0.08f, 0.1f, 1f);
        style.BorderColor = borderColor;
        style.BorderWidthLeft = 3;
        style.BorderWidthTop = 3;
        style.BorderWidthRight = 3;
        style.BorderWidthBottom = 3;
        style.CornerRadiusTopLeft = 6;
        style.CornerRadiusTopRight = 6;
        style.CornerRadiusBottomLeft = 6;
        style.CornerRadiusBottomRight = 6;
        style.ContentMarginLeft = 0;
        style.ContentMarginTop = 0;
        style.ContentMarginRight = 0;
        style.ContentMarginBottom = 0;
        AddThemeStyleboxOverride("panel", style);
    }

    private static string FormatCardType(CardType type) => type switch
    {
        CardType.CREATURE => "Creature",
        CardType.RITUAL => "Ritual",
        CardType.RELIC => "Relic",
        CardType.CURSE => "Curse",
        CardType.TOKEN => "Token",
        _ => "?"
    };

    private static string FormatStrata(Strata strata) => strata switch
    {
        Strata.VERDANT => "Verdant",
        Strata.EMBER => "Ember",
        Strata.TIDE => "Tide",
        Strata.HOLLOW => "Hollow",
        Strata.DAWN => "Dawn",
        _ => "?"
    };

    private static string FormatRarity(Rarity rarity) => rarity switch
    {
        Rarity.COMMON => "Common",
        Rarity.UNCOMMON => "Uncommon",
        Rarity.RARE => "Rare",
        Rarity.RELIC => "Relic",
        _ => "?"
    };

    private static string FormatKeyword(string keyword) => RulesTextRenderer.FormatKeyword(keyword);
}