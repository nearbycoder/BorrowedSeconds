"""
Borrowed Seconds: procedural Blender asset generator.

    blender -b --factory-startup -t 4 -P ArtSource/build_assets.py            # all assets
    blender -b --factory-startup -t 4 -P ArtSource/build_assets.py -- Player  # one asset
    blender -b --factory-startup -t 4 -P ArtSource/build_assets.py -- --sheet # contact sheet only

Every asset is built from code in metres (1 tile = 1 m), origin at the tile centre on the floor
plane (z = 0), "front" facing -Y (Blender front view). Each one is saved to ArtSource/<name>.blend,
exported to Assets/Resources/Models/<name>.fbx, and rendered to ArtSource/previews/<name>.png.
Material *names* are the contract with the game: the runtime swaps them for palette materials.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_FBX = os.path.join(ROOT, "Assets", "Resources", "Models")
OUT_BLEND = os.path.join(ROOT, "ArtSource")
OUT_PREVIEW = os.path.join(ROOT, "ArtSource", "previews")

# preview colours (the game recolours by material name)
PALETTE = {
    "Floor": (0.925, 0.902, 0.855, 1), "Slate": (0.196, 0.227, 0.361, 1), "SlateTop": (0.29, 0.333, 0.51, 1),
    "Porcelain": (0.957, 0.937, 0.902, 1), "Amber": (1.0, 0.71, 0.28, 1), "Ink": (0.08, 0.1, 0.2, 1),
    "Graphite": (0.125, 0.14, 0.227, 1), "Coral": (1.0, 0.31, 0.39, 1), "Brass": (0.79, 0.63, 0.35, 1),
    "Mint": (0.33, 0.88, 0.68, 1), "MintGlass": (0.33, 0.88, 0.68, 0.6), "Gold": (1.0, 0.82, 0.48, 1),
    "Pad": (0.2, 0.55, 0.45, 1), "Glass": (0.8, 0.95, 1.0, 0.35), "Sand": (1.0, 0.82, 0.5, 1),
    "Crystal": (0.49, 0.96, 1.0, 0.35), "Plinth": (0.137, 0.157, 0.271, 1),
}
EMISSIVE = {"Amber", "Coral", "Gold", "Mint", "Sand"}


# ------------------------------------------------------------------ scene helpers

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for c in bpy.data.collections:
        bpy.data.collections.remove(c)


def material(name):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col = PALETTE.get(name, (0.8, 0.8, 0.8, 1))
    bsdf.inputs["Base Color"].default_value = col
    bsdf.inputs["Roughness"].default_value = 0.35
    if name in ("Brass", "Gold"):
        bsdf.inputs["Metallic"].default_value = 0.8
        bsdf.inputs["Roughness"].default_value = 0.3
    if col[3] < 1:
        bsdf.inputs["Alpha"].default_value = col[3]
        m.blend_method = "BLEND" if hasattr(m, "blend_method") else None
    if name in EMISSIVE:
        bsdf.inputs["Emission Color"].default_value = col
        bsdf.inputs["Emission Strength"].default_value = 2.0
    m.diffuse_color = col
    return m


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def mesh_obj(name, bm, mat):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = link(bpy.data.objects.new(name, me))
    ob.data.materials.append(material(mat))
    return ob


def apply_mods(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def smooth(ob, angle=35):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))


def box(name, size, center, mat, bevel=0.03, segments=3):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = mesh_obj(name, bm, mat)
    if bevel > 0:
        md = ob.modifiers.new("Bevel", "BEVEL")
        md.width = bevel
        md.segments = segments
        md.limit_method = "ANGLE"
        apply_mods(ob)
    smooth(ob)
    return ob


def cylinder(name, radius, depth, center, mat, verts=48, bevel=0.02, segments=3):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=radius, radius2=radius, depth=depth)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = mesh_obj(name, bm, mat)
    if bevel > 0:
        md = ob.modifiers.new("Bevel", "BEVEL")
        md.width = bevel
        md.segments = segments
        md.limit_method = "ANGLE"
        apply_mods(ob)
    smooth(ob)
    return ob


def cone(name, r1, r2, depth, center, mat, verts=48):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts, radius1=r1, radius2=r2, depth=depth)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = mesh_obj(name, bm, mat)
    smooth(ob)
    return ob


def sphere(name, radius, center, mat, scale=(1, 1, 1), segs=32, rings=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=radius)
    bmesh.ops.scale(bm, vec=Vector(scale), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = mesh_obj(name, bm, mat)
    smooth(ob, 80)
    return ob


def torus(name, major, minor, center, mat, seg=48, mseg=16):
    bm = bmesh.new()
    verts = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        verts.append(ring)
    for i in range(seg):
        for j in range(mseg):
            a, b = verts[i][j], verts[(i + 1) % seg][j]
            c, d = verts[(i + 1) % seg][(j + 1) % mseg], verts[i][(j + 1) % mseg]
            bm.faces.new((a, b, c, d))
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = mesh_obj(name, bm, mat)
    smooth(ob, 80)
    return ob


def lathe(name, profile, mat, seg=48):
    """profile: list of (radius, z) from bottom to top; closed with caps."""
    bm = bmesh.new()
    rings = []
    for (r, z) in profile:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(seg):
            a, b = rings[k][i], rings[k][(i + 1) % seg]
            c, d = rings[k + 1][(i + 1) % seg], rings[k + 1][i]
            bm.faces.new((a, b, c, d))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = mesh_obj(name, bm, mat)
    smooth(ob, 50)
    return ob


def gem(name, radius, height, center, mat, sides=8):
    bm = bmesh.new()
    top = bm.verts.new((0, 0, height * 0.5))
    bot = bm.verts.new((0, 0, -height * 0.5))
    ring = [bm.verts.new((radius * math.cos(2 * math.pi * i / sides), radius * math.sin(2 * math.pi * i / sides), 0)) for i in range(sides)]
    for i in range(sides):
        bm.faces.new((ring[i], ring[(i + 1) % sides], top))
        bm.faces.new((ring[(i + 1) % sides], ring[i], bot))
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    return mesh_obj(name, bm, mat)


def rotate_obj(ob, axis, degrees):
    ob.data.transform(Matrix.Rotation(math.radians(degrees), 4, axis))


def parent_all(root_name, objects):
    root = link(bpy.data.objects.new(root_name, None))
    for ob in objects:
        ob.parent = root
    return root


# ------------------------------------------------------------------ assets

def a_floor():
    return [box("Tile", (0.97, 0.97, 0.16), (0, 0, -0.08), "Floor", bevel=0.03)]


def a_wall():
    body = box("Body", (1.0, 1.0, 0.62), (0, 0, 0.17), "Slate", bevel=0.035)
    cap = box("Cap", (0.84, 0.84, 0.07), (0, 0, 0.5), "SlateTop", bevel=0.025)
    trim = box("Trim", (0.9, 0.9, 0.02), (0, 0, 0.465), "Brass", bevel=0.005, segments=1)
    return [body, cap, trim]


def a_player():
    base = cylinder("Base", 0.26, 0.09, (0, 0, 0.045), "Porcelain", bevel=0.03)
    body = lathe("Torso", [(0.21, 0.08), (0.205, 0.14), (0.175, 0.24), (0.14, 0.36), (0.125, 0.46),
                           (0.15, 0.52), (0.165, 0.56), (0.12, 0.6), (0.0, 0.61)], "Porcelain")
    core = torus("Core", 0.17, 0.032, (0, 0, 0.3), "Amber")
    collar = torus("Collar", 0.155, 0.018, (0, 0, 0.545), "Brass", mseg=10)
    head = sphere("Head", 0.19, (0, 0, 0.79), "Porcelain")
    eye_l = sphere("EyeL", 0.032, (-0.068, -0.172, 0.81), "Ink", scale=(1, 0.6, 1.25), segs=16, rings=8)
    eye_r = sphere("EyeR", 0.032, (0.068, -0.172, 0.81), "Ink", scale=(1, 0.6, 1.25), segs=16, rings=8)
    return [base, body, core, collar, head, eye_l, eye_r]


def a_slider():
    body = box("Body", (0.84, 0.84, 0.82), (0, 0, 0.43), "Graphite", bevel=0.06, segments=4)
    parts = [body]
    for i, (dx, dy, sx, sy) in enumerate([(0, -0.425, 0.66, 0.02), (0, 0.425, 0.66, 0.02), (-0.425, 0, 0.02, 0.66), (0.425, 0, 0.02, 0.66)]):
        parts.append(box(f"Strip{i}", (sx, sy, 0.06), (dx, dy, 0.15), "Coral", bevel=0.008, segments=1))
        parts.append(box(f"Panel{i}", (sx if sx > 0.1 else 0.015, sy if sy > 0.1 else 0.015, 0.36), (dx * 1.0, dy * 1.0, 0.52), "Ink", bevel=0.005, segments=1))
    parts.append(box("Top", (0.6, 0.6, 0.03), (0, 0, 0.845), "Ink", bevel=0.01, segments=1))
    return parts


def a_laser():
    base = box("Base", (0.72, 0.72, 0.12), (0, 0, 0.06), "Graphite", bevel=0.03)
    housing = box("Housing", (0.5, 0.56, 0.3), (0, 0.02, 0.27), "Graphite", bevel=0.08, segments=4)
    fin1 = box("FinL", (0.06, 0.4, 0.2), (-0.27, 0.05, 0.24), "Ink", bevel=0.015)
    fin2 = box("FinR", (0.06, 0.4, 0.2), (0.27, 0.05, 0.24), "Ink", bevel=0.015)
    barrel = cylinder("Barrel", 0.11, 0.2, (0, 0, 0), "Brass", verts=32, bevel=0.015)
    rotate_obj(barrel, "X", 90)
    barrel.data.transform(Matrix.Translation((0, -0.3, 0.27)))
    lens = cylinder("Lens", 0.075, 0.03, (0, 0, 0), "Coral", verts=32, bevel=0.008)
    rotate_obj(lens, "X", 90)
    lens.data.transform(Matrix.Translation((0, -0.405, 0.27)))
    return [base, housing, fin1, fin2, barrel, lens]


def a_rotor_hub():
    drum = cylinder("Drum", 0.31, 0.72, (0, 0, 0.36), "Graphite", bevel=0.05)
    ring = torus("Ring", 0.325, 0.03, (0, 0, 0.56), "Coral")
    cap = cylinder("Cap", 0.2, 0.06, (0, 0, 0.75), "Brass", bevel=0.02)
    dome = sphere("Dome", 0.12, (0, 0, 0.78), "Brass", scale=(1, 1, 0.5))
    band = torus("Band", 0.318, 0.012, (0, 0, 0.12), "Brass", mseg=8)
    return [drum, ring, cap, dome, band]


def a_rotor_arm(length):
    l = length + 0.3
    bar = box("Bar", (0.2, l, 0.22), (0, -(l * 0.5 + 0.1), 0), "Graphite", bevel=0.05, segments=3)
    strip = box("Strip", (0.07, l - 0.15, 0.03), (0, -(l * 0.5 + 0.1), 0.115), "Coral", bevel=0.008, segments=1)
    tip = box("Tip", (0.26, 0.12, 0.28), (0, -(l + 0.13), 0), "Coral", bevel=0.04, segments=3)
    collar = box("Collar", (0.24, 0.12, 0.26), (0, -0.2, 0), "Brass", bevel=0.03)
    return [bar, strip, tip, collar]


def a_plate():
    parts = []
    for i, (cx, cy, sx, sy) in enumerate([(0, -0.405, 0.86, 0.05), (0, 0.405, 0.86, 0.05), (-0.405, 0, 0.05, 0.76), (0.405, 0, 0.05, 0.76)]):
        parts.append(box(f"Rim{i}", (sx, sy, 0.035), (cx, cy, 0.0175), "Brass", bevel=0.01, segments=2))
    parts.append(box("Pad", (0.74, 0.74, 0.05), (0, 0, 0.025), "Pad", bevel=0.015))
    return parts


def a_gate_posts():
    return [box("PostL", (0.09, 0.28, 0.86), (-0.47, 0, 0.43), "Brass", bevel=0.025),
            box("PostR", (0.09, 0.28, 0.86), (0.47, 0, 0.43), "Brass", bevel=0.025),
            box("CapL", (0.13, 0.32, 0.05), (-0.47, 0, 0.88), "Gold", bevel=0.015),
            box("CapR", (0.13, 0.32, 0.05), (0.47, 0, 0.88), "Gold", bevel=0.015)]


def a_gate_slab():
    slab = box("Slab", (0.86, 0.14, 0.76), (0, 0, 0.38), "MintGlass", bevel=0.03)
    bars = [box(f"Bar{i}", (0.03, 0.15, 0.7), (-0.3 + i * 0.2, 0, 0.38), "Brass", bevel=0.008, segments=1) for i in range(4)]
    return [slab] + bars


def a_lock():
    dial = cylinder("Dial", 0.44, 0.06, (0, 0, 0.03), "Brass", bevel=0.02)
    face = cylinder("Face", 0.36, 0.02, (0, 0, 0.065), "Ink", bevel=0.006, segments=2)
    parts = [dial, face]
    for k in range(12):
        a = math.radians(90 - (k * 30 + 15))
        seg = box(f"Seg{k:02d}", (0.1, 0.085, 0.03), (0, 0, 0), "Gold", bevel=0.01, segments=2)
        seg.data.transform(Matrix.Rotation(a + math.pi / 2, 4, "Z"))
        seg.data.transform(Matrix.Translation((0.27 * math.cos(a), 0.27 * math.sin(a), 0.085)))
        parts.append(seg)
    parts.append(gem("Gem", 0.085, 0.09, (0, 0, 0.1), "Gold", sides=8))
    return parts


def a_exit():
    base = cylinder("Base", 0.45, 0.07, (0, 0, 0.035), "Brass", bevel=0.02)
    disc = cylinder("Disc", 0.36, 0.02, (0, 0, 0.075), "Gold", bevel=0.006, segments=2)
    return [base, disc]


def a_hourglass():
    top = cylinder("TopCap", 0.17, 0.035, (0, 0, 0.42), "Brass", bevel=0.01)
    bot = cylinder("BotCap", 0.17, 0.035, (0, 0, 0.0), "Brass", bevel=0.01)
    glass_t = cone("GlassTop", 0.025, 0.14, 0.2, (0, 0, 0.3), "Glass")
    glass_b = cone("GlassBot", 0.14, 0.025, 0.2, (0, 0, 0.12), "Glass")
    sand = cone("Sand", 0.11, 0.02, 0.08, (0, 0, 0.06), "Sand")
    pillars = []
    for i in range(3):
        a = 2 * math.pi * i / 3
        pillars.append(cylinder(f"Pillar{i}", 0.014, 0.4, (0.15 * math.cos(a), 0.15 * math.sin(a), 0.21), "Brass", verts=12, bevel=0))
    return [top, bot, glass_t, glass_b, sand] + pillars


def a_crystal_box():
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    ob = mesh_obj("Crystal", bm, "Crystal")
    md = ob.modifiers.new("Bevel", "BEVEL")
    md.width = 0.16
    md.segments = 1
    apply_mods(ob)
    # facet jitter for a cut-gem look
    import random
    random.seed(7)
    for v in ob.data.vertices:
        v.co += Vector((random.uniform(-0.025, 0.025), random.uniform(-0.025, 0.025), random.uniform(-0.025, 0.025)))
    ob.data.transform(Matrix.Translation((0, 0, 0.5)))
    return [ob]


def a_crystal_pawn():
    bm = bmesh.new()
    sides = 7
    rings = [(0.0, 0.0), (0.3, 0.12), (0.36, 0.45), (0.32, 0.85), (0.0, 1.08)]
    vs = []
    import random
    random.seed(3)
    for r, z in rings:
        ring = []
        for i in range(sides):
            a = 2 * math.pi * (i + 0.5 * (z > 0.5)) / sides
            rr = r * random.uniform(0.92, 1.06)
            ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), z)))
        vs.append(ring)
    for k in range(len(vs) - 1):
        for i in range(sides):
            a, b = vs[k][i], vs[k][(i + 1) % sides]
            c, d = vs[k + 1][(i + 1) % sides], vs[k + 1][i]
            if len({a, b, c, d}) == 4:
                bm.faces.new((a, b, c, d))
            else:
                bm.faces.new(tuple(dict.fromkeys((a, b, c, d))))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.001)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return [mesh_obj("Crystal", bm, "Crystal")]


def a_chevron():
    a = box("A", (0.07, 0.3, 0.04), (0, 0, 0), "Coral", bevel=0.01, segments=1)
    a.data.transform(Matrix.Rotation(math.radians(-40), 4, "Z"))
    a.data.transform(Matrix.Translation((-0.09, 0.0, 0)))
    b = box("B", (0.07, 0.3, 0.04), (0, 0, 0), "Coral", bevel=0.01, segments=1)
    b.data.transform(Matrix.Rotation(math.radians(40), 4, "Z"))
    b.data.transform(Matrix.Translation((0.09, 0.0, 0)))
    return [a, b]


def a_gear(teeth=24, r=2.2, w=0.18):
    """Large decorative gear for the void (flat, thin)."""
    bm = bmesh.new()
    pts = []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4)
        rr = r + (0.22 if (i % 4) in (1, 2) else 0)
        pts.append((rr * math.cos(a), rr * math.sin(a)))
    outer = [bm.verts.new((x, y, 0)) for x, y in pts]
    face = bm.faces.new(outer)
    bmesh.ops.extrude_face_region(bm, geom=[face])
    for v in bm.verts:
        if v.co.z == 0 and v not in outer:
            pass
    bm.verts.ensure_lookup_table()
    for v in bm.verts[len(outer):]:
        v.co.z = w
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = mesh_obj("Gear", bm, "Brass")
    return [ob]


ASSETS = {
    "FloorTile": a_floor,
    "WallBlock": a_wall,
    "Player": a_player,
    "Slider": a_slider,
    "Laser": a_laser,
    "RotorHub": a_rotor_hub,
    "RotorArm2": lambda: a_rotor_arm(2),
    "RotorArm3": lambda: a_rotor_arm(3),
    "Plate": a_plate,
    "GatePosts": a_gate_posts,
    "GateSlab": a_gate_slab,
    "Lock": a_lock,
    "Exit": a_exit,
    "Hourglass": a_hourglass,
    "CrystalBox": a_crystal_box,
    "CrystalPawn": a_crystal_pawn,
    "Chevron": a_chevron,
    "Gear": a_gear,
}


# ------------------------------------------------------------------ export & preview

def export(name, objects):
    os.makedirs(OUT_FBX, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for ob in objects:
        ob.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT_FBX, name + ".fbx"), use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
        use_custom_props=False, path_mode="AUTO")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_BLEND, name + ".blend"), compress=True)


def setup_preview(target_objects, size=640):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.06, 0.07, 0.13, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    scene.world = world
    # bounds
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for ob in target_objects:
        for c in ob.bound_box:
            w = ob.matrix_world @ Vector(c)
            mn = Vector(map(min, mn, w))
            mx = Vector(map(max, mx, w))
    center = (mn + mx) * 0.5
    radius = max((mx - mn).length * 0.5, 0.3)
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 50
    cam = link(bpy.data.objects.new("Cam", cam_data))
    direction = Vector((0.55, -1.0, 0.75)).normalized()
    cam.location = center + direction * radius * 3.6
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.camera = cam
    key = bpy.data.lights.new("Key", "SUN")
    key.energy = 3.2
    key.color = (1.0, 0.92, 0.82)
    kob = link(bpy.data.objects.new("Key", key))
    kob.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
    fill = bpy.data.lights.new("Fill", "SUN")
    fill.energy = 0.8
    fill.color = (0.55, 0.62, 1.0)
    fob = link(bpy.data.objects.new("Fill", fill))
    fob.rotation_euler = (math.radians(60), math.radians(-20), math.radians(150))
    return cam


def preview(name, objects):
    os.makedirs(OUT_PREVIEW, exist_ok=True)
    setup_preview(objects)
    bpy.context.scene.render.filepath = os.path.join(OUT_PREVIEW, name + ".png")
    bpy.ops.render.render(write_still=True)


def build(name):
    reset()
    objects = ASSETS[name]()
    export(name, objects)
    preview(name, objects)
    print(f"[assets] built {name}: {len(objects)} parts")


def contact_sheet():
    """Lays every exported preview into one image using Blender's compositor-free pixel API."""
    names = [n for n in ASSETS if os.path.exists(os.path.join(OUT_PREVIEW, n + ".png"))]
    cols = 6
    tile = 320
    rows = (len(names) + cols - 1) // cols
    W, H = cols * tile, rows * tile
    sheet = [0.05] * (W * H * 4)
    for idx, n in enumerate(names):
        img = bpy.data.images.load(os.path.join(OUT_PREVIEW, n + ".png"))
        img.scale(tile, tile)
        px = list(img.pixels)
        ox, oy = (idx % cols) * tile, (rows - 1 - idx // cols) * tile
        for y in range(tile):
            row_src = y * tile * 4
            row_dst = ((oy + y) * W + ox) * 4
            sheet[row_dst:row_dst + tile * 4] = px[row_src:row_src + tile * 4]
    out = bpy.data.images.new("sheet", W, H)
    out.pixels = sheet
    out.filepath_raw = os.path.join(OUT_PREVIEW, "_contact_sheet.png")
    out.file_format = "PNG"
    out.save()
    print("[assets] contact sheet:", ", ".join(names))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--sheet" in argv:
        contact_sheet()
    else:
        targets = [a for a in argv if a in ASSETS] or list(ASSETS)
        for t in targets:
            build(t)
        contact_sheet()
