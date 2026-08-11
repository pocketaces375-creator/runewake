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

    public override void _Draw()
    {
        if (_texture == null) return;
        var rect = new Rect2(Vector2.Zero, Size);
        // Keep aspect centered within rect
        float texW = _texture.GetWidth();
        float texH = _texture.GetHeight();
        float scaleX = rect.Size.X / texW;
        float scaleY = rect.Size.Y / texH;
        float scale = Mathf.Min(scaleX, scaleY);
        float drawW = texW * scale;
        float drawH = texH * scale;
        float offsetX = (rect.Size.X - drawW) / 2f;
        float offsetY = (rect.Size.Y - drawH) / 2f;
        var src = new Rect2(Vector2.Zero, _texture.GetSize());
        var dst = new Rect2(offsetX, offsetY, drawW, drawH);
        DrawTextureRectRegion(_texture, dst, src);
    }
}