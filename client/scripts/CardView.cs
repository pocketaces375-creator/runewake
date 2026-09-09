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
    private HBoxContainer _keywordLine;
    private Label _rulesLabel;
    private Label _flavorLabel;
    private Label _attackLabel;
    private Label _vigorLabel;
    private Control _statsPanel;
    private Button _closeBtn;

    /// <summary>The CardDef this view currently displays, or null.</summary>
    public CardDef? CurrentCard { get; private set; }

    /// <summary>Fired when the user clicks the close button.</summary>
    public event Action? Dismissed;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("Margin/VBox/Header/NameLabel");
        _costLabel = GetNode<Label>("Margin/VBox/Header/CostLabel");
        _artRect = GetNode<TextureRect>("Margin/VBox/ArtRect");
        _typeLine = GetNode<Label>("Margin/VBox/TypeLine");
        _keywordLine = GetNode<HBoxContainer>("Margin/VBox/KeywordLine");
        _rulesLabel = GetNode<Label>("Margin/VBox/RulesLabel");
        _flavorLabel = GetNode<Label>("Margin/VBox/FlavorLabel");
        _attackLabel = GetNode<Label>("Margin/VBox/StatsPanel/AttackLabel");
        _vigorLabel = GetNode<Label>("Margin/VBox/StatsPanel/VigorLabel");
        _statsPanel = GetNode<Control>("Margin/VBox/StatsPanel");
        _closeBtn = GetNode<Button>("CloseBtn");

        // Style the close button
        _closeBtn.AddThemeColorOverride("font_color", new Color(0.8f, 0.7f, 0.5f));
        _closeBtn.Pressed += OnClosePressed;

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

        // Keywords — icon + label pairs
        ClearKeywordLine();
        if (card.Keywords.Count > 0)
        {
            _keywordLine.Visible = true;
            foreach (var kw in card.Keywords)
            {
                // Keyword icon
                string iconPath = KeywordIconPath(kw);
                if (ResourceLoader.Exists(iconPath, nameof(Texture2D)))
                {
                    var icon = new TextureRect
                    {
                        Texture = ResourceLoader.Load<Texture2D>(iconPath),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        CustomMinimumSize = new Vector2(18, 18),
                        Size = new Vector2(18, 18),
                        MouseFilter = MouseFilterEnum.Ignore,
                    };
                    _keywordLine.AddChild(icon);
                }
                // Keyword name label
                var kwLabel = new Label
                {
                    Text = RulesTextRenderer.FormatKeyword(kw),
                    VerticalAlignment = VerticalAlignment.Center,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                kwLabel.AddThemeColorOverride("font_color", ThemeTokens.TextPrimary);
                kwLabel.AddThemeFontSizeOverride("font_size", 14);
                _keywordLine.AddChild(kwLabel);
            }
        }
        else
        {
            _keywordLine.Visible = false;
        }

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
        ClearKeywordLine();
        _rulesLabel.Visible = false;
        _flavorLabel.Visible = false;
        _statsPanel.Visible = false;
        ClearArt();
        SetStrataStyle(Strata.VERDANT);
    }

    private void OnClosePressed()
    {
        Visible = false;
        Dismissed?.Invoke();
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

    private void ClearKeywordLine()
    {
        foreach (var child in _keywordLine.GetChildren())
            child.QueueFree();
        _keywordLine.Visible = false;
    }

    /// <summary>
    /// Map keyword constant to its icon resource path.
    /// </summary>
    public static string KeywordIconPath(string keyword) => keyword.ToUpperInvariant() switch
    {
        "GUARD" => "res://content/art/icons/kw_guard.webp",
        "SWIFT" => "res://content/art/icons/kw_swift.webp",
        "PIERCE" => "res://content/art/icons/kw_pierce.webp",
        "WARD" => "res://content/art/icons/kw_ward.webp",
        "VENOM" => "res://content/art/icons/kw_venom.webp",
        "REACH" => "res://content/art/icons/kw_reach.webp",
        "ROOTED" => "res://content/art/icons/kw_rooted.webp",
        "UNEARTH" => "res://content/art/icons/kw_unearth.webp",
        "ECHO" => "res://content/art/icons/kw_echo.webp",
        "FRAGILE" => "res://content/art/icons/kw_fragile.webp",
        "SEALED" => "res://content/art/icons/kw_sealed.webp",
        _ => ""
    };

    /// <summary>
    /// Map strata constant to its icon resource path.
    /// </summary>
    public static string StrataIconPath(Strata strata) => strata switch
    {
        Strata.VERDANT => "res://content/art/icons/str_verdant.webp",
        Strata.EMBER => "res://content/art/icons/str_ember.webp",
        Strata.TIDE => "res://content/art/icons/str_tide.webp",
        Strata.HOLLOW => "res://content/art/icons/str_hollow.webp",
        Strata.DAWN => "res://content/art/icons/str_dawn.webp",
        _ => ""
    };

    private static string FormatKeyword(string keyword) => RulesTextRenderer.FormatKeyword(keyword);
}