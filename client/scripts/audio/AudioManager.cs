using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Runewake.Client;

/// <summary>
/// AudioManager autoload — plays music, SFX, and ambient sounds by string ID
/// from a manifest file. Safe with zero audio files: missing IDs log a warning,
/// never crash. All volume is controlled via the bus hierarchy:
///   Master → (Music, SFX, Ambient)
/// </summary>
public partial class AudioManager : Node
{
    // ── Manifest structure ──────────────────────────────────────────────
    private struct AudioEntry
    {
        public string path { get; set; }
        public string bus { get; set; }
        public float volume { get; set; }
    }

    private class Manifest
    {
        public Dictionary<string, AudioEntry> music { get; set; }
        public Dictionary<string, AudioEntry> sfx { get; set; }
        public Dictionary<string, AudioEntry> ambient { get; set; }
    }

    // ── Fields ──────────────────────────────────────────────────────────
    private readonly Dictionary<string, AudioEntry> _musicMap = new();
    private readonly Dictionary<string, AudioEntry> _sfxMap = new();
    private readonly Dictionary<string, AudioEntry> _ambientMap = new();

    // Music player — only one track at a time, CrossfadeMusic cross-fades
    private AudioStreamPlayer? _musicPlayer;
    private AudioStreamPlayer? _musicPlayer2; // for cross-fade
    private string? _currentMusicId;
    private Tween? _musicFadeTween;

    // SFX pool — 4 players so overlapping taps don't cut each other off
    private const int SfxPoolSize = 4;
    private readonly AudioStreamPlayer[] _sfxPlayers = new AudioStreamPlayer[SfxPoolSize];
    private int _sfxIndex;

    // Ambient — one continuous layer
    private AudioStreamPlayer? _ambientPlayer;
    private string? _currentAmbientId;

    // Bus management — created in _Ready if missing
    private bool _busesReady;

    public override void _Ready()
    {
        // Ensure the bus hierarchy exists: Master → Music, SFX, Ambient
        EnsureBuses();

        // Load manifest
        LoadManifest();

        // Create music players
        _musicPlayer = new AudioStreamPlayer { Bus = "Music" };
        AddChild(_musicPlayer);
        _musicPlayer2 = new AudioStreamPlayer { Bus = "Music" };
        AddChild(_musicPlayer2);

        // Create SFX pool
        for (int i = 0; i < SfxPoolSize; i++)
        {
            _sfxPlayers[i] = new AudioStreamPlayer { Bus = "SFX" };
            AddChild(_sfxPlayers[i]);
        }

        // Create ambient player
        _ambientPlayer = new AudioStreamPlayer { Bus = "Ambient" };
        AddChild(_ambientPlayer);

        GD.Print("[AudioManager] initialized");
    }

    /// <summary>Ensure the Music, SFX, and Ambient buses exist under Master.</summary>
    private static void EnsureBuses()
    {
        void EnsureBus(string name)
        {
            for (int i = 0; i < AudioServer.BusCount; i++)
            {
                if (AudioServer.GetBusName(i) == name)
                    return;
            }
            AudioServer.AddBus();
            int idx = AudioServer.BusCount - 1;
            AudioServer.SetBusName(idx, name);
        }

        EnsureBus("Music");
        EnsureBus("SFX");
        EnsureBus("Ambient");
        GD.Print("[AudioManager] Buses ensured: Master → Music, SFX, Ambient");
    }

    private void LoadManifest()
    {
        const string manifestPath = "res://content/audio/audio_manifest.json";
        if (!ResourceLoader.Exists(manifestPath))
        {
            GD.Print("[AudioManager] No manifest found — running silent. Add content/audio/audio_manifest.json to enable audio.");
            return;
        }

        try
        {
            using var file = Godot.FileAccess.Open(manifestPath, Godot.FileAccess.ModeFlags.Read);
            string json = file.GetAsText();
            var manifest = JsonSerializer.Deserialize<Manifest>(json);
            if (manifest == null)
            {
                GD.PrintErr("[AudioManager] Failed to parse manifest");
                return;
            }

            foreach (var (id, entry) in manifest.music)
                _musicMap[id] = entry;
            foreach (var (id, entry) in manifest.sfx)
                _sfxMap[id] = entry;
            foreach (var (id, entry) in manifest.ambient)
                _ambientMap[id] = entry;

            GD.Print($"[AudioManager] Loaded {_musicMap.Count} music, {_sfxMap.Count} sfx, {_ambientMap.Count} ambient entries");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioManager] Error loading manifest: {ex.Message}");
        }
    }

    /// <summary>Load an audio stream from a res:// path. Returns null on failure.</summary>
    private static AudioStream? LoadStream(string path)
    {
        if (!ResourceLoader.Exists(path))
        {
            GD.PrintErr($"[AudioManager] Audio file not found: {path}");
            return null;
        }
        return ResourceLoader.Load<AudioStream>(path);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Music API
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Play a music track by manifest ID. No-op if already playing.</summary>
    public void PlayMusic(string id, float fadeInSec = 0.0f)
    {
        if (!_musicMap.TryGetValue(id, out var entry))
        {
            GD.Print($"[AudioManager] Music '{id}' not in manifest (safe no-op)");
            return;
        }

        if (_currentMusicId == id && _musicPlayer!.Playing)
        {
            // Already playing this track — silent no-op
            return;
        }

        var stream = LoadStream(entry.path);
        if (stream == null) return;

        // Stop current music gracefully
        if (_musicPlayer!.Playing)
            StopMusic(fadeInSec > 0 ? 0.2f : 0.0f);

        _currentMusicId = id;
        _musicPlayer.Stream = stream;
        _musicPlayer.Play();

        if (fadeInSec > 0.001f)
        {
            _musicPlayer.VolumeDb = -80;
            _musicFadeTween?.Kill();
            _musicFadeTween = CreateTween();
            _musicFadeTween.TweenProperty(_musicPlayer, "volume_db", 0.0f, fadeInSec);
        }
    }

    /// <summary>Fade out current music over fadeOutSec seconds.</summary>
    public void StopMusic(float fadeOutSec = 0.0f)
    {
        if (!_musicPlayer!.Playing) return;

        if (fadeOutSec > 0.001f)
        {
            _musicFadeTween?.Kill();
            _musicFadeTween = CreateTween();
            _musicFadeTween.TweenProperty(_musicPlayer, "volume_db", -80.0f, fadeOutSec);
            _musicFadeTween.TweenCallback(Callable.From(() =>
            {
                _musicPlayer.Stop();
                _currentMusicId = null;
            }));
        }
        else
        {
            _musicPlayer.Stop();
            _currentMusicId = null;
        }
    }

    /// <summary>Cross-fade from current track to a new one.</summary>
    public void CrossfadeMusic(string id, float sec = 1.0f)
    {
        if (!_musicMap.TryGetValue(id, out var entry))
        {
            GD.Print($"[AudioManager] Music '{id}' not in manifest (safe no-op)");
            return;
        }

        var stream = LoadStream(entry.path);
        if (stream == null) return;

        // If nothing is playing, just play
        if (_musicPlayer == null || !_musicPlayer.Playing)
        {
            PlayMusic(id, sec);
            return;
        }

        // Fade out current on _musicPlayer, play new on _musicPlayer2, then swap
        _musicFadeTween?.Kill();
        _musicFadeTween = CreateTween().SetParallel(false);

        // Fade out current
        _musicFadeTween.TweenProperty(_musicPlayer, "volume_db", -80.0f, sec);

        // Set up the second player
        _musicPlayer2!.Stream = stream;
        _musicPlayer2.VolumeDb = -80;
        _musicPlayer2.Play();

        // Fade in new track
        _musicFadeTween.TweenProperty(_musicPlayer2, "volume_db", 0.0f, sec);

        // After fade completes, stop old player and swap references
        _musicFadeTween.TweenCallback(Callable.From(() =>
        {
            _musicPlayer!.Stop();
            _currentMusicId = id;

            // Swap references so _musicPlayer is always the active one
            (_musicPlayer, _musicPlayer2) = (_musicPlayer2, _musicPlayer);
        }));
    }

    // ════════════════════════════════════════════════════════════════════
    //  SFX API
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Play a one-shot SFX from the pool. Overlapping calls get separate players.</summary>
    public void PlaySfx(string id)
    {
        if (!_sfxMap.TryGetValue(id, out var entry))
        {
            GD.Print($"[AudioManager] SFX '{id}' not in manifest (safe no-op)");
            return;
        }

        var stream = LoadStream(entry.path);
        if (stream == null) return;

        var player = _sfxPlayers[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % SfxPoolSize;

        player.Stream = stream;
        player.Play();
    }

    // ════════════════════════════════════════════════════════════════════
    //  Ambient API
    // ════════════════════════════════════════════════════════════════════

    /// <summary>Start or swap the ambient sound layer. Old ambient is stopped (no cross-fade for ambient).</summary>
    public void PlayAmbient(string id)
    {
        if (!_ambientMap.TryGetValue(id, out var entry))
        {
            GD.Print($"[AudioManager] Ambient '{id}' not in manifest (safe no-op)");
            return;
        }

        if (_currentAmbientId == id && _ambientPlayer!.Playing)
            return; // Already playing this ambient

        var stream = LoadStream(entry.path);
        if (stream == null) return;

        _ambientPlayer!.Stop();
        _currentAmbientId = id;
        _ambientPlayer.Stream = stream;
        _ambientPlayer.Play();
    }

    /// <summary>Stop the ambient layer.</summary>
    public void StopAmbient()
    {
        if (_ambientPlayer == null) return;
        _ambientPlayer.Stop();
        _currentAmbientId = null;
    }
}