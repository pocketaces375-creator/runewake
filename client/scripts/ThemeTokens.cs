using Godot;
using Runewake.Engine.Cards;

/// <summary>
/// Design tokens for the Runewake archaeological high-fantasy theme.
/// Single source of truth for colors, type scale, spacing, and border treatments.
/// Apply these in scene code instead of hardcoded Color() calls.
/// Target atmosphere: Middle-earth adjacent — aged parchment, worn stone, tarnished metal.
/// NOT bright, NOT cartoonish, NOT clean sci-fi.
/// </summary>
public static class ThemeTokens
{
    // ════════════════════════════════════════════
    // Backgrounds & Surfaces
    // ════════════════════════════════════════════

    /// <summary>Deep pitch-black-brown — void / deep earth</summary>
    public static readonly Color BgVoid = Color.FromHtml("#0E0D0B");
    /// <summary>Main background — dark warm brown-black</summary>
    public static readonly Color BgDark = Color.FromHtml("#1A1816");
    /// <summary>Surface panels — worn stone</summary>
    public static readonly Color SurfaceStone = Color.FromHtml("#252220");
    /// <summary>Card face — weathered paper</summary>
    public static readonly Color CardFace = Color.FromHtml("#2C2824");
    /// <summary>Elevated surface — tarnished metal</summary>
    public static readonly Color SurfaceMetal = Color.FromHtml("#322E28");

    // ════════════════════════════════════════════
    // Text
    // ════════════════════════════════════════════

    /// <summary>Primary reading text — aged parchment</summary>
    public static readonly Color TextPrimary = Color.FromHtml("#E8DCC8");
    /// <summary>Secondary / supporting text — warm tan</summary>
    public static readonly Color TextSecondary = Color.FromHtml("#B8A88A");
    /// <summary>Muted / meta text — dusty stone</summary>
    public static readonly Color TextMuted = Color.FromHtml("#8A7D6B");
    /// <summary>Disabled / greyed out — deep shadow</summary>
    public static readonly Color TextInactive = Color.FromHtml("#5A5048");

    // ════════════════════════════════════════════
    // Accents & Status
    // ════════════════════════════════════════════

    /// <summary>Primary accent — tarnished gold (highlights, interactions)</summary>
    public static readonly Color Gold = Color.FromHtml("#C9A84C");
    /// <summary>Attunement / mana — warm amber</summary>
    public static readonly Color Amber = Color.FromHtml("#D4893A");
    /// <summary>Damage / danger — deep ember red</summary>
    public static readonly Color Ember = Color.FromHtml("#A83A2A");
    /// <summary>Healing / growth — faded moss</summary>
    public static readonly Color Moss = Color.FromHtml("#5A8A4A");
    /// <summary>Toast feedback — warm gold</summary>
    public static readonly Color ToastGold = Color.FromHtml("#C9A84C");

    // ════════════════════════════════════════════
    // Strata colors (archaeological palette)
    // ════════════════════════════════════════════

    /// <summary>Verdant — faded moss green</summary>
    public static readonly Color StrataVerdant = Color.FromHtml("#5A8A4A");
    /// <summary>Ember — rust</summary>
    public static readonly Color StrataEmber = Color.FromHtml("#A85A2A");
    /// <summary>Tide — weathered slate blue</summary>
    public static readonly Color StrataTide = Color.FromHtml("#3A6A8A");
    /// <summary>Hollow — faded amethyst</summary>
    public static readonly Color StrataHollow = Color.FromHtml("#5A3A5A");
    /// <summary>Dawn — tarnished brass</summary>
    public static readonly Color StrataDawn = Color.FromHtml("#8A7A3A");

    // ════════════════════════════════════════════
    // Borders
    // ════════════════════════════════════════════

    /// <summary>Standard border — worn metal grey</summary>
    public static readonly Color BorderStandard = Color.FromHtml("#3A3530");
    /// <summary>Subtle border — deeper shadow</summary>
    public static readonly Color BorderSubtle = Color.FromHtml("#2A2622");
    /// <summary>Highlight border — gold glow</summary>
    public static readonly Color BorderHighlight = Color.FromHtml("#C9A84C");

    // ════════════════════════════════════════════
    // Type Scale
    // ════════════════════════════════════════════

    public const int FontTiny = 9;
    public const int FontSmall = 13;
    public const int FontBody = 15;
    public const int FontLargeBody = 18;
    public const int FontSubtitle = 24;
    public const int FontTitle = 30;
    public const int FontLarge = 38;

    // ════════════════════════════════════════════
    // Spacing (4px grid)
    // ════════════════════════════════════════════

    public const int Space0 = 0;
    public const int Space1 = 4;
    public const int Space2 = 8;
    public const int Space3 = 12;
    public const int Space4 = 16;
    public const int Space5 = 24;
    public const int Space6 = 32;

    // ════════════════════════════════════════════
    // Card & Layout dimensions
    // ════════════════════════════════════════════

    /// <summary>Hand card width (small, dense — phone-optimized)</summary>
    public const int CardWidth = 75;
    /// <summary>Hand card height</summary>
    public const int CardHeight = 130;
    /// <summary>Art region height within the card</summary>
    public const int CardArtHeight = 55;
    /// <summary>How many pixels each successive card overlaps the previous</summary>
    public const int HandOverlap = 38;
    /// <summary>How far the selected card lifts up</summary>
    public const int CardLiftY = -24;
    /// <summary>Card bottom margin (space for name text)</summary>
    public const int CardNameHeight = 28;

    // ════════════════════════════════════════════
    // Font path (Cinzel — fantasy serif)
    // ════════════════════════════════════════════

    /// <summary>Cinzel is OFL-licensed, fits high-fantasy archaeological theme.</summary>
    public const string FontCinzel = "res://assets/fonts/Cinzel.ttf";

    /// <summary>Inter is SIL-licensed, clean readable sans-serif for body text.</summary>
    public const string FontInter = "res://assets/fonts/Inter-Variable.ttf";

    // ════════════════════════════════════════════
    // Font loading helpers (cache by size)
    // ════════════════════════════════════════════

    private static readonly Dictionary<int, Font> _headerFontCache = new();
    private static readonly Dictionary<int, Font> _bodyFontCache = new();

    /// <summary>Get a header font (Cinzel serif) at the given pixel size.</summary>
    public static Font GetHeaderFont(int size)
    {
        if (_headerFontCache.TryGetValue(size, out var cached))
            return cached;

        var fontFile = ResourceLoader.Load<FontFile>(FontCinzel);
        if (fontFile == null) return null!;

        var variation = new FontVariation();
        variation.BaseFont = fontFile;
        _headerFontCache[size] = variation;
        return variation;
    }

    /// <summary>Get a body font (Inter sans-serif) at the given pixel size.</summary>
    public static Font GetBodyFont(int size)
    {
        if (_bodyFontCache.TryGetValue(size, out var cached))
            return cached;

        var fontFile = ResourceLoader.Load<FontFile>(FontInter);
        if (fontFile == null) return null!;

        var variation = new FontVariation();
        variation.BaseFont = fontFile;
        _bodyFontCache[size] = variation;
        return variation;
    }

    /// <summary>Apply header font to a Control node (label) at the given size.</summary>
    public static void ApplyHeaderFont(Control label, int size)
    {
        var font = GetHeaderFont(size);
        if (font != null)
        {
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", size);
        }
    }

    /// <summary>Apply body font to a Control node (label) at the given size.</summary>
    public static void ApplyBodyFont(Control label, int size)
    {
        var font = GetBodyFont(size);
        if (font != null)
        {
            label.AddThemeFontOverride("font", font);
            label.AddThemeFontSizeOverride("font_size", size);
        }
    }

    public const int RadiusSmall = 3;
    public const int RadiusMedium = 5;
    public const int RadiusLarge = 8;
    public const int BorderThin = 1;
    public const int BorderNormal = 2;
    public const int BorderThick = 3;

    // ════════════════════════════════════════════
    // Opacity
    // ════════════════════════════════════════════

    public const float OpacityDim = 0.7f;      // mulligan/dim overlay
    public const float OpacitySubtle = 0.15f;  // very faint
    public const float OpacityDisabled = 0.5f; // greyed out

    // ════════════════════════════════════════════
    // Convenience — strata color lookup
    // ════════════════════════════════════════════

    public static Color StrataColor(Strata strata) => strata switch
    {
        Strata.VERDANT => StrataVerdant,
        Strata.EMBER => StrataEmber,
        Strata.TIDE => StrataTide,
        Strata.HOLLOW => StrataHollow,
        Strata.DAWN => StrataDawn,
        _ => TextMuted
    };

    // ════════════════════════════════════════════
    // Helpers — build StyleBoxFlat quickly
    // ════════════════════════════════════════════

    /// <summary>Worn metal border with dark fill.</summary>
    public static StyleBoxFlat StyleWornBorder(
        Color? borderColor = null,
        int width = 2,
        int radius = 4,
        Color? bgColor = null)
    {
        return new StyleBoxFlat
        {
            BgColor = bgColor ?? SurfaceStone,
            BorderColor = borderColor ?? BorderStandard,
            BorderWidthLeft = width,
            BorderWidthTop = width,
            BorderWidthRight = width,
            BorderWidthBottom = width,
            CornerRadiusTopLeft = radius,
            CornerRadiusTopRight = radius,
            CornerRadiusBottomLeft = radius,
            CornerRadiusBottomRight = radius
        };
    }

    /// <summary>For card frames — thicker border with strata accent on one side.</summary>
    public static StyleBoxFlat StyleCardFrame(Color strataColor)
    {
        var style = StyleWornBorder(BorderStandard, BorderThick, RadiusMedium, CardFace);
        style.BorderColor = strataColor;
        return style;
    }
}