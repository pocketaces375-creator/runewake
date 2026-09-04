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
    // Atmosphere (TASK-UI3d) — all values here so art can retune without code
    // ════════════════════════════════════════════

    // ── Warm ember glow (lower-left corner) ──
    /// <summary>Ember glow color — warm reddish-orange like forge embers</summary>
    public static readonly Color AtmosphereEmberGlow = Color.FromHtml("#D4502A");
    /// <summary>Ember glow center X as fraction of viewport width (0=left, 1=right)</summary>
    public const float AtmosphereEmberCenterX = 0.08f;
    /// <summary>Ember glow center Y as fraction of viewport height (0=top, 1=bottom)</summary>
    public const float AtmosphereEmberCenterY = 0.92f;
    /// <summary>Ember glow max radius as fraction of viewport diagonal</summary>
    public const float AtmosphereEmberRadius = 0.45f;
    /// <summary>Ember glow peak alpha (inner ring) — minimal, plate carries own light</summary>
    public const float AtmosphereEmberAlpha = 0.02f;

    // ── Cool moon glow (upper-right corner) ──
    /// <summary>Moon glow color — cool pale blue-white</summary>
    public static readonly Color AtmosphereMoonGlow = Color.FromHtml("#6A8BC4");
    /// <summary>Moon glow center X as fraction of viewport width</summary>
    public const float AtmosphereMoonCenterX = 0.92f;
    /// <summary>Moon glow center Y as fraction of viewport height</summary>
    public const float AtmosphereMoonCenterY = 0.08f;
    /// <summary>Moon glow max radius as fraction of viewport diagonal</summary>
    public const float AtmosphereMoonRadius = 0.40f;
    /// <summary>Moon glow peak alpha (inner ring) — minimal, plate carries own light</summary>
    public const float AtmosphereMoonAlpha = 0.02f;

    // ── Mist band ──
    /// <summary>Mist band color — pale grey-blue at low opacity</summary>
    public static readonly Color AtmosphereMistColor = Color.FromHtml("#8A9BB0");
    /// <summary>Mist band center Y as fraction of viewport height</summary>
    public const float AtmosphereMistCenterY = 0.45f;
    /// <summary>Mist band total height as fraction of viewport height</summary>
    public const float AtmosphereMistHeight = 0.08f;
    /// <summary>Mist band max opacity — minimal, plate carries own atmosphere</summary>
    public const float AtmosphereMistAlpha = 0.01f;

    // ── Vignette ──
    /// <summary>Vignette color — dark brown-black</summary>
    public static readonly Color AtmosphereVignetteColor = Color.FromHtml("#0A0907");
    /// <summary>Vignette peak alpha at edges — PAINTED-PLATE-1: plate carries its own light falloff, set to 0.</summary>
    public const float AtmosphereVignetteAlpha = 0.0f;
    /// <summary>Vignette softness as fraction of viewport (0=hard, 1=full soft)</summary>
    public const float AtmosphereVignetteSoftness = 0.35f;

    // ── Dust motes ──
    /// <summary>Number of static dust motes to render</summary>
    public const int AtmosphereDustMoteCount = 7;
    /// <summary>Minimum dust mote radius in pixels</summary>
    public const float AtmosphereDustMoteMinRadius = 1.0f;
    /// <summary>Maximum dust mote radius in pixels</summary>
    public const float AtmosphereDustMoteMaxRadius = 3.0f;
    /// <summary>Dust mote color — warm faint gold</summary>
    public static readonly Color AtmosphereDustMoteColor = Color.FromHtml("#C9A84C");
    /// <summary>Dust mote max opacity — subtle floating particles</summary>
    public const float AtmosphereDustMoteAlpha = 0.20f;

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
    // Unified Card Frame (TASK-UI4-ARSENAL)
    // Gold two-layer border system shared by all card types
    // ════════════════════════════════════════════

    /// <summary>Outer gold border — warm rich gold</summary>
    public static readonly Color FrameGoldOuter = Color.FromHtml("#C9A84C");
    /// <summary>Inner gold border highlight — brighter accent</summary>
    public static readonly Color FrameGoldInner = Color.FromHtml("#E8D48C");

    // ════════════════════════════════════════════
    // Artifact Card Frame (TASK-UI4-ARSENAL)
    // Teal-gold rim for artifact identity — distinct from creature gold
    // ════════════════════════════════════════════

    /// <summary>Artifact outer border — teal-gold blend</summary>
    public static readonly Color ArtifactFrameOuter = Color.FromHtml("#5A8A7A");
    /// <summary>Artifact inner border highlight — brighter teal-gold</summary>
    public static readonly Color ArtifactFrameInner = Color.FromHtml("#7AB8A8");
    /// <summary>Artifact fill inside the teal-gold border</summary>
    public static readonly Color ArtifactFrameFill = Color.FromHtml("#363E38");
    /// <summary>ARTIFACT tag text color</summary>
    public static readonly Color ArtifactTagColor = Color.FromHtml("#6AAAAA");
    /// <summary>Suppressed art overlay — ashen desaturated tint</summary>
    public static readonly Color ArtifactSuppressedOverlay = Color.FromHtml("#2A2A2A");
    /// <summary>Suppressed border — muted greyed teal</summary>
    public static readonly Color ArtifactSuppressedBorder = Color.FromHtml("#3A3A3A");
    /// <summary>Card face fill inside the gold border</summary>
    public static readonly Color FrameFill = Color.FromHtml("#2C2824");
    /// <summary>Name band background — semi-transparent dark</summary>
    public static readonly Color FrameNameBand = Color.FromHtml("#281E16");
    /// <summary>Name band text color</summary>
    public static readonly Color FrameNameText = Color.FromHtml("#F0E4D0");
    /// <summary>Stat rail background — dark stone</summary>
    public static readonly Color FrameStatRail = Color.FromHtml("#201E1A");
    /// <summary>Attack stat color — deep ember red</summary>
    public static readonly Color FrameStatAttack = Color.FromHtml("#A83A2A");
    /// <summary>Vigor stat color — faded moss</summary>
    public static readonly Color FrameStatVigor = Color.FromHtml("#5A8A4A");
    /// <summary>Stat text color (over stat chip)</summary>
    public static readonly Color FrameStatText = Color.FromHtml("#FFFFFF");
    /// <summary>Hex cost badge fill</summary>
    public static readonly Color FrameHexFill = Color.FromHtml("#2A2418");
    /// <summary>Hex cost badge border — gold</summary>
    public static readonly Color FrameHexBorder = Color.FromHtml("#C9A84C");
    /// <summary>Hex cost text</summary>
    public static readonly Color FrameHexText = Color.FromHtml("#E8DCC8");
    /// <summary>Name band height as fraction of card height</summary>
    public const float FrameNameBandFraction = 0.18f;
    /// <summary>Stat rail height as fraction of card height</summary>
    public const float FrameStatRailFraction = 0.12f;
    /// <summary>Card gold border width in px</summary>
    public const float FrameBorderWidth = 2.0f;
    /// <summary>Inner highlight line width in px</summary>
    public const float FrameInnerBorderWidth = 1.0f;
    /// <summary>Hex cost badge width as fraction of card width</summary>
    public const float FrameHexSizeFraction = 0.18f;
    /// <summary>Stat chip width as fraction of card width</summary>
    public const float FrameStatChipFraction = 0.25f;

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

    /// <summary>Hand card width (TASK-UI3e: exactly 104 at design scale)</summary>
    public const int CardWidth = 104;
    /// <summary>Hand card height (TASK-UI3e: exactly 152 at design scale)</summary>
    public const int CardHeight = 152;
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
    // Charge Pips (TASK-AC2)
    // ════════════════════════════════════════════

    /// <summary>Color for filled charge pips — warm bright gold</summary>
    public static readonly Color ChargeFilled = Color.FromHtml("#D4B84C");
    /// <summary>Color for empty charge pips — faint muted gold</summary>
    public static readonly Color ChargeEmpty = Color.FromHtml("#5A5048");
    /// <summary>Pulse color when charges reach max — bright arcane blue-white</summary>
    public static readonly Color ChargeFullPulse = Color.FromHtml("#8AC4FF");
    /// <summary>Pulse scale multiplier during animation</summary>
    public const float ChargePulseScale = 1.4f;
    /// <summary>Pulse duration in seconds (≤0.5s requirement)</summary>
    public const float ChargePulseDuration = 0.35f;

    // ════════════════════════════════════════════
    // Opacity
    // ════════════════════════════════════════════

    public const float OpacityDim = 0.7f;      // mulligan/dim overlay
    public const float OpacitySubtle = 0.15f;  // very faint
    public const float OpacityDisabled = 0.5f; // greyed out

    // ════════════════════════════════════════════
    // Board Skins (TASK-BD1)
    // ════════════════════════════════════════════

    private static readonly Dictionary<string, string> _boardSkins = new()
    {
        { "default", "res://content/art/board/default.png" },
        { "backdrop_default", "res://content/art/board/backdrop_default.png" },
        { "plate_default", "res://content/art/board/plate_default.png" },
        { "ember", "res://content/art/board/default.png" },
        { "backdrop_ember", "res://content/art/board/backdrop_default.png" },
        { "plate_ember", "res://content/art/board/plate_default.png" },
        { "tide", "res://content/art/board/default.png" },
        { "backdrop_tide", "res://content/art/board/backdrop_default.png" },
        { "plate_tide", "res://content/art/board/plate_default.png" },
        { "dawn", "res://content/art/board/default.png" },
        { "backdrop_dawn", "res://content/art/board/backdrop_default.png" },
        { "plate_dawn", "res://content/art/board/plate_default.png" }
    };

    /// <summary>Get the texture path for a board skin ID. Returns null for unknown IDs.</summary>
    public static string? GetBoardSkinPath(string skinId)
    {
        return _boardSkins.TryGetValue(skinId, out var path) ? path : null;
    }

    /// <summary>Get the backdrop texture path — the landscape environment behind the altar field.</summary>
    public static string? GetBackdropPath(string skinId = "default")
    {
        string backdropKey = $"backdrop_{skinId}";
        if (_boardSkins.TryGetValue(backdropKey, out var backdrop))
            return backdrop;
        return _boardSkins.GetValueOrDefault("backdrop_default");
    }

    /// <summary>Get the painted plate texture path — single-image battlefield with ring painted in.</summary>
    public static string? GetPlatePath(string skinId = "default")
    {
        string plateKey = $"plate_{skinId}";
        if (_boardSkins.TryGetValue(plateKey, out var plate))
            return plate;
        return _boardSkins.GetValueOrDefault("plate_default");
    }

    /// <summary>Get the tint color for a board/map skin. Default = white (no tint). Ember = warm orange tint. Tide = cool blue tint.</summary>
    public static Color GetSkinTint(string skinId)
    {
        return skinId.ToLowerInvariant() switch
        {
            "ember" or "backdrop_ember" or "plate_ember" => new Color(1.0f, 0.6f, 0.3f, 1.0f), // warm ember tint
            "tide" or "backdrop_tide" or "plate_tide" => new Color(0.23f, 0.62f, 0.77f, 1.0f), // cool tide tint
            "dawn" or "backdrop_dawn" or "plate_dawn" => new Color(0.78f, 0.65f, 0.25f, 1.0f), // warm dawn gold tint
            _ => Colors.White
        };
    }

    // ════════════════════════════════════════════
    // Battlefield Ring Geometry (PAINTED-PLATE-1)
    // Canonical constants — single source of truth.
    // The painted ring center and radius, expressed as
    // fractions of the board rect (BoardBg / Board Control).
    // Slot arcs in PopulateLanes derive from these.
    //
    // ZONE PLATES: Every future zone plate (painted battlefield image)
    // MUST paint its ring to THIS EXACT geometry so slot alignment holds.
    // Replace client/content/art/board/plate_default.png with the new zone
    // plate — the same ring constants slot the cards correctly.
    // ════════════════════════════════════════════

    /// <summary>Ring center X as fraction of board width</summary>
    public const float RingCenterX = 0.50f;
    /// <summary>Ring center Y as fraction of board height</summary>
    public const float RingCenterY = 0.50f;
    /// <summary>Ring radius as fraction of board width</summary>
    public const float RingRadiusW = 0.40f;
    /// <summary>Ring radius as fraction of board height</summary>
    public const float RingRadiusH = 0.36f;

    /// <summary>Number of lane slots per side</summary>
    public const int LaneCount = 5;

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