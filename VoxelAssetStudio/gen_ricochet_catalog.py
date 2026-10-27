#!/usr/bin/env python3
"""Generate a ricochet catalog DERIVED FROM THE MP3 FIT
(sounds/ricochet/reference/ricochet.mp3 -> ricochet_dl.stsound genome),
in TWO surface families:

  ricochet_02..13  — METAL (fitted as-is: ringing strike, harmonic whine)
  ricochet_stone_01..06 — STONE (concrete/brick/asphalt voicing: damped
                     strike, grit/debris tail, same bullet-tumble whine)

  attack  : noise strike ~2928 Hz (Q~1.4) + chirp thump 711->237 Hz
  sweep 1 : dive 2216->931 Hz, exp, harmN=3 @0.59  (the howl, NO sine vib —
            measured residual was curvature+tracker noise, not periodic wobble)
  sweep 2 : counter-RISE 1379->2394 Hz, harmN=2     (real: rising whine exists)
  partials: ring body 1227/1400/1529/1680 Hz, fast-settling
  tail    : faint broadband air ~0.15
  master  : drive 1.6 (fitted crest), masterRelease = 0.3*dur

Each variant re-voices the measured register rather than inventing new
material. Long-tail pass kept: durations 1.1-2.2s, ring-outs stretched so
the whine blooms and dies naturally. vibRate/vibDepth only appear on the
one deliberately-stylized warble entry — the real sound has none.

Writes complete .stsound recipes into sounds/ricochet/ so the studio's
Sound Library and ProceduralAudio.GetClip("sounds/ricochet/ricochet_XX.stsound")
pick them up immediately. Never touches ricochet_01 (user's WIP).

Usage:  python gen_ricochet_catalog.py
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
SOUNDS = os.path.normpath(os.path.join(HERE, "..", "Assets", "StreamingAssets", "sounds", "ricochet"))

def base(name, dur, seed, drive=1.5, gain=0.8, echo=None, tail=0.04, mrel=None):
    return {
        "name": name, "version": 4, "sampleRate": 44100, "duration": dur,
        "seed": seed, "envelope": {"mode": "exp"}, "gain": gain, "drive": drive,
        "phaseStep": 1.7, "rootNote": 60, "pitch": 0, "coupleDecay": True,
        "tailFade": tail,
        "masterAttack": 0,
        "masterRelease": mrel if mrel is not None else round(dur * 0.3, 2),
        "eq": [
            {"type": "lowshelf",  "hz": 250,  "gainDb": 0, "q": 0.7, "on": False},
            {"type": "peaking",   "hz": 900,  "gainDb": 0, "q": 1.0, "on": False},
            {"type": "peaking",   "hz": 3200, "gainDb": 0, "q": 1.0, "on": False},
            {"type": "highshelf", "hz": 8000, "gainDb": 0, "q": 0.7, "on": False}],
        "echo": echo or {"on": False, "time": 0.09, "feedback": 0.35, "mix": 0.25},
        "layers": [],
    }

def atk(amp, decay, tone=0, q=1.0, cutoff=0, src=None, startHz=0, endHz=0, crack=0):
    L = {"type": "attack", "amp": amp, "decay": decay}
    if src: L["src"] = src                         # exciter: noise|impulse|chirp (absent = noise)
    if src == "chirp": L["startHz"], L["endHz"] = startHz, endHz   # pitch-drop range
    if tone > 0: L["tone"], L["toneQ"] = tone, q   # resonant bandpass on the click (0 = white)
    if cutoff > 0: L["cutoff"] = cutoff            # 1-pole LP brightness, after tone (0 = off)
    if crack > 0: L["crack"] = crack               # fracture texture in first ~35ms (0 = smooth)
    return L
def sw(amp, decay, s, e, curve="exp", vr=0, vd=0, harmN=0, harmAmp=0.5,
       amRate=0, amDepth=0):
    L = {"type": "sweep", "amp": amp, "decay": decay,
         "startHz": s, "endHz": e, "curve": curve}
    if vr or vd: L["vibRate"], L["vibDepth"] = vr, vd
    if harmN: L["harmN"], L["harmAmp"] = harmN, harmAmp  # overtones riding the glide (the howl)
    if amRate or amDepth: L["amRate"], L["amDepth"] = amRate, amDepth  # loudness tumble pulse
    return L
def pts(amp, decay, hz_amp):    return {"type": "partials", "amp": amp, "decay": decay,
                                        "partials": [{"hz": h, "amp": a} for h, a in hz_amp]}
def tail(amp, decay, lp):       return {"type": "noiseTail", "amp": amp, "decay": decay,
                                        "lowpass": lp}
def echo(t, fb, mix):           return {"on": True, "time": t, "feedback": fb, "mix": mix}
def thump(amp, s=711, e=237):   return atk(amp, 55, src="chirp", startHz=s, endHz=e, cutoff=3000)
# STONE strikes — the FOOTSTEP PRESET VERBATIM as the ricochet striker.
# audio_studio.html footstep = {attack 0.40/90} + {noiseTail 0.35/30/lp800}:
# a blunt-thing-scuffs-pavement contact with zero resonance. scale=1.0 is the
# literal swap; scale>1 is the same voice at ballistic amplitude (amp only).
def foot(scale=1.0):
    return [
        {"type": "attack",    "amp": round(0.40 * scale, 2), "decay": 90},
        {"type": "noiseTail", "amp": round(0.35 * scale, 2), "decay": 30, "lowpass": 800},
    ]

CATALOG = [
    # 02 — THE MEASURED FAMILY: near-clone of the mp3 fit, tail stretched.
    # Strike 2928 + thump 237, dive 2216->931 (3 harmonics), rise 1379->2394.
    dict(id="02", dur=1.6, seed=301, drive=1.6, tail=0.08, echo=echo(0.03, 0.30, 0.15), layers=[
        atk(0.60, 90, tone=2928, q=1.4),
        thump(0.35),
        sw(0.45, 4.5, 2216, 931, harmN=3, harmAmp=0.59),
        sw(0.28, 5.5, 1379, 2394, harmN=2, harmAmp=0.59),
        pts(0.42, 7, [(1227, .40), (1680, .39), (1400, .38), (1529, .34)]),
        tail(0.15, 10, 6500)]),
    # 03 — heavy pwang: fitted register down ~40%, bigger thump, slower fall
    dict(id="03", dur=1.9, seed=302, drive=1.7, tail=0.10, echo=echo(0.04, 0.35, 0.18), layers=[
        atk(0.65, 80, tone=1950, q=1.3),
        thump(0.45, s=560, e=170),
        sw(0.48, 4, 1550, 640, harmN=3, harmAmp=0.55),
        sw(0.26, 5, 960, 1600, harmN=2, harmAmp=0.50),
        pts(0.48, 8, [(860, .45), (980, .40), (1070, .35), (1180, .28)]),
        tail(0.15, 11, 4800)]),
    # 04 — long dive: widest measured-style fall, slowest glide, the movie arc
    dict(id="04", dur=2.2, seed=303, drive=1.5, tail=0.12, echo=echo(0.05, 0.40, 0.20), layers=[
        atk(0.55, 100, tone=2600, q=1.2),
        thump(0.30),
        sw(0.42, 3.5, 2600, 650, harmN=3, harmAmp=0.60),
        sw(0.24, 5, 1500, 2200, harmN=2, harmAmp=0.50),
        pts(0.40, 7, [(1300, .38), (1500, .36), (1700, .30)]),
        tail(0.14, 12, 6000)]),
    # 05 — rise-forward: the counter-whine becomes the lead ("peww" character)
    dict(id="05", dur=1.5, seed=304, drive=1.6, tail=0.08, echo=echo(0.04, 0.32, 0.16), layers=[
        atk(0.55, 85, tone=3100, q=1.5),
        thump(0.30),
        sw(0.30, 5, 2100, 1000, harmN=2, harmAmp=0.55),
        sw(0.44, 4.5, 1200, 2650, harmN=3, harmAmp=0.55),
        pts(0.40, 7, [(1250, .38), (1550, .34), (1750, .28)]),
        tail(0.14, 10, 7000)]),
    # 06 — hard ka: impulse exciter + noise strike, brighter snap, same glide family
    dict(id="06", dur=1.4, seed=305, drive=1.8, tail=0.08, echo=echo(0.03, 0.28, 0.14), layers=[
        atk(0.50, 100, tone=3400, q=1.6),
        atk(0.30, 70, src="impulse", tone=2600, q=3.2),
        thump(0.30),
        sw(0.42, 4.5, 2400, 950, harmN=3, harmAmp=0.55),
        sw(0.24, 5.5, 1450, 2300, harmN=2, harmAmp=0.55),
        pts(0.42, 7, [(1350, .40), (1600, .35), (1800, .30)]),
        tail(0.16, 11, 7500)]),
    # 07 — the howler: whistler-length, harmN 3 pushed hot, light AM tumble
    dict(id="07", dur=2.2, seed=306, drive=1.4, tail=0.12, echo=echo(0.06, 0.45, 0.22), layers=[
        atk(0.45, 100, tone=2400, q=1.2),
        thump(0.28),
        sw(0.42, 3.5, 2350, 800, harmN=4, harmAmp=0.65, amRate=4.5, amDepth=0.28),
        sw(0.22, 5, 1400, 2100, harmN=2, harmAmp=0.55),
        pts(0.38, 6, [(1250, .36), (1500, .32), (1700, .26)]),
        tail(0.15, 12, 5500)]),
    # 08 — snap & ring: loud short strike, glide settles fast, partials carry the tail
    dict(id="08", dur=1.3, seed=307, drive=2.0, tail=0.07, echo=echo(0.035, 0.30, 0.15), layers=[
        atk(0.80, 115, tone=3600, q=1.7, cutoff=15000),
        thump(0.35),
        sw(0.40, 6, 2300, 1100, harmN=3, harmAmp=0.55),
        sw(0.20, 7, 1400, 2200, harmN=2, harmAmp=0.50),
        pts(0.55, 9, [(1300, .42), (1550, .38), (1750, .32), (1950, .24)]),
        tail(0.18, 13, 6000)]),
    # 09 — high ping: fitted register up ~60%, thin bright distant ricochet
    dict(id="09", dur=1.3, seed=308, drive=1.4, tail=0.08, echo=echo(0.03, 0.30, 0.15), layers=[
        atk(0.50, 105, tone=4700, q=1.6),
        thump(0.22, s=900, e=300),
        sw(0.36, 5, 3550, 1500, harmN=3, harmAmp=0.55),
        sw(0.22, 6, 2200, 3800, harmN=2, harmAmp=0.50),
        pts(0.36, 8, [(1950, .38), (2250, .34), (2450, .30), (2700, .22)]),
        tail(0.13, 12, 9000)]),
    # 10 — stone scrape: noise-forward, dull strike, low partials (concrete hit)
    dict(id="10", dur=1.4, seed=309, drive=1.9, tail=0.09, echo=echo(0.04, 0.35, 0.18), layers=[
        atk(0.70, 70, tone=1400, q=1.0, cutoff=4200),
        thump(0.40, s=500, e=150),
        sw(0.34, 5.5, 1800, 700, harmN=2, harmAmp=0.50),
        sw(0.20, 6.5, 1050, 1800, harmN=2, harmAmp=0.45),
        pts(0.50, 9, [(900, .42), (1050, .38), (1200, .32)]),
        tail(0.28, 12, 3000)]),
    # 11 — stylized warble: the ONE vib entry — slow deep tumble, deliberately
    # exaggerated (real source has none; this is the cartoon option)
    dict(id="11", dur=1.8, seed=310, drive=1.5, tail=0.10, echo=echo(0.05, 0.38, 0.20), layers=[
        atk(0.55, 90, tone=2800, q=1.4),
        thump(0.32),
        sw(0.46, 4.5, 2300, 850, vr=6, vd=180, harmN=3, harmAmp=0.58, amRate=5, amDepth=0.35),
        sw(0.26, 5.5, 1400, 2300, vr=7, vd=150, harmN=2, harmAmp=0.55),
        pts(0.42, 7, [(1250, .38), (1550, .34), (1750, .30)]),
        tail(0.15, 11, 6500)]),
    # 12 — twin dives: no rise — two detuned descending howls beat together
    dict(id="12", dur=1.7, seed=311, drive=1.6, tail=0.09, echo=echo(0.045, 0.38, 0.19), layers=[
        atk(0.58, 90, tone=3000, q=1.4),
        thump(0.33),
        sw(0.40, 4.5, 2250, 900, harmN=3, harmAmp=0.58),
        sw(0.30, 5, 2180, 960, harmN=3, harmAmp=0.55),
        pts(0.42, 7, [(1250, .40), (1550, .36), (1700, .30), (1900, .22)]),
        tail(0.15, 11, 6500)]),
    # 13 — kitchen sink: full fitted stack + third slow dive, longest ring
    dict(id="13", dur=2.0, seed=312, drive=1.8, tail=0.10, echo=echo(0.05, 0.45, 0.22), layers=[
        atk(0.60, 85, tone=2928, q=1.5),
        atk(0.28, 70, src="impulse", tone=2200, q=3.0),
        thump(0.38),
        sw(0.40, 4, 2216, 900, harmN=3, harmAmp=0.60, amRate=5, amDepth=0.25),
        sw(0.26, 5, 1379, 2394, harmN=2, harmAmp=0.55),
        sw(0.18, 6, 1900, 700, harmN=2, harmAmp=0.50),
        pts(0.45, 8, [(1227, .40), (1400, .38), (1529, .34), (1680, .30)]),
        tail(0.16, 12, 7000)]),

    # =====================================================================
    # STONE FAMILY — FOOTSTEP-STRIKE swap experiment. The strike (the "ka-"
    # before the whine) is the footstep preset's own two layers: noise-burst
    # scuff + dark 800Hz body, zero resonance, zero added crack/cutoff.
    #   stone_01 = LITERAL swap (amp 0.40/0.35 — the footstep unchanged);
    #   stone_02..06 = same layers scaled (amp only) — is "footstep" enough,
    #   or does a bullet need the same voice louder?
    # =====================================================================

    # stone_01 — the VERBATIM footstep strike → whine. Pure A/B test.
    dict(id="stone_01", dur=1.5, seed=401, drive=2.0, tail=0.08, echo=echo(0.05, 0.35, 0.18), layers=[
        *foot(1.0),
        sw(0.40, 4.5, 2100, 880, harmN=2, harmAmp=0.50),
        sw(0.22, 5.5, 1250, 2150, harmN=2, harmAmp=0.50),
        pts(0.14, 14, [(650, .30), (900, .22)]),
        tail(0.26, 11, 3200)]),
    # stone_02 — scaled footstep: same swap, strike amp ×1.3 (a harder step)
    dict(id="stone_02", dur=1.5, seed=402, drive=2.0, tail=0.09, echo=echo(0.04, 0.32, 0.16), layers=[
        *foot(1.3),
        sw(0.38, 5, 1900, 780, harmN=2, harmAmp=0.45),
        sw(0.20, 6, 1100, 1900, harmN=1, harmAmp=0.50),
        pts(0.10, 15, [(550, .28)]),
        tail(0.24, 12, 2400)]),
    # stone_03 — scaled footstep ×1.5 — hardest step of the set
    dict(id="stone_03", dur=1.5, seed=403, drive=2.0, tail=0.08, echo=echo(0.045, 0.35, 0.17), layers=[
        *foot(1.5),
        sw(0.40, 4.5, 2200, 900, harmN=2, harmAmp=0.52),
        sw(0.22, 5.5, 1350, 2300, harmN=2, harmAmp=0.50),
        pts(0.16, 13, [(950, .32), (1400, .24)]),
        tail(0.22, 11, 3600)]),
    # stone_04 — glancing footstep ×1.2 → the longest street whine
    dict(id="stone_04", dur=2.1, seed=404, drive=1.9, tail=0.12, echo=echo(0.06, 0.42, 0.20), layers=[
        *foot(1.2),
        sw(0.44, 3.5, 2400, 750, harmN=3, harmAmp=0.55, amRate=4.5, amDepth=0.25),
        sw(0.24, 5, 1400, 2300, harmN=2, harmAmp=0.50),
        pts(0.12, 14, [(750, .30), (1000, .24)]),
        tail(0.25, 12, 3000)]),
    # stone_05 — scuff & spall: footstep strike ×1.4 + chip grit burst + dust
    dict(id="stone_05", dur=1.6, seed=405, drive=2.1, tail=0.09, echo=echo(0.05, 0.36, 0.18), layers=[
        *foot(1.4),
        sw(0.40, 4.5, 2150, 850, harmN=2, harmAmp=0.50),
        sw(0.22, 5.5, 1280, 2200, harmN=2, harmAmp=0.50),
        pts(0.12, 14, [(800, .30)]),
        tail(0.30, 18, 2800),
        tail(0.14, 5, 1800)]),
    # stone_06 — heel-on-rail: footstep ×1.35 + quiet impulse ping (the one
    # deliberate resonance — grazed a metal edge on the skip)
    dict(id="stone_06", dur=1.6, seed=406, drive=2.0, tail=0.08, echo=echo(0.05, 0.38, 0.18), layers=[
        *foot(1.35),
        atk(0.16, 60, src="impulse", tone=2600, q=3.5),
        sw(0.42, 4.5, 2250, 880, harmN=3, harmAmp=0.55),
        sw(0.22, 5.5, 1300, 2250, harmN=2, harmAmp=0.50),
        pts(0.20, 10, [(850, .32), (1200, .28), (1600, .20)]),
        tail(0.24, 11, 3400)]),
]

os.makedirs(SOUNDS, exist_ok=True)
written = []
for c in CATALOG:
    name = f"ricochet_{c['id']}"
    r = base(name, c["dur"], c["seed"], drive=c["drive"],
             gain=c.get("gain", 0.8), echo=c.get("echo"), tail=c.get("tail", 0.04))
    r["layers"] = c["layers"]
    path = os.path.join(SOUNDS, name + ".stsound")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(r, f, indent=1)
        f.write("\n")
    written.append(name + ".stsound")

print(f"wrote {len(written)} files to {SOUNDS}")
for w in written: print("  " + w)
