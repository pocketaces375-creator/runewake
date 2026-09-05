using Godot;

namespace Runewake.Client;

/// <summary>
/// One finger press must be one press.
///
/// On a touchscreen Godot delivers a tap twice: as an InputEventScreenTouch, and again as an
/// InputEventMouseButton emulated from that touch (input_devices/pointing/emulate_mouse_from_touch,
/// on by default). Any handler that accepts both event types therefore fires twice per tap on a
/// phone and once per click on a desktop. For a toggle-style handler — select a card, select it
/// again to deselect — the second fire silently undoes the first, and the game feels dead to touch
/// while working perfectly with a mouse.
///
/// TapGuard collapses that pair. Give each control its own instance and ask it whether an event is
/// a real press. Two presses closer together than the window are treated as one.
/// </summary>
public sealed class TapGuard
{
    /// <summary>The touch and its emulated mouse event arrive in the same frame; a human cannot tap twice this fast.</summary>
    private const ulong WindowMs = 250;

    private ulong _lastPressMs;

    /// <summary>True when this event is a press that should be acted on.</summary>
    public bool Accept(InputEvent @event)
    {
        bool press =
            (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            || (@event is InputEventScreenTouch touch && touch.Pressed);

        if (!press)
            return false;

        ulong now = Time.GetTicksMsec();
        if (_lastPressMs != 0 && now - _lastPressMs < WindowMs)
            return false;

        _lastPressMs = now;
        return true;
    }
}
