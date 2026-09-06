using Godot;

namespace Runewake.Client;

/// <summary>
/// Root-Bound (option 5) 9-slice card border overlay.
/// Uses the 8 individual slice PNGs (4 corners + 4 edges) with
/// band_px computed as round(card_width * 0.07) so the border
/// scales proportionally at ALL card sizes.
/// 
/// Corners are drawn at band_px × band_px square and are never stretched.
/// Edges stretch along their length only: top/bottom stretch horizontally,
/// left/right stretch vertically.
/// 
/// This is NOT a NinePatchRect — the NinePatchRect approach with
/// rootbound_full.png produces wrong proportions because the patch
/// margins are in source-image-relative pixels (17.8% of source width)
/// rather than the 7% target-relative spec.
/// </summary>
public partial class RootBoundBorder : Control
{
    public RootBoundBorder()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    private TextureRect? _cornerTL;
    private TextureRect? _cornerTR;
    private TextureRect? _cornerBL;
    private TextureRect? _cornerBR;
    private TextureRect? _edgeTop;
    private TextureRect? _edgeBottom;
    private TextureRect? _edgeLeft;
    private TextureRect? _edgeRight;

    private static Texture2D? _cornerTLTex;
    private static Texture2D? _cornerTRTex;
    private static Texture2D? _cornerBLTex;
    private static Texture2D? _cornerBRTex;
    private static Texture2D? _edgeTopTex;
    private static Texture2D? _edgeBottomTex;
    private static Texture2D? _edgeLeftTex;
    private static Texture2D? _edgeRightTex;

    private static bool _texturesLoaded;

    private static void LoadTextures()
    {
        if (_texturesLoaded) return;
        _cornerTLTex = TryLoad("res://content/art/border/rootbound_corner_tl.png");
        _cornerTRTex = TryLoad("res://content/art/border/rootbound_corner_tr.png");
        _cornerBLTex = TryLoad("res://content/art/border/rootbound_corner_bl.png");
        _cornerBRTex = TryLoad("res://content/art/border/rootbound_corner_br.png");
        _edgeTopTex = TryLoad("res://content/art/border/rootbound_edge_top.png");
        _edgeBottomTex = TryLoad("res://content/art/border/rootbound_edge_bottom.png");
        _edgeLeftTex = TryLoad("res://content/art/border/rootbound_edge_left.png");
        _edgeRightTex = TryLoad("res://content/art/border/rootbound_edge_right.png");
        _texturesLoaded = true;
    }

    private static Texture2D? TryLoad(string resPath)
    {
        if (ResourceLoader.Exists(resPath))
        {
            var tex = ResourceLoader.Load<Texture2D>(resPath);
            if (tex != null)
                GD.Print($"[ROOTBOUND] Loaded {resPath}");
            else
                GD.PrintErr($"[ROOTBOUND] {resPath} Load returned null");
            return tex;
        }
        GD.PrintErr($"[ROOTBOUND] {resPath} does not exist");
        return null;
    }

    /// <summary>Set up the 8-slice border. Safe to call multiple times — reuses nodes.</summary>
    public void Setup(float cardWidth, float cardHeight)
    {
        LoadTextures();

        int bandPx = Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));
        float innerW = cardWidth - 2 * bandPx;
        float innerH = cardHeight - 2 * bandPx;

        // Lazy-create all 8 TextureRect children
        TextureRect MakeChild(Texture2D? tex, string name, float px, float py, float pw, float ph, TextureRect.StretchModeEnum stretch, TextureRect.ExpandModeEnum expand)
        {
            var tr = new TextureRect
            {
                Texture = tex,
                Name = name,
                MouseFilter = MouseFilterEnum.Ignore,
                StretchMode = stretch,
                ExpandMode = expand,
                Position = new Vector2(px, py),
                Size = new Vector2(pw, ph),
                CustomMinimumSize = new Vector2(pw, ph)
            };
            AddChild(tr);
            return tr;
        }

        if (_cornerTL == null)
        {
            _cornerTL = MakeChild(_cornerTLTex, "CornerTL", 0, 0, bandPx, bandPx,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);
            _cornerTR = MakeChild(_cornerTRTex, "CornerTR", cardWidth - bandPx, 0, bandPx, bandPx,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);
            _cornerBL = MakeChild(_cornerBLTex, "CornerBL", 0, cardHeight - bandPx, bandPx, bandPx,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);
            _cornerBR = MakeChild(_cornerBRTex, "CornerBR", cardWidth - bandPx, cardHeight - bandPx, bandPx, bandPx,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);

            _edgeTop = MakeChild(_edgeTopTex, "EdgeTop", bandPx, 0, innerW, bandPx,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);
            _edgeBottom = MakeChild(_edgeBottomTex, "EdgeBottom", bandPx, cardHeight - bandPx, innerW, bandPx,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);
            _edgeLeft = MakeChild(_edgeLeftTex, "EdgeLeft", 0, bandPx, bandPx, innerH,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);
            _edgeRight = MakeChild(_edgeRightTex, "EdgeRight", cardWidth - bandPx, bandPx, bandPx, innerH,
                TextureRect.StretchModeEnum.Scale, TextureRect.ExpandModeEnum.IgnoreSize);
        }
        else
        {
            // Update positions and sizes
            _cornerTL.Position = Vector2.Zero;
            _cornerTL.Size = new Vector2(bandPx, bandPx);
            _cornerTL.CustomMinimumSize = new Vector2(bandPx, bandPx);

            _cornerTR.Position = new Vector2(cardWidth - bandPx, 0);
            _cornerTR.Size = new Vector2(bandPx, bandPx);
            _cornerTR.CustomMinimumSize = new Vector2(bandPx, bandPx);

            _cornerBL.Position = new Vector2(0, cardHeight - bandPx);
            _cornerBL.Size = new Vector2(bandPx, bandPx);
            _cornerBL.CustomMinimumSize = new Vector2(bandPx, bandPx);

            _cornerBR.Position = new Vector2(cardWidth - bandPx, cardHeight - bandPx);
            _cornerBR.Size = new Vector2(bandPx, bandPx);
            _cornerBR.CustomMinimumSize = new Vector2(bandPx, bandPx);

            _edgeTop.Position = new Vector2(bandPx, 0);
            _edgeTop.Size = new Vector2(innerW, bandPx);
            _edgeTop.CustomMinimumSize = new Vector2(innerW, bandPx);

            _edgeBottom.Position = new Vector2(bandPx, cardHeight - bandPx);
            _edgeBottom.Size = new Vector2(innerW, bandPx);
            _edgeBottom.CustomMinimumSize = new Vector2(innerW, bandPx);

            _edgeLeft.Position = new Vector2(0, bandPx);
            _edgeLeft.Size = new Vector2(bandPx, innerH);
            _edgeLeft.CustomMinimumSize = new Vector2(bandPx, innerH);

            _edgeRight.Position = new Vector2(cardWidth - bandPx, bandPx);
            _edgeRight.Size = new Vector2(bandPx, innerH);
            _edgeRight.CustomMinimumSize = new Vector2(bandPx, innerH);
        }

        // Size RootBoundBorder to the card
        Position = Vector2.Zero;
        Size = new Vector2(cardWidth, cardHeight);
        CustomMinimumSize = new Vector2(cardWidth, cardHeight);
    }

    /// <summary>Band pixel width for CardPlate positioning.</summary>
    public static int GetBandPx(float cardWidth) => Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));
}