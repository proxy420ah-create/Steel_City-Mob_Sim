#!/usr/bin/env python3
"""Analyze a reference SFX (mp3/wav) and emit a .stsound recipe that
approximates its structure — strike tone, whine glide, ring partials, tail.

Usage:  python analyze_sfx.py <input.mp3|input.wav> [out_name]

Needs ffmpeg on PATH (mp3 only) and numpy. The recipe is a STARTING POINT —
load it in audio_studio.html, then dial it in by ear. Structure over fidelity:
we fit exciter + resonator + envelope + motion, not samples.

v2 fits: per-layer amps (band-energy attribution), vibrato from glide
residuals (periodicity-gated: MAD-clipped + autocorr ≥0.30 — glide curvature
and tracker noise fail, so aperiodic sources emit no vib), echo repeat from
envelope autocorrelation, per-partial decays, drive from source crest factor.
"""
import json, math, os, subprocess, sys, tempfile
import numpy as np

RATE = 44100
HERE = os.path.dirname(os.path.abspath(__file__))
SOUNDS = os.path.normpath(os.path.join(HERE, "..", "Assets", "StreamingAssets", "sounds", "ricochet"))

def decode(path):
    if path.lower().endswith(".mp3"):
        tmp = tempfile.NamedTemporaryFile(suffix=".wav", delete=False)
        tmp.close()
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", path,
                        "-ac", "1", "-ar", str(RATE), tmp.name], check=True)
        path = tmp.name
    import wave
    with wave.open(path, "rb") as w:
        n = w.getnframes()
        raw = w.readframes(n)
        x = np.frombuffer(raw, dtype=np.int16).astype(np.float64) / 32768.0
        if w.getnchannels() > 1:
            x = x.reshape(-1, w.getnchannels()).mean(axis=1)
    return x

def rms_env(x, hop=220):                       # ~5ms hops
    n = len(x) // hop
    return np.sqrt((x[:n*hop].reshape(n, hop) ** 2).mean(axis=1) + 1e-12)

def fit_decay(env, i0, hop_sec):
    """exp decay fit on the post-peak slope (log domain) -> (decay, peak_idx)."""
    peak = i0 + int(np.argmax(env[i0:i0 + int(0.06 / hop_sec)]))
    pk = env[peak]
    seg = []
    for i in range(peak, len(env)):
        if env[i] < pk * 0.05: break
        seg.append((i * hop_sec, math.log(env[i] / pk + 1e-9)))
    if len(seg) < 8: return 20.0, peak
    t = np.array([s[0] - seg[0][0] for s in seg])
    y = np.array([s[1] for s in seg])
    d = -np.polyfit(t, y, 1)[0]
    return float(np.clip(d, 1.0, 200.0)), peak

def stft(x, i0, nfft=2048, hop=512):
    win = np.hanning(nfft)
    frames = []
    for s in range(i0, len(x) - nfft, hop):
        frames.append(np.abs(np.fft.rfft(x[s:s+nfft] * win)))
    freqs = np.fft.rfftfreq(nfft, 1 / RATE)
    return np.array(frames), freqs, hop / RATE

def peak_track(F, freqs, lo=200, hi=14000):
    """Per-frame dominant freq + a second peak outside +-15% of the first."""
    band = (freqs >= lo) & (freqs <= hi)
    t1, t2 = [], []
    for f in F:
        mag = np.where(band, f, 0)
        i1 = int(np.argmax(mag)); p1 = (freqs[i1], f[i1])
        keep = band & ((freqs < p1[0] * 0.85) | (freqs > p1[0] * 1.18))
        mag2 = np.where(keep, f, 0)
        i2 = int(np.argmax(mag2)); p2 = (freqs[i2], f[i2])
        t1.append(p1); t2.append(p2)
    return t1, t2

def fit_glide(track, dt, amp_floor):
    """Longest COHERENT run (neighbors < half-octave apart) -> exp fit over it.
    The chaotic onset breaks coherence, so the whine segment wins.
    Returns (startHz, endHz, r2, t_start, t_end, run_idx) or None."""
    runs, cur, prev = [], [], None
    for i, (fr, a) in enumerate(track):
        ok = fr > 0 and a > amp_floor and (prev is None or abs(math.log2(fr / prev)) < 0.6)
        if ok: cur.append((i, i * dt, fr))
        else:
            if len(cur) > len(runs): runs = cur
            cur = [(i, i * dt, fr)] if fr > 0 and a > amp_floor else []
        if fr > 0 and a > amp_floor: prev = fr
    if len(cur) > len(runs): runs = cur
    if len(runs) < 6: return None
    t = np.array([p[1] - runs[0][1] for p in runs])
    lf = np.log([p[2] for p in runs])
    c = np.polyfit(t, lf, 1)
    pred = np.polyval(c, t)
    ss_res = float(((lf - pred) ** 2).sum()); ss_tot = float(((lf - lf.mean()) ** 2).sum())
    r2 = 1 - ss_res / ss_tot if ss_tot > 0 else 0
    idx = [p[0] for p in runs]
    return (float(math.exp(pred[0])), float(math.exp(pred[-1])), r2,
            runs[0][1], runs[-1][1], idx, lf - pred)

def top_partials(F, freqs, exclude_hz, lo=300, hi=9000, count=4):
    """Mean spectrum peaks away from the glide track — the ring body."""
    mean = F.mean(axis=0)
    out, guard = [], 0
    cand = mean.copy()
    cand[freqs < lo] = 0; cand[freqs > hi] = 0
    for ex in exclude_hz:
        cand[np.abs(freqs - ex) < ex * 0.10] = 0
    while len(out) < count and guard < 20:
        guard += 1
        i = int(np.argmax(cand))
        if cand[i] <= 0: break
        out.append((float(freqs[i]), float(mean[i])))
        cand[np.abs(freqs - freqs[i]) < freqs[i] * 0.08] = 0
    mx = out[0][1] if out else 1.0
    return [(round(h), round(a / mx * 0.4, 3)) for h, a in out]

def band_energy(F, freqs, center, tol=0.12, frames=None):
    band = (freqs >= center * (1 - tol)) & (freqs <= center * (1 + tol))
    X = F[frames] if frames is not None else F
    return float(X[:, band].mean())

def track_energy(F, freqs, track, frames):
    """Energy inside the +-12% band following the peak track, per frame."""
    if not frames: return 0.0
    tot = 0.0
    for i in frames:
        fr, _ = track[i]
        if fr <= 0: continue
        band = (freqs >= fr * 0.88) & (freqs <= fr * 1.12)
        tot += float(F[i, band].sum())
    return tot / len(frames)

def fit_harmonics(F, freqs, track, frames):
    """Energy at 2f/3f along the glide track -> (harmN, harmAmp) or None.
    A lone sine = test tone; real howls carry overtones."""
    if not frames: return None
    e1 = e2 = e3 = 0.0
    for i in frames:
        fr, _ = track[i]
        if fr <= 0: continue
        for h, acc in ((1, 'e1'), (2, 'e2'), (3, 'e3')):
            f2 = fr * h
            if f2 > freqs[-1] * 0.95: continue
            band = (freqs >= f2 * 0.92) & (freqs <= f2 * 1.08)
            e = float(F[i, band].sum())
            if acc == 'e1': e1 += e
            elif acc == 'e2': e2 += e
            else: e3 += e
    if e1 <= 0: return None
    r2_, r3_ = e2 / e1, e3 / e1
    harmN = 0
    if r2_ > 0.12: harmN = 1
    if r2_ > 0.12 and r3_ > 0.08: harmN = 2
    if r2_ > 0.25 and r3_ > 0.15: harmN = 3
    if harmN == 0: return None
    return harmN, round(float(np.clip(math.sqrt(r2_), 0.2, 0.85)), 2)

def fit_vib(resid_lf, dt):
    """Emit vib only if the residual wobble is REAL and periodic:
    clip tracker outliers (a one-frame +268c bin-hop is not wobble),
    then the dominant 3-20Hz bin must hold under autocorrelation —
    otherwise the residual is glide curvature or tracker noise, not vibrato."""
    if resid_lf is None or len(resid_lf) < 12: return None
    r = resid_lf.copy()
    med = np.median(r); mad = np.median(np.abs(r - med)) + 1e-9
    r = np.clip(r, med - 4 * mad, med + 4 * mad)        # bin-hop outliers out
    r -= r.mean()
    if r.std() * 1731.23 < 40: return None              # <40c rms = inaudible
    spec = np.abs(np.fft.rfft(r * np.hanning(len(r))))
    frs = np.fft.rfftfreq(len(r), dt)
    band = (frs >= 3) & (frs <= 20)
    if not band.any(): return None
    rate = float(frs[band][np.argmax(spec[band])])
    lag = max(1, round(1.0 / rate / dt))
    if lag >= len(r): return None
    ac = np.corrcoef(r[:-lag], r[lag:])[0, 1]           # periodicity gate
    if ac < 0.30: return None                           # curvature/noise, not wobble
    depth = float(np.sqrt(2) * r.std() * 1731.23)
    return round(rate, 1), int(np.clip(depth, 0, 400))

def fit_echo(env, hop_sec):
    """Envelope autocorrelation -> repeat period 30-250ms, or None."""
    e = env - env.mean()
    ac = np.correlate(e, e, mode="full")[len(e) - 1:]
    ac = ac / (ac[0] + 1e-9)
    lo, hi = int(0.03 / hop_sec), int(0.25 / hop_sec)
    seg = ac[lo:hi]
    if len(seg) < 4: return None
    i = int(np.argmax(seg))
    if seg[i] < 0.35: return None
    return round((lo + i) * hop_sec, 3), float(np.clip(seg[i], 0.2, 0.6))

def partial_decays(F, freqs, p_hz, dt):
    """Per-partial amplitude envelope -> exp decay fit -> energy-weighted mean."""
    decays, weights = [], []
    for hz, amp in p_hz:
        band = (freqs >= hz * 0.94) & (freqs <= hz * 1.06)
        envp = F[:, band].mean(axis=1) + 1e-9
        pk = envp.max()
        i0 = int(np.argmax(envp))
        seg = [(i * dt, math.log(envp[i] / pk)) for i in range(i0, len(envp)) if envp[i] > pk * 0.15]
        if len(seg) < 8: continue
        t = np.array([s[0] - seg[0][0] for s in seg]); y = np.array([s[1] for s in seg])
        d = float(np.clip(-np.polyfit(t, y, 1)[0], 1.0, 120.0))
        decays.append(d); weights.append(amp)
    if not decays: return None
    return round(float(np.average(decays, weights=weights)), 1)

def main():
    src = sys.argv[1]
    name = sys.argv[2] if len(sys.argv) > 2 else os.path.splitext(os.path.basename(src))[0]
    x = decode(src)

    env = rms_env(x); hop_sec = 220 / RATE
    pk = env.max()
    i0 = int(np.argmax(env > pk * 0.02))
    end = len(env) - 1 - int(np.argmax(env[::-1] > pk * 0.02))
    dur = round(max(0.25, (end - i0) * hop_sec + 0.05), 2)
    decay, ipeak = fit_decay(env, i0, hop_sec)
    s0 = i0 * 220

    # attack: 45ms spectrum -> strike band + low thump
    aseg = x[s0:s0 + 2048] * np.hanning(2048)
    amag = np.abs(np.fft.rfft(aseg)); af = np.fft.rfftfreq(2048, 1 / RATE)
    band = (af > 500) & (af < 8000)
    tone_hz = int(np.clip(af[np.argmax(np.where(band, amag, 0))], 500, 8000))
    lob = (af > 120) & (af < 500)
    thump_hz = float(af[np.argmax(np.where(lob, amag, 0))])
    thump_amp = float(np.where(lob, amag, 0).max()) / float(amag.max() + 1e-9)

    F, freqs, dt = stft(x, s0)
    t1, t2 = peak_track(F, freqs)
    a_floor = np.median(F[:, (freqs > 200) & (freqs < 14000)].max(axis=1)) * 0.25
    g1 = fit_glide(t1, dt, a_floor)
    g2 = fit_glide(t2, dt, a_floor)

    excl = [g1[0], g1[1]] if g1 else []
    partials = top_partials(F, freqs, excl)
    p_decay = partial_decays(F, freqs, partials, dt) if partials else None

    # ---- amplitude attribution: how much of the source each component owns
    tot = float(F[:, (freqs > 150) & (freqs < 15000)].mean()) + 1e-9
    onset_frames = list(range(0, min(4, F.shape[0])))
    e_attack = band_energy(F, freqs, tone_hz, 0.25, onset_frames) * 1.5
    e_sw1 = track_energy(F, freqs, t1, g1[5]) if g1 else 0.0
    e_sw2 = track_energy(F, freqs, t2, g2[5]) if g2 else 0.0
    e_pts = sum(band_energy(F, freqs, h, 0.07) for h, _ in partials)
    e_noise = float(F[:, freqs > 8000].mean())

    def amp(frac, lo, hi):
        return round(float(np.clip(1.4 * math.sqrt(frac / tot), lo, hi)), 2)
    amps = {
        "attack": amp(e_attack, 0.25, 1.10),
        "sw1":    amp(e_sw1,    0.10, 0.60),
        "sw2":    amp(e_sw2,    0.08, 0.40),
        "pts":    amp(e_pts,    0.20, 0.80),
        "noise":  amp(e_noise * 1.4, 0.03, 0.30),
    }

    # vibrato from the dominant glide's residual (real wobble, not a default)
    vib = fit_vib(g1[6], dt) if g1 else None
    # harmonic content of the glide — sine vs howl (the "doesn't sound real" fix)
    harm = fit_harmonics(F, freqs, t1, g1[5]) if g1 else None
    # echo repeat from envelope autocorrelation
    echo_fit = fit_echo(env[i0:end], hop_sec)
    # drive from source crest factor (compressed/bricky -> more saturation)
    crest = float(np.abs(x[s0:s0 + int(dur * RATE)]).max() /
                  (np.sqrt((x[s0:s0 + int(dur * RATE)] ** 2).mean()) + 1e-9))
    drive = round(float(np.clip(7.0 / crest, 1.0, 3.0)), 1)
    nz_lp = int(np.clip(freqs[np.searchsorted(np.cumsum(F.mean(axis=0)),
                        0.97 * F.mean(axis=0).sum())], 1500, 18000))

    recipe = {
        "name": name, "version": 4, "sampleRate": RATE, "duration": dur,
        "seed": 12345, "envelope": {"mode": "exp"}, "gain": 0.8, "drive": drive,
        "phaseStep": 1.7, "rootNote": 60, "pitch": 0, "coupleDecay": True,
        "tailFade": 0.08, "masterAttack": 0, "masterRelease": round(dur * 0.3, 2),
        "eq": [{"type": "lowshelf", "hz": 250, "gainDb": 0, "q": 0.7, "on": False},
               {"type": "peaking", "hz": 900, "gainDb": 0, "q": 1.0, "on": False},
               {"type": "peaking", "hz": 3200, "gainDb": 0, "q": 1.0, "on": False},
               {"type": "highshelf", "hz": 8000, "gainDb": 0, "q": 0.7, "on": False}],
        "echo": ({"on": True, "time": echo_fit[0], "feedback": echo_fit[1], "mix": 0.22}
                 if echo_fit else {"on": False, "time": 0.09, "feedback": 0.35, "mix": 0.25}),
        "layers": [],
    }
    L = recipe["layers"]
    L.append({"type": "attack", "amp": amps["attack"],
              "decay": round(min(140, decay * 6), 1), "tone": tone_hz, "toneQ": 1.4})
    if thump_amp > 0.5:
        L.append({"type": "attack", "amp": round(0.35 * thump_amp, 2), "decay": 55,
                  "src": "chirp", "startHz": int(min(1200, thump_hz * 3)), "endHz": int(thump_hz)})
    if g1:
        L.append({"type": "sweep", "amp": amps["sw1"], "decay": round(decay * 0.55, 1),
                  "startHz": int(g1[0]), "endHz": int(g1[1]), "curve": "exp",
                  **({"vibRate": vib[0], "vibDepth": vib[1]} if vib else {}),
                  **({"harmN": harm[0], "harmAmp": harm[1]} if harm else {})})
    if g2 and g1 and abs(math.log(g2[0] / g1[0])) > 0.25:
        L.append({"type": "sweep", "amp": amps["sw2"], "decay": round(decay * 0.6, 1),
                  "startHz": int(g2[0]), "endHz": int(g2[1]), "curve": "exp",
                  **({"harmN": max(1, harm[0] - 1), "harmAmp": harm[1]} if harm else {})})
    if partials:
        L.append({"type": "partials", "amp": amps["pts"],
                  "decay": p_decay or round(decay * 0.7, 1),
                  "partials": [{"hz": h, "amp": a} for h, a in partials]})
    L.append({"type": "noiseTail", "amp": amps["noise"],
              "decay": round(decay * 0.8, 1), "lowpass": nz_lp})

    out = os.path.join(SOUNDS, name + ".stsound")
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(recipe, f, indent=1); f.write("\n")

    print(f"== {name} ==")
    print(f"  duration {dur}s  onset@{i0*hop_sec:.3f}s  peak@{ipeak*hop_sec:.3f}s  decay {decay:.1f}  crest {crest:.1f} -> drive {drive}")
    print(f"  attack tone ~{tone_hz}Hz  thump {thump_hz:.0f}Hz @{thump_amp:.2f} rel")
    if g1: print(f"  glide1 {int(g1[0])} -> {int(g1[1])} Hz  (r2={g1[2]:.2f}, {g1[3]:.2f}-{g1[4]:.2f}s)"
                 + (f"  vib {vib[0]}Hz/{vib[1]}c" if vib else "  no vib")
                 + (f"  harms {harm[0]}@{harm[1]}" if harm else "  pure sine"))
    if g2: print(f"  glide2 {int(g2[0])} -> {int(g2[1])} Hz  (r2={g2[2]:.2f})")
    print(f"  partials {partials}  decay {p_decay}")
    print(f"  amps {amps}  echo {echo_fit}")
    print(f"wrote {out}")

if __name__ == "__main__":
    main()
