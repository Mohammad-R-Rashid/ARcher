# Synthesizes the game's sound effects as WAV files (no external assets needed).
# Run: python3 Tools/gen_audio.py <project_root>
import os, sys
import numpy as np
from scipy import signal
from scipy.io import wavfile

ROOT = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()
OUT = os.path.join(ROOT, "Assets", "Siege", "Audio")
os.makedirs(OUT, exist_ok=True)
SR = 44100
rng = np.random.default_rng(11)

def N(d): return int(round(SR * d))
def t_(d): return np.arange(N(d)) / SR
def noise(d): return rng.uniform(-1, 1, N(d))
def lp(x, f, o=2): return signal.sosfilt(signal.butter(o, min(f, SR / 2 - 100), "low", fs=SR, output="sos"), x)
def hp(x, f, o=2): return signal.sosfilt(signal.butter(o, f, "high", fs=SR, output="sos"), x)
def bp(x, lo, hi, o=2): return signal.sosfilt(signal.butter(o, [lo, hi], "band", fs=SR, output="sos"), x)
def env(d, a, dec, curve=4.0):
    t = t_(d)
    e = np.minimum(t / max(a, 1e-4), 1.0) * np.exp(-np.maximum(t - a, 0) * curve / max(dec, 1e-4))
    return e
def sweep_lp(x, f0, f1):
    """Time-varying lowpass by blockwise filtering (good enough for whooshes/roars)."""
    out = np.zeros_like(x); n = len(x); blk = 512; zi = None
    for i in range(0, n, blk):
        f = f0 + (f1 - f0) * (i / n)
        sos = signal.butter(2, max(40, min(f, SR / 2 - 200)), "low", fs=SR, output="sos")
        if zi is None: zi = np.zeros((sos.shape[0], 2))
        out[i:i + blk], zi = signal.sosfilt(sos, x[i:i + blk], zi=zi)
    return out
def norm(x, peak=0.9):
    m = np.max(np.abs(x)) or 1
    return x / m * peak
def pad(x, d): return np.concatenate([x, np.zeros(N(d))])
def mix(*parts):
    n = max(len(p) for p in parts); out = np.zeros(n)
    for p in parts: out[:len(p)] += p
    return out
def at(x, delay): return np.concatenate([np.zeros(N(delay)), x])
def save(name, x, peak=0.9):
    x = norm(x, peak)
    fade = min(len(x), 400)
    x[-fade:] *= np.linspace(1, 0, fade)
    wavfile.write(os.path.join(OUT, name + ".wav"), SR, (x * 32767).astype(np.int16))
def loopable(x, xf=0.25):
    n = N(xf); a = x[:n]; b = x[-n:]
    w = np.linspace(0, 1, n)
    body = x[n:-n].copy()
    return np.concatenate([b * (1 - w) + a * w, body])

def thump(freq=55, d=0.6, drop=0.5):
    t = t_(d)
    f = freq * (1 + drop * np.exp(-t * 18))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * env(d, 0.002, d * 0.6, 5)

def crackles(d, rate, decay=0.004, color=4000):
    x = np.zeros(N(d))
    n = int(rate * d)
    for p in rng.integers(0, len(x) - 2000, n):
        L = N(rng.uniform(0.002, 0.012))
        x[p:p + L] += noise(L / SR) * np.exp(-np.arange(L) / (SR * decay)) * rng.uniform(0.2, 1)
    return bp(x, 300, color)

# ---- bow
d = 1.4; t = t_(d)
creak = np.zeros_like(t)
for i, p in enumerate(np.linspace(0.05, 1.25, 14)):
    s = int(p * SR); L = N(0.05)
    tt = np.arange(L) / SR
    creak[s:s + L] += np.sin(2 * np.pi * (180 + i * 18) * tt) * np.exp(-tt * 60) * (0.4 + 0.04 * i)
creak = bp(creak + bp(noise(d), 400, 1600) * 0.05 * np.linspace(0.3, 1, len(t)), 120, 2400)
save("bow_draw", creak, 0.6)

d = 0.6; t = t_(d)
twang = np.sin(2 * np.pi * 110 * t) * np.exp(-t * 9) + 0.5 * np.sin(2 * np.pi * 220 * t * (1 - 0.02 * t)) * np.exp(-t * 14)
twang += 0.4 * np.sin(2 * np.pi * 333 * t) * np.exp(-t * 20)
snap = hp(noise(0.03), 1500) * env(0.03, 0.0005, 0.02, 6)
whip = sweep_lp(noise(d), 3000, 600) * env(d, 0.005, 0.3, 4) * 0.4
save("bow_release", mix(twang * 0.8, snap, whip), 0.85)

d = 0.7
save("arrow_whoosh", bp(noise(d), 500, 3000) * np.sin(np.linspace(0, np.pi, N(d))) ** 2, 0.5)

d = 0.4; t = t_(d)
thunk = np.sin(2 * np.pi * 190 * t * (1 - 0.3 * t)) * np.exp(-t * 28) + bp(noise(d), 600, 2500) * np.exp(-t * 45) * 0.6
quiver = np.sin(2 * np.pi * 34 * t) * np.sin(2 * np.pi * 640 * t) * np.exp(-t * 9) * 0.25
save("arrow_hit_wood", thunk + quiver, 0.9)

d = 0.3; t = t_(d)
save("arrow_hit_stone", hp(noise(d), 2000) * np.exp(-t * 60) + np.sin(2 * np.pi * 1900 * t) * np.exp(-t * 50) * 0.3, 0.7)

# ---- catapult
d = 1.0
ratchet = np.zeros(N(d))
for p in np.arange(0.02, d - 0.05, 0.085):
    s = int(p * SR); L = N(0.03)
    ratchet[s:s + L] += hp(noise(0.03), 900) * np.exp(-np.arange(L) / (SR * 0.006))
wood_groan = bp(noise(d), 150, 500) * (0.3 + 0.2 * np.sin(2 * np.pi * 3 * t_(d)))
save("catapult_crank", ratchet + wood_groan * 0.4, 0.7)

d = 1.4; t = t_(d)
swoosh = sweep_lp(noise(d), 300, 2400) * env(d, 0.15, 0.6, 4)
release = mix(thump(70, 0.5, 1.2), hp(noise(0.05), 800) * env(0.05, 0.001, 0.04))
save("catapult_launch", mix(release, swoosh * 0.6), 0.9)

d = 1.2; t = t_(d)
slam = thump(48, d, 0.8) * 1.2 + bp(noise(d), 80, 900) * env(d, 0.001, 0.25, 5) + \
       np.sin(2 * np.pi * 95 * t) * np.sin(2 * np.pi * 7 * t) * np.exp(-t * 5) * 0.3
save("catapult_slam", slam, 0.95)

# ---- fireball
d = 3.0; t = t_(d)
roar = lp(noise(d), 700) * (0.7 + 0.3 * lp(np.abs(noise(d)), 6) * 6) + crackles(d, 30, 0.004, 5000) * 0.35
save("fireball_loop", loopable(roar), 0.7)

d = 1.6; t = t_(d)
dop = np.exp(-((t - 0.8) / 0.32) ** 2)
fly = sweep_lp(noise(d), 4000, 400) * dop + lp(noise(d), 300) * dop * 1.2 + crackles(d, 40, 0.003, 6000) * dop * 0.5
save("fireball_flyby", fly, 0.9)

def explosion(d, low=40, body=1.0, debris=1.0):
    t = t_(d)
    boom = thump(low, d, 1.5) * 1.3
    blast = lp(noise(d), 2200) * env(d, 0.002, d * 0.35, 5) * body
    rumble = lp(noise(d), 160) * env(d, 0.05, d * 0.9, 3) * 1.6
    deb = crackles(d, 70, 0.006, 4000) * np.linspace(1, 0, len(t)) ** 1.5 * debris
    return mix(boom, blast, rumble, deb * 0.6)

save("explosion", explosion(2.4), 0.95)
save("explosion_tnt", mix(explosion(3.2, 33, 1.4, 1.5), at(explosion(1.5, 50, 0.6, 0.5) * 0.5, 0.18)), 0.98)

d = 2.2; t = t_(d)
crumble = np.zeros(N(d))
for p in rng.uniform(0, 1.6, 90):
    L = N(rng.uniform(0.02, 0.09)); s = int(p * SR)
    f = rng.uniform(250, 1400)
    tt = np.arange(L) / SR
    crumble[s:s + L] += (bp(noise(L / SR), f * 0.6, f * 1.6) + np.sin(2 * np.pi * f * 0.3 * tt) * 0.3) * np.exp(-tt * 70) * rng.uniform(0.2, 1)
crumble = mix(crumble, lp(noise(d), 120) * env(d, 0.05, 1.4, 3) * 1.2, thump(45, 0.8, 0.6) * 0.8)
save("stone_crumble", crumble, 0.9)

d = 1.4; t = t_(d)
crack = np.zeros(N(d))
for p in [0, 0.03, 0.07, 0.15, 0.26, 0.4]:
    L = N(0.09); s = int(p * SR); tt = np.arange(L) / SR
    crack[s:s + L] += bp(noise(0.09), 300, 3500) * np.exp(-tt * 35)
crack = mix(crack, thump(80, 0.6, 0.6) * 0.8, crackles(d, 50, 0.005, 3000) * np.linspace(1, 0, len(t)))
save("wood_break", crack, 0.9)

d = 4.0
fire = lp(noise(d), 500) * 0.35 + crackles(d, 18, 0.003, 6000) * 1.2
save("fire_crackle", loopable(fire), 0.6)

d = 0.5; t = t_(d)
save("thud", mix(thump(70, d, 0.6), lp(noise(d), 600) * env(d, 0.001, 0.12, 5) * 0.6), 0.9)

# ---- music stings
def brass(freq, d, vib=5.0):
    t = t_(d)
    f = freq * (1 + 0.006 * np.sin(2 * np.pi * vib * t))
    ph = 2 * np.pi * np.cumsum(f) / SR
    saw = sum(np.sin(k * ph) / k for k in range(1, 9))
    e = np.minimum(t / 0.06, 1) * np.minimum((d - t) / 0.12, 1)
    return sweep_lp(saw, 900, 2600) * np.clip(e, 0, 1)

def notes(seq, gap=0.0):
    out = np.zeros(0)
    for f, d in seq:
        out = np.concatenate([out, brass(f, d), np.zeros(N(gap))])
    return out

C4, D4, E4, F4, G4, A4, B4, C5, D5, E5, G5 = 261.6, 293.7, 329.6, 349.2, 392.0, 440.0, 493.9, 523.3, 587.3, 659.3, 784.0
horn = mix(brass(G4 / 2, 1.6, 4), brass(D4 / 2 * 1.5, 1.6, 4) * 0.5) * 0.7
horn = mix(horn, at(mix(brass(C4 / 2, 2.0, 4), brass(G4 / 2, 2.0, 4) * 0.6), 1.2))
save("war_horn", horn, 0.85)
vic = mix(notes([(G4, 0.18), (C5, 0.18), (E5, 0.18), (G5, 0.7)], 0.03),
          at(mix(brass(C4, 1.4), brass(E4, 1.4), brass(G4, 1.4)) * 0.6, 0.66))
vic = mix(vic, at(crackles(1.2, 8, 0.02, 2500) * 0.0, 0))
save("victory", vic, 0.85)
lose = mix(notes([(A4 / 2, 0.6), (F4 / 2, 0.6), (D4 / 2, 1.6)], 0.05), lp(noise(2.9), 90) * 0.4)
save("defeat", lose, 0.8)

d = 0.6; t = t_(d)
coin = np.zeros(N(d))
for i, p in enumerate([0, 0.05, 0.11, 0.18, 0.24]):
    s = int(p * SR); L = N(0.3); tt = np.arange(L) / SR
    f = 2600 + 400 * (i % 3)
    seg = (np.sin(2 * np.pi * f * tt) + 0.6 * np.sin(2 * np.pi * f * 1.5 * tt)) * np.exp(-tt * 14)
    coin[s:s + L] += seg[:len(coin) - s]
save("coins", coin, 0.6)

d = 0.25; t = t_(d)
save("ui_tick", np.sin(2 * np.pi * 880 * t) * np.exp(-t * 30) + 0.5 * np.sin(2 * np.pi * 1320 * t) * np.exp(-t * 40), 0.5)

d = 0.5; t = t_(d)
hit = np.sin(2 * np.pi * 1400 * t) * np.exp(-t * 25) + np.sin(2 * np.pi * 2100 * t) * np.exp(-t * 30) * 0.6
save("hit_marker", hit, 0.5)

d = 0.6; t = t_(d)
grunt_f = 140 * (1 - 0.25 * t)
ph = 2 * np.pi * np.cumsum(grunt_f) / SR
voice = sum(np.sin(k * ph) / k for k in range(1, 12))
voice = bp(voice, 300, 900) * 0.8 + bp(voice, 1000, 1400) * 0.4
save("knight_grunt", voice * env(d, 0.02, 0.25, 4), 0.7)
print("audio ->", OUT, len(os.listdir(OUT)), "files")
