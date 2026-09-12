using Godot;
using System.Collections.Generic;

namespace Runewake.Client;

/// <summary>
/// CardPlate — ONE baked card texture + TWO dynamic stat numerals.
/// Everything else (frame, art, name, cost, badge shapes) is baked.
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

    private static readonly Color CREAM = new Color(0.941f, 0.894f, 0.816f);
    private static readonly Color ATK_COLOR = new Color(150f/255f, 45f/255f, 38f/255f);
    private static readonly Color VIG_COLOR = new Color(58f/255f, 105f/255f, 58f/255f);
    private static readonly Color RED_TINT = new Color(120f/255f, 30f/255f, 34f/255f);
    private static readonly Color GREEN_TINT = new Color(34f/255f, 90f/255f, 34f/255f);

    public void Setup(string cardId, int? attack, int? vigor,
        float cardWidth, float cardHeight, int cost = 0, bool isArtifact = false,
        Texture2D? artTexture = null)
    {
        Position = Vector2.Zero;
        Size = new Vector2(cardWidth, cardHeight);
        _baseAtk = attack ?? -1; _baseVig = vigor ?? -1;
        _hasB = attack.HasValue && vigor.HasValue;
        _atkR = RectOf(cardId, "attack_badge");
        _vigR = RectOf(cardId, "vigor_badge");

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

            _atkNum = MkNum(); AddChild(_atkNum);
            _vigNum = MkNum(); AddChild(_vigNum);
        }

        string ip = $"res://content/art/cards_baked/{cardId}.webp";
        var tex = ResourceLoader.Load<Texture2D>(ip);
        if (tex != null)
            _baked.Texture = tex;
        else
            GD.PrintErr($"[BAKE] load failed: {ip}");

        float fs = 0;
        if (_hasB)
        {
            float ax = _atkR.X * cardWidth, ay = _atkR.Y * cardHeight;
            _atkNum.Position = new Vector2(ax, ay);
            _atkNum.Size = new Vector2(_atkR.Z * cardWidth, _atkR.W * cardHeight);
            float vx = _vigR.X * cardWidth, vy = _vigR.Y * cardHeight;
            _vigNum.Position = new Vector2(vx, vy);
            _vigNum.Size = new Vector2(_vigR.Z * cardWidth, _vigR.W * cardHeight);
            fs = Mathf.Clamp(Mathf.RoundToInt(_atkR.W * cardHeight * 0.55f), 9, 20);
        }
        _atkNum.Visible = _hasB;
        _vigNum.Visible = _hasB;

        if (_hasB)
        {
            _atkNum.Text = attack!.Value.ToString();
            _vigNum.Text = vigor!.Value.ToString();
            _atkNum.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(fs));
            _vigNum.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(fs));
            // Set chip-specific numeral colours (replaces default CREAM)
            _atkNum.AddThemeColorOverride("font_color", ATK_COLOR);
            _vigNum.AddThemeColorOverride("font_color", VIG_COLOR);
        }
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

    private static Label MkNum()
    {
        var l = new Label { MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        l.AddThemeColorOverride("font_color", ATK_COLOR);
        return l;
    }

    public Label? GetNameLabel() => null;
    public Rect2 GetNameRect() => new Rect2();
}