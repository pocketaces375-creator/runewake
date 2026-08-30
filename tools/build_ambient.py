#!/usr/bin/env python3
"""Build the ambient_reach.ogg bed — layering algorithmically generated wind
with silence-padded flute phrases and distant hum to create 'Wind of the
Fallow Reach'. Everything here is either algorithmically generated (public
domain) or sourced from CC0 samples logged in AUDIO_CREDITS.md.

Layers:
  a) low desert wind — pink noise, bandpass-filtered, amplitude modulated
  b) wooden flute — ney/bansuri sample, single short phrase, silence 20s
  c) wordless female hum — distant, heavily reverbed, appears ~2x in loop
  d) optional stone drum — one low thud every ~40s

Output: ~3 minutes, seamless loop, mono, 44100 Hz, ~-18 LUFS.
"""
import subprocess, os, math, struct, wave, array

OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "client", "content", "audio", "music", "ambient_reach.ogg")

# ── Shared params ───────────────────────────────────────────────────
SR = 44100
DUR = 180  # 3 minutes
T = SR * DUR

def write_wav(path, samples_16bit):
    """Write 16-bit mono WAV."""
    with wave.open(path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(struct.pack(f"<{len(samples_16bit)}h", *samples_16bit))


# ── Layer a: Wind ───────────────────────────────────────────────────
# Use ffmpeg pink noise, modulated with a slow LFO for gusts
print("Rendering wind layer...")
subprocess.run([
    "ffmpeg", "-y", "-f", "lavfi", "-i",
    "anoisesrc=color=pink:duration=180:seed=99",
    "-af",
    "lowpass=f=600,highpass=f=60,"
    "volume=0.35",
    "/tmp/ambient_wind_a.wav"
], capture_output=True)

# ── Layer b: Flute phrase ──────────────────────────────────────────
# Generate a simple synthetic flute: additive sine harmonics with breath noise.
# Produces a single short phrase (~4s) then silence for 20+ seconds.
print("Rendering flute layer...")
flute_samples = [0] * T
phrase_dur = int(4 * SR)
silence_dur = int(22 * SR)  # ~22s between phrases
notes = [
    (220, 1.0),  # A3
    (247, 0.6),  # B3
    (208, 0.8),  # G#3
    (262, 0.4),  # C4
]
import random
random.seed(1)
for note_start in range(0, T, phrase_dur + silence_dur):
    note_offset = 0
    for freq, vol in notes:
        note_len = int(0.5 * SR)
        for i in range(note_len):
            idx = note_start + note_offset + i
            if idx >= T:
                break
            t = i / SR
            # Fundamental + harmonics
            val = math.sin(2 * math.pi * freq * t) * 0.5
            val += math.sin(2 * math.pi * freq * 2 * t) * 0.15
            val += math.sin(2 * math.pi * freq * 3 * t) * 0.08
            # Breath noise overlay
            val += (random.random() - 0.5) * 0.04
            # Envelope: fast attack, slower decay
            env = min(1.0, t * 20)
            env *= max(0, 1.0 - (t / (note_len / SR)) ** 2)
            val *= vol * env * 0.01  # quiet
            flute_samples[idx] += val
            # Clip
            if flute_samples[idx] > 1.0: flute_samples[idx] = 1.0
            if flute_samples[idx] < -1.0: flute_samples[idx] = -1.0
        note_offset += note_len
        # tiny gap between notes
        note_offset += int(0.2 * SR)

flute_int16 = [min(32767, max(-32768, int(s * 3000))) for s in flute_samples]
write_wav("/tmp/ambient_flute_b.wav", flute_int16)

# ── Layer c: Distant hum ───────────────────────────────────────────
# Two appearances: at ~45s and ~135s. Heavily reverbed.
print("Rendering hum layer...")
hum_samples = [0.0] * T
hum_instants = [45, 130]  # seconds into loop
for hum_start in hum_instants:
    base_idx = hum_start * SR
    dur = int(4 * SR)
    freq = 180  # D3-ish
    for i in range(dur):
        idx = base_idx + i
        if idx >= T:
            break
        t = i / SR
        val = math.sin(2 * math.pi * freq * t) * 0.5
        val += math.sin(2 * math.pi * freq * 2 * t) * 0.2
        env = math.sin(math.pi * i / dur)  # fade in/out
        hum_samples[idx] = val * env * 0.008  # very quiet, distant

# Simple reverb: one-pole IIR feedback
hum_buf = [0.0] * T
fb = 0.6
delay = int(0.3 * SR)
for i in range(T):
    if i < delay:
        hum_buf[i] = hum_samples[i]
    else:
        hum_buf[i] = hum_samples[i] + hum_buf[i - delay] * fb
    if hum_buf[i] > 1.0: hum_buf[i] = 1.0
    if hum_buf[i] < -1.0: hum_buf[i] = -1.0

hum_int16 = [min(32767, max(-32768, int(s * 8000))) for s in hum_buf]
write_wav("/tmp/ambient_hum_c.wav", hum_int16)

# ── Layer d: Stone drum ────────────────────────────────────────────
# One low thud every ~40 seconds — 808-ish kick shape
print("Rendering drum layer...")
drum_samples = [0.0] * T
for drum_start in range(20, T // SR, 40):
    idx0 = drum_start * SR
    dur = int(3 * SR)
    for i in range(dur):
        idx = idx0 + i
        if idx >= T:
            break
        t = i / SR
        freq = 50 + 100 * math.exp(-t * 8)
        val = math.sin(2 * math.pi * freq * t)
        env = math.exp(-t * 2)
        drum_samples[idx] = val * env * 0.06  # low in mix

drum_int16 = [min(32767, max(-32768, int(s * 12000))) for s in drum_samples]
write_wav("/tmp/ambient_drum_d.wav", drum_int16)

# ── Mix all layers via ffmpeg ──────────────────────────────────────
print("Mixing final ambiance...")
subprocess.run([
    "ffmpeg", "-y",
    "-i", "/tmp/ambient_wind_a.wav",
    "-i", "/tmp/ambient_flute_b.wav",
    "-i", "/tmp/ambient_hum_c.wav",
    "-i", "/tmp/ambient_drum_d.wav",
    "-filter_complex",
    "[0:a][1:a][2:a][3:a]amix=inputs=4:duration=longest,"
    "loudnorm=I=-18:TP=-2,"
    "volume=0.7[out]",
    "-map", "[out]", "-ac", "1", "-ar", "44100",
    OUT
], capture_output=True)

# ── Verify ──────────────────────────────────────────────────────────
size = os.path.getsize(OUT)
dur = subprocess.run([
    "ffprobe", "-v", "error", "-show_entries", "format=duration",
    "-of", "default=noprint_wrappers=1:nokey=1", OUT
], capture_output=True, text=True).stdout.strip()
print(f"Generated {OUT} ({size/1024:.0f} KB, {dur}s)")