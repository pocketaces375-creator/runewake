using Godot;
using System.Collections.Generic;

namespace Runewake.Client;

/// <summary>
/// CardPlate — ONE baked card texture + three code-drawn gems (attack,
/// vigor, cost). FABLE-019: the gems replaced the baked badge chips, which
/// were too small to read on the board. Frame, art and name stay baked.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    private static Dictionary<string, Godot.Collections.Dictionary> _layout = null!;
    private static bool _layoutLoaded = false;

    private static void Ensure()
    {
        if (_layoutLoaded) return;
        _layoutLoaded = true;
        using var f = Godot.FileAccess.Open("res://content/art/cards_baked/layout.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PrintErr("[BAKE] layout.json not found"); return; }
        var d = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        _layout = new();
        var cards = d["cards"].AsGodotDictionary();
        foreach (var k in cards.Keys)
            _layout[(string)k] = cards[k].AsGodotDictionary();
    }

    private static Vector4 RectOf(string cardId, string key)
    {
        Ensure();
        if (!_layout.ContainsKey(cardId)) return Vector4.Zero;
        var c = _layout[cardId];
        if (!c.ContainsKey(key)) return Vector4.Zero;
        var a = c[key].AsGodotArray();
        return new Vector4((float)(double)a[0], (float)(double)a[1], (float)(double)a[2], (float)(double)a[3]);
    }

    private TextureRect? _baked;
    private Label? _atkNum, _vigNum;
    private Vector4 _atkR, _vigR;
    private int _baseAtk = -1, _baseVig = -1;
    private bool _hasB;
    private int _cost = 0;
    private int _attuneAvailable = 0;
    // FABLE-019: code-drawn gems that replace the tiny baked badges. See Gem().
    private Panel? _atkGem, _vigGem, _costGem;
    private Label? _costNum;

    private static readonly Color CREAM = new Color(0.941f, 0.894f, 0.816f);
    // FABLE-019: these used to be near-cream pastels that were invisible on the
    // old 20px chips. On the gems they must read at a glance: reduced = hot
    // coral, raised = bright leaf. The black outline carries them on either fill.
    private static readonly Color RED_TINT = new Color(1.00f, 0.55f, 0.48f);
    private static readonly Color GREEN_TINT = new Color(0.66f, 1.00f, 0.55f);
    private static readonly Color GEM_RIM = new Color(0.79f, 0.66f, 0.30f);       // gold #C9A84C
    private static readonly Color ATK_FILL = new Color(0.47f, 0.11f, 0.08f);      // deep ember
    private static readonly Color VIG_FILL = new Color(0.12f, 0.35f, 0.16f);      // deep moss
    private static readonly Color COST_FILL = new Color(0.10f, 0.08f, 0.06f);     // near-black stone
    private static readonly Color COST_READY = new Color(1.00f, 0.82f, 0.36f);    // lit gold
    private static readonly Color COST_SHORT = new Color(0.62f, 0.52f, 0.38f);    // dull brass

    /// <summary>
    /// FABLE-019: gem diameter as a fraction of card WIDTH.
    ///
    /// Trikzos: "strength and vigor need to be way more visible. I can't see
    /// just about any of them on the field." Measured on a rendered board: the
    /// baked badges were 13.5% x 5.3% of the card (about 28 x 14 px on a
    /// 205 px board card) with a numeral clamped to 9–20 px. The gems are 30%
    /// of the width — about 62 px on the same card, numeral ~38 px — and the
    /// cost gem 26%. They sit on the corners and are allowed to overhang the
    /// frame a little, the way card games generally present stats, so they do
    /// not eat the art or the name.
    /// </summary>
    private const float StatGemFrac = 0.30f;
    private const float CostGemFrac = 0.26f;

    public void Setup(string cardId, int? attack, int? vigor,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        Position = Vector2.Zero;
        Size = new Vector2(cardWidth, cardHeight);
        _baseAtk = attack ?? -1; _baseVig = vigor ?? -1;
        _atkR = RectOf(cardId, "attack_badge");
        _vigR = RectOf(cardId, "vigor_badge");
        // FABLE-019: a card has stats to show only if its bake HAS stat badges.
        // Relics and spells carry Attack/Vigor = 0 in the registry; their badge
        // rects are 0,0,0,0. The old chips were zero-size on them and so
        // invisible; the new gems must check explicitly or every relic shows 0/0.
        _hasB = attack.HasValue && vigor.HasValue && _atkR.Z > 0f && _vigR.Z > 0f;

        if (_baked == null)
        {
            _baked = new TextureRect
            {
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Position = Vector2.Zero, Size = new Vector2(cardWidth, cardHeight)
            };
            AddChild(_baked);

            // FABLE-019: gems, not a baked chip plus a floating label. The old
            // cost-pip dock is gone: its pips were 7px * (cardHeight/1080), about
            // 2px on a board card, i.e. invisible.
            (_atkGem, _atkNum) = Gem("AttackGem", ATK_FILL); AddChild(_atkGem);
            (_vigGem, _vigNum) = Gem("VigorGem", VIG_FILL); AddChild(_vigGem);
            (_costGem, _costNum) = Gem("CostGem", COST_FILL); AddChild(_costGem);
        }

        string ip = $"res://content/art/cards_baked/{cardId}.webp";
        var tex = ResourceLoader.Load<Texture2D>(ip);
        if (tex != null)
            _baked.Texture = tex;
        else
            GD.PrintErr($"[BAKE] load failed: {ip}");

        // ── FABLE-019: stat gems ──
        // Centred on the corners of the name band, overhanging the frame a
        // touch. Centres are fixed fractions of the card rather than the baked
        // badge rects, which are 0,0,0,0 on non-creatures and sit too far in on
        // creatures for a gem this size (they would cover the name).
        float statD = Mathf.Round(cardWidth * StatGemFrac);
        float costD = Mathf.Round(cardWidth * CostGemFrac);
        if (_hasB)
        {
            PlaceGem(_atkGem!, _atkNum!, statD, new Vector2(cardWidth * 0.13f, cardHeight * 0.925f));
            PlaceGem(_vigGem!, _vigNum!, statD, new Vector2(cardWidth * 0.87f, cardHeight * 0.925f));
            _atkNum!.Text = attack!.Value.ToString();
            _vigNum!.Text = vigor!.Value.ToString();
        }
        _atkGem!.Visible = _hasB;
        _vigGem!.Visible = _hasB;

        // ── FABLE-019: cost gem ── over the baked cost circle (top-right),
        // big enough to cover it. Colour tracks affordability (SetAttunementAvailable).
        _cost = cost;
        _costGem!.Visible = cost > 0;
        if (cost > 0)
        {
            PlaceGem(_costGem, _costNum!, costD, new Vector2(cardWidth * 0.86f, cardHeight * 0.085f));
            _costNum!.Text = cost.ToString();
            RecolourCost();
        }
    }

    public void SetAttunementAvailable(int available)
    {
        _attuneAvailable = available;
        RecolourCost();
    }

    /// <summary>Lit gold numeral when affordable, dull brass when not.</summary>
    private void RecolourCost()
    {
        if (_costNum == null || _cost <= 0) return;
        _costNum.AddThemeColorOverride("font_color", _cost <= _attuneAvailable ? COST_READY : COST_SHORT);
    }

    public void SetStatValues(int? attack, int? vigor)
    {
        if (!_hasB || _atkNum == null || _vigNum == null) return;
        if (attack.HasValue)
        {
            _atkNum.Text = attack.Value.ToString();
            if (_baseAtk >= 0 && attack.Value < _baseAtk) _atkNum.AddThemeColorOverride("font_color", RED_TINT);
            else if (_baseAtk >= 0 && attack.Value > _baseAtk) _atkNum.AddThemeColorOverride("font_color", GREEN_TINT);
            else _atkNum.AddThemeColorOverride("font_color", CREAM);
        }
        if (vigor.HasValue)
        {
            _vigNum.Text = vigor.Value.ToString();
            if (_baseVig >= 0 && vigor.Value < _baseVig) _vigNum.AddThemeColorOverride("font_color", RED_TINT);
            else if (_baseVig >= 0 && vigor.Value > _baseVig) _vigNum.AddThemeColorOverride("font_color", GREEN_TINT);
            else _vigNum.AddThemeColorOverride("font_color", CREAM);
        }
    }

    private static Font? _numeralFont;

    /// <summary>
    /// FABLE-019: Inter at weight 800 for numerals. The display serif (Cinzel)
    /// draws "1" as a capital I — fine in a title, wrong on a stat you read in
    /// a glance mid-fight. Inter has plain lining figures. Falls back to the
    /// button font if the file is missing, so a missing asset costs style, not
    /// numbers.
    /// </summary>
    private static Font? NumeralFont()
    {
        if (_numeralFont != null) return _numeralFont;
        try
        {
            var inter = ResourceLoader.Exists(ThemeTokens.FontInter) ? GD.Load<FontFile>(ThemeTokens.FontInter) : null;
            if (inter != null)
            {
                var v = new FontVariation { BaseFont = inter };
                var ts = TextServerManager.GetPrimaryInterface();
                v.VariationOpentype = new Godot.Collections.Dictionary { { ts.NameToTag("wght"), 800 } };
                _numeralFont = v;
                return _numeralFont;
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[CardPlate] numeral font: {ex.Message} — falling back");
        }
        _numeralFont = ThemeTokens.GetButtonFont(32);
        return _numeralFont;
    }

    /// <summary>A round gem: filled disc, gold rim, drop shadow, one big numeral.</summary>
    private static (Panel gem, Label num) Gem(string name, Color fill)
    {
        var gem = new Panel { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = GEM_RIM,
            ShadowColor = new Color(0, 0, 0, 0.55f),
            AntiAliasing = true,
        };
        gem.AddThemeStyleboxOverride("panel", style);

        var num = new Label
        {
            Name = "Num",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        num.AddThemeColorOverride("font_color", CREAM);
        num.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        var font = NumeralFont();
        if (font != null) num.AddThemeFontOverride("font", font);
        gem.AddChild(num);
        return (gem, num);
    }

    /// <summary>Size and centre a gem; everything scales with its diameter.</summary>
    private static void PlaceGem(Panel gem, Label num, float d, Vector2 centre)
    {
        gem.Size = new Vector2(d, d);
        gem.Position = centre - new Vector2(d, d) / 2f;
        if (gem.GetThemeStylebox("panel") is StyleBoxFlat st)
        {
            int r = Mathf.RoundToInt(d / 2f);
            st.CornerRadiusTopLeft = st.CornerRadiusTopRight = st.CornerRadiusBottomLeft = st.CornerRadiusBottomRight = r;
            int rim = Mathf.Max(2, Mathf.RoundToInt(d * 0.075f));
            st.BorderWidthLeft = st.BorderWidthTop = st.BorderWidthRight = st.BorderWidthBottom = rim;
            st.ShadowSize = Mathf.Max(2, Mathf.RoundToInt(d * 0.08f));
            st.ShadowOffset = new Vector2(0, Mathf.Max(1, d * 0.04f));
            st.CornerDetail = 16;
        }
        num.Position = Vector2.Zero;
        num.Size = new Vector2(d, d);
        num.AddThemeFontSizeOverride("font_size", Mathf.Max(12, Mathf.RoundToInt(d * 0.62f)));
        num.AddThemeConstantOverride("outline_size", Mathf.Max(3, Mathf.RoundToInt(d * 0.09f)));
    }

    public Label? GetNameLabel() => null;
    public Rect2 GetNameRect() => new Rect2();
}