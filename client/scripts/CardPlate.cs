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
    private int _cost = 0;
    private int _attuneAvailable = 0;
    private Control? _pipDock;
    private ColorRect? _pipGradient;

    private static readonly Color CREAM = new Color(0.941f, 0.894f, 0.816f);
    private static readonly Color RED_TINT = new Color(0.941f, 0.800f, 0.750f);
    private static readonly Color GREEN_TINT = new Color(0.750f, 0.941f, 0.800f);

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

            // Cost-pip dock
            _pipDock = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Name = "PipDock" };
            AddChild(_pipDock);
            _pipGradient = new ColorRect
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Color = new Color(0.082f, 0.074f, 0.059f, 0.85f),
                Name = "PipGradient"
            };
            _pipDock.AddChild(_pipGradient);

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
        }
        // A1: Cost-pip dock — N diamonds on gradient pill, top-left
        _cost = cost;
        RebuildCostPips(cardWidth, cardHeight);
    }

    public void SetAttunementAvailable(int available)
    {
        _attuneAvailable = available;
        if (_pipDock != null) RebuildCostPips(Size.X, Size.Y);
    }

    private void RebuildCostPips(float cardWidth, float cardHeight)
    {
        if (_pipDock == null) return;
        // Remove old pips
        foreach (var child in _pipDock.GetChildren())
        {
            if (child is ColorRect cr && cr.Name.ToString().StartsWith("Pip_"))
                cr.QueueFree();
        }
        if (_cost <= 0) { _pipDock.Visible = false; return; }
        _pipDock.Visible = true;
        float scale = cardHeight / 1080f;
        float pipS = 7f * scale;
        float gap = 3f * scale;
        float totalW = _cost * pipS + (_cost - 1) * gap;
        float dockW = totalW + 12f * scale;
        float dockH = pipS + 10f * scale;
        _pipGradient!.Size = new Vector2(dockW, dockH);
        float dockX = 6f * scale;
        float dockY = 6f * scale;
        _pipDock.Position = new Vector2(dockX, dockY);
        _pipGradient.Position = Vector2.Zero;
        for (int i = 0; i < _cost; i++)
        {
            var pip = new ColorRect
            {
                Name = $"Pip_{i}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                CustomMinimumSize = new Vector2(pipS, pipS),
                Size = new Vector2(pipS, pipS),
                Color = i < _attuneAvailable ? Color.FromHtml("#E3B23C") : Color.FromHtml("#6B5636"),
            };
            pip.Position = new Vector2(6f * scale + i * (pipS + gap), (dockH - pipS) / 2f);
            pip.Rotation = Mathf.DegToRad(45f);
            _pipDock.AddChild(pip);
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
        l.AddThemeColorOverride("font_color", CREAM);
        l.AddThemeConstantOverride("outline_size", 1);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.4f));
        return l;
    }

    public Label? GetNameLabel() => null;
    public Rect2 GetNameRect() => new Rect2();
}