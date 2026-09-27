using Godot;

namespace Runewake.Client;

/// <summary>
/// FABLE-039: grab the page itself to scroll it.
///
/// Trikzos: "it currently functions where you grab a scroll wheel to move up and down. I would
/// rather… just click and grab the page itself to scroll, much easier and less pinpoint accuracy."
///
/// Godot's ScrollContainer only drags on events that reach IT, and a grid of card buttons eats
/// every press first — so on the Deck Forge the only thing that scrolled was the thin bar. This
/// watches the raw input (_Input runs before the GUI), and once a press that started over the
/// scroll area has moved more than a small threshold it takes over: the page follows the
/// finger/mouse 1:1 and, on release, glides on with the throw's speed and eases to a stop.
///
/// It never consumes input, so taps still reach the cards. A tap that turned into a drag must
/// not ALSO add a card on release — callers check <see cref="Dragged"/> in their Pressed handler.
/// Native touch-drag on the container is switched off (huge deadzone) so the two never fight.
/// </summary>
public partial class DragScroll : Node
{
    private ScrollContainer _s = null!;
    private const float Threshold = 14f;
    private bool _armed, _dragging;
    private Vector2 _pressPos;
    private float _startScroll;
    private float _vel;          // px/s, positive = content moves up (scroll value grows)
    private float _lastY;
    private ulong _lastUsec;
    private ulong _lastFrame;
    private float _pos;          // float scroll position while gliding (ScrollVertical is an int)

    /// <summary>True from the moment a gesture became a drag until the next press begins.</summary>
    public bool Dragged { get; private set; }

    public static DragScroll Attach(ScrollContainer scroll)
    {
        var d = new DragScroll { _s = scroll, Name = "DragScroll" };
        scroll.ScrollDeadzone = 100000;   // native touch-drag off — this node owns dragging
        scroll.AddChild(d);
        return d;
    }

    /// <summary>Stop any glide (e.g. the page is being rebuilt or wheel-scrolled).</summary>
    public void Halt() { _vel = 0; _dragging = false; _armed = false; }

    public override void _Input(InputEvent e)
    {
        if (!IsInstanceValid(_s) || !_s.IsVisibleInTree()) return;

        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) { _vel = 0; return; }
            if (mb.ButtonIndex != MouseButton.Left) return;
            if (mb.Pressed)
            {
                Dragged = false;
                _dragging = false;
                _armed = _s.GetGlobalRect().HasPoint(mb.Position);
                if (!_armed) return;
                _vel = 0;
                _pressPos = mb.Position;
                _startScroll = _s.ScrollVertical;
                _lastY = mb.Position.Y;
                _lastUsec = Time.GetTicksUsec();
            }
            else
            {
                if (_dragging)
                {
                    // a finger that stopped before lifting shouldn't fling
                    // (a slow frame shouldn't count as stopping, hence the frame check too)
                    if (Time.GetTicksUsec() - _lastUsec > 90_000 && Godot.Engine.GetProcessFrames() - _lastFrame > 2) _vel = 0;
                    _pos = _s.ScrollVertical;
                }
                _armed = false;
                _dragging = false;
            }
            return;
        }

        if (e is InputEventMouseMotion mm && _armed && (mm.ButtonMask & MouseButtonMask.Left) != 0)
        {
            float dy = mm.Position.Y - _pressPos.Y;
            if (!_dragging)
            {
                if (Mathf.Abs(dy) < Threshold) return;
                // only when what's under the pointer is part of this page (not a popup over it)
                var over = GetViewport().GuiGetHoveredControl();
                if (over != null && over != _s && !_s.IsAncestorOf(over)) { _armed = false; return; }
                _dragging = true;
                Dragged = true;
                _pressPos = mm.Position;             // no jump when the drag starts
                _startScroll = _s.ScrollVertical;
                dy = 0;
            }
            _s.ScrollVertical = (int)Mathf.Clamp(_startScroll - dy, 0, MaxScroll());

            ulong now = Time.GetTicksUsec();
            float dt = Mathf.Max(0.001f, (now - _lastUsec) / 1_000_000f);
            float inst = -(mm.Position.Y - _lastY) / dt;
            _vel = Mathf.Clamp(Mathf.Lerp(_vel, inst, 0.35f), -7000f, 7000f);
            _lastY = mm.Position.Y;
            _lastUsec = now;
            _lastFrame = Godot.Engine.GetProcessFrames();
        }
    }

    public override void _Process(double delta)
    {
        if (_dragging || Mathf.Abs(_vel) < 12f || !IsInstanceValid(_s)) { if (!_dragging) _vel = 0; return; }
        float dt = (float)delta;
        _pos = Mathf.Clamp(_pos + _vel * dt, 0, MaxScroll());
        _s.ScrollVertical = (int)_pos;
        _vel *= Mathf.Exp(-3.6f * dt);
        if (_pos <= 0 || _pos >= MaxScroll()) _vel = 0;
    }

    private float MaxScroll()
    {
        var bar = _s.GetVScrollBar();
        return Mathf.Max(0f, (float)(bar.MaxValue - bar.Page));
    }
}
