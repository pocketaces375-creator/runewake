#!/usr/bin/env python3
"""
Runewake — Ambient Music Generator (Middle-Eastern idiom)

Generates looping ambient music using additive/FM/Karplus-Strong synthesis.
Modes: Bayati on D (wandering/map), Hijaz on D (dark/ritual).
No drum kit, no fifths-based Western pads, no major-key resolutions.
Loops are seamless: start and end on the same pattern phase.
"""
import argparse, os, numpy as np
from scipy import signal as sp_signal
import soundfile as sf

SR = 44100
BPM = 90
BEAT_SAMPLES = int(SR * 60 / BPM)


def hz(n):
    """Note name to Hz (A4=440)."""
    NOTES = {'C':-9, 'C#':-8, 'Db':-8, 'D':-7, 'D#':-6, 'Eb':-6, 'E':-5,
             'F':-4, 'F#':-3, 'Gb':-3, 'G':-2, 'G#':-1, 'Ab':-1, 'A':0,
             'A#':1, 'Bb':1, 'B':2, 'Cb':2}
    return 440.0 * (2.0 ** ((NOTES.get(n[:-1],0) + (int(n[-1])-4)*12) / 12.0))


def half_flat(n):
    """Quarter-tone below the given note (50 cents)."""
    return hz(n) * (2.0 ** (-0.5 / 12.0))


# Scale definitions
BAYATI = {
    'name': 'Bayati on D',
    'tonic': hz('D3'), 'fifth': hz('A3'),
    'scale': [hz('D4'), half_flat('E4'), hz('F4'), hz('G4'),
              hz('A4'), hz('Bb4'), hz('C5')],
}
HIJAZ = {
    'name': 'Hijaz on D',
    'tonic': hz('D3'), 'fifth': hz('A3'),
    'scale': [hz('D4'), hz('Eb4'), hz('F#4'), hz('G4'),
              hz('A4'), hz('Bb4'), hz('C5')],
}


def env_pluck(n):
    """Pluck envelope: fast attack, quick decay to 5%, release."""
    e = np.ones(n)
    a = min(int(0.003*SR), n)
    d = min(a+int(0.15*SR), n)
    r = max(d, n-int(0.02*SR))
    e[:a] = np.linspace(0,1,a)
    e[a:d] = np.linspace(1,0.05,d-a)
    e[d:r] = 0.05
    e[r:] = np.linspace(0.05,0,n-r)
    return e


def env_bowed(n, a=0.3):
    """Bowed envelope with gentle amplitude wobble."""
    e = np.ones(n) * a
    w = 1.0 + 0.15 * np.sin(2*np.pi*0.2*np.arange(n)/SR)
    ons = min(int(0.3*SR), n)
    e[:ons] = np.linspace(0,a,ons)
    return e * w


def env_slow(n, attack=0.05, release=0.15):
    """Slow flute envelope."""
    e = np.ones(n)
    a = min(int(attack*SR), n)
    e[:a] = np.linspace(0,1,a)
    if n > a + int(release*SR):
        r = n - int(release*SR)
        e[r:] = np.linspace(1,0,n-r)
    return e


def karplus_strong(f, dur, feedback=0.98):
    """Karplus-Strong plucked string."""
    n = int(dur*SR)
    N = max(int(SR/f), 2, 64)
    buf = np.random.uniform(-1, 1, N)
    out = np.zeros(n)
    for i in range(n):
        out[i] = buf[i % N]
        buf[i % N] = (buf[i%N] + buf[(i-1)%N]) * 0.5 * feedback
    return out * env_pluck(n) * 0.3


def fm_flute(f, dur, mod_rate=5, mod_idx=4, breath=0.3):
    """FM synthesis ney-like flute with filtered noise."""
    n = int(dur*SR)
    t = np.arange(n) / SR
    carrier = np.sin(2*np.pi*f*t + mod_idx*np.sin(2*np.pi*mod_rate*t))
    noise = np.random.normal(0, 1, n)
    nyq = 0.5 * SR
    low = (f*0.5)/nyq
    high = min(f*2.0/nyq, 0.99)
    if low < high and low > 0:
        b,a = sp_signal.butter(2, [low, high], btype='band')
        noise = sp_signal.filtfilt(b,a,noise)
    mixed = (1-breath)*carrier + breath*noise
    env = env_slow(n)
    return mixed * env * 0.25


def drum_hit(dur, low_tone=True):
    """Synthesized frame-drum hit."""
    n = int(dur*SR)
    t = np.arange(n) / SR
    if low_tone:
        fr = 90 + 20*np.random.random()
        sig = (np.sin(2*np.pi*fr*t)*0.4 + np.sin(2*np.pi*55*t)*0.4
               + np.random.normal(0,1,n)*0.3*np.exp(-t*25))
        env = np.exp(-t*8)
    else:
        fr = 200 + 400*np.random.random()
        sig = (np.sin(2*np.pi*fr*t)*0.3*np.exp(-t*30)
               + np.sin(2*np.pi*400*t)*0.3*np.exp(-t*15)
               + np.random.normal(0,1,n)*0.5*np.exp(-t*40))
        env = np.exp(-t*15)
    return sig * env * 0.12


def maqsum(total_dur):
    """Frame-drum Maqsum pattern: DUM - tek - DUM - tek at ~90 BPM."""
    n = int(total_dur*SR)
    out = np.zeros(n)
    beat = 0
    while beat < n:
        bi = (beat % (4*BEAT_SAMPLES)) // BEAT_SAMPLES
        if bi == 0:  # DUM (beat 1)
            end = min(beat+int(0.15*SR), n)
            if end > beat:
                out[beat:end] = drum_hit(0.15)[:end-beat]
        elif bi == 1:  # tek (beat 2) — slight off-beat swing
            start = beat + int(0.85*BEAT_SAMPLES)
            end = min(start+int(0.08*SR), n)
            if start < n:
                out[start:end] = drum_hit(0.08, low_tone=False)[:end-start]
        elif bi == 2:  # DUM (beat 3)
            end = min(beat+int(0.12*SR), n)
            if end > beat:
                out[beat:end] = drum_hit(0.12)[:end-beat]
        elif bi == 3:  # tek (beat 4)
            start = beat + int(0.9*BEAT_SAMPLES)
            end = min(start+int(0.07*SR), n)
            if start < n:
                out[start:end] = drum_hit(0.07, low_tone=False)[:end-start]
        beat += BEAT_SAMPLES
    return out * 0.10


def oud_melody(scale, total_dur, density=0.35):
    """Plucked oud-like melody using Karplus-Strong."""
    n = int(total_dur*SR)
    out = np.zeros(n)
    step = int(2*60/BPM*SR)  # half notes
    for i in range(0, n, step):
        if np.random.random() > density or i+100 >= n:
            continue
        degree = np.random.choice(7, p=[0.30,0.10,0.12,0.10,0.20,0.08,0.10])
        f = scale[degree]
        note_len = min(int((0.5+np.random.random()*0.4)*step), n-i)
        if note_len < 100:
            continue
        pluck = karplus_strong(f, note_len/SR)
        out[i:i+len(pluck)] += pluck * 0.7
        delay = int(0.03*SR)
        if i+delay+len(pluck) <= n:
            out[i+delay:i+delay+len(pluck)] += pluck * 0.3
    return out


def ney_melody(scale, total_dur, density=0.12):
    """Ney-like breathy flute melody."""
    n = int(total_dur*SR)
    out = np.zeros(n)
    step = int(4*60/BPM*SR)  # whole note
    for i in range(0, n, step):
        if np.random.random() > density or i+100 >= n:
            continue
        degree = np.random.choice(7, p=[0.15,0.10,0.15,0.10,0.20,0.10,0.20])
        f = scale[degree]
        note_len = min(int((0.5+np.random.random()*0.4)*step), n-i)
        if note_len < 100:
            continue
        fl = fm_flute(f, note_len/SR, mod_rate=4+np.random.random()*3,
                       mod_idx=3+np.random.randint(3),
                       breath=0.3+np.random.random()*0.2)
        out[i:i+len(fl)] += fl * 0.35
    return out


def drone_layer(tonic, fifth, total_dur, amplitude=0.12):
    """Bowed drone on tonic and fifth with slow vibrato."""
    n = int(total_dur*SR)
    t = np.arange(n) / SR
    d1 = np.sin(2*np.pi*tonic*t)
    d2 = np.sin(2*np.pi*fifth*t)
    d3 = np.sin(2*np.pi*(tonic/2)*t)
    vibrato = 1.0 + 0.04*np.sin(2*np.pi*0.3*t)
    env = env_bowed(n, amplitude)
    return (d1*0.5 + d2*0.3 + d3*0.3) * vibrato * env


def generate(scale_d, duration, output_path):
    """Generate ambient music using the given scale and write to OGG."""
    print(f"  Scale: {scale_d['name']}")

    # Calculate pattern-aligned duration for seamless loop
    pat_samples = int(4 * 60 / BPM * SR)  # Maqsum pattern length in samples
    target_samples = int(duration * SR)
    # Round UP to nearest pattern boundary for seamless percussion loop
    aligned_samples = ((target_samples + pat_samples - 1) // pat_samples) * pat_samples
    if aligned_samples < pat_samples * 4:  # minimum 4 patterns
        aligned_samples = pat_samples * 4
    loop_duration = aligned_samples / SR

    master = np.zeros(aligned_samples)

    print("    Drone...")
    master += drone_layer(scale_d['tonic'], scale_d['fifth'], loop_duration, 0.10)

    print("    Oud...")
    master += oud_melody(scale_d['scale'], loop_duration, 0.35)

    print("    Ney...")
    master += ney_melody(scale_d['scale'], loop_duration, 0.12)

    print("    Maqsum...")
    master += maqsum(loop_duration)

    print("    Crossfade loop seam...")
    # Apply 80ms crossfade at the loop boundary (end into start)
    cf_len = min(int(0.080 * SR), aligned_samples // 4)
    cf_fade = np.linspace(0, 1, cf_len)
    # Fade in the start
    master[:cf_len] *= cf_fade
    # Fade out the end, blend into start
    tail = master[-cf_len:].copy()
    master[-cf_len:] *= (1 - cf_fade)
    master[:cf_len] += tail[::-1] * (1 - cf_fade)

    print("    Normalize...")
    mx = np.max(np.abs(master))
    if mx > 0.99:
        master *= 0.99 / mx

    tmp_wav = "/tmp/ambient_tmp.wav"
    os.makedirs(os.path.dirname(os.path.abspath(output_path)), exist_ok=True)

    sf.write(tmp_wav, master, SR, subtype='PCM_16')

    ret = os.system(
        f'ffmpeg -y -i "{tmp_wav}" -c:a libvorbis -b:a 128k -ar 44100 "{output_path}" 2>/dev/null'
    )
    if ret == 0:
        os.remove(tmp_wav)
    else:
        wav_path = output_path.replace('.ogg', '.wav')
        sf.write(wav_path, master, SR, subtype='PCM_16')
        print(f"  [!!] ffmpeg not available; WAV fallback at {wav_path}")

    print(f"  Written -> {output_path}")


# Cue-to-mode mapping (every existing cue name)
CUE_MAP = {
    'ambient_reach': 'bayati',  # wandering map theme -> Bayati
}


def main():
    p = argparse.ArgumentParser(description="Runewake Ambient Music Generator")
    p.add_argument('--output', default='client/content/audio/music/ambient_reach.ogg')
    p.add_argument('--duration', type=int, default=180)
    p.add_argument('--mode', choices=['bayati', 'hijaz', 'auto'], default='auto')
    args = p.parse_args()

    # Detect mode from filename if auto
    fn = os.path.basename(args.output).replace('.ogg', '').replace('.wav', '')
    req_mode = args.mode
    if req_mode == 'auto':
        for cue_name, mod in CUE_MAP.items():
            if cue_name == fn:
                req_mode = mod
                break
    if req_mode == 'auto':
        req_mode = 'bayati'  # default for map/wandering

    scale = BAYATI if req_mode == 'bayati' else HIJAZ

    print(f"Generating {args.duration}s ambient music: {scale['name']}")
    print(f"  Output: {args.output}")
    generate(scale, args.duration, args.output)
    print("Done!")


if __name__ == '__main__':
    main()