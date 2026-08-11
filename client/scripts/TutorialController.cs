using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Runewake.Client;

/// <summary>
/// JSON wrapper for the tutorial content file.
/// </summary>
public class TutorialContentPack
{
    [JsonPropertyName("popups")]
    public List<TutorialContent> Popups { get; set; } = new();
}

/// <summary>
/// Manages tutorial content for the current encounter.
/// Separates the teaching content (loaded from JSON) from its
/// visual presentation (handled by an ITutorialPresenter).
///
/// A new profile starts at r1_n01 which is flagged IsTutorial=true.
/// Popups are shown once and persisted so replays skip already-seen popups.
/// </summary>
public partial class TutorialController : Node
{
    /// <summary>Path to the tutorial popup content JSON, relative to res://.</summary>
    private const string ContentPath = "res://content/tutorial/tutorial_popups.json";

    /// <summary>True if the current encounter has tutorial popups available.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The presenter responsible for visual display.</summary>
    public ITutorialPresenter? Presenter { get; private set; }

    /// <summary>Set of popup IDs already shown this session.</summary>
    private readonly HashSet<string> _shownPopups = new();

    /// <summary>Lookup from popup ID to content.</summary>
    private Dictionary<string, TutorialContent> _contentMap = new();

    /// <summary>Callbacks for each popup: (onContinue, onSkip).</summary>
    private readonly Dictionary<string, (System.Action? onContinue, System.Action? onSkip)> _callbacks = new();

    // ── Initialization ──

    /// <summary>
    /// Initialize the controller for a tutorial encounter.
    /// Loads content from the tutorial JSON file and wires the presenter.
    /// </summary>
    public void Initialize(Control owner, ITutorialPresenter presenter)
    {
        Presenter = presenter;
        IsActive = true;

        // Load content from JSON (Godot FileAccess works in exports)
        LoadContent();

        // Wire presenter dismissal
        presenter.Dismissed += OnPresenterDismissed;
    }

    private void LoadContent()
    {
        try
        {
            string json = Godot.FileAccess.GetFileAsString(ContentPath);
            if (string.IsNullOrEmpty(json))
            {
                GD.PrintErr($"[TutorialController] No content at {ContentPath}");
                return;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var pack = JsonSerializer.Deserialize<TutorialContentPack>(json, options);
            if (pack == null || pack.Popups.Count == 0)
            {
                GD.PrintErr($"[TutorialController] Content file empty or invalid: {ContentPath}");
                return;
            }

            _contentMap = pack.Popups.ToDictionary(p => p.PopupId);
            GD.Print($"[TutorialController] Loaded {_contentMap.Count} popup content entries.");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[TutorialController] Failed to load content: {ex.Message}");
        }
    }

    // ── Showing popups ──

    /// <summary>
    /// Show a tutorial popup. Looks up content by ID from the loaded JSON.
    /// If already shown this session, fires onContinue immediately (skip).
    /// </summary>
    /// <param name="popupId">Unique ID matching a popup in tutorial_popups.json.</param>
    /// <param name="onContinue">Called when the player taps Continue.</param>
    /// <param name="onSkip">Called when the player taps Skip.</param>
    public void ShowPopup(
        string popupId,
        System.Action? onContinue = null,
        System.Action? onSkip = null)
    {
        if (!IsActive || Presenter == null) return;

        // If already shown this session, skip immediately
        if (_shownPopups.Contains(popupId))
        {
            onContinue?.Invoke();
            return;
        }

        // Look up content
        if (!_contentMap.TryGetValue(popupId, out var content))
        {
            GD.PrintErr($"[TutorialController] Unknown popup ID: {popupId}");
            onContinue?.Invoke(); // Skip unknown popups gracefully
            return;
        }

        // Mark shown
        _shownPopups.Add(popupId);

        // Store callbacks for this popup (retrieved on Dismissed)
        _callbacks[popupId] = (onContinue, onSkip);

        // Show via presenter
        Presenter.Show(content);
    }

    /// <summary>
    /// Called when the presenter fires Dismissed.
    /// Routes to the correct callback based on whether the popup was skipped.
    /// </summary>
    private void OnPresenterDismissed()
    {
        // Determine which popup was just dismissed
        // Since Show only allows one at a time, we look at the last shown
        if (_shownPopups.Count == 0) return;

        string lastPopupId = _shownPopups.Last();

        if (_callbacks.TryGetValue(lastPopupId, out var callbacks))
        {
            bool wasSkipped = Presenter is TutorialPopup popup && popup.WasSkipped;

            if (wasSkipped)
                callbacks.onSkip?.Invoke();
            else
                callbacks.onContinue?.Invoke();

            _callbacks.Remove(lastPopupId);
        }
    }

    // ── State ──

    /// <summary>
    /// True if a popup is currently visible (board should be paused).
    /// </summary>
    public bool IsPopupOpen => Presenter != null && IsInstanceValid(Presenter as Godot.Node);

    /// <summary>
    /// End the tutorial session. Unsubscribes from the presenter.
    /// </summary>
    public void EndTutorial()
    {
        IsActive = false;
        if (Presenter != null)
        {
            Presenter.Dismissed -= OnPresenterDismissed;
        }
        _callbacks.Clear();
    }
}