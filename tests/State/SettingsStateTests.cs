using System;
using System.IO;
using System.Linq;
using Runewake.Engine.State;
using Runewake.Persistence;
using Xunit;

namespace Runewake.Tests.State;

public class SettingsStateTests
{
    [Fact]
    public void SettingsState_Defaults_AreCorrect()
    {
        var s = new SettingsState();
        Assert.Equal(1.0f, s.MasterVolume);
        Assert.Equal(0.8f, s.MusicVolume);
        Assert.Equal(1.0f, s.SfxVolume);
        Assert.False(s.ReduceMotion);
        Assert.False(s.LargeText);
        Assert.False(s.HighContrast);
        Assert.Equal("en", s.Language);
    }

    [Fact]
    public void SettingsState_Clone_IsDeepCopy_MutationDoesNotAffectOriginal()
    {
        var original = new SettingsState
        {
            MasterVolume = 0.5f,
            ReduceMotion = true,
            LargeText = true
        };

        var clone = original.Clone();
        clone.MasterVolume = 0.2f;
        clone.ReduceMotion = false;
        clone.LargeText = false;

        Assert.Equal(0.5f, original.MasterVolume);
        Assert.True(original.ReduceMotion);
        Assert.True(original.LargeText);
    }
}

/// <summary>
/// Tests against the REAL SaveRepository using temp-file SQLite.
/// </summary>
public class SettingsPersistenceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SettingsPersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rw_settings_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "save.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private SaveRepository NewRepo() => new(_dbPath);

    [Fact]
    public void SaveRepository_SaveAndLoadSettings_RoundTrip()
    {
        var repo = NewRepo();

        var saved = new SettingsState
        {
            MasterVolume = 0.3f,
            MusicVolume = 0.5f,
            SfxVolume = 0.7f,
            ReduceMotion = true,
            LargeText = true,
            HighContrast = false,
            Language = "en"
        };

        repo.SaveSettings(saved);

        var loaded = repo.LoadSettings();
        Assert.Equal(0.3f, loaded.MasterVolume);
        Assert.Equal(0.5f, loaded.MusicVolume);
        Assert.Equal(0.7f, loaded.SfxVolume);
        Assert.True(loaded.ReduceMotion);
        Assert.True(loaded.LargeText);
        Assert.False(loaded.HighContrast);
        Assert.Equal("en", loaded.Language);
    }

    [Fact]
    public void SaveRepository_LoadSettings_EmptyDb_ReturnsDefaults()
    {
        var repo = NewRepo();
        // Don't save anything — fresh DB
        var loaded = repo.LoadSettings();
        Assert.Equal(1.0f, loaded.MasterVolume);
        Assert.Equal(0.8f, loaded.MusicVolume);
        Assert.Equal(1.0f, loaded.SfxVolume);
        Assert.False(loaded.ReduceMotion);
        Assert.Equal("en", loaded.Language);
    }

    [Fact]
    public void SaveRepository_LoadSettings_PartialDb_FillsDefaults()
    {
        var repo = NewRepo();
        // Save only master_volume
        var partial = new SettingsState { MasterVolume = 0.5f };
        repo.SaveSettings(partial);

        // Now read back — music, sfx, etc. should be defaults
        var loaded = repo.LoadSettings();
        Assert.Equal(0.5f, loaded.MasterVolume);
        Assert.Equal(0.8f, loaded.MusicVolume); // default
        Assert.Equal(1.0f, loaded.SfxVolume);   // default
        Assert.False(loaded.ReduceMotion);       // default
        Assert.Equal("en", loaded.Language);      // default
    }
}