using Godot;
using System.Collections.Generic;

namespace Runewake.Client;

/// <summary>
/// CardPlate — one baked card face + three live numerals.
///
/// FABLE-037 (the RUNESTONE layout, chosen by Trikzos over five rounds of mock-ups): the bake
/// (pipeline/bake_cards.py) now carries everything that never changes — art, stone frame, the
/// carved name band with its gold rule, the two slim heater shields (oxblood + sword, moss +
/// heart) and the gold cost diamond whose face is the card TYPE's colour (creature green,
/// ritual blue, relic violet). The only thing drawn here is what CAN change during a duel: the
/// Strength, Vigor and cost numerals, in the bake's own numeral face (Cinzel Black), cream with
/// a dark outline, each centred on the spot the bake reserved for it (layout.json: *_num =
/// visual centre x, y and em size, in card fractions).
///
/// The earlier code-drawn chips (FABLE-019c…030) and the vigor "cover" patch are gone: the
/// shields and diamond are part of the painting now, so nothing sits on top of the art but
/// three numbers.
/// </summary>
public partial class CardPlate : Control
{
    public CardPlate() { MouseFilter = MouseFilterEnum.Ignore; }

    private static Dictionary<string, Godot.Collections.Dictionary> _layout = null!;
    private static bool _layoutLoaded;
    /// <summary>Baseline sits this share of the em below a numeral's visual centre (from the bake).</summary>
    private static float _numeralMid = 0.3355f;

    private static void Ensure()
    {
        if (_layoutLoaded) return;
        _layoutLoaded = true;
        _layout = new();
        using var f = Godot.FileAccess.Open("res://content/art/cards_baked/layout.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PrintErr("[BAKE] layout.json not found"); return; }
        var d = Json.ParseString(f.GetAsText()).AsGodotDictionary();
        if (d.ContainsKey("numeral_mid")) _numeralMid = (float)(double)d["numeral_mid"];
        var cards = d["cards"].AsGodotDictionary();
        foreach (var k in cards.Keys)
            _layout[(string)k] = cards[k].AsGodotDictionary();
    }

    private static float[] ArrayOf(string cardId, string key, int n)
    {
        Ensure();
        var r = new float[n];
        if (string.IsNullOrEmpty(cardId) || !_layout.ContainsKey(cardId)) return r;
        var c = _layout[cardId];
        if (!c.ContainsKey(key)) return r;
        var a = c[key].AsGodotArray();
        for (int i = 0; i < n && i < a.Count; i++) r[i] = (float)(double)a[i];
        return r;
    }

    /// <summary>Kept for callers and probes from the chip era; the layout is baked now, so it has no effect.</summary>
    public static string StatsLayout = "left";

    private TextureRect? _baked;
    private Label? _atkNum, _vigNum, _costNum;
    private int _baseAtk = -1, _baseVig = -1;
    private bool _hasStats;
    private int _cost;
    private int _attuneAvailable = int.MaxValue;

    private static readonly Color CREAM = new(0.957f, 0.925f, 0.855f);
    private static readonly Color OUTLINE = new(0.05f, 0.035f, 0.02f, 1f);
    private static readonly Color BUFF = new(0.74f, 1.00f, 0.62f);
    private static readonly Color BUFF_EDGE = new(0.03f, 0.18f, 0.03f, 1f);
    private static readonly Color HURT = new(1.00f, 0.58f, 0.50f);
    private static readonly Color HURT_EDGE = new(0.25f, 0.02f, 0.02f, 1f);
    private static readonly Color SHORT = new(0.64f, 0.61f, 0.56f);   // cost you cannot pay yet

    public void Setup(string cardId, int? attack, int? vigor,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        Position = Vector2.Zero;
        Size = new Vector2(cardWidth, cardHeight);
        _baseAtk = attack ?? -1; _baseVig = vigor ?? -1;

        if (_baked == null)
        {
            _baked = new TextureRect
            {
                Name = "Baked",
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                TextureFilter = TextureFilterEnum.Linear,
            };
            AddChild(_baked);
            _atkNum = Numeral("AttackNum");
            _vigNum = Numeral("VigorNum");
            _costNum = Numeral("CostNum");
        }
        _baked.Position = Vector2.Zero;
        _baked.Size = new Vector2(cardWidth, cardHeight);

        // FABLE-028: hand cards are set up once before their id is known — don't try to load "".
        if (!string.IsNullOrEmpty(cardId))
        {
            string ip = $"res://content/art/cards_baked/{cardId}.webp";
            var tex = ResourceLoader.Exists(ip) ? ResourceLoader.Load<Texture2D>(ip) : null;
            if (tex != null) _baked.Texture = tex;
            else GD.PrintErr($"[BAKE] no baked art for {cardId}");
        }

        var atk = ArrayOf(cardId, "attack_num", 3);
        var vig = ArrayOf(cardId, "vigor_num", 3);
        var cst = ArrayOf(cardId, "cost_num", 3);

        // Stats only where the bake painted shields. Relics and rituals carry 0/0 or nothing in
        // the registry; their numeral slots are empty and they must not show 0/0.
        _hasStats = attack.HasValue && vigor.HasValue && atk[2] > 0f && vig[2] > 0f;
        _atkNum!.Visible = _vigNum!.Visible = _hasStats;
        if (_hasStats)
        {
            Place(_atkNum, atk, cardWidth, cardHeight);
            Place(_vigNum, vig, cardWidth, cardHeight);
            _atkNum.Text = attack!.Value.ToString();
            _vigNum.Text = vigor!.Value.ToString();
            Tint(_atkNum, attack.Value, _baseAtk);
            Tint(_vigNum, vigor.Value, _baseVig);
        }

        _cost = cost;
        _costNum!.Visible = cst[2] > 0f;
        if (_costNum.Visible)
        {
            Place(_costNum, cst, cardWidth, cardHeight);
            _costNum.Text = cost.ToString();
            RecolourCost();
        }
    }

    public void SetAttunementAvailable(int available)
    {
        _attuneAvailable = available;
        RecolourCost();
    }

    /// <summary>Cream when you can pay it, greyed when you can't. The diamond itself stays baked.</summary>
    private void RecolourCost()
    {
        if (_costNum == null) return;
        _costNum.AddThemeColorOverride("font_color", _cost <= _attuneAvailable ? CREAM : SHORT);
    }

    public void SetStatValues(int? attack, int? vigor)
    {
        if (!_hasStats || _atkNum == null || _vigNum == null) return;
        if (attack.HasValue) { _atkNum.Text = attack.Value.ToString(); Tint(_atkNum, attack.Value, _baseAtk); }
        if (vigor.HasValue) { _vigNum.Text = vigor.Value.ToString(); Tint(_vigNum, vigor.Value, _baseVig); }
    }

    /// <summary>A changed value jumps out: green-cream when raised, rose when lowered, cream at base.</summary>
    private static void Tint(Label num, int value, int baseline)
    {
        bool hurt = baseline >= 0 && value < baseline, buff = baseline >= 0 && value > baseline;
        num.AddThemeColorOverride("font_color", hurt ? HURT : buff ? BUFF : CREAM);
        num.AddThemeColorOverride("font_outline_color", hurt ? HURT_EDGE : buff ? BUFF_EDGE : OUTLINE);
    }

    private static Font? _numeralFont;
    /// <summary>The bake's numeral face: Cinzel at weight 900 (Black).</summary>
    private static Font NumeralFont()
    {
        if (_numeralFont != null) return _numeralFont;
        const string path = "res://assets/fonts/Cinzel.ttf";
        var baseFont = ResourceLoader.Exists(path) ? GD.Load<FontFile>(path) : null;
        if (baseFont == null) return _numeralFont = ThemeTokens.GetButtonFont(32);
        var v = new FontVariation { BaseFont = baseFont };
        try
        {
            var ts = TextServerManager.GetPrimaryInterface();
            v.VariationOpentype = new Godot.Collections.Dictionary { { ts.NameToTag("wght"), 900 } };
        }
        catch (System.Exception ex) { GD.PrintErr($"[CardPlate] numeral weight: {ex.Message}"); }
        return _numeralFont = v;
    }

    private Label Numeral(string name)
    {
        var l = new Label
        {
            Name = name,
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            ClipText = false,
        };
        l.AddThemeFontOverride("font", NumeralFont());
        l.AddThemeColorOverride("font_color", CREAM);
        l.AddThemeColorOverride("font_outline_color", OUTLINE);
        l.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.7f));
        l.AddThemeConstantOverride("line_spacing", 0);
        AddChild(l);
        return l;
    }

    /// <summary>
    /// Put a numeral's VISUAL centre on the bake's spot. A Label's first baseline sits one
    /// ascent below its top; the digits' middle sits numeral_mid × em above the baseline.
    /// </summary>
    private static void Place(Label l, float[] n, float cardW, float cardH)
    {
        int fs = Mathf.Max(8, Mathf.RoundToInt(n[2] * cardH));
        var font = NumeralFont();
        float ascent = font.GetAscent(fs), height = font.GetHeight(fs);
        float boxW = fs * 2.4f;
        float cx = n[0] * cardW, baseline = n[1] * cardH + _numeralMid * fs;
        l.Position = new Vector2(cx - boxW / 2f, baseline - ascent);
        l.Size = new Vector2(boxW, height);
        l.AddThemeFontSizeOverride("font_size", fs);
        int outline = Mathf.Max(2, Mathf.RoundToInt(fs * 0.13f));
        l.AddThemeConstantOverride("outline_size", outline);
        l.AddThemeConstantOverride("shadow_outline_size", outline);
        l.AddThemeConstantOverride("shadow_offset_x", 0);
        l.AddThemeConstantOverride("shadow_offset_y", Mathf.Max(1, Mathf.RoundToInt(fs * 0.05f)));
    }

    public Label? GetNameLabel() => null;
    public Rect2 GetNameRect() => new Rect2();
}
