# Builds the enemy army and its camp into Assets/Siege/Models/Army.fbx: a life-size footman split into body parts
# (+ sword, spear, round shield), the siege kit (Ladder, Ram) and the war camp (Tent, Tent_Ridge, Campfire, Crate_Stack).
# Run: blender -b -P Tools/blender/build_army.py -- <project_root>
# Names and pivots follow Tools/CONTRACTS.md (Stream B). The footman keeps the knight's rounded-toy look at life size
# but is lofted from low-sided rings, because ~25 are on screen (body + one weapon + shield stays under 2k tris).
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from siege_common import *   # noqa: F401,F403  (palette, primitives, join/empty/export)
import bmesh, math
from mathutils import Vector, Matrix, Euler

# Convention: Blender +Y = model front, +X = the soldier's right hand, +Z = up.
C.update({
    "skin": swatch(14, 1), "gambeson": swatch(13, 1), "hose": swatch(18, 6), "boot": swatch(16, 8),
    "boot_cuff": swatch(16, 7), "belt": swatch(16, 9), "hair": swatch(16, 8), "mail": swatch(17, 5),
    "cream": swatch(13, 0), "canvas": swatch(13, 1), "ash": swatch(17, 8), "ember": swatch(14, 6),
    "burlap": swatch(14, 2), "shadow": swatch(16, 9), "stone_mid": swatch(17, 5), "stew": swatch(16, 6),
    "blade": swatch(17, 2),
})

# ---------------------------------------------------------------- multi-colour mesh helpers
KEYS = []

def key_id(k):
    if k not in KEYS:
        KEYS.append(k)
    return KEYS.index(k)

def _mesh(bm, name="part"):
    """bmesh with a per-face 'col' palette layer -> object whose UVs point at each face's swatch."""
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    uv = me.uv_layers.new(name="UVMap")
    col = me.attributes["col"]
    for poly in me.polygons:
        u, v = C[KEYS[col.data[poly.index].value]]
        for li in poly.loop_indices:
            uv.data[li].uv = (u, v)
    me.attributes.remove(me.attributes["col"])
    return o

def lathe(rings, color, sides=10, loc=(0, 0, 0), rot=(0, 0, 0), mat=None, side_color=None, lip=None,
          loop=False, caps=(True, True), cap_keys=None, wobble=None):
    """Loft of rings (rx, ry, z[, cx, cy]) along local Z; a zero-radius ring is a pole. Face i of every band is
    centred on angle 90 + i*360/sides degrees, so face 0 looks down +Y. color: one key or one per band;
    side_color(i, j, key) recolours single faces (stripes, tabards); lip=(keys, d) lifts the faces of those
    colours by d so a cloth panel gets a real edge; wobble(a, j) scales the radius of ring j at angle a."""
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("col")
    step = 2 * math.pi / sides
    a0 = math.pi / 2 - step / 2
    rows = []
    for j, ring in enumerate(rings):
        rx, ry, z = ring[:3]
        cx, cy = (ring[3], ring[4]) if len(ring) > 3 else (0.0, 0.0)
        if rx == 0 and ry == 0:
            rows.append([bm.verts.new((cx, cy, z))])
        else:
            w = (lambda a: wobble(a, j)) if wobble else (lambda a: 1.0)
            rows.append([bm.verts.new((cx + rx * w(a) * math.cos(a), cy + ry * w(a) * math.sin(a), z))
                         for a in (a0 + i * step for i in range(sides))])
    nseg = len(rows) if loop else len(rows) - 1
    keys = list(color) if isinstance(color, (list, tuple)) else [color] * nseg
    sides_f = []
    for j in range(nseg):
        A, B = rows[j], rows[(j + 1) % len(rows)]
        for i in range(sides):
            i2 = (i + 1) % sides
            if len(A) == 1 and len(B) == 1:
                continue
            vs = (A[0], B[i2], B[i]) if len(A) == 1 else (A[i], A[i2], B[0]) if len(B) == 1 else (A[i], A[i2], B[i2], B[i])
            f = bm.faces.new(vs)
            k = keys[j]
            if side_color:
                k = side_color(i, j, k) or k
            f[lay] = key_id(k)
            sides_f.append(f)
    closed = loop
    if not loop:
        ck = cap_keys or (keys[0], keys[-1])
        closed = True
        for row, nxt, cap, k in ((rows[0], rows[1], caps[0], ck[0]), (rows[-1], rows[-2], caps[1], ck[1])):
            if len(row) > 1 and cap:
                up = row[0].co.z > nxt[0].co.z
                f = bm.faces.new(row if up else list(reversed(row)))
                f[lay] = key_id(k)
            elif len(row) > 1:
                closed = False
    bm.normal_update()
    if closed:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    else:
        # open loft: side faces point away from the axis (the profile may run either way)
        s = sum(f.normal.dot(Vector((f.calc_center_median().x, f.calc_center_median().y, 0))) * f.calc_area() for f in sides_f)
        if s < 0:
            for f in sides_f:
                f.normal_flip()
    if lip:
        lk, d = lip
        ids = {key_id(k) for k in lk}
        region = [f for f in bm.faces if f[lay] in ids]
        if region:
            before = set(bm.faces)
            ret = bmesh.ops.extrude_face_region(bm, geom=region)
            new_faces = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMFace)]
            new_verts = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMVert)]
            bmesh.ops.delete(bm, geom=region, context="FACES_ONLY")
            bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context="EDGES")
            bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
            nf = set(new_faces)
            bm.normal_update()
            for v in new_verts:
                n = Vector()
                for f in v.link_faces:
                    if f in nf:
                        n += f.normal
                v.co += n.normalized() * d
            for f in bm.faces:
                if f not in before and f not in nf:      # rim of the lifted panel takes the panel's colour
                    for e in f.edges:
                        nb = [g for g in e.link_faces if g in nf]
                        if nb:
                            f[lay] = nb[0][lay]
                            break
            bm.normal_update()
    M = mat if mat is not None else Matrix.Translation(Vector(loc)) @ Euler(rad(rot)).to_matrix().to_4x4()
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts[:])
    return _mesh(bm)

def blob(center, radii, color, sides=6, rings=3, rot=(0, 0, 0)):
    """Low-poly ellipsoid (eyes, stones, sacks' tufts, embers)."""
    rx, ry, rz = radii
    pts = [(0, 0, -rz)] + [(rx * math.sin(math.pi * k / rings), ry * math.sin(math.pi * k / rings),
                            -rz * math.cos(math.pi * k / rings)) for k in range(1, rings)] + [(0, 0, rz)]
    return lathe(pts, color, sides=sides, loc=center, rot=rot)

def axis_mat(p0, p1):
    a, b = Vector(p0), Vector(p1)
    q = Vector((0, 0, 1)).rotation_difference((b - a).normalized())
    return Matrix.Translation(a) @ q.to_matrix().to_4x4(), (b - a).length

def rod(p0, p1, r, color, sides=6, r1=None, caps=(True, True), cap_keys=None):
    M, L = axis_mat(p0, p1)
    r1 = r if r1 is None else r1
    return lathe([(r, r, 0), (r1, r1, L)], color, sides=sides, mat=M, caps=caps, cap_keys=cap_keys)

def strand(points, r, color, sides=5, r1=None, cap=None):
    """Tube along a polyline (rope loops, crooked branch stubs); rings are mitred at the joints, the ends left open
    (buried in whatever they grow from) unless cap names a colour for the last end."""
    pts = [Vector(p) for p in points]
    r1 = r if r1 is None else r1
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("col")
    tans = [((pts[min(k + 1, len(pts) - 1)] - pts[k]).normalized() + (pts[k] - pts[max(k - 1, 0)]).normalized()).normalized()
            for k in range(len(pts))]
    ref = tans[0].orthogonal().normalized()
    rows = []
    for k, (p, t) in enumerate(zip(pts, tans)):
        ref = (ref - t * ref.dot(t)).normalized()                     # parallel-transport the frame along the path
        side = t.cross(ref)
        rr = r + (r1 - r) * k / (len(pts) - 1)
        rows.append([bm.verts.new(p + (ref * math.cos(a) + side * math.sin(a)) * rr)
                     for a in (2 * math.pi * i / sides for i in range(sides))])
    for A, B in zip(rows, rows[1:]):
        for i in range(sides):
            bm.faces.new((A[i], A[(i + 1) % sides], B[(i + 1) % sides], B[i]))[lay] = key_id(color)
    bm.normal_update()
    mid = lambda f: f.calc_center_median()
    if sum(f.normal.dot(mid(f) - min(pts, key=lambda q: (q - mid(f)).length)) for f in bm.faces) < 0:
        for f in bm.faces:
            f.normal_flip()
    if cap:
        f = bm.faces.new(rows[-1])
        f[lay] = key_id(cap)
        f.normal_update()
        if f.normal.dot(tans[-1]) < 0:
            f.normal_flip()
    return _mesh(bm)

def hullp(points, color):
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("col")
    for p in points:
        bm.verts.new(p)
    ret = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    junk = [g for g in ret["geom_interior"] + ret["geom_unused"] if isinstance(g, bmesh.types.BMVert)]
    if junk:
        bmesh.ops.delete(bm, geom=junk, context="VERTS")
    for f in bm.faces:
        f[lay] = key_id(color)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return _mesh(bm)

def sheet(polys, thick=0.025):
    """Thin cloth from [(points, key)] polygons; shared corners are welded, then solidified both ways."""
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("col")
    for pts, k in polys:
        f = bm.faces.new([bm.verts.new(p) for p in pts])
        f[lay] = key_id(k)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    o = _mesh(bm)
    activate(o)
    m = o.modifiers.new("solid", "SOLIDIFY")
    m.thickness = thick
    m.offset = 0
    bpy.ops.object.modifier_apply(modifier="solid")
    return o

def ring_angles(sides):
    step = 2 * math.pi / sides
    return [math.pi / 2 - step / 2 + i * step for i in range(sides)]

# ---------------------------------------------------------------- footman (enemy infantry)
TAB_F, TAB_B = {9, 0, 1}, {4, 5, 6}           # front / back faces of a 10-sided loft
SHIELD_OFF, SHIELD_TURN = Vector((-0.04, 0.06, 0.0)), 25.0     # disc centre from its pivot; yaw toward the centre line

def build_soldier():
    def tabard(red_rows, trim_rows=()):
        def f(i, j, k):
            if i in TAB_F or i in TAB_B:
                if j in trim_rows:
                    return "gold"
                if j in red_rows:
                    return "red"
            return None
        return f

    # Hips: padded gambeson skirt carrying the tabard's lower half (gold hem), belt and buckle. The hem is short and
    # flared deep front-to-back so a 30 degree marching stride leaves below it instead of cutting through the tabard.
    hp = [lathe([(0.168, 0.132, 1.02), (0.198, 0.16, 0.93), (0.232, 0.218, 0.81), (0.244, 0.256, 0.73),
                 (0.246, 0.262, 0.71), (0.14, 0.14, 0.70), (0, 0, 0.704)],
                "gambeson", sides=10, side_color=tabard((0, 1, 2), (3,)), lip=({"red", "gold"}, 0.012), caps=(False, True)),
          lathe([(0.184, 0.146, 0.952), (0.206, 0.166, 0.982), (0.184, 0.146, 1.014)], "belt", sides=10, loop=True),
          box((0.07, 0.03, 0.055), (0, 0.17, 0.983), "iron", bevel=0)]
    join(hp, "Soldier_Hips", (0, 0, 0.95), smooth_angle=60)

    # Torso: gambeson body with the red tabard lifted off it (one horizontal cream band, no symbols), mail mantle
    # flared out over the shoulder caps
    tp = [lathe([(0.158, 0.125, 0.92), (0.172, 0.135, 1.02), (0.205, 0.152, 1.16, 0, 0.008), (0.222, 0.162, 1.28, 0, 0.012),
                 (0.215, 0.155, 1.38, 0, 0.006), (0.175, 0.13, 1.45), (0.11, 0.092, 1.50), (0, 0, 1.515)],
                "gambeson", sides=10, side_color=lambda i, j, k: ("cream" if j == 2 else "red") if (i in TAB_F or i in TAB_B) and j < 6 else None,
                lip=({"red", "cream"}, 0.012), caps=(False, True)),
          lathe([(0.27, 0.2, 1.39), (0.168, 0.14, 1.49), (0.1, 0.094, 1.55)], "mail",
                sides=10, caps=(False, False))]
    join(tp, "Soldier_Torso", (0, 0, 1.05), smooth_angle=60)

    # Head: round face in a mail coif under a steel kettle hat (domed crown, wide drooping brim)
    hd = [lathe([(0.085, 0.085, 1.47), (0.096, 0.098, 1.53), (0.128, 0.13, 1.60, 0, -0.014), (0.134, 0.136, 1.675, 0, -0.014),
                 (0.122, 0.124, 1.745, 0, -0.008), (0, 0, 1.775)], "mail", sides=10, caps=(False, True)),
          blob((0, 0.06, 1.632), (0.09, 0.09, 0.1), "skin", sides=8, rings=4),
          blob((0, 0.156, 1.622), (0.02, 0.022, 0.025), "skin", sides=6, rings=3),
          blob((0, 0.146, 1.597), (0.05, 0.016, 0.014), "hair", sides=6, rings=2),
          lathe([(0, 0, 1.72), (0.135, 0.135, 1.723), (0.222, 0.222, 1.683), (0.244, 0.244, 1.69), (0.234, 0.234, 1.707),
                 (0.15, 0.15, 1.745), (0.148, 0.148, 1.785), (0.13, 0.13, 1.836), (0.085, 0.085, 1.874), (0, 0, 1.893)],
                ["steel_dark", "steel_dark", "steel", "steel", "steel", "steel", "steel", "steel", "steel"], sides=12)]
    for x in (-0.037, 0.037):
        hd.append(blob((x, 0.14, 1.654), (0.014, 0.01, 0.02), "black", sides=5, rings=3))
    join(hd, "Soldier_Head", (0, 0, 1.50), smooth_angle=60)

    # Arms: puffy quilted sleeves hanging straight down (shoulder cap tucked under the mail mantle), leather gauntlets;
    # the fist sits forward on the weapon grip (0.24, 0.06, 0.78) so the grip runs through it.
    for side, x in (("L", -0.24), ("R", 0.24)):
        ap = [lathe([(0, 0, 1.465), (0.066, 0.064, 1.455), (0.09, 0.086, 1.41), (0.075, 0.072, 1.345), (0.084, 0.08, 1.285),
                     (0.07, 0.068, 1.215), (0.078, 0.075, 1.145), (0.066, 0.064, 1.075), (0.062, 0.06, 0.99)], "gambeson",
                    sides=8, loc=(x, 0, 0), caps=(True, False)),
              lathe([(0.07, 0.068, 1.0), (0.078, 0.076, 0.95), (0.06, 0.062, 0.89, 0, 0.01), (0.064, 0.074, 0.81, 0, 0.045),
                     (0.05, 0.06, 0.745, 0, 0.05), (0, 0, 0.728, 0, 0.045)],
                    ["boot_cuff", "boot_cuff", "boot", "boot", "boot"], sides=8, loc=(x, 0, 0), caps=(False, True))]
        join(ap, "Soldier_Arm" + side, (x, 0, 1.42), smooth_angle=60)

    # Legs: sturdy hose, turned-down leather boots
    for side, x in (("L", -0.10), ("R", 0.10)):
        lp = [lathe([(0.08, 0.08, 0.96), (0.096, 0.098, 0.85), (0.084, 0.088, 0.68), (0.07, 0.074, 0.52, 0, 0.006),
                     (0.074, 0.078, 0.42, 0, -0.008), (0.062, 0.066, 0.30)], "hose", sides=8, loc=(x, 0, 0), caps=(False, False)),
              lathe([(0.066, 0.07, 0.05), (0.078, 0.082, 0.14, 0, 0.005), (0.074, 0.078, 0.25), (0.09, 0.093, 0.27),
                     (0.092, 0.095, 0.345), (0.07, 0.074, 0.345)], ["boot", "boot", "boot_cuff", "boot_cuff", "boot_cuff"],
                    sides=8, loc=(x, 0, 0), caps=(False, False)),
              lathe([(0, 0, -0.105), (0.06, 0.058, -0.07), (0.066, 0.062, 0.035, 0, 0.004), (0.058, 0.048, 0.14, 0, 0.012),
                     (0, 0, 0.18, 0, 0.014)], "boot", sides=6, loc=(x, 0.025, 0.062), rot=(-90, 0, 0))]
        join(lp, "Soldier_Leg" + side, (x, 0, 0.92), smooth_angle=60)

    # Sword: grip in the right fist, broad blade forward/up at 45 degrees
    G = Vector((0.24, 0.06, 0.78))
    d = Vector((0, math.cos(math.radians(45)), math.sin(math.radians(45))))
    n = Vector((0, -d.z, d.y))
    X = Vector((1, 0, 0))
    P = lambda t, x=0.0, k=0.0: G + d * t + X * x + n * k
    sw = [hullp([P(0.1, 0.029), P(0.1, -0.029), P(0.1, 0, 0.008), P(0.1, 0, -0.008),
                 P(0.69, 0.024), P(0.69, -0.024), P(0.69, 0, 0.007), P(0.69, 0, -0.007), P(0.8)], "blade"),
          rod(P(0.09, -0.1), P(0.09, 0.1), 0.019, "iron_dark", sides=6),
          rod(P(-0.09), P(0.08), 0.018, "leather", sides=6),
          blob(P(-0.11), (0.032, 0.032, 0.032), "iron", sides=6, rings=3)]
    join(sw, "Soldier_Sword", tuple(G), smooth_angle=40)

    # Spear: vertical, butt on the ground, leaf head with a red tassel
    S = Vector((0.24, 0.06, 0.80))
    sp = [lathe([(0.025, 0.025, 0.07), (0.024, 0.024, 1.2), (0.021, 0.021, 2.17), (0.028, 0.028, 2.18), (0.02, 0.02, 2.27)],
                ["wood", "wood", "iron_dark", "iron_dark"], sides=6, loc=(S.x, S.y, 0), caps=(False, False)),
          lathe([(0, 0, 0.0), (0.024, 0.024, 0.015), (0.028, 0.028, 0.09)], "iron_dark", sides=6, loc=(S.x, S.y, 0), caps=(False, True)),
          lathe([(0.029, 0.029, 0.72), (0.029, 0.029, 0.88)], "leather", sides=6, loc=(S.x, S.y, 0), caps=(False, False)),
          hullp([(S.x + 0.015, S.y, 2.25), (S.x - 0.015, S.y, 2.25), (S.x, S.y + 0.015, 2.25), (S.x, S.y - 0.015, 2.25),
                 (S.x + 0.052, S.y, 2.34), (S.x - 0.052, S.y, 2.34), (S.x, S.y + 0.011, 2.34), (S.x, S.y - 0.011, 2.34),
                 (S.x, S.y, 2.52)], "blade"),
          blob((S.x, S.y, 2.11), (0.042, 0.042, 0.065), "red", sides=6, rings=3)]
    join(sp, "Soldier_Spear", tuple(S), smooth_angle=50)

    # Shield: round, red field inside a cream band, iron rim and boss; turned toward the body's centre line and held
    # forward/out of its pivot on two forearm straps, so the disc clears the belt and fist (and small back-swings).
    SH = Vector((-0.33, 0.10, 1.0))
    turn = math.radians(SHIELD_TURN)
    nrm = Vector((-math.sin(turn), math.cos(turn), 0))
    M = Matrix.Translation(SH + SHIELD_OFF) @ Matrix.Rotation(turn, 4, "Z") @ Matrix.Rotation(math.radians(-90), 4, "X")
    sh = [lathe([(0, 0, -0.016), (0.27, 0.27, -0.016), (0.298, 0.298, 0.0), (0.28, 0.28, 0.018), (0.228, 0.228, 0.022),
                 (0.085, 0.085, 0.03), (0.07, 0.07, 0.052), (0, 0, 0.062)],
                ["wood_dark", "iron", "iron", "cream", "red", "iron", "iron"], sides=14, mat=M)]
    for z in (0.93, 1.07):
        f = Vector((-0.245, 0.03, z))                                  # inside the forearm
        reach = (SH + SHIELD_OFF - nrm * 0.016 - f).dot(nrm) + 0.008    # to just inside the shield's back
        sh.append(box((0.07, reach, 0.03), f + nrm * (reach / 2), "leather", rot=(0, 0, SHIELD_TURN), bevel=0))
    join(sh, "Soldier_Shield", tuple(SH), smooth_angle=40)

# ---------------------------------------------------------------- siege kit
def build_ladder():
    p = []
    for x in (-0.3, 0.3):
        p.append(lathe([(0.05, 0.05, 0.0), (0.052, 0.05, 2.6, 0.004, 0.003), (0.05, 0.049, 5.1, -0.003, 0.0),
                        (0.045, 0.045, 7.5)], "wood", sides=6, loc=(x, 0, 0)))
        p.append(lathe([(0.06, 0.06, 7.32), (0.068, 0.068, 7.35), (0.06, 0.06, 7.38)], "rope", sides=6, loc=(x, 0, 0), loop=True))
        p.append(beam((x, 0.0, 7.36), (x, 0.0, 7.62), 0.05, "iron_dark", bevel=0))
        p.append(beam((x, -0.01, 7.6), (x, 0.17, 7.5), 0.045, "iron_dark", bevel=0))
    for k in range(1, 22):
        p.append(rod((-0.305, 0, 0.35 * k), (0.305, 0, 0.35 * k), 0.032, "wood_light" if k % 3 else "wood", sides=6, caps=(False, False)))
    return join(p, "Ladder", (0, 0, 0), smooth_angle=65)

def build_ram():
    # Log: tapers from the butt (r 0.25) to the iron sleeve (r 0.29), bows a few cm and has an uneven, hand-hewn
    # section (two low-frequency wobbles per ring) so it reads as a trunk rather than a pipe. Loft Z -> +Y.
    ys = [-3.0, -2.93, -2.25, -1.5, -0.75, 0.0, 0.75, 1.55, 2.45]
    bow = [(0, 0), (0, 0), (0.012, 0.008), (0.022, 0.016), (0.027, 0.02), (0.024, 0.018), (0.015, 0.012), (0.006, 0.005), (0, 0)]
    amp = [0.6, 1, 1, 1, 1, 1, 1, 0.5, 0]
    rings, phase = [], []
    for j, y in enumerate(ys):
        r = 0.236 if j == 0 else 0.25 + 0.04 * (y + 2.93) / 5.38
        e = rng.uniform(0.95, 1.0)
        phase.append((rng.uniform(0, 2 * math.pi), rng.uniform(0, 2 * math.pi)))
        rings.append((r, r * e, y, bow[j][0], -bow[j][1]))          # loft (x, y) -> world (x, -z)
    wob = lambda a, j: 1 + amp[j] * (0.022 * math.sin(2 * a + phase[j][0]) + 0.014 * math.sin(3 * a + phase[j][1])) if j < len(ys) else 1.0
    toY = Matrix.Rotation(math.radians(-90), 4, "X")

    def at(y):
        """World centre (x, z) and radii (rx, rz) of the log at y."""
        for r0, r1 in zip(rings, rings[1:]):
            if r0[2] <= y <= r1[2]:
                t = (y - r0[2]) / (r1[2] - r0[2])
                L = lambda k: r0[k] + (r1[k] - r0[k]) * t
                return L(3), -L(4), L(0), L(1)

    def hoop(y, prof, color):
        """Band round the log at y; prof = [(radius scale, radius add, dy)], sunk below the wobble at its edges."""
        cx, cz, rx, rz = at(y)
        return lathe([(rx * k + d, rz * k + d, dy) for k, d, dy in prof], color, sides=12, loop=True,
                     mat=Matrix.Translation((cx, y, cz)) @ toY)

    # iron-shod head: a riveted sleeve ending in a crowned, bevelled striking cap
    head = [(0.3, 0.3, 2.45), (0.318, 0.318, 2.51), (0.318, 0.318, 2.8), (0.296, 0.296, 2.875), (0.238, 0.238, 2.94),
            (0.15, 0.15, 2.972), (0, 0, 2.985)]
    keys = ["wood"] * (len(rings) - 1) + ["iron"] * (len(head) - 2) + ["steel_dark"] * 2
    bark = lambda i, j, k: "plank" if k == "wood" and (i * 5 + j // 3 * 7) % 4 == 0 else None     # darker bark furrows
    p = [lathe(rings + head, keys, sides=12, mat=toY, cap_keys=("wood_light", "iron"), wobble=wob, side_color=bark)]
    for y in (-2.72, -1.15, 0.35, 1.9):
        p.append(hoop(y, [(0.95, 0, -0.05), (1.04, 0.012, 0.0), (0.95, 0, 0.05)], "iron"))
        cx, cz, rx, rz = at(y)
        for a in (30, 150, 270):
            r = math.radians(a)
            p.append(blob((cx + math.cos(r) * (rx * 1.04 + 0.008), y, cz + math.sin(r) * (rz * 1.04 + 0.008)), (0.02, 0.02, 0.02),
                          "rivet", sides=6, rings=2))
    cx, cz, rx, rz = at(2.2)
    for a in (45, 135, 225, 315):
        r = math.radians(a)
        rs = (rx + rz) / 2 + 0.006
        p.append(box((0.07, 0.5, 0.018), (math.cos(r) * rs, 2.27, math.sin(r) * rs), "iron", rot=(0, 90 - a, 0), bevel=0))
        p.append(blob((math.cos(r) * 0.318, 2.7, math.sin(r) * 0.318), (0.024, 0.024, 0.024), "rivet", sides=6, rings=2))
        p.append(blob((math.cos(r) * (rs + 0.009), 2.1, math.sin(r) * (rs + 0.009)), (0.02, 0.02, 0.02), "rivet", sides=6, rings=2))
    # rope wraps, each with a loop hanging on either side for a carrier's hand
    for y in (-1.95, -0.4, 1.15):
        p.append(hoop(y, [(0.96, 0, -0.055), (1.05, 0.012, -0.035), (1.05, 0.012, 0.035), (0.96, 0, 0.055)], "rope"))
        cx, cz, rx, rz = at(y)
        for sx in (-1, 1):
            b = Vector((cx + sx * rx * 0.8, y, cz - rz * 0.6))
            p.append(strand([b + Vector(o) for o in ((-sx * 0.04, -0.03, 0.03), (sx * 0.035, -0.06, -0.09), (sx * 0.05, -0.055, -0.17),
                                                     (sx * 0.055, 0, -0.21), (sx * 0.05, 0.055, -0.17), (sx * 0.035, 0.06, -0.09),
                                                     (-sx * 0.04, 0.03, 0.03))], 0.022, "rope", sides=5))
    # lopped branch stubs: crooked, tapering, pale cut ends
    for y, a, ln, r0 in ((-1.6, 40, 0.22, 0.068), (0.85, 130, 0.18, 0.058), (0.2, -12, 0.12, 0.048)):
        cx, cz, rx, rz = at(y)
        u = Vector((math.cos(math.radians(a)), 0, math.sin(math.radians(a))))
        root = Vector((cx, y, cz)) + Vector((u.x * rx, 0, u.z * rz)) * 0.75
        d = (u + Vector((0, 0.3, 0))).normalized()
        kink = d.cross(Vector((0, 1, 0))).normalized() * 0.03
        p.append(strand([root, root + d * (0.07 + ln * 0.5) + kink, root + d * (0.07 + ln)], r0, "wood", sides=6, r1=r0 * 0.6,
                        cap="wood_light"))
    return join(p, "Ram", (0, 0, 0), smooth_angle=40)

# ---------------------------------------------------------------- camp
def stripes(rows, a="red", b="cream"):
    return lambda i, j, k: (a if i % 2 == 0 else b) if j in rows else None

def dagged_ring(r, z_top, z_corner, z_tip, sides, a="cream", b="red"):
    """Dagged valance hanging round an eave: one pointed tongue per loft face, alternating colours."""
    angs = ring_angles(sides)
    polys = []
    for i in range(sides):
        a0, a1 = angs[i], angs[(i + 1) % sides]
        am = (a0 + a1) / 2 + (math.pi if a1 < a0 else 0)
        pt = lambda a, z, rr=r: (rr * math.cos(a), rr * math.sin(a), z)
        polys.append(([pt(a0, z_top), pt(a0, z_corner), pt(am, z_tip, r * 1.01), pt(a1, z_corner), pt(a1, z_top)], a if i % 2 else b))
    return sheet(polys, 0.03)

def dagged_line(x, y0, y1, z_top, z_corner, z_tip, n, a="red", b="red_dark", out=0.0):
    polys = []
    for i in range(n):
        ya, yb = y0 + (y1 - y0) * i / n, y0 + (y1 - y0) * (i + 1) / n
        polys.append(([(x, ya, z_top), (x, ya, z_corner), (x + out, (ya + yb) / 2, z_tip), (x, yb, z_corner), (x, yb, z_top)], a if i % 2 else b))
    return sheet(polys, 0.03)

def pennant(base, length, h0, color="red"):
    """Swallow-tailed streamer flying toward +X with a gentle wave."""
    bx, by, bz = base
    cols = []
    for t in (0.0, 0.3, 0.6, 0.82):
        h = h0 * (1 - 0.45 * t)
        y = by + 0.06 * math.sin(t * math.pi * 1.6)
        cols.append([(bx + t * length, y, bz + h / 2), (bx + t * length, y, bz), (bx + t * length, y, bz - h / 2)])
    polys = []
    for c0, c1 in zip(cols, cols[1:]):
        polys.append(([c0[0], c1[0], c1[1], c0[1]], color))
        polys.append(([c0[1], c1[1], c1[2], c0[2]], color))
    c = cols[-1]
    ty = by + 0.06 * math.sin(math.pi * 1.6)
    polys.append(([c[0], (bx + length, ty, bz + h0 * 0.22), (bx + length * 0.9, ty, bz), c[1]], color))
    polys.append(([c[1], (bx + length * 0.9, ty, bz), (bx + length, ty, bz - h0 * 0.22), c[2]], color))
    return sheet(polys, 0.02)

def guy(p_top, p_ground):
    return [rod(p_top, p_ground, 0.012, "rope", sides=4, caps=(False, False)),
            beam((p_ground[0], p_ground[1], -0.05), (p_ground[0] * 1.02, p_ground[1] * 1.02, 0.2), 0.06, "wood", bevel=0)]

def build_tent():
    p = [lathe([(2.06, 2.06, 0.0), (2.0, 2.0, 0.14), (1.96, 1.96, 1.58), (2.0, 2.0, 1.72), (2.32, 2.32, 1.78),
                (2.24, 2.24, 1.9), (1.5, 1.5, 2.55), (0.78, 0.78, 3.12), (0.2, 0.2, 3.6), (0, 0, 3.66)],
               ["red_dark", "red", "gold", "gold", "gold", "red", "red", "red", "red"], sides=16,
               side_color=stripes((1, 5, 6, 7, 8)), caps=(False, True)),
         dagged_ring(2.3, 1.77, 1.6, 1.47, 16, a="red", b="cream"),
         rod((0, 0, 3.5), (0, 0, 4.4), 0.045, "wood_dark", sides=6),
         blob((0, 0, 4.44), (0.08, 0.08, 0.08), "gold", sides=8, rings=3),
         pennant((0.03, 0, 4.22), 1.2, 0.34)]
    # door: dark opening with both flaps tied back
    p.append(hullp([(sx * 0.48, y, 0.0) for sx in (-1, 1) for y in (1.93, 2.02)] +
                   [(sx * 0.48, y, 1.2) for sx in (-1, 1) for y in (1.93, 2.02)] + [(0, y, 1.62) for y in (1.93, 2.02)], "shadow"))
    for sx in (-1, 1):
        p.append(hullp([(sx * 0.1, 2.04, 1.6), (sx * 0.5, 2.05, 0.02), (sx * 0.86, 1.88, 0.02),
                        (sx * 0.1, 2.0, 1.6), (sx * 0.5, 2.01, 0.02), (sx * 0.86, 1.84, 0.02)], "cream"))
        # rope tie cinched round the middle of the gathered flap
        p.append(lathe([(0.112, 0.024, -0.025), (0.124, 0.036, 0.0), (0.112, 0.024, 0.025)], "rope", sides=8, loop=True,
                       mat=Matrix.Translation((sx * 0.412, 1.98, 0.75)) @ Matrix.Rotation(math.atan2(-0.091, sx * 0.194), 4, "Z")))
    for a in (22.5 + 45 * k for k in range(8)):
        r = math.radians(a + 90)
        p += guy((math.cos(r) * 2.3, math.sin(r) * 2.3, 1.76), (math.cos(r) * 3.4, math.sin(r) * 3.4, 0.0))
    return join(p, "Tent", (0, 0, 0), smooth_angle=40)

def build_tent_ridge():
    W, L, H, E = 1.5, 2.0, 2.5, 0.68
    ys = [-L, -1.2, -0.4, 0.4, 1.2, L]
    bm = bmesh.new()
    lay = bm.faces.layers.int.new("col")
    grid = []
    for y in ys:
        sag = 0.03 + 0.07 * (1 - (y / L) ** 2)
        prof = [(-W - 0.06, 0.0), (-W, E), (-W / 2 + sag * 0.5, (E + H) / 2 - sag), (0, H), (W / 2 - sag * 0.5, (E + H) / 2 - sag),
                (W, E), (W + 0.06, 0.0)]
        grid.append([bm.verts.new((x, y, z)) for x, z in prof])
    for s in range(len(ys) - 1):
        for k in range(6):
            f = bm.faces.new((grid[s][k], grid[s + 1][k], grid[s + 1][k + 1], grid[s][k + 1]))
            f[lay] = key_id("canvas" if k in (0, 5) else ("red" if s % 2 else "cream"))
    for row, flip in ((grid[0], False), (grid[-1], True)):
        f = bm.faces.new(list(reversed(row)) if flip else row)
        f[lay] = key_id("cream")
    bm.normal_update()
    for f in bm.faces:                                   # outward: away from the tent's centre line
        c = f.calc_center_median()
        if f.normal.dot(Vector((c.x, c.y if abs(c.y) > L - 0.01 else 0, c.z - 0.8))) < 0:
            f.normal_flip()
    p = [_mesh(bm, "ridge")]
    for sx in (-1, 1):
        p.append(dagged_line(sx * (W + 0.02), -L, L, E + 0.04, E - 0.1, E - 0.22, 8))
    for y in (-L - 0.06, L + 0.06):
        p.append(rod((0, y, 0), (0, y, H + 0.24), 0.05, "wood_dark", sides=6))
        p.append(blob((0, y, H + 0.28), (0.065, 0.065, 0.065), "gold", sides=8, rings=3))
        p += guy((0, y, H + 0.12), (0, y * 1.75, 0))
    p.append(rod((0, -L - 0.2, H + 0.03), (0, L + 0.2, H + 0.03), 0.045, "wood_dark", sides=6))
    p.append(pennant((0.03, L + 0.06, H + 0.05), 0.9, 0.26))
    p.append(hullp([(sx * 0.62, y, 0.0) for sx in (-1, 1) for y in (L - 0.01, L + 0.03)] + [(0, y, 1.85) for y in (L - 0.01, L + 0.03)], "shadow"))
    for sx in (-1, 1):
        p.append(hullp([(sx * 0.06, L + 0.05, 1.8), (sx * 0.62, L + 0.05, 0.02), (sx * 0.95, L - 0.08, 0.02),
                        (sx * 0.06, L + 0.02, 1.8), (sx * 0.62, L + 0.02, 0.02), (sx * 0.95, L - 0.11, 0.02)], "cream"))
        for y in (-1.2, 0.0, 1.2):
            p += guy((sx * W, y, E), (sx * (W + 1.0), y, 0))
    return join(p, "Tent_Ridge", (0, 0, 0), smooth_angle=40)

def build_campfire():
    p = []
    for i in range(9):
        a = 2 * math.pi * i / 9 + rng.uniform(-0.12, 0.12)
        r = 0.62 + rng.uniform(-0.03, 0.03)
        s = rng.uniform(0.85, 1.15)
        p.append(blob((math.cos(a) * r, math.sin(a) * r, 0.05 * s), (0.17 * s, 0.13 * s, 0.11 * s),
                      ["stone", "stone_mid", "stone_dark"][i % 3], sides=7, rings=4, rot=(0, rng.uniform(-8, 8), math.degrees(a))))
    p.append(lathe([(0.56, 0.56, 0.0), (0.5, 0.5, 0.03), (0, 0, 0.04)], "ash", sides=12, caps=(False, True)))
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.3
        b = Vector((math.cos(a) * 0.42, math.sin(a) * 0.42, 0.06))
        t = Vector((math.cos(a) * 0.05, math.sin(a) * 0.05, 0.52))
        M, Lg = axis_mat(b, t)
        p.append(lathe([(0.062, 0.062, 0), (0.056, 0.056, Lg * 0.7), (0.05, 0.05, Lg)], ["wood_dark", "black"], sides=7, mat=M,
                       cap_keys=("wood_light", "black")))
    for i in range(6):
        a = 2 * math.pi * i / 6 + 0.5
        r = rng.uniform(0.12, 0.32)
        p.append(blob((math.cos(a) * r, math.sin(a) * r, 0.045), (0.05, 0.045, 0.03), ["ember", "red", "gold"][i % 3], sides=6, rings=2))
    # cooking spit with a hanging pot
    for sx in (-1, 1):
        p.append(rod((sx * 0.92, 0, 0), (sx * 0.92, 0, 0.98), 0.035, "wood_dark", sides=5))
        p.append(rod((sx * 0.92, 0, 0.78), (sx * 1.04, 0, 1.06), 0.026, "wood_dark", sides=5))
    p.append(rod((-1.08, 0, 0.98), (1.08, 0, 0.98), 0.03, "wood", sides=5))
    p.append(lathe([(0, 0, 0.6), (0.12, 0.12, 0.615), (0.165, 0.165, 0.69), (0.158, 0.158, 0.8), (0.145, 0.145, 0.83),
                    (0.128, 0.128, 0.82), (0, 0, 0.8)], ["iron_dark", "iron_dark", "iron_dark", "iron_dark", "iron_dark", "stew"], sides=10))
    for sx in (-1, 1):
        p.append(rod((sx * 0.148, 0, 0.82), (0, 0, 0.97), 0.008, "iron_dark", sides=4, caps=(False, False)))
    return join(p, "Campfire", (0, 0, 0), smooth_angle=50)

def crate(c, s, yaw):
    cx, cy, cz = c
    p = [box((s - 0.03, s - 0.03, s - 0.03), c, "wood_light", rot=(0, 0, yaw), bevel=0.02, segs=1),
         box((s - 0.02, s - 0.02, 0.025), c, "wood", rot=(0, 0, yaw), bevel=0)]
    for dz in (-1, 1):
        p.append(box((s, s, 0.07), (cx, cy, cz + dz * (s / 2 - 0.035)), "wood_dark", rot=(0, 0, yaw), bevel=0.015, segs=1))
    ya = math.radians(yaw)
    for sx in (-1, 1):
        for sy in (-1, 1):
            lx, ly = sx * (s / 2 - 0.03), sy * (s / 2 - 0.03)
            p.append(box((0.07, 0.07, s - 0.14), (cx + lx * math.cos(ya) - ly * math.sin(ya), cy + lx * math.sin(ya) + ly * math.cos(ya), cz),
                         "wood_dark", rot=(0, 0, yaw), bevel=0))
    return p

def sack(loc, rot=(0, 0, 0), s=1.0):
    return lathe([(0.2 * s, 0.18 * s, 0.0), (0.25 * s, 0.22 * s, 0.1 * s), (0.24 * s, 0.21 * s, 0.34 * s), (0.15 * s, 0.13 * s, 0.48 * s),
                  (0.055 * s, 0.05 * s, 0.54 * s), (0.075 * s, 0.07 * s, 0.58 * s), (0, 0, 0.63 * s)],
                 ["burlap", "burlap", "burlap", "burlap", "rope", "burlap"], sides=8, loc=loc, rot=rot)

SACK_Z = 0.936                     # the lying sack's belly rests on the small crate's lid (z 0.75)

def build_crates():
    p = crate((-0.45, 0.0, 0.4), 0.8, 4) + crate((0.42, 0.05, 0.375), 0.75, -8) + crate((-0.15, -0.02, 1.125), 0.65, 18)
    p.append(lathe([(0.25, 0.25, 0.0), (0.258, 0.258, 0.045), (0.287, 0.287, 0.2), (0.292, 0.292, 0.24), (0.292, 0.292, 0.52),
                    (0.287, 0.287, 0.56), (0.258, 0.258, 0.715), (0.25, 0.25, 0.76)],
                   ["iron", "wood", "iron", "wood", "iron", "wood", "iron"], sides=12, loc=(-1.25, -0.25, 0),
                   cap_keys=("wood_dark", "wood_light")))
    p += [sack((0.95, -0.6, 0)), sack((1.3, 0.05, 0), rot=(0, 0, 40), s=0.9), sack((0.55, -0.05, SACK_Z), rot=(0, 84, 20), s=0.85)]
    # two spears leaning on the back top edge of the big crate
    for x0, y0 in ((-0.80, 0.56), (-0.64, 0.572)):
        top = (x0 + 0.06, y0 - 0.43, 2.05)
        p.append(rod((x0, y0, 0), top, 0.018, "wood", sides=5))
        tv = Vector(top) + (Vector(top) - Vector((x0, y0, 0))).normalized() * 0.2
        p.append(hullp([Vector(top) + Vector(o) for o in ((0.025, 0, 0), (-0.025, 0, 0), (0, 0.01, 0), (0, -0.01, 0))] + [tv], "steel"))
    return join(p, "Crate_Stack", (0, 0, 0), smooth_angle=50)

# ---------------------------------------------------------------- build + export
def build_army():
    reset()
    build_soldier()
    # props are spread out along +X so nothing overlaps in the file; Unity re-centres them by name
    for x, b in ((3.0, build_ladder), (6.0, build_ram), (12.0, build_tent), (19.0, build_tent_ridge),
                 (25.0, build_campfire), (29.0, build_crates)):
        b().location.x += x
    bpy.context.view_layer.update()      # export() reads matrix_world, which is stale until the next update
    export(os.path.join(OUT, "Army.fbx"))

build_army()
