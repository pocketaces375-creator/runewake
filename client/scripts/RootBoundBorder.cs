using Godot;

namespace Runewake.Client;

/// <summary>
/// Root-Bound (option 5) 9-slice card border overlay.
/// Uses a single NinePatchRect with rootbound_full.png and the
/// patch margins from rootbound_9slice.json to render the
/// stone frame at correct proportions.
/// 
/// Controls the card-size fill via LayoutPreset.FullRect anchors.
/// Band thickness: band_px = round(card_width * 0.07).
/// Corners preserve their aspect ratio; edges stretch.
/// </summary>
public partial class RootBoundBorder : Control
{
    private NinePatchRect? _npr;

    // 9-slice window margins (from rootbound_9slice.json):
    // window_px = [left=148, top=172, right_edge=675, bottom_edge=1019]
    // on source 832x1216.
    private const int PatchLeft = 148;
    private const int PatchTop = 172;
    private const int PatchRight = 832 - 675;    // = 157
    private const int PatchBottom = 1216 - 1019;  // = 197

    private float _cardWidth;
    private float _cardHeight;

    private static Texture2D? _cachedTexture;

    private static Texture2D? GetBorderTexture()
    {
        if (_cachedTexture == null)
        {
            if (ResourceLoader.Exists("res://content/art/border/rootbound_full.png"))
            {
                _cachedTexture = ResourceLoader.Load<Texture2D>("res://content/art/border/rootbound_full.png");
                if (_cachedTexture != null)
                    GD.Print("[ROOTBOUND] rootbound_full.png loaded");
                else
                    GD.PrintErr("[ROOTBOUND] rootbound_full.png Load returned null");
            }
            else
            {
                GD.PrintErr("[ROOTBOUND] rootbound_full.png does not exist at res://content/art/border/rootbound_full.png");
            }
        }
        return _cachedTexture;
    }

    /// <summary>Set up the NinePatchRect border. Safe to call multiple times — reuses node.</summary>
    public void Setup(float cardWidth, float cardHeight)
    {
        _cardWidth = cardWidth;
        _cardHeight = cardHeight;

        if (_npr == null)
        {
            var tex = GetBorderTexture();
            if (tex == null) return;

            _npr = new NinePatchRect
            {
                Texture = tex,
                MouseFilter = MouseFilterEnum.Ignore,
                PatchMarginLeft = PatchLeft,
                PatchMarginTop = PatchTop,
                PatchMarginRight = PatchRight,
                PatchMarginBottom = PatchBottom,
                DrawCenter = false,
                AxisStretchHorizontal = NinePatchRect.AxisStretchMode.Stretch,
                AxisStretchVertical = NinePatchRect.AxisStretchMode.Stretch
            };
            _npr.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(_npr);
        }

        // Size RootBoundBorder to the card
        Position = new Vector2(0, 0);
        Size = new Vector2(cardWidth, cardHeight);
        CustomMinimumSize = new Vector2(cardWidth, cardHeight);
    }

    /// <summary>Band pixel width for CardPlate positioning.</summary>
    public static int GetBandPx(float cardWidth) => Mathf.Max(1, Mathf.RoundToInt(cardWidth * 0.07f));
}