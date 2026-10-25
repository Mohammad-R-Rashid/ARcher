# Shared modelling helpers for the Siege AR Blender build scripts (import with `from siege_common import *`).
# Run any build script as: blender -b -P Tools/blender/<script>.py -- <project_root>
# Every part's UVs point at a single swatch of the ithappy palette atlas, so the models share the
# Kingdom pack's material (one batch) and match its soft, rounded toy look: generous bevels,
# smooth-by-angle shading, chunky proportions.
# Hierarchy is flattened on export and rebuilt in Unity by name (see SiegeAssetBuilder).
import bpy, bmesh, math, os, random, sys
from mathutils import Vector, Matrix

ROOT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.getcwd()
OUT = os.path.join(ROOT, "Assets", "Siege", "Models")
os.makedirs(OUT, exist_ok=True)
rng = random.Random(7)

CELL = 51.2 / 1024.0
def swatch(col, row):
    return ((col + 0.5) * CELL, 1.0 - (row + 0.5) * CELL)

C = {
    "wood": swatch(16, 4), "wood_dark": swatch(16, 6), "wood_light": swatch(16, 3), "wood_deep": swatch(16, 8),
    "plank": swatch(16, 5), "rope": swatch(16, 2), "rope_dark": swatch(16, 3), "leather": swatch(16, 7),
    "iron": swatch(18, 7), "iron_dark": swatch(18, 9), "rivet": swatch(18, 5),
    "steel": swatch(17, 3), "steel_mid": swatch(17, 4), "steel_dark": swatch(17, 6), "steel_blue": swatch(18, 3),
    "red": swatch(0, 6), "red_dark": swatch(0, 9), "red_light": swatch(0, 4),
    "gold": swatch(13, 5), "gold_dark": swatch(13, 7), "black": swatch(17, 9), "white": swatch(17, 0),
    # town
    "plaster": swatch(14, 1), "plaster_warm": swatch(13, 1), "stone": swatch(17, 4), "stone_dark": swatch(17, 6),
    "roof_red": swatch(0, 7), "roof_blue": swatch(5, 7), "roof_orange": swatch(15, 7), "roof_green": swatch(9, 7),
    "glass": swatch(5, 9), "leaf": swatch(9, 5), "fruit_red": swatch(0, 5), "fruit_orange": swatch(14, 5),
    "cloth_red": swatch(0, 5), "cloth_cream": swatch(13, 0), "cloth_blue": swatch(5, 5),
    "cream_feather": swatch(16, 1),
}

# ---------------------------------------------------------------- primitives
def reset():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    for block in (bpy.data.meshes, bpy.data.curves, bpy.data.objects):
        for b in list(block):
            if b.users == 0:
                block.remove(b)

def activate(o):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o

def set_uv(obj, color):
    me = obj.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    u, v = C[color]
    for d in me.uv_layers.active.data:
        d.uv = (u, v)

def finish(obj, color, bevel, segs=3):
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if bevel > 0:
        m = obj.modifiers.new("bevel", "BEVEL")
        m.width = bevel
        m.segments = segs
        m.limit_method = "ANGLE"
        m.angle_limit = math.radians(40)
        m.harden_normals = False
        bpy.ops.object.modifier_apply(modifier="bevel")
    set_uv(obj, color)
    return obj

def rad(rot):
    return [math.radians(a) for a in rot]

def box(size, loc, color, rot=(0, 0, 0), bevel=0.05, segs=3):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rad(rot))
    o = bpy.context.active_object
    o.scale = size
    return finish(o, color, min(bevel, min(size) * 0.45), segs)

def cyl(r, depth, loc, color, rot=(0, 0, 0), verts=16, bevel=0.03, r2=None, segs=2):
    if r2 is None:
        bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rad(rot))
    else:
        bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r, radius2=r2, depth=depth, location=loc, rotation=rad(rot))
    return finish(bpy.context.active_object, color, min(bevel, depth * 0.45), segs)

def sphere(r, loc, color, scale=(1, 1, 1), segs=14, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=max(6, segs // 2), radius=r, location=loc, rotation=rad(rot))
    o = bpy.context.active_object
    o.scale = scale
    return finish(o, color, 0)

def dome(r, loc, color, scale=(1, 1, 1), rot=(0, 0, 0), segs=16, thick=0.04):
    """Half sphere shell (open at -Z before rotation)."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=segs // 2, radius=r, location=(0, 0, 0))
    o = bpy.context.active_object
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-4], context="VERTS")
    bm.to_mesh(o.data); bm.free()
    m = o.modifiers.new("solid", "SOLIDIFY"); m.thickness = thick; m.offset = -1
    activate(o); bpy.ops.object.modifier_apply(modifier="solid")
    o.scale = scale
    o.rotation_euler = rad(rot)
    o.location = loc
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return finish(o, color, 0)

def tube(r, depth, thick, loc, color, rot=(0, 0, 0), verts=20):
    """Open band (hoop / tyre) of given outer radius."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, end_fill_type="NOTHING", location=(0, 0, 0))
    o = bpy.context.active_object
    m = o.modifiers.new("solid", "SOLIDIFY"); m.thickness = thick; m.offset = -1
    activate(o); bpy.ops.object.modifier_apply(modifier="solid")
    o.rotation_euler = rad(rot); o.location = loc
    return finish(o, color, min(0.02, thick * 0.4), 2)

def torus(major, minor, loc, color, rot=(0, 0, 0), seg=20, minor_seg=8):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=seg, minor_segments=minor_seg,
                                     location=loc, rotation=rad(rot))
    return finish(bpy.context.active_object, color, 0)

def beam(p0, p1, thick, color, bevel=0.06, depth=None, jitter=0.0):
    """Rectangular beam between two points; optional tiny jitter for a hand-built feel."""
    a, b = Vector(p0), Vector(p1)
    d = b - a
    bpy.ops.mesh.primitive_cube_add(size=1, location=(a + b) / 2)
    o = bpy.context.active_object
    o.scale = (thick, depth or thick, d.length)
    o.rotation_mode = "QUATERNION"
    q = Vector((0, 0, 1)).rotation_difference(d.normalized())
    if jitter:
        from mathutils import Euler
        q = q @ Euler((0, 0, rng.uniform(-jitter, jitter))).to_quaternion()
    o.rotation_quaternion = q
    return finish(o, color, min(bevel, thick * 0.4))

def sweep(points, radii, color, res=2, verts=10):
    """Tube swept along a poly path with per-point radius (limbs, plumes, ropes)."""
    cu = bpy.data.curves.new("sweep", "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = 1.0
    cu.bevel_resolution = res
    cu.use_fill_caps = True
    sp = cu.splines.new("POLY")
    sp.points.add(len(points) - 1)
    for p, (pt, r) in zip(sp.points, zip(points, radii)):
        p.co = (pt[0], pt[1], pt[2], 1)
        p.radius = r
    o = bpy.data.objects.new("sweep", cu)
    bpy.context.scene.collection.objects.link(o)
    activate(o)
    bpy.ops.object.convert(target="MESH")
    o = bpy.context.active_object
    return finish(o, color, 0)

def rope_wrap(center, axis_rot, radius, turns, color="rope", spacing=0.07, minor=0.035):
    return [torus(radius, minor, (center[0], center[1], center[2]), color, rot=axis_rot, seg=14, minor_seg=6)
            for _ in range(1)] + [torus(radius, minor, Vector(center) + Vector(axis_vec(axis_rot)) * spacing * (i + 1), color, rot=axis_rot, seg=14, minor_seg=6)
                                  for i in range(turns - 1)]

def axis_vec(rot):
    m = Matrix.Rotation(math.radians(rot[0]), 3, "X") @ Matrix.Rotation(math.radians(rot[1]), 3, "Y") @ Matrix.Rotation(math.radians(rot[2]), 3, "Z")
    return m @ Vector((0, 0, 1))

def rivets(points, color="rivet", r=0.045):
    return [sphere(r, p, color, segs=8) for p in points]

def join(objs, name, pivot, smooth_angle=40):
    activate(objs[0])
    for o in objs:
        o.select_set(True)
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth_angle))
    bpy.context.scene.cursor.location = pivot
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return o

def empty(name, loc):
    e = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(e)
    e.location = loc
    return e

def export(path):
    # Turn the model so Blender +Y (our "front") becomes Unity +Z.
    rz = Matrix.Rotation(math.pi, 4, "Z")
    for o in list(bpy.data.objects):
        o.parent = None
        o.matrix_world = rz @ o.matrix_world
    for o in bpy.data.objects:
        if o.type == "MESH":
            activate(o)
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        else:
            o.rotation_euler = (0, 0, 0)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, object_types={"MESH", "EMPTY"},
        mesh_smooth_type="OFF", add_leaf_bones=False, use_mesh_modifiers=True)
    tris = sum(len(o.data.loop_triangles) if (o.type == "MESH" and not o.data.calc_loop_triangles()) else 0 for o in bpy.data.objects)
    print("exported", path, "tris", tris)

