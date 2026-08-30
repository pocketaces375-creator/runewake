namespace Runewake.Engine.State;

/// <summary>
/// Player settings and accessibility preferences.
/// Persisted to SQLite via SaveRepository.
/// </summary>
public class SettingsState
{
    /// <summary>Master volume (0.0–1.0).</summary>
    public float MasterVolume { get; set; } = 1.0f;

    /// <summary>Music volume (0.0–1.0).</summary>
    public float MusicVolume { get; set; } = 0.8f;

    /// <summary>SFX volume (0.0–1.0).</summary>
    public float SfxVolume { get; set; } = 1.0f;

    /// <summary>Ambient volume (0.0–1.0).</summary>
    public float AmbientVolume { get; set; } = 0.5f;

    /// <summary>Master mute override — silences all audio when true.</summary>
    public bool MasterMute { get; set; } = false;

    /// <summary>Reduce motion: skip scale/fade animations.</summary>
    public bool ReduceMotion { get; set; } = false;

    /// <summary>Large text: base font scale ×1.3.</summary>
    public bool LargeText { get; set; } = false;

    /// <summary>High contrast: increased border thickness.</summary>
    public bool HighContrast { get; set; } = false;

    /// <summary>Language code (reserved for i18n).</summary>
    public string Language { get; set; } = "en";

    /// <summary>Whether the player has seen the story intro splash.</summary>
    public bool IntroSeen { get; set; } = false;

    /// <summary>
    /// Creates a shallow clone. Mutable fields (string) are reference types,
    /// but Language is set-once in practice so shallow copy is sufficient.
    /// </summary>
    public SettingsState Clone() => (SettingsState)MemberwiseClone();
}