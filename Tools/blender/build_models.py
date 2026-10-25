# Builds the catapult (legacy), knight, arrow and town models and exports FBX into Assets/Siege/Models.
# Run: blender -b -P Tools/blender/build_models.py -- <project_root>
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from siege_common import *   # noqa: F401,F403  (palette, primitives, join/empty/export)
import bmesh, math
from mathutils import Vector, Matrix

# Convention: Blender +Y = model front, +Z = up.

# ---------------------------------------------------------------- catapult
def spoked_wheel(x, y, z, name):
    s = 1 if x > 0 else -1
    p = [tube(0.78, 0.26, 0.07, (x, y, z), "iron", rot=(0, 90, 0), verts=24),            # iron tyre
         tube(0.72, 0.3, 0.14, (x, y, z), "wood_dark", rot=(0, 90, 0), verts=24),        # felloe
         cyl(0.2, 0.42, (x, y, z), "wood_deep", rot=(0, 90, 0), verts=12, bevel=0.05),     # hub
         cyl(0.13, 0.5, (x + s * 0.02, y, z), "iron_dark", rot=(0, 90, 0), verts=10, bevel=0.03),
         tube(0.22, 0.1, 0.04, (x + s * 0.16, y, z), "iron", rot=(0, 90, 0), verts=12)]
    for i in range(8):
        a = i * math.pi / 4
        p.append(beam((x, y + math.cos(a) * 0.18, z + math.sin(a) * 0.18),
                      (x, y + math.cos(a) * 0.62, z + math.sin(a) * 0.62), 0.09, "wood", bevel=0.03))
    for i in range(8):
        a = i * math.pi / 4 + math.pi / 8
        p.append(sphere(0.04, (x + s * 0.13, y + math.cos(a) * 0.76, z + math.sin(a) * 0.76), "rivet", segs=6))
    return join(p, name, (x, y, z), smooth_angle=35)

def build_catapult():
    reset()
    parts = []
    W = 1.25
    RZ = 0.82
    # Twin base rails with iron end caps and straps
    for x in (-W, W):
        parts.append(box((0.4, 6.3, 0.42), (x, 0, RZ), "wood", bevel=0.09))
        parts.append(box((0.03, 6.1, 0.06), (x + math.copysign(0.2, x), 0, RZ + 0.08), "wood_dark", bevel=0.01))
        for y in (-3.12, 3.12):
            parts.append(box((0.46, 0.12, 0.48), (x, y, RZ), "iron", bevel=0.03))
        for y in (-2.7, -1.2, 0.9, 2.7):
            parts.append(box((0.48, 0.16, 0.5), (x, y, RZ), "iron", bevel=0.03))
            parts += rivets([(x + math.copysign(0.25, x), y, RZ + 0.12), (x + math.copysign(0.25, x), y, RZ - 0.12)])
    for y in (-2.7, 0.9, 2.7):
        parts.append(box((2 * W + 0.36, 0.38, 0.36), (0, y, RZ + 0.03), "wood_dark", bevel=0.08))
    # Ammo deck planks with slight irregularity
    for i in range(6):
        parts.append(box((2 * W - 0.38 + rng.uniform(-0.05, 0.05), 0.28, 0.09),
                         (rng.uniform(-0.03, 0.03), -2.42 + i * 0.3, RZ + 0.22), "plank" if i % 2 else "wood_light",
                         rot=(0, 0, rng.uniform(-2, 2)), bevel=0.03))
    # Uprights, braces, stop beam
    TOPZ = 3.75
    for x in (-W, W):
        parts.append(beam((x, 0.9, RZ - 0.1), (x, 0.9, TOPZ + 0.2), 0.4, "wood", bevel=0.09, jitter=0.02))
        parts.append(beam((x, 2.7, RZ + 0.12), (x, 1.02, TOPZ - 0.3), 0.3, "wood_dark", bevel=0.07, jitter=0.03))
        parts.append(beam((x, -1.2, RZ + 0.12), (x, 0.78, TOPZ - 0.7), 0.28, "wood_dark", bevel=0.07, jitter=0.03))
        for zz in (TOPZ - 0.3, RZ + 0.5, (RZ + TOPZ) / 2):
            parts.append(box((0.48, 0.48, 0.13), (x, 0.9, zz), "iron", bevel=0.03))
            parts += rivets([(x, 1.15, zz), (x, 0.65, zz)])
        # Rope lashing where braces meet the rail
        for yy in (2.55, -1.05):
            parts += [torus(0.3, 0.04, (x, yy, RZ + 0.24 + k * 0.08), "rope", rot=(0, 0, 0), seg=12, minor_seg=6) for k in range(3)]
    parts.append(box((2 * W + 0.62, 0.46, 0.46), (0, 0.9, TOPZ), "wood_dark", bevel=0.1))
    for x in (-W - 0.2, W + 0.2):
        parts.append(box((0.14, 0.5, 0.5), (x, 0.9, TOPZ), "iron", bevel=0.04))
    # Padded buffer where the arm slams in
    parts.append(box((1.0, 0.42, 0.56), (0, 0.6, TOPZ - 0.04), "leather", bevel=0.18))
    parts.append(box((0.86, 0.3, 0.44), (0, 0.48, TOPZ - 0.04), "red_dark", bevel=0.14))
    for x in (-0.5, -0.17, 0.17, 0.5):
        parts.append(box((0.07, 0.62, 0.64), (x, 0.66, TOPZ - 0.04), "rope", bevel=0.03))
    # Torsion skein the arm sits in
    PIV = Vector((0, -0.75, RZ + 0.48))
    parts.append(cyl(0.36, 2 * W - 0.2, PIV, "rope", rot=(0, 90, 0), verts=16, bevel=0.06))
    for k in range(9):
        x = -W + 0.25 + k * (2 * W - 0.5) / 8
        parts.append(torus(0.36, 0.035, (x, PIV.y, PIV.z), "rope_dark", rot=(0, 90, 0), seg=16, minor_seg=6))
    for x in (-W, W):
        parts.append(cyl(0.46, 0.2, (x * 0.94, PIV.y, PIV.z), "iron", rot=(0, 90, 0), verts=16, bevel=0.04))
        parts.append(cyl(0.16, 0.36, (x * 1.13, PIV.y, PIV.z), "iron_dark", rot=(0, 90, 0), verts=10, bevel=0.04))
        parts.append(box((0.1, 0.9, 0.12), (x * 1.2, PIV.y, PIV.z), "iron_dark", bevel=0.03))  # tension lever
        parts += rivets([(x * 1.03, PIV.y + 0.3, PIV.z + 0.3), (x * 1.03, PIV.y - 0.3, PIV.z - 0.3),
                         (x * 1.03, PIV.y + 0.3, PIV.z - 0.3), (x * 1.03, PIV.y - 0.3, PIV.z + 0.3)])
    # Axles + bearing blocks
    for y in (-2.0, 2.0):
        parts.append(cyl(0.12, 2 * W + 1.0, (0, y, 0.74), "iron_dark", rot=(0, 90, 0), verts=10, bevel=0.03))
        for x in (-W, W):
            parts.append(box((0.52, 0.52, 0.28), (x, y, RZ - 0.22), "iron", bevel=0.04))
    # Winch posts and ratchet
    WZ = RZ + 0.78
    for x in (-W, W):
        parts.append(box((0.32, 0.34, 0.95), (x, -2.65, RZ + 0.42), "wood", bevel=0.07))
        parts.append(cyl(0.2, 0.16, (x * 1.0, -2.65, WZ), "iron", rot=(0, 90, 0), verts=12, bevel=0.03))
    parts.append(box((0.08, 0.5, 0.12), (W + 0.12, -2.4, WZ + 0.22), "iron_dark", rot=(35, 0, 0), bevel=0.02))  # pawl
    # Trigger pin and release cord hook
    parts.append(cyl(0.06, 0.6, (0, -1.85, RZ + 0.42), "iron_dark", rot=(90, 0, 0), verts=8, bevel=0.02))
    parts.append(torus(0.1, 0.025, (0, -1.55, RZ + 0.42), "iron", rot=(90, 0, 0), seg=10, minor_seg=6))
    frame = join(parts, "Frame", (0, 0, 0))

    for (x, y), n in zip([(-W - 0.44, 2.0), (W + 0.44, 2.0), (-W - 0.44, -2.0), (W + 0.44, -2.0)],
                         ["Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR"]):
        spoked_wheel(x, y, 0.74, n)

    # Throwing arm in its fired pose (upright, leaning into the buffer).
    tilt = math.radians(14)
    ARM = 3.6
    up = Vector((0, math.sin(tilt), math.cos(tilt)))
    top = PIV + up * ARM
    ap = [beam(PIV - up * 0.15, top, 0.32, "wood", bevel=0.1)]
    ap.append(beam(PIV + up * 0.3, PIV.lerp(top, 0.55), 0.38, "wood", bevel=0.1))   # thicker butt section
    for t in (0.22, 0.5, 0.74):
        c = PIV.lerp(top, t)
        for k in range(3):
            ap.append(torus(0.22, 0.035, c + up * (k * 0.07), "rope", rot=(-14, 0, 0), seg=12, minor_seg=6))
    ap.append(box((0.42, 0.42, 0.14), PIV.lerp(top, 0.92), "iron", rot=(-14, 0, 0), bevel=0.03))
    # Bowl-shaped bucket, opening toward the back (it faces up once the arm is cranked down)
    cup_c = top + Vector((0, -0.05, 0.1))
    ap.append(dome(0.62, cup_c, "wood_dark", scale=(1, 1, 0.62), rot=(90 - 14 + 180, 0, 0), thick=0.08))
    ap.append(torus(0.6, 0.06, cup_c, "iron", rot=(90 - 14, 0, 0), seg=22))
    for i in range(6):
        a = i * math.pi / 3
        ap.append(sphere(0.05, cup_c + Vector((math.cos(a) * 0.6, -0.02, math.sin(a) * 0.6 * 0.97)), "rivet", segs=6))
    ap.append(box((0.4, 0.4, 0.3), top - up * 0.15, "iron", rot=(-14, 0, 0), bevel=0.05))
    arm = join(ap, "Arm", PIV)
    empty("FireballSocket", cup_c + Vector((0, -0.32, 0.08)))
    empty("RopeArm", PIV.lerp(top, 0.5) + Vector((0, -0.25, 0)))

    wp = [cyl(0.24, 2 * W - 0.1, (0, -2.65, WZ), "wood_dark", rot=(0, 90, 0), verts=12, bevel=0.04)]
    for k in range(7):
        wp.append(torus(0.28, 0.04, (-0.25 + k * 0.08, -2.65, WZ), "rope", rot=(0, 90, 0), seg=14, minor_seg=6))
    for x in (-W - 0.14, W + 0.14):
        wp.append(cyl(0.16, 0.14, (x, -2.65, WZ), "iron", rot=(0, 90, 0), verts=10, bevel=0.03))
        for a in (0, 90):
            wp.append(box((0.12, 1.55, 0.14), (x, -2.65, WZ), "wood", rot=(a, 0, 0), bevel=0.04))
        for a in range(4):
            ang = a * math.pi / 2
            wp.append(cyl(0.08, 0.16, (x + math.copysign(0.1, x), -2.65 + math.cos(ang) * 0.75, WZ + math.sin(ang) * 0.75),
                          "leather", rot=(0, 90, 0), verts=8, bevel=0.03))
    join(wp, "Winch", (0, -2.65, WZ))
    empty("RopeWinch", (0, -2.65, WZ + 0.28))
    empty("KnightSpot", (W + 1.15, -2.65, 0))
    empty("AmmoSpot", (-W - 1.7, -1.4, 0))
    empty("BannerSpot", (-W, -3.0, RZ + 0.2))
    export(os.path.join(OUT, "Catapult.fbx"))

# ---------------------------------------------------------------- knight (enemy siege engineer)
def build_knight():
    reset()
    HIP = 1.0
    # Hips: plated skirt (faulds) + belt
    hp = [cyl(0.4, 0.34, (0, 0, HIP - 0.04), "steel_mid", verts=16, bevel=0.05, r2=0.35),
          cyl(0.44, 0.1, (0, 0, HIP - 0.2), "steel", verts=16, bevel=0.04),
          torus(0.39, 0.045, (0, 0, HIP + 0.12), "leather", seg=18),
          box((0.16, 0.06, 0.12), (0, 0.4, HIP + 0.12), "gold", bevel=0.03)]
    for i in range(6):
        a = math.radians(-75 + i * 30)
        hp.append(box((0.2, 0.05, 0.24), (math.sin(a) * 0.4, math.cos(a) * 0.4, HIP - 0.28), "red", rot=(0, 0, -math.degrees(a)), bevel=0.02))
    join(hp, "Hips", (0, 0, HIP))
    for side, x in (("L", -0.18), ("R", 0.18)):
        lp = [cyl(0.14, 0.34, (x, 0, HIP - 0.3), "steel_dark", verts=12, bevel=0.04),
              sphere(0.15, (x, 0.04, HIP - 0.52), "steel", segs=12),
              sphere(0.06, (x, 0.17, HIP - 0.52), "gold", segs=8),
              cyl(0.15, 0.36, (x, 0, HIP - 0.74), "steel_mid", verts=12, bevel=0.04, r2=0.13),
              box((0.27, 0.46, 0.18), (x, 0.08, 0.1), "steel_dark", bevel=0.08),
              box((0.25, 0.2, 0.12), (x, 0.24, 0.07), "steel_mid", bevel=0.05)]
        join(lp, "Leg_" + side, (x, 0, HIP - 0.12))
    # Torso: rounded breastplate, tabard with gold trim (no cross), pauldrons, cape mount, sword
    T = HIP + 0.1
    tp = [sphere(0.44, (0, 0, HIP + 0.52), "steel", scale=(1.0, 0.72, 1.0), segs=18),
          cyl(0.36, 0.3, (0, 0, HIP + 0.22), "steel_mid", verts=16, bevel=0.04, r2=0.4),
          box((0.56, 0.06, 0.62), (0, 0.31, HIP + 0.24), "red", bevel=0.03),
          box((0.6, 0.065, 0.06), (0, 0.33, HIP - 0.06), "gold", bevel=0.02),
          box((0.06, 0.065, 0.6), (-0.28, 0.33, HIP + 0.24), "gold", bevel=0.02),
          box((0.06, 0.065, 0.6), (0.28, 0.33, HIP + 0.24), "gold", bevel=0.02),
          cyl(0.11, 0.04, (0, 0.35, HIP + 0.36), "gold", rot=(90, 0, 0), verts=14, bevel=0.012),
          cyl(0.065, 0.05, (0, 0.36, HIP + 0.36), "red_dark", rot=(90, 0, 0), verts=12, bevel=0.01),
          torus(0.2, 0.06, (0, 0, HIP + 0.9), "steel_mid", seg=16)]
    for x in (-1, 1):
        tp.append(dome(0.27, (x * 0.44, 0, HIP + 0.78), "steel", scale=(1.05, 1, 0.75), rot=(0, x * 25, 0), thick=0.05))
        tp.append(dome(0.23, (x * 0.5, 0, HIP + 0.68), "steel_mid", scale=(1.05, 1, 0.7), rot=(0, x * 35, 0), thick=0.04))
        tp.append(torus(0.25, 0.025, (x * 0.44, 0, HIP + 0.8), "gold", rot=(0, x * 25, 0), seg=16, minor_seg=6))
        tp.append(sphere(0.06, (x * 0.3, 0.12, HIP + 0.85), "gold", segs=8))   # cape clasps
    tp += [box((0.07, 0.12, 0.85), (0.47, 0.05, HIP - 0.28), "leather", rot=(10, 0, 0), bevel=0.03),   # scabbard
           box((0.34, 0.1, 0.07), (0.47, 0.0, HIP + 0.17), "gold", bevel=0.03),
           cyl(0.035, 0.22, (0.47, -0.01, HIP + 0.3), "leather", verts=8, bevel=0.01),
           sphere(0.06, (0.47, -0.01, HIP + 0.43), "gold", segs=8)]
    join(tp, "Torso", (0, 0, T))
    # Cape: tapered, bent cloth hanging from the shoulders (separate so it can sway)
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=6, y_subdivisions=10, size=1, location=(0, 0, 0))
    cape = bpy.context.active_object
    cape.scale = (0.78, 1.25, 1)
    activate(cape); bpy.ops.object.transform_apply(scale=True)
    for v in cape.data.vertices:
        t = (0.625 - v.co.y) / 1.25            # 0 at top, 1 at bottom
        v.co.x *= 1.0 + 0.45 * t
        v.co = Vector((v.co.x, -0.3 - 0.18 * t - 0.05 * math.cos(v.co.x * 9) * t, HIP + 0.86 - t * 1.25))
    m = cape.modifiers.new("solid", "SOLIDIFY"); m.thickness = 0.04
    bpy.ops.object.modifier_apply(modifier="solid")
    finish(cape, "red_dark", 0)
    join([cape], "Cape", (0, -0.3, HIP + 0.86), smooth_angle=60)
    # Helm: rounded great helm, visor slit, breaths, gold band and a flowing plume (no cross)
    NECK = HIP + 0.92
    hd = [cyl(0.36, 0.5, (0, 0, NECK + 0.3), "steel", verts=20, bevel=0.12, segs=4),
          dome(0.36, (0, 0, NECK + 0.53), "steel", scale=(1, 1, 0.62), segs=20, thick=0.36),
          box((0.52, 0.12, 0.075), (0, 0.33, NECK + 0.36), "black", bevel=0.03),
          torus(0.36, 0.035, (0, 0, NECK + 0.08), "gold", seg=22, minor_seg=6),
          torus(0.355, 0.025, (0, 0, NECK + 0.46), "steel_dark", seg=22, minor_seg=6),
          box((0.06, 0.06, 0.3), (0, 0.35, NECK + 0.6), "gold", rot=(-25, 0, 0), bevel=0.02)]
    for i in range(3):
        hd.append(box((0.04, 0.05, 0.1), (0.14 + i * 0.07, 0.33, NECK + 0.2), "black", bevel=0.015))
    pts, radii = [], []
    for i in range(9):
        t = i / 8
        pts.append((0, 0.05 - t * 0.75, NECK + 0.78 + math.sin(t * math.pi) * 0.22 - t * 0.35))
        radii.append(0.12 * (1 - t) + 0.04)
    hd.append(sweep(pts, radii, "red", verts=10))
    hd.append(cyl(0.08, 0.12, (0, 0.03, NECK + 0.74), "gold", verts=10, bevel=0.03))
    join(hd, "Head", (0, 0, NECK))
    for side, x in (("L", -0.52), ("R", 0.52)):
        SH = HIP + 0.76
        s = -1 if side == "L" else 1
        ap = [cyl(0.12, 0.3, (x, 0, SH - 0.18), "steel_dark", verts=12, bevel=0.03),
              sphere(0.14, (x, 0, SH - 0.36), "steel", segs=12),
              cyl(0.14, 0.3, (x, 0, SH - 0.53), "steel_mid", verts=12, bevel=0.04, r2=0.12),
              torus(0.135, 0.025, (x, 0, SH - 0.66), "gold", seg=14, minor_seg=6),
              box((0.25, 0.27, 0.22), (x, 0.02, SH - 0.78), "steel_dark", bevel=0.09),
              box((0.08, 0.14, 0.12), (x - s * 0.1, 0.1, SH - 0.74), "steel_dark", bevel=0.03)]
        join(ap, "Arm_" + side, (x, 0, SH))
    export(os.path.join(OUT, "Knight.fbx"))

# ---------------------------------------------------------------- bow + arrow
def build_arrow():
    """A believable war arrow: slim shaft, forged bodkin point, three real-shaped feather vanes."""
    reset()
    L = 1.0
    parts = [cyl(0.011, L, (0, L / 2, 0), "wood_light", rot=(90, 0, 0), verts=10, bevel=0),
             cyl(0.0135, 0.07, (0, L - 0.03, 0), "leather", rot=(90, 0, 0), verts=10, bevel=0.003),   # sinew wrap at head
             cyl(0.0135, 0.05, (0, 0.27, 0), "leather", rot=(90, 0, 0), verts=10, bevel=0.003),       # wrap at fletching
             cyl(0.013, 0.035, (0, 0.012, 0), "wood_deep", rot=(90, 0, 0), verts=10, bevel=0.003)]    # horn nock
    # Bodkin point: long square-section steel spike with a socket
    parts.append(cyl(0.016, 0.05, (0, L + 0.02, 0), "steel_dark", rot=(90, 0, 0), verts=8, bevel=0.004, r2=0.013))
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=0.02, radius2=0.0, depth=0.13, location=(0, L + 0.11, 0), rotation=rad((-90, 0, 45)))
    parts.append(finish(bpy.context.active_object, "steel_mid", 0))
    # Feather vanes: tall at the rear, tapering forward, gently curved
    for i, a in enumerate((0, 120, 240)):
        bm = bmesh.new()
        prof = [(0.03, 0.0), (0.05, 0.035), (0.1, 0.042), (0.17, 0.034), (0.22, 0.016), (0.245, 0.0)]
        verts = [bm.verts.new((0, y, 0.011)) for y, _ in prof] + [bm.verts.new((0.004 * (k % 2), y, 0.011 + h)) for k, (y, h) in reversed(list(enumerate(prof)))]
        bm.faces.new(verts)
        me = bpy.data.meshes.new("vane"); bm.to_mesh(me); bm.free()
        o = bpy.data.objects.new("vane", me); bpy.context.scene.collection.objects.link(o)
        activate(o)
        m = o.modifiers.new("solid", "SOLIDIFY"); m.thickness = 0.0025
        bpy.ops.object.modifier_apply(modifier="solid")
        o.location.y += 0.02
        o.rotation_euler = (0, math.radians(a), 0)
        parts.append(finish(o, "steel" if i == 0 else "cream_feather", 0))
    join(parts, "Arrow", (0, 0, 0), smooth_angle=50)
    export(os.path.join(OUT, "Arrow.fbx"))

# ---------------------------------------------------------------- town (houses the player defends)
def hull(points, color):
    bm = bmesh.new()
    for p in points:
        bm.verts.new(p)
    bmesh.ops.convex_hull(bm, input=bm.verts)
    me = bpy.data.meshes.new("hull"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("hull", me); bpy.context.scene.collection.objects.link(o)
    return finish(o, color, 0.04, 2)

def gable_roof(W, D, H, R, color, o=0.45, t=0.22, wall="plaster"):
    """Pitched roof with ridge along X over a W x D box whose walls top out at H."""
    parts = [hull([(sx * W / 2, sy * D / 2, H) for sx in (-1, 1) for sy in (-1, 1)] +
                  [(sx * W / 2, 0, H + R) for sx in (-1, 1)], wall)]
    half = D / 2 + o
    ang = math.atan2(R, D / 2)
    ln = half / math.cos(ang)
    for sy in (-1, 1):
        cy = sy * (half / 2) - sy * 0.0
        cz = H + R - (half / 2) * math.tan(ang) + 0.08
        parts.append(box((W + 2 * o, ln, t), (0, sy * half / 2, cz), color, rot=(-sy * math.degrees(ang), 0, 0), bevel=0.08))
    parts.append(cyl(0.16, W + 2 * o + 0.1, (0, 0, H + R + 0.1), "wood_dark", rot=(0, 90, 0), verts=8, bevel=0.03))
    return parts

def window(x, y, z, face_y, w=0.7, h=0.85, shutters=True, rot=0):
    d = 0.08 if face_y > 0 else -0.08
    p = [box((w, 0.12, h), (x, y + d * 0.2, z), "glass", rot=(0, 0, rot), bevel=0.03),
         box((w + 0.16, 0.16, 0.12), (x, y + d, z - h / 2 - 0.04), "wood_dark", rot=(0, 0, rot), bevel=0.03),
         box((w + 0.12, 0.14, 0.1), (x, y + d, z + h / 2 + 0.04), "wood_dark", rot=(0, 0, rot), bevel=0.03),
         box((0.07, 0.14, h), (x, y + d, z), "wood_dark", rot=(0, 0, rot), bevel=0.02)]
    if shutters:
        for sx in (-1, 1):
            p.append(box((w * 0.45, 0.08, h * 0.95), (x + sx * (w * 0.5 + w * 0.25), y + d * 0.6, z), "roof_blue" if rot == 0 else "roof_green", rot=(0, 0, rot), bevel=0.03))
    return p

def timber_frame(W, D, z0, z1, front_y):
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(box((0.22, 0.22, z1 - z0), (sx * W / 2, sy * D / 2, (z0 + z1) / 2), "wood_dark", bevel=0.04))
    for sy in (-1, 1):
        p.append(box((W + 0.1, 0.2, 0.2), (0, sy * D / 2, z1 - 0.05), "wood_dark", bevel=0.04))
        p.append(box((W + 0.1, 0.18, 0.16), (0, sy * D / 2, z0 + 0.05), "wood_dark", bevel=0.03))
    for sx in (-1, 1):
        p.append(box((0.18, D + 0.1, 0.2), (sx * W / 2, 0, z1 - 0.05), "wood_dark", bevel=0.04))
    # braces on the front
    for sx in (-1, 1):
        p.append(beam((sx * W / 2, front_y, z0 + 0.1), (sx * W * 0.22, front_y, z1 - 0.1), 0.15, "wood_dark", bevel=0.03, depth=0.12))
    return p

def door(y, w=1.0, h=1.7, x=0.0):
    return [box((w, 0.2, h), (x, y, h / 2 + 0.55), "wood", bevel=0.05),
            box((w + 0.25, 0.24, 0.18), (x, y + 0.02, h + 0.62), "stone_dark", bevel=0.05),
            box((0.12, 0.25, 0.12), (x + w * 0.3, y + 0.05, h * 0.55 + 0.55), "iron", bevel=0.02),
            box((w * 0.9, 0.22, 0.08), (x, y + 0.01, h * 0.35 + 0.55), "iron_dark", bevel=0.01),
            box((w * 0.9, 0.22, 0.08), (x, y + 0.01, h * 0.75 + 0.55), "iron_dark", bevel=0.01)]

def chimney(x, y, base, top):
    return [box((0.75, 0.75, top - base), (x, y, (base + top) / 2), "stone", bevel=0.07),
            box((0.92, 0.92, 0.22), (x, y, top), "stone_dark", bevel=0.05)]

def house_cottage():
    W, D, H = 4.6, 3.6, 2.9
    p = [box((W + 0.5, D + 0.5, 0.6), (0, 0, 0.3), "stone", bevel=0.12),
         box((W, D, H - 0.6), (0, 0, 0.6 + (H - 0.6) / 2), "plaster", bevel=0.06)]
    p += timber_frame(W, D, 0.6, H, D / 2 + 0.02)
    p += gable_roof(W, D, H, 1.9, "roof_red")
    p += door(D / 2 + 0.05)
    p += window(-1.4, D / 2 + 0.05, 1.75, 1) + window(1.4, D / 2 + 0.05, 1.75, 1)
    p += window(-1.0, -D / 2 - 0.05, 1.75, -1, shutters=False)
    p += chimney(1.4, -0.8, H, H + 2.4)
    p.append(box((1.2, 0.35, 0.3), (1.4, D / 2 + 0.3, 1.15), "wood", bevel=0.05))
    for i in range(4):
        p.append(sphere(0.16, (1.0 + i * 0.27, D / 2 + 0.33, 1.38), "fruit_red" if i % 2 else "leaf", segs=8))
    return join(p, "House_Cottage", (0, 0, 0))

def house_tall():
    W, D = 4.4, 4.0
    p = [box((W + 0.4, D + 0.4, 0.6), (0, 0, 0.3), "stone", bevel=0.12),
         box((W, D, 2.4), (0, 0, 1.8), "stone_dark", bevel=0.08),
         box((W + 0.5, D + 0.5, 2.4), (0, 0, 4.2), "plaster_warm", bevel=0.06)]   # jettied upper floor
    p += timber_frame(W + 0.5, D + 0.5, 3.0, 5.4, (D + 0.5) / 2 + 0.02)
    for sx in (-1.6, 0, 1.6):
        p.append(box((0.22, 0.6, 0.22), (sx, D / 2 + 0.15, 2.95), "wood_dark", bevel=0.04))   # jetty joists
    p += gable_roof(W + 0.5, D + 0.5, 5.4, 2.3, "roof_blue", wall="plaster_warm")
    p += door(D / 2 + 0.05, w=1.1)
    p += window(-1.4, D / 2 + 0.05, 1.8, 1, shutters=False)
    p += window(-1.3, (D + 0.5) / 2 + 0.05, 4.2, 1) + window(1.3, (D + 0.5) / 2 + 0.05, 4.2, 1)
    p += chimney(-1.5, -1.0, 5.4, 8.2)
    # hanging sign
    p += [box((1.2, 0.12, 0.12), (1.9, D / 2 + 0.55, 2.7), "iron_dark", bevel=0.02),
          box((0.9, 0.1, 0.7), (2.15, D / 2 + 0.6, 2.2), "wood_light", bevel=0.05),
          cyl(0.2, 0.12, (2.15, D / 2 + 0.67, 2.2), "gold", rot=(90, 0, 0), verts=12, bevel=0.02)]
    return join(p, "House_Tall", (0, 0, 0))

def house_turret():
    p = [cyl(2.2, 0.6, (0, 0, 0.3), "stone", verts=18, bevel=0.1),
         cyl(1.9, 4.2, (0, 0, 2.7), "stone", verts=18, bevel=0.06),
         cyl(1.95, 0.25, (0, 0, 4.8), "stone_dark", verts=18, bevel=0.05),
         cyl(2.35, 2.8, (0, 0, 6.3), "roof_orange", verts=18, bevel=0.06, r2=0.05),
         sphere(0.2, (0, 0, 7.8), "gold", segs=10),
         cyl(0.04, 1.2, (0, 0, 8.3), "iron_dark", verts=6, bevel=0)]
    p.append(box((0.9, 0.06, 0.6), (0.45, 0, 8.6), "cloth_blue", bevel=0.02))
    p += door(1.85, w=1.0)
    for a in (35, -35, 150):
        r = math.radians(a)
        x, y = math.sin(r) * 1.92, math.cos(r) * 1.92
        p.append(box((0.6, 0.2, 0.9), (x, y, 3.4), "glass", rot=(0, 0, -a), bevel=0.04))
        p.append(box((0.8, 0.26, 0.14), (x, y, 2.88), "wood_dark", rot=(0, 0, -a), bevel=0.03))
    return join(p, "House_Turret", (0, 0, 0))

def market_stall():
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(box((0.2, 0.2, 2.6), (sx * 1.5, sy * 1.0, 1.3), "wood", bevel=0.04))
    p.append(box((3.2, 1.6, 0.2), (0, 0.3, 1.0), "wood_light", bevel=0.05))
    p.append(box((3.2, 0.15, 0.9), (0, 1.05, 0.55), "wood_dark", bevel=0.04))
    for i in range(6):
        p.append(box((3.4 / 6, 2.6, 0.08), (-1.7 + 3.4 / 12 + i * 3.4 / 6, 0.2, 2.75), "cloth_red" if i % 2 else "cloth_cream", rot=(-12, 0, 0), bevel=0.02))
    for i in range(7):
        p.append(sphere(0.2, (-1.2 + i * 0.4, 0.35 + (i % 2) * 0.25, 1.28), ["fruit_red", "fruit_orange", "leaf"][i % 3], segs=8))
    p.append(box((0.8, 0.6, 0.55), (1.0, -0.4, 1.4), "wood", bevel=0.05))
    return join(p, "Market_Stall", (0, 0, 0))

def town_well():
    p = [tube(1.1, 0.9, 0.3, (0, 0, 0.45), "stone", verts=16),
         cyl(0.85, 0.2, (0, 0, 0.6), "glass", verts=16, bevel=0)]
    for sx in (-1, 1):
        p.append(box((0.2, 0.2, 2.3), (sx * 1.0, 0, 1.6), "wood", bevel=0.04))
    p.append(cyl(0.1, 2.2, (0, 0, 2.3), "wood_dark", rot=(0, 90, 0), verts=8, bevel=0.02))
    p += gable_roof(2.4, 1.6, 2.6, 0.8, "roof_red", o=0.25, t=0.14, wall="wood")
    p.append(cyl(0.22, 0.35, (0, 0, 1.4), "wood", verts=10, bevel=0.03))
    return join(p, "Town_Well", (0, 0, 0))

def build_town():
    reset()
    builders = [house_cottage, house_tall, house_turret, market_stall, town_well]
    for i, b in enumerate(builders):
        o = b()
        o.location.x += i * 12   # spread out so nothing overlaps in the file; Unity re-centres by name
    export(os.path.join(OUT, "Town.fbx"))

build_catapult()
build_knight()

build_arrow()
build_town()
