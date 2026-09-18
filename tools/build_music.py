#!/usr/bin/env python3
"""
build_music.py — Runewake's score: one theme, three moods, all seamless loops.

Everything here is synthesised from first principles with numpy — oscillators,
Karplus-Strong plucked strings, filtered noise, a Schroeder reverb. No samples,
no recordings, no third-party material of any kind. The output is therefore
public domain by construction, exactly like ambient_reach.ogg before it
(see tools/build_ambient.py and content/audio/AUDIO_CREDITS.md).

── The music ────────────────────────────────────────────────────────────────
One motif carries all three tracks, so the game sounds like one place rather
than three. It is stated in D Hijaz (D Eb F# G A Bb C) — the raised fourth and
the flat second over a fifth drone are the "Arabian nights" colour Trikzos
asked for. The harmony underneath moves in D Dorian, which is where the
medieval/RuneScape feeling lives: modal, plain, no leading tone, no cadence
that resolves the way a classical ear expects. Sitting the ornate mode on top
of the plain one is the whole trick.

  hall_of_runes     title, menu, map.   Ney flute states the motif high and
                                        slow over an oud arpeggio, wide reverb,
                                        a frame drum only every other bar.
  thorn_reach       duel.               The same motif an octave down on the
                                        oud, drone soured with a flat second,
                                        drum steady like a pulse. Flute reduced
                                        to a distant answer.
  stones_and_dust   deck, shop, relics. Drone and the motif's first three notes,
                                        far apart, no drum. Meant to be ignored.

── Seamlessness ─────────────────────────────────────────────────────────────
Each track is rendered LONGER than its loop, then the overflow — reverb tails,
any note still ringing across the barline — is folded back onto the beginning.
The result is a loop with no seam and no gap, so AudioManager can play it on
repeat forever without a click or a swell.

Usage:  python3 tools/build_music.py [--fast]
        --fast renders short stubs for a quick smoke test.
"""
import argparse
import os
import subprocess
import sys
import wave

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_DIR = os.path.join(ROOT, "client", "content", "audio", "music")

# ── Modes (semitone offsets from the root) ──────────────────────────────────
HIJAZ = [0, 1, 4, 5, 7, 8, 10]      # D Eb F# G A Bb C — the ornamented voice
DORIAN = [0, 2, 3, 5, 7, 9, 10]     # D E F  G A B  C  — the plain harmony

D3 = 146.83  # Hz


def hz(root_hz, mode, degree):
    """Frequency of a scale degree, wrapping octaves for degrees outside 0..6."""
    octave, idx = divmod(degree, len(mode))
    return root_hz * (2.0 ** (mode[idx] / 12.0)) * (2.0 ** octave)


# ── Building blocks ─────────────────────────────────────────────────────────

def comb(x, delay, g):
    """Feedback comb, computed a delay-length block at a time so numpy can do it."""
    y = x.astype(np.float64).copy()
    for i in range(delay, len(y), delay):
        n = min(delay, len(y) - i)
        y[i:i + n] += g * y[i - delay:i - delay + n]
    return y


def allpass(x, delay, g):
    y = np.zeros(len(x), dtype=np.float64)
    head = min(delay, len(x))
    y[:head] = -g * x[:head]
    for i in range(delay, len(x), delay):
        n = min(delay, len(x) - i)
        y[i:i + n] = (-g * x[i:i + n]
                      + x[i - delay:i - delay + n]
                      + g * y[i - delay:i - delay + n])
    return y


def reverb(x, size=1.0, wet=0.35, seed=0):
    """
    Schroeder reverb: four parallel combs into two series allpasses. The delay
    set is jittered per call so the left and right channels decorrelate, which
    is what gives the stereo image width without any actual stereo source.
    """
    rng = np.random.default_rng(seed)
    base = np.array([1557, 1617, 1491, 1422])
    delays = (base * size * rng.uniform(0.97, 1.03, 4)).astype(int)
    gains = [0.84, 0.83, 0.85, 0.82]
    acc = np.zeros(len(x), dtype=np.float64)
    for d, g in zip(delays, gains):
        acc += comb(x, int(max(64, d)), g)
    acc /= len(delays)
    for d, g in ((225, 0.7), (556, 0.7)):
        acc = allpass(acc, int(d * size), g)
    return (1.0 - wet) * x + wet * acc


def ks_pluck(freq, dur, amp=1.0, damp=0.50, decay_sec=1.0, seed=0):
    """
    Karplus-Strong: a burst of noise round a delay line with a one-pole
    averaging filter in the loop. Short delay and heavy damping reads as an
    oud; longer and brighter reads as a hammered dulcimer.

    decay_sec is a TIME constant, not a per-period gain. That distinction
    matters: the loop runs once per period, so a fixed per-period gain decays
    high notes far faster than low ones — at 590 Hz a gain of 0.9995^n killed
    the note in about 0.15 s, which turned every pluck into a click and left
    the duel track as drum and drone with no tune in it at all.
    """
    n = max(2, int(round(SR / float(freq))))
    total = int(dur * SR)
    loop_gain = float(np.exp(-n / (SR * max(0.05, decay_sec))))
    rng = np.random.default_rng(seed)
    buf = np.zeros(total + n + 1, dtype=np.float64)
    burst = rng.uniform(-1.0, 1.0, n)
    # Pre-filter the excitation — an unfiltered burst is a spitty click.
    burst = np.convolve(burst, np.array([0.25, 0.5, 0.25]), mode="same")
    buf[1:n + 1] = burst
    for i in range(n + 1, total + n + 1, n):
        k = min(n, total + n + 1 - i)
        cur = buf[i - n:i - n + k]
        prev = buf[i - n - 1:i - n - 1 + k]
        buf[i:i + k] = ((1.0 - damp) * cur + damp * prev) * loop_gain
    out = buf[1:total + 1]
    # Gentle body resonance so it is not a bare string
    out = out + 0.18 * np.roll(out, int(SR * 0.011))
    return amp * out


def ney(freq, dur, amp=1.0, breath=0.030, seed=0):
    """
    End-blown reed flute: a handful of harmonics, a slow vibrato that only
    arrives after the note has settled, and breath noise shaped by the envelope.
    """
    t = np.arange(int(dur * SR)) / SR
    rng = np.random.default_rng(seed)
    vib_on = np.clip((t - 0.35) / 0.5, 0.0, 1.0)
    vib = 1.0 + 0.004 * vib_on * np.sin(2 * np.pi * 4.6 * t + rng.uniform(0, 6))
    phase = 2 * np.pi * freq * np.cumsum(vib) / SR
    tone = (np.sin(phase)
            + 0.30 * np.sin(2 * phase)
            + 0.14 * np.sin(3 * phase)
            + 0.05 * np.sin(4 * phase))
    atk = np.clip(t / 0.13, 0.0, 1.0) ** 1.5
    rel = np.clip((dur - t) / 0.35, 0.0, 1.0)
    env = atk * rel
    air = rng.normal(0, 1, len(t))
    air = np.convolve(air, np.ones(14) / 14, mode="same")  # soften to a hiss
    return amp * env * (0.85 * tone + breath * air)


def frame_drum(dur, f0=78.0, amp=1.0, tone=0.55, seed=0):
    """Bendir/daf: a membrane pitch-dropping into a thud, plus a rim rustle."""
    t = np.arange(int(dur * SR)) / SR
    rng = np.random.default_rng(seed)
    sweep = f0 * (1.0 + 1.6 * np.exp(-t * 28))
    body = np.sin(2 * np.pi * np.cumsum(sweep) / SR)
    skin = rng.normal(0, 1, len(t)) * np.exp(-t * 46)
    env = np.exp(-t * 7.5)
    # ~2.5 ms attack. Without it the onset is a vertical edge, which is both
    # unnatural and the thing that makes the ogg encoder overshoot 0 dBFS.
    env = env * np.clip(t / 0.0025, 0.0, 1.0)
    return amp * env * (tone * body + (1.0 - tone) * skin)


def drone(freqs, dur, amp=1.0, seed=0):
    """Stacked, slowly beating sines. The floor everything else stands on."""
    t = np.arange(int(dur * SR)) / SR
    rng = np.random.default_rng(seed)
    out = np.zeros(len(t), dtype=np.float64)
    for k, f in enumerate(freqs):
        for det in (-0.12, 0.0, 0.13):        # three-way detune = slow beating
            swell = 1.0 + 0.16 * np.sin(2 * np.pi * (0.021 + 0.004 * k) * t
                                        + rng.uniform(0, 6))
            out += swell * np.sin(2 * np.pi * (f + det) * t + rng.uniform(0, 6))
        out += 0.22 * np.sin(2 * np.pi * 2 * f * t + rng.uniform(0, 6))
    return amp * out / (len(freqs) * 3.5)


def place(buf, sig, at_sec):
    """Mix sig into buf at a time offset, letting it run past the end."""
    i = int(at_sec * SR)
    if i >= len(buf):
        return
    n = min(len(sig), len(buf) - i)
    buf[i:i + n] += sig[:n]


def wrap_tail(buf, loop_n):
    """Fold everything past the loop point back onto the head, then truncate."""
    tail = buf[loop_n:]
    out = buf[:loop_n].copy()
    n = min(len(tail), loop_n)
    out[:n] += tail[:n]
    return out


def normalise(x, target_rms=0.11, ceiling=0.82):
    rms = float(np.sqrt(np.mean(x ** 2))) or 1.0
    x = x * (target_rms / rms)
    peak = float(np.max(np.abs(x))) or 1.0
    if peak > ceiling:
        x = x * (ceiling / peak)
    return x


def write_ogg(path, left, right, quality="3"):
    wav = path.replace(".ogg", ".tmp.wav")
    stereo = np.stack([left, right], axis=1)
    pcm = np.clip(stereo, -1.0, 1.0)
    pcm = (pcm * 32767.0).astype("<i2")
    with wave.open(wav, "w") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())
    subprocess.run(
        ["ffmpeg", "-y", "-loglevel", "error", "-i", wav,
         "-c:a", "libvorbis", "-q:a", quality, path],
        check=True)
    os.remove(wav)
    return os.path.getsize(path)


# ── The motif ───────────────────────────────────────────────────────────────
# (scale degree, length in beats). Degree 7 is the octave.
MOTIF_A = [(0, 2), (3, 1), (4, 1), (5, 2), (4, 1), (3, 1), (1, 2), (0, 6)]
MOTIF_B = [(4, 2), (5, 1), (6, 1), (5, 2), (4, 2), (3, 2), (2, 2), (0, 4)]


def phrase_times(motif, beat, start):
    """Expand (degree, beats) into (degree, start_sec, dur_sec)."""
    out, t = [], start
    for deg, beats in motif:
        out.append((deg, t, beats * beat))
        t += beats * beat
    return out


# ── Tracks ──────────────────────────────────────────────────────────────────

def render_hall_of_runes(bars=24, bpm=68):
    """Title / menu / map. Open, unhurried, a little ceremonial."""
    beat = 60.0 / bpm
    loop = bars * 4 * beat
    tail = 6.0
    n = int((loop + tail) * SR)
    lead = np.zeros(n)
    pluck = np.zeros(n)
    perc = np.zeros(n)

    # Flute states the theme twice, with the answering phrase between.
    plan = [(MOTIF_A, 0.0), (MOTIF_B, 4 * 4 * beat), (MOTIF_A, 8 * 4 * beat),
            (MOTIF_B, 12 * 4 * beat), (MOTIF_A, 16 * 4 * beat),
            (MOTIF_B, 20 * 4 * beat)]
    s = 0
    for motif, start in plan:
        for deg, t0, dur in phrase_times(motif, beat, start):
            s += 1
            f = hz(D3 * 2, HIJAZ, deg)
            lead += 0.0
            place(lead, ney(f, dur * 0.94, amp=0.80, seed=s), t0)

    # Oud arpeggio: root, fifth, octave, third of the bar's mode position.
    arp = [0, 4, 7, None, 4, None, 2, None]
    for bar in range(bars):
        for j, deg in enumerate(arp):
            if deg is None:
                continue
            s += 1
            t0 = bar * 4 * beat + j * beat * 0.5
            if t0 >= loop:
                break
            f = hz(D3, DORIAN, deg)
            amp = 0.30 if j == 0 else 0.17
            place(pluck, ks_pluck(f, 1.9, amp=amp, damp=0.50, decay_sec=1.35, seed=s), t0)

    # Frame drum, sparse: one low stroke every other bar, a light one before it.
    for bar in range(0, bars, 2):
        s += 1
        place(perc, frame_drum(1.5, 74.0, amp=0.42, seed=s), bar * 4 * beat)
        place(perc, frame_drum(0.7, 128.0, amp=0.13, tone=0.3, seed=s + 1),
              bar * 4 * beat + 2.5 * beat)

    bed = drone([D3 / 2, D3 * 2 ** (7 / 12) / 2], loop + tail, amp=0.21, seed=7)

    dry = 0.55 * pluck + 0.30 * perc + bed * 0.9
    left = wrap_tail(dry + reverb(lead, 1.25, 0.52, seed=1) + reverb(pluck * 0.5, 1.1, 0.4, seed=3), int(loop * SR))
    right = wrap_tail(dry + reverb(lead, 1.25, 0.52, seed=2) + reverb(pluck * 0.5, 1.1, 0.4, seed=4), int(loop * SR))
    return normalise(left), normalise(right), loop


def render_thorn_reach(bars=24, bpm=84):
    """Duel. The same theme, lower and tighter, with a pulse under it."""
    beat = 60.0 / bpm
    loop = bars * 4 * beat
    tail = 5.0
    n = int((loop + tail) * SR)
    lead = np.zeros(n)
    pluck = np.zeros(n)
    perc = np.zeros(n)
    s = 1000

    # Motif on the oud, an octave below the flute's statement.
    for motif, start in [(MOTIF_A, 0.0), (MOTIF_B, 4 * 4 * beat),
                         (MOTIF_A, 8 * 4 * beat), (MOTIF_A, 16 * 4 * beat),
                         (MOTIF_B, 20 * 4 * beat)]:
        for deg, t0, dur in phrase_times(motif, beat, start):
            s += 1
            # An octave above the drone, or the tune disappears into it.
            place(pluck, ks_pluck(hz(D3 * 2, HIJAZ, deg), min(dur * 1.2, 2.2),
                                  amp=0.46, damp=0.53, decay_sec=0.85, seed=s), t0)
            # Root doubled low for weight, quiet enough not to muddy it.
            place(pluck, ks_pluck(hz(D3, HIJAZ, deg), min(dur, 1.4),
                                  amp=0.13, damp=0.55, decay_sec=0.70, seed=s + 500), t0)

    # Distant flute answers only in the second half — it should feel like the
    # menu's theme heard from further away.
    for deg, t0, dur in phrase_times(MOTIF_B, beat, 12 * 4 * beat):
        s += 1
        place(lead, ney(hz(D3 * 2, HIJAZ, deg), dur * 0.8, amp=0.38, seed=s), t0)

    # Steady heartbeat: dum on 1, tek on 2-and and 4.
    for bar in range(bars):
        b0 = bar * 4 * beat
        s += 1
        place(perc, frame_drum(0.9, 70.0, amp=0.44, seed=s), b0)
        place(perc, frame_drum(0.45, 150.0, amp=0.16, tone=0.25, seed=s + 1), b0 + 1.5 * beat)
        place(perc, frame_drum(0.6, 96.0, amp=0.24, tone=0.4, seed=s + 2), b0 + 3.0 * beat)

    # Drone soured with the flat second — the mode's own tension, held under
    # everything so the duel never quite settles.
    bed = drone([D3 / 2, D3 * 2 ** (7 / 12) / 2], loop + tail, amp=0.23, seed=11)
    bed += drone([D3 * 2 ** (1 / 12) / 2], loop + tail, amp=0.085, seed=12)

    dry = 0.62 * pluck + 0.42 * perc + bed * 0.95
    left = wrap_tail(dry + reverb(lead, 1.0, 0.42, seed=5) + reverb(pluck * 0.35, 0.85, 0.3, seed=7), int(loop * SR))
    right = wrap_tail(dry + reverb(lead, 1.0, 0.42, seed=6) + reverb(pluck * 0.35, 0.85, 0.3, seed=8), int(loop * SR))
    return normalise(left, 0.105, 0.70), normalise(right, 0.105, 0.70), loop


def render_stones_and_dust(bars=16, bpm=56):
    """Deck, shop, reliquary. Deliberately almost nothing."""
    beat = 60.0 / bpm
    loop = bars * 4 * beat
    tail = 7.0
    n = int((loop + tail) * SR)
    lead = np.zeros(n)
    pluck = np.zeros(n)
    s = 2000

    # Only the head of the motif, left hanging, twice in the loop.
    for start in (1.0 * beat, 8 * 4 * beat + beat):
        for deg, t0, dur in phrase_times(MOTIF_A[:3], beat * 2.2, start):
            s += 1
            place(pluck, ks_pluck(hz(D3 * 2, HIJAZ, deg), 3.4, amp=0.34,
                                  damp=0.50, decay_sec=1.6, seed=s), t0)

    # One long flute note near the end of each half, barely there.
    for start, deg in ((5 * 4 * beat, 4), (13 * 4 * beat, 0)):
        s += 1
        place(lead, ney(hz(D3 * 2, HIJAZ, deg), 4.0, amp=0.30, breath=0.018, seed=s), start)

    bed = drone([D3 / 2, D3 * 2 ** (7 / 12) / 2], loop + tail, amp=0.26, seed=21)

    dry = 0.5 * pluck + bed
    left = wrap_tail(dry + reverb(lead, 1.5, 0.55, seed=9) + reverb(pluck * 0.6, 1.4, 0.5, seed=11), int(loop * SR))
    right = wrap_tail(dry + reverb(lead, 1.5, 0.55, seed=10) + reverb(pluck * 0.6, 1.4, 0.5, seed=12), int(loop * SR))
    return normalise(left, 0.085), normalise(right, 0.085), loop


TRACKS = [
    ("hall_of_runes", "Hall of Runes", render_hall_of_runes),
    ("thorn_reach", "The Thorn Reach", render_thorn_reach),
    ("stones_and_dust", "Stones and Dust", render_stones_and_dust),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--fast", action="store_true", help="short stubs, for a smoke test")
    ap.add_argument("--only", default="", help="render just this track id")
    args = ap.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)
    total = 0
    for tid, title, fn in TRACKS:
        if args.only and args.only != tid:
            continue
        kwargs = {"bars": 4} if args.fast else {}
        print(f"── {title} ({tid}) ──", flush=True)
        left, right, loop = fn(**kwargs)
        path = os.path.join(OUT_DIR, f"{tid}.ogg")
        size = write_ogg(path, left, right)
        total += size
        # Seam report: the loudest single-sample step across the wrap point. A
        # real seam shows up here as a spike, so this number is the proof that
        # the loop is clean, not an assertion that it is.
        seam = abs(float(left[0]) - float(left[-1]))
        interior = float(np.max(np.abs(np.diff(left))))
        print(f"   {loop:6.1f}s   {size/1024:7.1f} KB   "
              f"seam step {seam:.4f} vs worst interior step {interior:.4f}"
              f"   {'OK' if seam <= interior else 'SEAM!'}", flush=True)
    print(f"\ntotal {total/1048576:.2f} MB in {OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
