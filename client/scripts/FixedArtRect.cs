using Godot;

namespace Runewake.Client;

/// <summary>
/// A TextureRect replacement that does NOT let its texture drive
/// the layout minimum size. Reports a fixed minimum of (100, 70)
/// regardless of texture dimensions.
/// Use StretchMode to control how the texture scales within the rect.
/// </summary>
public partial class FixedArtRect : Control
{
    private Texture2D? _texture;
    private Vector2 _fixedMinSize = new(100, 70);

    /// <summary>
    /// Shown centered when no texture is set — never a black void (FIX 4).
    /// </summary>
    public string PlaceholderText { get; set; } = "";

    /// <summary>
    /// The texture to display. Does NOT affect layout minimum size.
    /// </summary>
    public Texture2D? Texture
    {
        get => _texture;
        set
        {
            _texture = value;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Fixed minimum size reported to the layout system.
    /// Default (100, 70). Texture dimensions never influence this.
    /// </summary>
    public Vector2 FixedMinSize
    {
        get => _fixedMinSize;
        set { _fixedMinSize = value; EmitSignal(Control.SignalName.MinimumSizeChanged); }
    }

    public override Vector2 _GetMinimumSize() => _fixedMinSize;

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_texture == null)
        {
            // Never a black void — draw parchment/stone placeholder with name (FIX 4)
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect, new Color(0.20f, 0.18f, 0.14f)); // neutral stone base
            DrawRect(rect.Grow(-2), new Color(0.16f, 0.14f, 0.11f)); // inner inset

            if (!string.IsNullOrEmpty(PlaceholderText))
            {
                var font = ThemeDB.FallbackFont;
                int fontSize = Mathf.Max(12, Mathf.RoundToInt(Size.Y * 0.12f));
                Vector2 textSize = font.GetStringSize(PlaceholderText, HorizontalAlignment.Center, -1, fontSize);
                Vector2 pos = new Vector2(
                    (Size.X - textSize.X) / 2f,
                    (Size.Y - textSize.Y) / 2f + textSize.Y
                );
                DrawString(font, pos, PlaceholderText, HorizontalAlignment.Center, Size.X, fontSize,
                    new Color(0.85f, 0.80f, 0.70f, 0.7f));
            }

            return;
        }

        var texRect = new Rect2(Vector2.Zero, Size);
        // Keep aspect cover within rect (fill the card face, crop edges)
        float texW = _texture.GetWidth();
        float texH = _texture.GetHeight();
        float scaleX = texRect.Size.X / texW;
        float scaleY = texRect.Size.Y / texH;
        float scale = Mathf.Max(scaleX, scaleY);
        float drawW = texW * scale;
        float drawH = texH * scale;
        float offsetX = (texRect.Size.X - drawW) / 2f;
        float offsetY = (texRect.Size.Y - drawH) / 2f;
        var src = new Rect2(Vector2.Zero, _texture.GetSize());
        var dst = new Rect2(offsetX, offsetY, drawW, drawH);
        DrawTextureRectRegion(_texture, dst, src);
    }
}