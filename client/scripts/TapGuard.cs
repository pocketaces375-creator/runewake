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
    private ulong _lastReleaseMs;

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

    /// <summary>
    /// FABLE-004: the same collapse for the finger coming back up.
    ///
    /// A lift is delivered twice as well — InputEventScreenTouch(pressed:false) and the
    /// emulated InputEventMouseButton(pressed:false). A handler that only guards the press
    /// but acts on the release still fires twice per tap. That is what made hand cards
    /// impossible to select: the first release selected the card, the second one hit the
    /// tap-again-to-deselect branch and undid it, leaving a "Deselected." toast and a board
    /// that looked like it wanted to work.
    ///
    /// The press and release windows are tracked separately, so the guard does not care
    /// whether the driver interleaves the pair as press/press/release/release or
    /// press/release/press/release.
    /// </summary>
    public bool AcceptRelease(InputEvent @event)
    {
        bool release =
            (@event is InputEventMouseButton mouse && !mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            || (@event is InputEventScreenTouch touch && !touch.Pressed);

        if (!release)
            return false;

        ulong now = Time.GetTicksMsec();
        if (_lastReleaseMs != 0 && now - _lastReleaseMs < WindowMs)
            return false;

        _lastReleaseMs = now;
        return true;
    }
}
