using Godot;

namespace Runewake.Client;

/// <summary>
/// Root-Bound (option 5) 9-slice card border overlay.
/// Corners draw at band_px × band_px (never stretched); edges stretch along their length only.
/// Band thickness: band_px = round(card_width * 0.07).
/// Loads the pre-cut 9-slice assets from client/content/art/border/rootbound_*.png.
/// 
/// Add as the FIRST child of any card-sized Control. The art renders behind the border's
/// center window — the border is an overlay, not a background.
/// </summary>
public partial class RootBoundBorder : Control
{
    // ── 9-slice texture rects ──
    private TextureRect? _cornerTl;
    private TextureRect? _cornerTr;
    private TextureRect? _cornerBl;
    private TextureRect? _cornerBr;
    private TextureRect? _edgeTop;
    private TextureRect? _edgeBottom;
    private TextureRect? _edgeLeft;
    private TextureRect? _edgeRight;

    private static readonly Vector2 FullSize = new(832, 1216);

    private static Texture2D? LoadSlice(string name)
    {
        string path = $"res://content/art/border/rootbound_{name}.png";
        if (ResourceLoader.Exists(path))
            return ResourceLoader.Load<Texture2D>(path);
        GD.PrintErr($"[ROOTBOUND] Missing slice: {path}");
        return null;
    }

    /// <summary>
    /// Create all 8 TextureRect children. Safe to call multiple times — reuses existing nodes.
    /// </summary>
    public void Setup(float cardWidth, float cardHeight)
    {
        int bandPx = Mathf.RoundToInt(cardWidth * 0.07f);
        if (bandPx < 1) bandPx = 1;

        // Lazy-init all 8 slices
        if (_cornerTl == null)
        {
            _cornerTl = MakeSlice("corner_tl");
            _cornerTr = MakeSlice("corner_tr");
            _cornerBl = MakeSlice("corner_bl");
            _cornerBr = MakeSlice("corner_br");
            _edgeTop = MakeSlice("edge_top");
            _edgeBottom = MakeSlice("edge_bottom");
            _edgeLeft = MakeSlice("edge_left");
            _edgeRight = MakeSlice("edge_right");
        }

        // ═══ LAYOUT ═══
        // Corners: band_px × band_px at each corner
        _cornerTl.Position = new Vector2(0, 0);
        _cornerTl.Size = new Vector2(bandPx, bandPx);

        _cornerTr.Position = new Vector2(cardWidth - bandPx, 0);
        _cornerTr.Size = new Vector2(bandPx, bandPx);

        _cornerBl.Position = new Vector2(0, cardHeight - bandPx);
        _cornerBl.Size = new Vector2(bandPx, bandPx);

        _cornerBr.Position = new Vector2(cardWidth - bandPx, cardHeight - bandPx);
        _cornerBr.Size = new Vector2(bandPx, bandPx);

        // Edges stretch along one axis, fixed band_px on the other
        _edgeTop.Position = new Vector2(bandPx, 0);
        _edgeTop.Size = new Vector2(cardWidth - bandPx * 2, bandPx);

        _edgeBottom.Position = new Vector2(bandPx, cardHeight - bandPx);
        _edgeBottom.Size = new Vector2(cardWidth - bandPx * 2, bandPx);

        _edgeLeft.Position = new Vector2(0, bandPx);
        _edgeLeft.Size = new Vector2(bandPx, cardHeight - bandPx * 2);

        _edgeRight.Position = new Vector2(cardWidth - bandPx, bandPx);
        _edgeRight.Size = new Vector2(bandPx, cardHeight - bandPx * 2);
    }

    /// <summary>Get the band pixel width (for use by CardPlate positioning).</summary>
    public int GetBandPx(float cardWidth) => Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));

    private static TextureRect MakeSlice(string name)
    {
        var tr = new TextureRect
        {
            Texture = LoadSlice(name),
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Size = new Vector2(1, 1)
        };
        return tr;
    }

    /// <summary>Add all 8 slice nodes to a parent. Call once during card setup.</summary>
    public void AttachTo(Control parent)
    {
        if (_cornerTl == null) return;
        // Insert behind everything
        parent.AddChild(_cornerTl);
        parent.MoveChild(_cornerTl, 0);
        parent.AddChild(_cornerTr);
        parent.MoveChild(_cornerTr, 0);
        parent.AddChild(_cornerBl);
        parent.MoveChild(_cornerBl, 0);
        parent.AddChild(_cornerBr);
        parent.MoveChild(_cornerBr, 0);
        parent.AddChild(_edgeTop);
        parent.MoveChild(_edgeTop, 0);
        parent.AddChild(_edgeBottom);
        parent.MoveChild(_edgeBottom, 0);
        parent.AddChild(_edgeLeft);
        parent.MoveChild(_edgeLeft, 0);
        parent.AddChild(_edgeRight);
        parent.MoveChild(_edgeRight, 0);
    }
}