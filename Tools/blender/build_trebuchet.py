# Builds the counterweight trebuchet (Frame, Arm, Counterweight, Windlass, Pouch + rig empties) and exports
# Assets/Siege/Models/Trebuchet.fbx. Life-size (axle 9 m up, ~21.5 m with the arm vertical), ithappy toy style.
# Run: blender -b -P Tools/blender/build_trebuchet.py -- <project_root>
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from siege_common import *   # noqa: F401,F403  (palette, primitives, join/empty/export)
import bmesh, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# Convention: Blender +Y = front (towards the castle), +Z = up, metres. The arm is modelled horizontal.
AXLE = Vector((0, 0, 9.0))
HINGE = Vector((0, 3.2, 9.0))
WIN = Vector((0, -5.0, 1.3))                 # windlass axis
SX = 2.3                                     # sill / tower centre line (x = +-SX)
SILL_W, SILL_H = 0.5, 0.6                    # sills narrower than the legs: crew at CrewSpot1/2 clear them
LEG, LEG_Y, CAP_Z = 0.6, 4.1, 8.45           # A-frame leg size, feet on the sills, tower cap beam
FOOT, LEG_TOP = SILL_H - 0.05, CAP_Z - 0.1
# Counterweight box hangs this far below the hinge so the arm clears its rim in the +40 deg loaded pose,
# even with the box swung back ~10 deg (it is a lagging pendulum at runtime).
CW_DROP, CW_W, CW_D, CW_H = 2.1, 2.6, 2.2, 2.5
BACK_TIE = -5.75                             # split back cross tie; sets how long the capstan bars can be
BAR_R = 0.9
LOADED = math.radians(40)
SMOOTH = 62                                  # smooth-by-angle: rounds ropes/rivets, keeps 90 deg timber edges

C["stone_mid"] = swatch(17, 5)
C["stone_warm"] = swatch(16, 2)
C["earth"] = swatch(16, 7)

# ---------------------------------------------------------------- local helpers (cheaper than the shared ones)
def timber(p0, p1, thick, color, bevel=0.06, depth=None, segs=2):
    """beam() with a bevel segment count."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    bpy.ops.mesh.primitive_cube_add(size=1, location=(a + b) / 2)
    o = bpy.context.active_object
    o.scale = (thick, depth or thick, d.length)
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d.normalized())
    return finish(o, color, min(bevel, thick * 0.4, (depth or thick) * 0.4), segs)

def strap(p, direction, w, d, length=0.18, color="iron"):
    """Iron band clamped round a w x d beam at p."""
    dv = Vector(direction).normalized()
    p = Vector(p)
    return timber(p - dv * length / 2, p + dv * length / 2, w + 0.07, color, bevel=0.03, depth=d + 0.07, segs=1)

def studs(points, r=0.055):
    out = []
    for p in points:
        bpy.ops.mesh.primitive_uv_sphere_add(segments=6, ring_count=3, radius=r, location=p)
        out.append(finish(bpy.context.active_object, "rivet", 0))
    return out

def ring_x(r, depth, loc, color, verts=16):
    """Closed iron collar along X (the shaft it hugs is hidden inside)."""
    return cyl(r, depth, loc, color, rot=(0, 90, 0), verts=verts, bevel=min(0.02, depth * 0.3), segs=1)

def loop_tube(points, r, color, res=1, cyclic=True):
    """Rope/rim swept along a poly path (closed unless cyclic=False, then capped)."""
    cu = bpy.data.curves.new("loop", "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = r
    cu.bevel_resolution = res
    sp = cu.splines.new("POLY")
    sp.points.add(len(points) - 1)
    for pt, co in zip(sp.points, points):
        pt.co = (co[0], co[1], co[2], 1)
    sp.use_cyclic_u = cyclic
    cu.use_fill_caps = not cyclic
    o = bpy.data.objects.new("loop", cu)
    bpy.context.scene.collection.objects.link(o)
    activate(o)
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.active_object
    if not cyclic:   # weld the end caps onto the tube
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
        bm.to_mesh(o.data)
        bm.free()
    return finish(o, color, 0)

def lash(center, direction, w, d, turns=3, spacing=0.09, r=0.042, color="rope", n=12):
    """Rope turns hugging a w x d rectangular beam (same local axes as timber())."""
    dv = Vector(direction).normalized()
    M = Vector((0, 0, 1)).rotation_difference(dv).to_matrix()
    hx, hy = w / 2 + r * 0.55, d / 2 + r * 0.55
    out = []
    for t in range(turns):
        off = Vector(center) + dv * spacing * (t - (turns - 1) / 2)
        pts = []
        for i in range(n):
            a = 2 * math.pi * (i + 0.5) / n
            ca, sa = math.cos(a), math.sin(a)
            pts.append(off + M @ Vector((hx * math.copysign(abs(ca) ** 0.45, ca), hy * math.copysign(abs(sa) ** 0.45, sa), 0)))
        out.append(loop_tube(pts, r, color))
    return out

def loft(sections, color, bevel=0.08, segs=3):
    """Rectangular beam lofted along Y through (y, w, h, xc, zc) sections."""
    bm = bmesh.new()
    rings = [[bm.verts.new((xc + sx * w / 2, y, zc + sz * h / 2)) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
             for y, w, h, xc, zc in sections]
    for a, b in zip(rings, rings[1:]):
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("loft")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("loft", me)
    bpy.context.scene.collection.objects.link(o)
    return finish(o, color, bevel, segs)

def rock(r, loc, color, squash=0.75):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r)
    for v in bm.verts:
        v.co *= rng.uniform(0.9, 1.08)
        v.co.z *= squash
    me = bpy.data.meshes.new("rock")
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("rock", me)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), rng.uniform(0, 6.28))
    return finish(o, color, 0)

def leg_y(z):
    """|y| of the A-frame leg centre line at height z."""
    return LEG_Y + (z - FOOT) * (0.42 - LEG_Y) / (LEG_TOP - FOOT)

# ---------------------------------------------------------------- frame
def build_frame():
    p = []
    # Twin base sills with iron end caps and straps
    for s in (-1, 1):
        x = s * SX
        p.append(box((SILL_W, 12.0, SILL_H), (x, 0, SILL_H / 2), "wood", bevel=0.12, segs=2))
        for y in (-5.95, 5.95):
            p.append(box((SILL_W + 0.08, 0.16, SILL_H + 0.08), (x, y, SILL_H / 2), "iron", bevel=0.03, segs=1))
        for y in (-3.3, -1.25, 1.25, 3.3):
            p.append(box((SILL_W + 0.07, 0.18, SILL_H + 0.07), (x, y, SILL_H / 2), "iron", bevel=0.03, segs=1))
            p += studs([(x + s * (SILL_W / 2 + 0.04), y, SILL_H / 2 + dz) for dz in (-0.15, 0.15)])
    # Cross ties (through-tenoned, iron capped); the back tie is split by the trough, the middle one carries the outriggers
    for y, half in ((5.55, 3.0), (2.6, 3.0), (0.0, 5.3), (-2.4, 3.0)):
        p.append(box((2 * half, 0.54, 0.5), (0, y, 0.3), "wood_dark", bevel=0.1, segs=2))
        for s in (-1, 1):
            p.append(box((0.14, 0.6, 0.56), (s * (half - 0.04), y, 0.3), "iron", bevel=0.03, segs=1))
    for s in (-1, 1):
        p.append(box((3.0 - 0.7, 0.54, 0.44), (s * (0.7 + 3.0) / 2, BACK_TIE, 0.27), "wood_dark", bevel=0.1, segs=2))
        p.append(box((0.14, 0.6, 0.5), (s * 2.96, BACK_TIE, 0.27), "iron", bevel=0.03, segs=1))
    # A-frame towers: two raked legs + king post, double collars, diagonal braces, cap beam, iron bearing
    for s in (-1, 1):
        x = s * SX
        for sy in (-1, 1):
            a, b = Vector((x, sy * LEG_Y, FOOT)), Vector((x, sy * 0.42, LEG_TOP))
            d = (b - a).normalized()
            p.append(timber(a, b, LEG, "wood", bevel=0.12, depth=LEG))
            p.append(strap(a + d * 0.5, d, LEG, LEG, 0.24))
            p.append(strap(b - d * 0.8, d, LEG, LEG, 0.2))
            p += studs([a + d * 0.5 + Vector((s * 0.38, 0, 0)), b - d * 0.8 + Vector((s * 0.38, 0, 0))])
        p.append(timber((x, 0, FOOT), (x, 0, CAP_Z), 0.64, "wood", bevel=0.12, depth=0.58))
        p.append(strap((x, 0, FOOT + 0.45), (0, 0, 1), 0.64, 0.58, 0.24))
        for z, extra in ((3.4, 0.45), (6.3, 0.42)):
            half = leg_y(z) + extra
            for dx in (-0.39, 0.39):
                p.append(box((0.18, 2 * half, 0.46), (x + dx, 0, z), "wood_dark", bevel=0.07, segs=2))
            p += studs([(x + s * 0.5, yy, z + dz) for yy in (-leg_y(z), 0, leg_y(z)) for dz in (-0.11, 0.11)])
        for sy in (-1, 1):
            b0, b1 = Vector((x, sy * 0.32, FOOT + 0.35)), Vector((x, sy * (leg_y(3.4) - 0.3), 3.22))
            p.append(timber(b0, b1, 0.38, "wood_dark", bevel=0.08, depth=0.34))
            p += lash(b0.lerp(b1, 0.84), b1 - b0, 0.38, 0.34, turns=2)
            p.append(timber((x, sy * 0.32, 3.62), (x, sy * (leg_y(6.3) - 0.25), 6.12), 0.34, "wood_dark", bevel=0.07, depth=0.3))
        # buttress from the front end of the sill into the front leg
        f0, f1 = Vector((x, 5.75, FOOT - 0.05)), Vector((x, leg_y(2.9) - 0.12, 2.9))
        p.append(timber(f0, f1, 0.4, "wood_dark", bevel=0.09, depth=0.4))
        p += lash(f0.lerp(f1, 0.82), f1 - f0, 0.4, 0.4)
        # cap beam + bearing saddle, housing and through bolts
        p.append(box((0.78, 2.0, 0.48), (x, 0, CAP_Z), "wood_dark", bevel=0.09, segs=2))
        for yy in (-0.86, 0.86):
            p.append(box((0.85, 0.14, 0.55), (x, yy, CAP_Z), "iron", bevel=0.03, segs=1))
        p.append(box((0.72, 1.08, 0.28), (x, 0, CAP_Z + 0.34), "iron", bevel=0.06, segs=2))
        p.append(cyl(0.42, 0.6, (x, 0, AXLE.z), "iron", rot=(0, 90, 0), verts=20, bevel=0.06, segs=2))
        p.append(ring_x(0.45, 0.16, (x, 0, AXLE.z), "iron_dark", verts=20))
        for yy in (-0.47, 0.47):
            p.append(cyl(0.075, 0.66, (x, yy, CAP_Z + 0.24), "iron_dark", verts=8, bevel=0.02, segs=1))
            p.append(cyl(0.12, 0.08, (x, yy, CAP_Z + 0.5), "iron_dark", verts=6, bevel=0.02, segs=1))
        p += studs([(x + s * 0.37, yy, CAP_Z + 0.34) for yy in (-0.3, 0.3)], r=0.06)
        # lateral outrigger brace from the long middle tie up to the tower
        o0, o1 = Vector((s * 5.0, 0, 0.5)), Vector((s * 2.66, 0, 6.2))
        od = (o1 - o0).normalized()
        p.append(timber(o0, o1, 0.46, "wood", bevel=0.1, depth=0.46))
        p.append(strap(o0 + od * 0.45, od, 0.46, 0.46, 0.2))
        p += lash(o0.lerp(o1, 0.86), od, 0.46, 0.46)
        p.append(box((0.66, 0.66, 0.12), (s * 5.0, 0, 0.56), "iron", bevel=0.03, segs=1))
        p += [cyl(0.1, 0.75, (s * 5.42, yy, 0.37), "wood_deep", verts=8, bevel=0.03, segs=1) for yy in (-0.36, 0.36)]
        # windlass post with cap, bearing bush, knee brace and tie to the back leg
        wx = s * 2.12
        p.append(box((0.32, 0.38, 1.42), (wx, WIN.y, FOOT + 0.71), "wood", bevel=0.07, segs=2))
        p.append(box((0.44, 0.5, 0.14), (wx, WIN.y, FOOT + 1.47), "wood_dark", bevel=0.04, segs=1))
        p.append(ring_x(0.21, 0.44, (wx, WIN.y, WIN.z), "iron", verts=12))
        k0, k1 = Vector((wx, WIN.y - 0.15, 1.62)), Vector((wx, -5.85, FOOT + 0.02))
        p.append(timber(k0, k1, 0.22, "wood_dark", bevel=0.05, depth=0.22))
        t0, t1 = Vector((wx, WIN.y + 0.1, 1.8)), Vector((s * 2.3, -leg_y(1.8), 1.8))
        p.append(timber(t0, t1, 0.2, "wood_dark", bevel=0.05, depth=0.22))
    # pawl on the right post, resting on the windlass ratchet
    p.append(timber((1.86, -5.08, 2.02), (1.86, -5.34, 1.63), 0.08, "iron_dark", bevel=0.02, depth=0.13, segs=1))
    p.append(cyl(0.06, 0.18, (1.93, -5.08, 2.02), "iron", rot=(0, 90, 0), verts=8, bevel=0.02, segs=1))
    # maintenance ladder leaning on the outer face of the left cap beam
    l0, l1 = Vector((-3.98, 0.65, 0)), Vector((-2.79, 0.65, 8.4))
    ld = (l1 - l0).normalized()
    for dy in (-0.3, 0.3):
        p.append(timber(l0 + Vector((0, dy, 0)), l1 + Vector((0, dy, 0)), 0.12, "wood_light", bevel=0.04, depth=0.1))
    for i in range(23):
        c = l0 + ld * (0.35 + i * 0.36)
        p.append(box((0.07, 0.62, 0.07), c, "wood", bevel=0))
    # ground trough along the back the pouch slides out of
    for y in (-3.4, -5.6, -8.2, -10.6, -12.7):
        p.append(box((1.7, 0.4, 0.28), (0, y, 0.14), "wood_dark", bevel=0.06, segs=2))
    for i, x in enumerate((-0.41, -0.137, 0.137, 0.41)):
        p.append(box((0.26, 10.3 + rng.uniform(-0.1, 0.1), 0.16), (x, -8.15 + rng.uniform(-0.05, 0.05), 0.34),
                     "plank" if i % 2 else "wood_light", bevel=0.04, segs=2))
    for s in (-1, 1):
        p.append(box((0.14, 10.4, 0.5), (s * 0.62, -8.15, 0.55), "wood", bevel=0.05, segs=2))
        for y in (-4.3, -7.0, -9.6, -12.2):
            p.append(box((0.16, 0.16, 0.9), (s * 0.77, y, 0.45), "wood_deep", bevel=0.05, segs=1))
            p += studs([(s * 0.86, y, 0.65)], r=0.045)
    p.append(box((1.38, 0.16, 0.5), (0, -13.3, 0.55), "wood", bevel=0.05, segs=2))
    join(p, "Frame", (0, 0, 0), smooth_angle=SMOOTH)

# ---------------------------------------------------------------- arm
def arm_wh(y):
    if y >= 0:
        t = min(y / 3.2, 1.0)
        return 0.8 + (0.64 - 0.8) * t, 1.0 + (0.74 - 1.0) * t
    t = min(-y / 12.45, 1.0)
    return 0.8 + (0.38 - 0.8) * t, 1.0 + (0.42 - 1.0) * t

def build_arm():
    z = AXLE.z
    p = [loft([(3.5, 0.64, 0.74, 0, z), (0, 0.8, 1.0, 0, z), (-12.45, 0.38, 0.42, 0, z)], "wood", bevel=0.12)]
    # fish plates bolted to both sides round the axle
    CH = 0.12
    for s in (-1, 1):
        secs = [(yy, CH, arm_wh(yy)[1] * hk, s * (arm_wh(yy)[0] / 2 + CH / 2 - 0.01), z)
                for yy, hk in ((2.3, 0.72), (0, 0.86), (-2.9, 0.7))]
        p.append(loft(secs, "wood_dark", bevel=0.04, segs=2))
        for yy in (-2.4, -1.4, 1.2, 1.9):
            w, h = arm_wh(yy)
            p += studs([(s * (w / 2 + CH + 0.01), yy, z + dz) for dz in (-0.22, 0.22)])
    # axle with hub plates and collars
    p.append(cyl(0.22, 5.56, AXLE, "iron_dark", rot=(0, 90, 0), verts=16, bevel=0.03))
    w0 = arm_wh(0)[0] / 2 + CH
    for s in (-1, 1):
        p.append(cyl(0.5, 0.07, (s * (w0 + 0.02), 0, z), "iron", rot=(0, 90, 0), verts=20, bevel=0.025, segs=1))
        p += studs([(s * (w0 + 0.07), math.cos(a) * 0.4, z + math.sin(a) * 0.4)
                    for a in (i * math.pi / 4 + math.pi / 8 for i in range(8))], r=0.05)
        for xx in (1.94, 2.66):
            p.append(ring_x(0.3, 0.12, (s * xx, 0, z), "iron"))
        p.append(box((0.05, 0.08, 0.5), (s * 2.74, 0, z), "iron_dark", bevel=0.015, segs=1))   # linch pin
    # iron bands (with rivets) and rope wraps along the arm
    for yy in (2.55, 1.7, -1.9, -3.3, -5.5, -7.8, -10.5, -11.65):
        w, h = arm_wh(yy)
        wb = w + (2 * CH if -2.9 < yy < 2.3 else 0)
        p.append(box((wb + 0.08, 0.16, h + 0.08), (0, yy, z), "iron", bevel=0.03, segs=1))
        p += studs([(sx * (wb / 2 + 0.045), yy, z + dz) for sx in (-1, 1) for dz in (-h * 0.22, h * 0.22)], r=0.045)
    for yy in (-4.4, -6.65, -9.15):
        w, h = arm_wh(yy)
        p += lash((0, yy, z), (0, 1, 0), w, h, turns=3, spacing=0.1, r=0.048, n=14)
    # hinge end: iron end strap and the counterweight pin
    w, h = arm_wh(3.2)
    p.append(box((w + 0.07, 0.62, h + 0.07), (0, 3.25, z), "iron", bevel=0.07, segs=2))
    p += studs([(sx * (w / 2 + 0.05), yy, z + dz) for sx in (-1, 1) for yy in (3.05, 3.42) for dz in (-0.24, 0.24)], r=0.045)
    p.append(cyl(0.1, 1.2, HINGE, "iron_dark", rot=(0, 90, 0), verts=12, bevel=0.02, segs=1))
    for s in (-1, 1):
        p.append(cyl(0.16, 0.07, (s * 0.58, HINGE.y, z), "iron", rot=(0, 90, 0), verts=12, bevel=0.025, segs=1))
    # tip: iron cap, release prong (sling hook), fixed sling ring, rope eye for the winch
    p.append(box((0.46, 0.5, 0.5), (0, -12.28, z), "iron", bevel=0.07, segs=2))
    p.append(cyl(0.065, 0.55, (0, -12.72, z + 0.1), "steel_dark", rot=(68, 0, 0), verts=8, bevel=0.02, r2=0.028))
    p.append(sphere(0.09, (0, -12.52, z + 0.02), "iron_dark", segs=8))
    p.append(torus(0.1, 0.03, (0, -12.3, z - 0.33), "iron", rot=(0, 90, 0), seg=12, minor_seg=6))
    p.append(torus(0.11, 0.032, (0, -10.5, 8.66), "iron", rot=(0, 90, 0), seg=12, minor_seg=6))
    join(p, "Arm", AXLE, smooth_angle=SMOOTH)

# ---------------------------------------------------------------- counterweight
def build_counterweight():
    cy = HINGE.y
    top, bot = HINGE.z - CW_DROP, HINGE.z - CW_DROP - CW_H
    BX, BY = CW_W / 2, CW_D / 2
    mid = (top + bot) / 2
    p = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(box((0.28, 0.28, CW_H + 0.08), (sx * (BX - 0.14), cy + sy * (BY - 0.14), mid), "wood_dark", bevel=0.08, segs=2))
    n = 5
    ph = CW_H / n
    for i in range(n):
        zz = bot + ph * (i + 0.5)
        col = ("wood", "plank", "wood_light", "plank", "wood")[i]
        for sy in (-1, 1):
            p.append(box((2 * BX - 0.24, 0.12, ph - 0.035), (rng.uniform(-0.02, 0.02), cy + sy * (BY - 0.07), zz),
                         col, rot=(0, rng.uniform(-0.6, 0.6), 0), bevel=0.04, segs=2))
        for sx in (-1, 1):
            p.append(box((0.12, 2 * BY - 0.24, ph - 0.035), (sx * (BX - 0.07), cy + rng.uniform(-0.02, 0.02), zz),
                         col, rot=(rng.uniform(-0.6, 0.6), 0, 0), bevel=0.04, segs=2))
    p.append(box((2 * BX, 2 * BY, 0.18), (0, cy, bot + 0.09), "wood_dark", bevel=0.06, segs=2))
    for sy in (-1, 1):
        p.append(box((2 * BX + 0.1, 0.22, 0.22), (0, cy + sy * (BY - 0.06), top), "wood_dark", bevel=0.07, segs=2))
    for sx in (-1, 1):
        p.append(box((0.22, 2 * BY + 0.1, 0.22), (sx * (BX - 0.06), cy, top), "wood_dark", bevel=0.07, segs=2))
    # iron corner brackets top and bottom, mid band round the box
    for sx in (-1, 1):
        for sy in (-1, 1):
            for zz in (top - 0.32, bot + 0.3):
                p.append(box((0.06, 0.55, 0.55), (sx * (BX + 0.02), cy + sy * (BY - 0.2), zz), "iron", bevel=0.02, segs=1))
                p.append(box((0.55, 0.06, 0.55), (sx * (BX - 0.2), cy + sy * (BY + 0.02), zz), "iron", bevel=0.02, segs=1))
                p += studs([(sx * (BX + 0.06), cy + sy * (BY - 0.3), zz), (sx * (BX - 0.3), cy + sy * (BY + 0.06), zz)], r=0.05)
    for sy in (-1, 1):
        p.append(box((2 * BX + 0.1, 0.05, 0.16), (0, cy + sy * (BY + 0.03), mid), "iron", bevel=0.02, segs=1))
    for sx in (-1, 1):
        p.append(box((0.05, 2 * BY + 0.1, 0.16), (sx * (BX + 0.03), cy, mid), "iron", bevel=0.02, segs=1))
    # stirrup: two iron straps from the hinge pin down the sides and under the floor
    for s in (-1, 1):
        p.append(tube(0.17, 0.1, 0.07, (s * 0.42, cy, HINGE.z), "iron", rot=(0, 90, 0), verts=12))
        a, b = Vector((s * 0.42, cy, HINGE.z - 0.15)), Vector((s * (BX + 0.06), cy, top + 0.08))
        p.append(timber(a, b, 0.07, "iron", bevel=0.02, depth=0.24, segs=1))
        p.append(box((0.07, 0.24, CW_H + 0.16), (s * (BX + 0.06), cy, mid), "iron", bevel=0.02, segs=1))
        p += studs([(s * (BX + 0.1), cy, zz) for zz in (top - 0.15, mid + 0.4, mid - 0.4, bot + 0.15)])
        p += studs([a.lerp(b, t) + Vector((s * 0.04, 0, 0)) for t in (0.35, 0.7)], r=0.045)
    p.append(box((2 * BX + 0.16, 0.24, 0.07), (0, cy, bot - 0.03), "iron", bevel=0.02, segs=1))
    # earth fill and a heap of stones; the heap stays under the arm's path in the loaded pose
    p.append(box((2 * BX - 0.3, 2 * BY - 0.3, 0.12), (0, cy, top - 0.02), "earth", bevel=0.04, segs=1))
    peak = 0.62
    placed = 0
    for i in range(80):
        if placed >= 26:
            break
        r = rng.uniform(0.2, 0.33)
        x = rng.uniform(-1, 1) * (BX - 0.16 - r * 0.8)
        yr = rng.uniform(-1, 1) * (BY - 0.16 - r * 0.8)
        hgt = peak * max(0.0, 1 - (x / 1.25) ** 2) * max(0.0, 1 - ((yr - 0.3) / 1.2) ** 2)
        zc = top + hgt + r * 0.1
        limit = HINGE.z - 0.75
        if yr < 0.25:
            limit = min(limit, HINGE.z + (yr - 0.25) * math.tan(LOADED) - 1.1)
        zc = min(zc, limit - r * 0.82)
        if zc < top - 0.02:
            continue
        p.append(rock(r, (x, cy + yr, zc), ("stone", "stone_mid", "stone_warm", "stone")[placed % 4]))
        placed += 1
    join(p, "Counterweight", HINGE, smooth_angle=SMOOTH)

# ---------------------------------------------------------------- windlass
def build_windlass():
    c = WIN
    p = [cyl(0.28, 4.0, c, "wood_dark", rot=(0, 90, 0), verts=16, bevel=0.05),
         cyl(0.12, 5.12, c, "iron_dark", rot=(0, 90, 0), verts=8, bevel=0)]
    for x in (-1.9, -0.9, 0.9, 1.62):
        p.append(ring_x(0.3, 0.1, (x, c.y, c.z), "iron"))
    for k in range(9):   # rope wound on the drum, second layer in the middle
        p.append(torus(0.322, 0.043, (-0.352 + k * 0.088, c.y, c.z), "rope", rot=(0, 90, 0), seg=18, minor_seg=6))
    for k in range(4):
        p.append(torus(0.4, 0.043, (-0.132 + k * 0.088, c.y, c.z), "rope_dark", rot=(0, 90, 0), seg=18, minor_seg=6))
    # ratchet wheel under the pawl
    p.append(ring_x(0.4, 0.1, (1.84, c.y, c.z), "iron", verts=16))
    for i in range(12):
        a = i * math.pi / 6
        p.append(box((0.1, 0.12, 0.1), (1.84, c.y + math.cos(a) * 0.42, c.z + math.sin(a) * 0.42), "iron_dark",
                     rot=(math.degrees(a) + 35, 0, 0), bevel=0))
    # capstan heads with crossed bars (crew behind the windlass)
    for s in (-1, 1):
        x = s * 1.45
        p.append(cyl(0.36, 0.32, (x, c.y, c.z), "wood", rot=(0, 90, 0), verts=12, bevel=0.05))
        for dx in (-0.13, 0.13):
            p.append(ring_x(0.38, 0.06, (x + dx, c.y, c.z), "iron", verts=12))
        for a in (45, 135):
            p.append(box((0.1, 2 * BAR_R, 0.11), (x, c.y, c.z), "wood_light", rot=(a, 0, 0), bevel=0.04, segs=2))
            for e in (-1, 1):
                ang = math.radians(a)
                p.append(cyl(0.07, 0.24, (x, c.y + e * math.cos(ang) * (BAR_R - 0.12), c.z + e * math.sin(ang) * (BAR_R - 0.12)), "leather",
                             rot=(a - 90, 0, 0), verts=6, bevel=0))
    # spoked hand wheels outside the posts
    for s in (-1, 1):
        x = s * 2.42
        p.append(cyl(0.16, 0.22, (x, c.y, c.z), "iron", rot=(0, 90, 0), verts=10, bevel=0.03, segs=1))
        p.append(torus(0.46, 0.05, (x, c.y, c.z), "wood", rot=(0, 90, 0), seg=18, minor_seg=5))
        for i in range(6):
            a = i * math.pi / 3
            dy, dz = math.cos(a), math.sin(a)
            p.append(timber((x, c.y + dy * 0.12, c.z + dz * 0.12), (x, c.y + dy * 0.55, c.z + dz * 0.55), 0.07, "wood",
                            bevel=0.025, segs=1))
            p.append(cyl(0.05, 0.13, (x, c.y + dy * 0.6, c.z + dz * 0.6), "wood_light",
                         rot=(math.degrees(a) - 90, 0, 0), verts=6, bevel=0, r2=0.04))
        p += studs([(x + s * 0.12, c.y + math.cos(a) * 0.09, c.z + math.sin(a) * 0.09) for a in (0.5, 2.6, 4.7)], r=0.035)
    join(p, "Windlass", c, smooth_angle=SMOOTH)

# ---------------------------------------------------------------- sling pouch
def build_pouch():
    c = Vector((0, -12.5, 0.6))
    rim = c.z + 0.13
    p = [dome(0.5, (c.x, c.y, rim), "leather", scale=(0.72, 0.95, 0.52), rot=(180, 0, 0), segs=16, thick=0.04)]
    p.append(loop_tube([(math.cos(a) * 0.37, c.y + math.sin(a) * 0.49, rim) for a in (i * math.pi / 10 for i in range(20))],
                       0.035, "wood_deep"))
    for sy in (-1, 1):
        p.append(torus(0.07, 0.02, (0, c.y + sy * 0.53, rim + 0.03), "iron", rot=(0, 90, 0), seg=10, minor_seg=6))
    me = p[0].data   # stitched seams laid onto the faceted leather underside
    tree = BVHTree.FromPolygons([v.co for v in me.vertices], [f.vertices for f in me.polygons])
    for sx in (-1, 1):
        pts = []
        for k in range(9):
            hit = tree.ray_cast(Vector((sx * 0.16, c.y + (k / 4 - 1) * 0.38, c.z - 1)), Vector((0, 0, 1)))[0]
            pts.append(hit + Vector((0, 0, -0.004)))
        p.append(loop_tube(pts, 0.012, "wood_deep", cyclic=False))
    join(p, "Pouch", c, smooth_angle=SMOOTH)

# ---------------------------------------------------------------- build
def build_trebuchet():
    reset()
    build_frame()
    build_arm()
    build_counterweight()
    build_windlass()
    build_pouch()
    for name, loc in (("SlingTip", (0, -12.5, 9.0)), ("RopeArm", (0, -10.5, 8.7)), ("RopeWinch", (0, -5.0, 1.6)),
                      ("KnightSpot", (3.4, -3.0, 0)), ("CrewSpot1", (-2.7, -5.0, 0)), ("CrewSpot2", (2.7, -5.0, 0)),
                      ("CrewSpot3", (-1.6, -7.0, 0)), ("CrewSpot4", (1.6, -7.0, 0)), ("AmmoSpot", (-4.0, -1.0, 0)),
                      ("BannerSpot", (2.4, -6.0, 0.6)), ("CampSpot", (0, -18.0, 0))):
        empty(name, loc)
    for o in bpy.data.objects:
        if o.type == "MESH":
            print("  %-14s tris %6d" % (o.name, sum(len(f.vertices) - 2 for f in o.data.polygons)))
    bpy.context.view_layer.update()   # export() reads matrix_world; fresh empties are stale until an update
    export(os.path.join(OUT, "Trebuchet.fbx"))

build_trebuchet()
