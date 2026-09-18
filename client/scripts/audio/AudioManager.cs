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
/// Also tracks every playback call for headless verification — see
/// GetAudioVerificationReport() and WriteAudioVerificationReport().
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

    // ── Call-tracking record for headless verification ───────────────────
    public sealed class AudioCallRecord
    {
        public string id { get; set; } = "";
        public string type { get; set; } = ""; // "music", "sfx", "ambient"
        public bool streamNonNull { get; set; }
        public bool enteredPlaying { get; set; }
        public int callCount { get; set; }
    }

    // ── Verification report ─────────────────────────────────────────────
    public sealed class AudioVerificationReport
    {
        public List<AudioCallRecord> exercised { get; set; } = new();
        public List<string> manifestEventIds { get; set; } = new();
        public List<string> unhookedEventIds { get; set; } = new();
        public bool musicExercised { get; set; }
        public bool sfxExercised { get; set; }
        public int totalMusicCalls { get; set; }
        public int totalSfxCalls { get; set; }
        public int totalAmbientCalls { get; set; }
    }

    // ── Fields ──────────────────────────────────────────────────────────
    private readonly Dictionary<string, AudioEntry> _musicMap = new();
    private readonly Dictionary<string, AudioEntry> _sfxMap = new();
    private readonly Dictionary<string, AudioEntry> _ambientMap = new();

    // Call-tracking store: key = "type:id", value = record
    private readonly Dictionary<string, AudioCallRecord> _callLog = new();

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
            // Already playing this track — silent no-op; still record the call
            RecordCall("music", id, true, true);
            return;
        }

        var stream = LoadStream(entry.path);
        if (stream == null)
        {
            RecordCall("music", id, false, false);
            return;
        }

        // FABLE-007: music must loop. Whether it does is a per-file .import param
        // that Godot regenerates on each machine, so a fresh checkout can silently
        // play the title theme once and then sit in silence. Set it on the stream.
        if (stream is AudioStreamOggVorbis oggLoop) oggLoop.Loop = true;

        // Stop current music gracefully
        if (_musicPlayer!.Playing)
            StopMusic(fadeInSec > 0 ? 0.2f : 0.0f);

        _currentMusicId = id;
        _musicPlayer.Stream = stream;
        _musicPlayer.Play();

        RecordCall("music", id, true, _musicPlayer.Playing);

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
    // ════════════════════════════════════════════════════════════════════
    //  FABLE-007: Scene music
    // ════════════════════════════════════════════════════════════════════
    //
    // One place decides what plays where, rather than a line of wiring in every
    // scene's _Ready. AudioManager is an autoload, so it outlives every scene
    // change and can simply watch which scene is current. A screen nobody
    // thought about still gets the quiet bed instead of silence, and adding a
    // scene later needs no audio code at all.
    //
    // Keyed by the scene's ROOT NODE NAME, which matches the script name for
    // every scene in this project.

    public const string TrackTitle = "hall_of_runes";
    public const string TrackDuel = "thorn_reach";
    public const string TrackQuiet = "stones_and_dust";

    private static readonly Dictionary<string, string> SceneTracks = new()
    {
        { "Main", TrackTitle },
        { "AccountsCarouselScene", TrackTitle },
        { "MapScene", TrackTitle },
        { "ChooseYourPathScene", TrackTitle },
        { "DuelScene", TrackDuel },
        { "ArenaScene", TrackDuel },
        // Everything else — deck, shop, reliquary, forge, rune page, dig,
        // settings — falls through to the quiet bed.
    };

    private string _lastSceneName = "";

    /// <summary>True to stop the scene watcher touching music (capture/soak runs).</summary>
    public bool SceneMusicEnabled { get; set; } = true;

    public override void _Process(double delta)
    {
        if (!SceneMusicEnabled) return;
        var tree = GetTree();
        var scene = tree?.CurrentScene;
        if (scene == null || !IsInstanceValid(scene)) return;

        string name = scene.Name.ToString();
        if (name == _lastSceneName) return;
        _lastSceneName = name;

        string id = SceneTracks.TryGetValue(name, out var mapped) ? mapped : TrackQuiet;
        GD.Print($"[AudioManager] scene '{name}' -> music '{id}'");
        CrossfadeMusic(id, 1.6f);
    }

    public void CrossfadeMusic(string id, float sec = 1.0f)
    {
        if (!_musicMap.TryGetValue(id, out var entry))
        {
            GD.Print($"[AudioManager] Music '{id}' not in manifest (safe no-op)");
            return;
        }

        // FABLE-007: already playing this? Leave it alone. Without this guard the
        // music restarted on every screen change that asked for the same track,
        // which is the opposite of "casually playing in the background".
        if (_currentMusicId == id && _musicPlayer != null && _musicPlayer.Playing)
            return;

        var stream = LoadStream(entry.path);
        if (stream == null) return;
        // FABLE-007: music must loop. Whether it does is a per-file .import param
        // that Godot regenerates on each machine, so a fresh checkout can silently
        // play the title theme once and then sit in silence. Set it on the stream.
        if (stream is AudioStreamOggVorbis oggLoop) oggLoop.Loop = true;

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

        // Claim the id NOW, not in the completion callback. The guard above reads
        // this: leaving it stale for the whole 1.6s fade meant a second scene
        // change inside the fade would start ANOTHER crossfade, kill the tween
        // mid-flight, and leave the swap callback unrun with a player stuck at
        // -80 dB.
        _currentMusicId = id;

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
            // Still record the call attempt so the report shows it was referenced
            RecordCall("sfx", id, false, false);
            return;
        }

        var stream = LoadStream(entry.path);
        if (stream == null)
        {
            RecordCall("sfx", id, false, false);
            return;
        }

        var player = _sfxPlayers[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % SfxPoolSize;

        player.Stream = stream;
        player.Play();

        RecordCall("sfx", id, true, player.Playing);
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
            RecordCall("ambient", id, false, false);
            return;
        }

        if (_currentAmbientId == id && _ambientPlayer!.Playing)
        {
            RecordCall("ambient", id, true, true);
            return; // Already playing this ambient
        }

        var stream = LoadStream(entry.path);
        if (stream == null)
        {
            RecordCall("ambient", id, false, false);
            return;
        }

        _ambientPlayer!.Stop();
        _currentAmbientId = id;
        _ambientPlayer.Stream = stream;
        _ambientPlayer.Play();

        RecordCall("ambient", id, true, _ambientPlayer.Playing);
    }

    /// <summary>Stop the ambient layer.</summary>
    public void StopAmbient()
    {
        if (_ambientPlayer == null) return;
        _ambientPlayer.Stop();
        _currentAmbientId = null;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Audio Verification API (headless gate support)
    // ════════════════════════════════════════════════════════════════════

    private void RecordCall(string type, string id, bool streamNonNull, bool enteredPlaying)
    {
        string key = $"{type}:{id}";
        if (_callLog.TryGetValue(key, out var existing))
        {
            existing.callCount++;
            existing.streamNonNull = existing.streamNonNull || streamNonNull;
            existing.enteredPlaying = existing.enteredPlaying || enteredPlaying;
        }
        else
        {
            _callLog[key] = new AudioCallRecord
            {
                id = id,
                type = type,
                streamNonNull = streamNonNull,
                enteredPlaying = enteredPlaying,
                callCount = 1
            };
        }
    }

    /// <summary>Build a verification report from the current call log and manifest.</summary>
    public AudioVerificationReport GetAudioVerificationReport()
    {
        var report = new AudioVerificationReport();
        var allManifestIds = new List<string>();
        int totalMusic = 0, totalSfx = 0, totalAmbient = 0;

        foreach (var kv in _musicMap)
        {
            allManifestIds.Add($"music:{kv.Key}");
            totalMusic += _callLog.TryGetValue($"music:{kv.Key}", out var r) ? r.callCount : 0;
        }
        foreach (var kv in _sfxMap)
        {
            allManifestIds.Add($"sfx:{kv.Key}");
            totalSfx += _callLog.TryGetValue($"sfx:{kv.Key}", out var r) ? r.callCount : 0;
        }
        foreach (var kv in _ambientMap)
        {
            allManifestIds.Add($"ambient:{kv.Key}");
            totalAmbient += _callLog.TryGetValue($"ambient:{kv.Key}", out var r) ? r.callCount : 0;
        }

        report.manifestEventIds = allManifestIds;
        report.totalMusicCalls = totalMusic;
        report.totalSfxCalls = totalSfx;
        report.totalAmbientCalls = totalAmbient;

        foreach (var kv in _callLog)
        {
            report.exercised.Add(kv.Value);
            if (kv.Value.type == "music" && kv.Value.streamNonNull && kv.Value.enteredPlaying)
                report.musicExercised = true;
            if (kv.Value.type == "sfx" && kv.Value.streamNonNull && kv.Value.enteredPlaying)
                report.sfxExercised = true;
        }

        // Compute unhooked events: manifest entries not found in call log
        foreach (var manifestId in allManifestIds)
        {
            if (!_callLog.ContainsKey(manifestId))
                report.unhookedEventIds.Add(manifestId);
        }

        report.unhookedEventIds.Sort();
        return report;
    }

    /// <summary>Write the audio verification report as JSON to the given file path.</summary>
    public void WriteAudioVerificationReport(string filePath)
    {
        try
        {
            var report = GetAudioVerificationReport();
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(json);
            GD.Print($"[AudioManager] Verification report written to {filePath}");
            GD.Print($"[AudioManager] Report: musicExercised={report.musicExercised} sfxExercised={report.sfxExercised} " +
                     $"musicCalls={report.totalMusicCalls} sfxCalls={report.totalSfxCalls} ambCalls={report.totalAmbientCalls} " +
                     $"unhooked={report.unhookedEventIds.Count}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[AudioManager] Failed to write verification report: {ex.Message}");
        }
    }
}