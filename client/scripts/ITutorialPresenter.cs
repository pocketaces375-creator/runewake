using System;
using Godot;

namespace Runewake.Client;

/// <summary>
/// Pure data for a single piece of tutorial content.
/// Deserialized from JSON — no UI references.
/// </summary>
public class TutorialContent
{
    /// <summary>Unique popup identifier, e.g. "p1_goal".</summary>
    public string PopupId { get; set; } = string.Empty;

    /// <summary>Bold title text (short, e.g. "ATTUNEMENT").</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>2-3 sentences of explanation.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Show Skip Tutorial link (first popup only).</summary>
    public bool ShowSkip { get; set; }
}

/// <summary>
/// Describes how tutorial content is presented on screen.
/// Implementations own the visual form — modal popup, contextual arrow,
/// inline hint, etc. The caller decides when and what to teach;
/// the presenter decides how it appears.
/// </summary>
public interface ITutorialPresenter
{
    /// <summary>
    /// Display the given content. Previous content is replaced.
    /// </summary>
    void Show(TutorialContent content);

    /// <summary>Fired when the player dismisses the current presentation.</summary>
    event Action? Dismissed;
}