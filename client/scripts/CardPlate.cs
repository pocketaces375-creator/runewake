using Godot;
using System.Collections.Generic;

namespace Runewake.Client;

/// <summary>
/// CardPlate — ONE baked card texture + three code-drawn chips (attack,
/// vigor, cost) in the bake's own style, bigger. FABLE-019c: the FABLE-019
/// round gems were replaced with these. Frame, art and name stay baked.
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
    // Code-drawn chips laid over the baked ones. See Chip()/PlaceChip().
    private Panel? _atkGem, _vigGem, _costGem;
    private Label? _costNum;

    private static readonly Color CREAM = new Color(0.941f, 0.894f, 0.816f);
    // FABLE-019d: numerals are BLACK on the red/green chips (Trikzos: "contrast
    // with the white and the little box … maybe we make them black"). A changed
    // value is the exception: bright, with a coloured edge, so a buff/debuff
    // still jumps out against the black baseline.
    private static readonly Color INK = new Color(0.06f, 0.04f, 0.03f);
    private static readonly Color INK_EDGE = new Color(1f, 0.96f, 0.86f, 0.35f);
    private static readonly Color RED_TINT = new Color(1.00f, 0.93f, 0.90f);
    private static readonly Color GREEN_TINT = new Color(0.92f, 1.00f, 0.88f);
    private static readonly Color RED_EDGE = new Color(0.45f, 0.02f, 0.02f, 0.95f);
    private static readonly Color GREEN_EDGE = new Color(0.02f, 0.30f, 0.05f, 0.95f);
    // FABLE-019c: the baked chips' own colours, sampled from the bake
    // (cinder_runner: attack 175,58,48 · vigor 76,139,77 · cost disc 74,59,21).
    private static readonly Color ATK_FILL = new Color(0.78f, 0.26f, 0.20f);
    private static readonly Color VIG_FILL = new Color(0.34f, 0.62f, 0.34f);
    private static readonly Color CHIP_EDGE = new Color(0.08f, 0.05f, 0.04f, 0.9f);
    private static readonly Color COST_FILL = new Color(0.29f, 0.23f, 0.08f);
    private static readonly Color COST_RIM_READY = new Color(0.98f, 0.85f, 0.45f);  // the bake's gold ring
    private static readonly Color COST_RIM_SHORT = new Color(0.55f, 0.47f, 0.28f);  // dimmed: can't afford

    /// <summary>
    /// FABLE-019c: the SAME chips as the bake, drawn in code so they can be
    /// bigger. Trikzos on the FABLE-019 round gems: "absolutely horrible …
    /// not even in the right place … looks very similar to before just much
    /// more visible please." So: the baked rounded-rectangle chips, in the
    /// baked colours, anchored to the same outer corners of the name band
    /// (they grow inward and cover the baked chip), at 1.75x the width and
    /// 1.8x the height. Nothing overhangs the frame, so fanned hand cards no
    /// longer draw on top of their neighbours. The cost disc is the baked gold
    /// ring, 1.5x, anchored to its own top-right corner.
    /// </summary>
    private const float ChipWScale = 1.75f;
    private const float ChipHScale = 1.8f;
    private const float CostScale = 1.5f;

    public void Setup(string cardId, int? attack, int? vigor,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        Position = Vector2.Zero;
        Size = new Vector2(cardWidth, cardHeight);
        _baseAtk = attack ?? -1; _baseVig = vigor ?? -1;
        _atkR = RectOf(cardId, "attack_badge");
        _vigR = RectOf(cardId, "vigor_badge");
        var costR = RectOf(cardId, "cost_badge");
        // A card has stats to show only if its bake HAS stat badges. Relics and
        // spells carry Attack/Vigor = 0 in the registry; their badge rects are
        // 0,0,0,0 and they must not show 0/0.
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
            (_atkGem, _atkNum) = Chip("AttackChip", ATK_FILL, ink: true); AddChild(_atkGem);
            (_vigGem, _vigNum) = Chip("VigorChip", VIG_FILL, ink: true); AddChild(_vigGem);
            (_costGem, _costNum) = Chip("CostDisc", COST_FILL); AddChild(_costGem);
        }

        string ip = $"res://content/art/cards_baked/{cardId}.webp";
        var tex = ResourceLoader.Load<Texture2D>(ip);
        if (tex != null)
            _baked.Texture = tex;
        else
            GD.PrintErr($"[BAKE] load failed: {ip}");

        if (_hasB)
        {
            // Baked chip rects in card fractions: x, y, w, h.
            float w = _atkR.Z * cardWidth * ChipWScale, h = _atkR.W * cardHeight * ChipHScale;
            // Attack keeps its LEFT edge and grows right; both keep the baked
            // chip's vertical centre, so the name above is not eaten.
            float cy = (_atkR.Y + _atkR.W / 2f) * cardHeight;
            PlaceChip(_atkGem!, _atkNum!, new Rect2(_atkR.X * cardWidth, cy - h / 2f, w, h), false);
            float vRight = (_vigR.X + _vigR.Z) * cardWidth;
            PlaceChip(_vigGem!, _vigNum!, new Rect2(vRight - w, cy - h / 2f, w, h), false);
            _atkNum!.Text = attack!.Value.ToString();
            _vigNum!.Text = vigor!.Value.ToString();
        }
        _atkGem!.Visible = _hasB;
        _vigGem!.Visible = _hasB;

        // Cost: the baked gold ring, bigger, anchored to its top-right corner.
        _cost = cost;
        _costGem!.Visible = cost > 0 && costR.Z > 0f;
        if (_costGem.Visible)
        {
            float d = costR.Z * cardWidth * CostScale;
            float right = (costR.X + costR.Z) * cardWidth, top = costR.Y * cardHeight;
            PlaceChip(_costGem, _costNum!, new Rect2(right - d, top, d, d), true);
            _costNum!.Text = cost.ToString();
            RecolourCost();
        }
    }

    public void SetAttunementAvailable(int available)
    {
        _attuneAvailable = available;
        RecolourCost();
    }

    /// <summary>Gold ring when affordable, dimmed when not. Numeral stays cream, as baked.</summary>
    private void RecolourCost()
    {
        if (_costGem == null || _cost <= 0) return;
        if (_costGem.GetThemeStylebox("panel") is StyleBoxFlat st)
            st.BorderColor = _cost <= _attuneAvailable ? COST_RIM_READY : COST_RIM_SHORT;
    }

    public void SetStatValues(int? attack, int? vigor)
    {
        if (!_hasB || _atkNum == null || _vigNum == null) return;
        if (attack.HasValue) { _atkNum.Text = attack.Value.ToString(); Tint(_atkNum, attack.Value, _baseAtk); }
        if (vigor.HasValue) { _vigNum.Text = vigor.Value.ToString(); Tint(_vigNum, vigor.Value, _baseVig); }
    }

    private static void Tint(Label num, int value, int baseline)
    {
        if (baseline >= 0 && value < baseline)
        {
            num.AddThemeColorOverride("font_color", RED_TINT);
            num.AddThemeColorOverride("font_outline_color", RED_EDGE);
        }
        else if (baseline >= 0 && value > baseline)
        {
            num.AddThemeColorOverride("font_color", GREEN_TINT);
            num.AddThemeColorOverride("font_outline_color", GREEN_EDGE);
        }
        else
        {
            num.AddThemeColorOverride("font_color", INK);
            num.AddThemeColorOverride("font_outline_color", INK_EDGE);
        }
    }

    /// <summary>A baked-style chip: filled rounded rectangle (or disc), thin dark edge, one numeral in the card's own serif.</summary>
    private static (Panel chip, Label num) Chip(string name, Color fill, bool ink = false)
    {
        var chip = new Panel { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = fill,
            BorderColor = CHIP_EDGE,
            ShadowColor = new Color(0, 0, 0, 0.45f),
            AntiAliasing = true,
        });
        var num = new Label
        {
            Name = "Num",
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        num.AddThemeColorOverride("font_color", ink ? INK : CREAM);
        num.AddThemeColorOverride("font_outline_color", ink ? INK_EDGE : new Color(0, 0, 0, 0.85f));
        chip.AddChild(num);
        return (chip, num);
    }

    /// <summary>Lay a chip over a rect. Corner radius, edge, shadow and numeral all scale with its height.</summary>
    private static void PlaceChip(Panel chip, Label num, Rect2 r, bool disc)
    {
        chip.Position = r.Position;
        chip.Size = r.Size;
        float h = r.Size.Y;
        if (chip.GetThemeStylebox("panel") is StyleBoxFlat st)
        {
            int radius = disc ? Mathf.RoundToInt(h / 2f) : Mathf.Max(2, Mathf.RoundToInt(h * 0.22f));
            st.CornerRadiusTopLeft = st.CornerRadiusTopRight = st.CornerRadiusBottomLeft = st.CornerRadiusBottomRight = radius;
            int edge = Mathf.Max(1, Mathf.RoundToInt(h * (disc ? 0.07f : 0.06f)));
            st.BorderWidthLeft = st.BorderWidthTop = st.BorderWidthRight = st.BorderWidthBottom = edge;
            st.ShadowSize = Mathf.Max(1, Mathf.RoundToInt(h * 0.06f));
            st.ShadowOffset = new Vector2(0, Mathf.Max(1, h * 0.03f));
            st.CornerDetail = disc ? 16 : 6;
        }
        num.Position = Vector2.Zero;
        num.Size = r.Size;
        int fs = Mathf.Max(10, Mathf.RoundToInt(h * (disc ? 0.66f : 0.80f)));
        num.AddThemeFontOverride("font", ThemeTokens.GetButtonFont(fs));   // Cormorant Bold: the bake's own numeral face
        num.AddThemeFontSizeOverride("font_size", fs);
        num.AddThemeConstantOverride("outline_size", Mathf.Max(1, Mathf.RoundToInt(fs * 0.08f)));
    }

    public Label? GetNameLabel() => null;
    public Rect2 GetNameRect() => new Rect2();
}