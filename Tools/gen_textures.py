# Generates the FX textures (soft dot, smoke, flame, spark, ring, scorch, noise, UI glyphs).
# Run: python3 Tools/gen_textures.py <project_root>
import math, os, sys
import numpy as np
from PIL import Image

ROOT = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()
OUT = os.path.join(ROOT, "Assets", "Siege", "Textures")
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(3)

def grid(n):
    y, x = np.mgrid[0:n, 0:n].astype(np.float32)
    return (x + 0.5) / n * 2 - 1, (y + 0.5) / n * 2 - 1

def value_noise(n, cells, seed):
    r = np.random.default_rng(seed)
    g = r.random((cells + 1, cells + 1)).astype(np.float32)
    g[-1, :] = g[0, :]; g[:, -1] = g[:, 0]          # tileable
    t = np.linspace(0, cells, n, endpoint=False)
    i = t.astype(int); f = t - i
    f = f * f * (3 - 2 * f)
    a = g[np.ix_(i, i)]; b = g[np.ix_(i, i + 1)]; c = g[np.ix_(i + 1, i)]; d = g[np.ix_(i + 1, i + 1)]
    fx = f[None, :]; fy = f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy

def fbm(n, seed, octaves=5, base=4):
    v = np.zeros((n, n), np.float32); amp = 0.5; tot = 0
    for o in range(octaves):
        v += value_noise(n, base * 2 ** o, seed + o) * amp; tot += amp; amp *= 0.5
    return v / tot

def save_rgba(name, rgb, a):
    rgb = np.clip(rgb, 0, 1); a = np.clip(a, 0, 1)
    img = np.dstack([rgb[..., 0], rgb[..., 1], rgb[..., 2], a]) * 255
    Image.fromarray(img.astype(np.uint8), "RGBA").save(os.path.join(OUT, name))

def white(n):
    return np.ones((n, n, 3), np.float32)

N = 128
x, y = grid(N)
r = np.sqrt(x * x + y * y)

# Soft glow dot
save_rgba("fx_dot.png", white(N), np.clip(1 - r, 0, 1) ** 2.2)

# Smoke puffs: 2x2 sheet of billowy noise blobs
S = 256
sheet = np.zeros((S, S), np.float32)
for k in range(4):
    xs, ys = grid(S // 2)
    rr = np.sqrt(xs * xs + ys * ys)
    nz = fbm(S // 2, 10 + k, 5, 3)
    blob = np.clip(1 - rr * (1.1 + (nz - 0.5) * 1.4), 0, 1) ** 1.3
    blob *= 0.55 + 0.6 * nz
    oy, ox = (k // 2) * (S // 2), (k % 2) * (S // 2)
    sheet[oy:oy + S // 2, ox:ox + S // 2] = blob
shade = 0.75 + 0.25 * np.clip(sheet, 0, 1)
save_rgba("fx_smoke.png", np.dstack([shade] * 3), sheet)

# Flame tongue: 2x2 sheet, hot core at the bottom fading to wispy tips
sheet_a = np.zeros((S, S), np.float32)
for k in range(4):
    xs, ys = grid(S // 2)
    nz = fbm(S // 2, 30 + k, 4, 4)
    yy = (ys + 1) / 2                                  # 0 top .. 1 bottom
    width = 0.15 + 0.55 * yy ** 0.7
    body = np.clip(1 - np.abs(xs + (nz - 0.5) * 0.5 * (1 - yy)) / width, 0, 1)
    body *= np.clip(yy * 1.6 - 0.15 + (nz - 0.5) * 0.6, 0, 1) * np.clip((1 - yy) * 6, 0, 1)
    oy, ox = (k // 2) * (S // 2), (k % 2) * (S // 2)
    sheet_a[oy:oy + S // 2, ox:ox + S // 2] = body ** 1.2
save_rgba("fx_flame.png", white(S), sheet_a)

# Spark streak (used stretched)
save_rgba("fx_spark.png", white(N), np.clip(1 - np.abs(x) * 1.0, 0, 1) ** 3 * np.clip(1 - np.abs(y) * 4, 0, 1) ** 1.5 +
          np.clip(1 - r * 3, 0, 1))

# Shockwave ring
ring = np.exp(-((r - 0.78) / 0.07) ** 2) + 0.35 * np.exp(-((r - 0.6) / 0.18) ** 2) * (r < 0.8)
save_rgba("fx_ring.png", white(N), ring)

# Scorch decal: dark burnt splat with ragged edge and ember speckles
nz = fbm(S, 50, 5, 4)
xs, ys = grid(S)
rr = np.sqrt(xs * xs + ys * ys)
ang = np.arctan2(ys, xs)
edge = 0.72 + 0.18 * np.sin(ang * 7 + nz * 6) * nz
a = np.clip((edge - rr) * 4, 0, 1) * (0.55 + 0.45 * nz)
rgb = np.dstack([0.06 + 0.05 * nz, 0.045 + 0.03 * nz, 0.035 + 0.02 * nz])
save_rgba("fx_scorch.png", rgb, a * 0.92)

# Tileable noise for the fireball surface
n2 = fbm(S, 70, 6, 4)
save_rgba("fx_noise.png", np.dstack([n2] * 3), np.ones_like(n2))

# Debris chip (small square-ish rock silhouette for stone chip particles)
save_rgba("fx_chip.png", white(64)[:64, :64], np.ones((64, 64), np.float32) * (np.maximum(np.abs(grid(64)[0]), np.abs(grid(64)[1])) < 0.8))

# UI: crosshair, radial vignette, heat edge, soft panel
xu, yu = grid(256)
ru = np.sqrt(xu * xu + yu * yu)
cross = np.exp(-((ru - 0.82) / 0.035) ** 2) + (ru < 0.09) * 1.0
for ax in (xu, yu):
    other = yu if ax is xu else xu
    cross += (np.abs(ax) < 0.025) * ((np.abs(other) > 0.45) & (np.abs(other) < 0.72))
save_rgba("ui_crosshair.png", white(256), np.clip(cross, 0, 1))
save_rgba("ui_vignette.png", white(256), np.clip((ru - 0.45) / 0.65, 0, 1) ** 1.6)
save_rgba("ui_ring.png", white(256), np.exp(-((ru - 0.85) / 0.06) ** 2))
panel = np.clip((1 - np.maximum(np.abs(xu), np.abs(yu))) * 8, 0, 1)
save_rgba("ui_panel.png", white(256), panel)
print("textures ->", OUT)
