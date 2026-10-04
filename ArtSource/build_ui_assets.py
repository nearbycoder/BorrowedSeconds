"""
Borrowed Seconds: UI hero assets, built procedurally in Blender.

    blender -b --factory-startup -t 4 -P ArtSource/build_ui_assets.py            # everything
    blender -b --factory-startup -t 4 -P ArtSource/build_ui_assets.py -- watch   # one group

* PocketWatch  : exported to Assets/Resources/Models/PocketWatch.fbx (separate hands / glass so the
                 game can animate them) and rendered to ArtSource/previews/PocketWatch.png.
* Medal coins  : rendered straight-on with transparency to Assets/Resources/UI/medal_*.png
                 (gold "Time Thief", silver, bronze, and an empty socket).
* Padlock      : rendered to Assets/Resources/UI/lock.png.
Front faces -Y (Blender front view); the watch crown points +Z.
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT_FBX = os.path.join(ROOT, "Assets", "Resources", "Models")
OUT_UI = os.path.join(ROOT, "Assets", "Resources", "UI")
OUT_PREVIEW = os.path.join(ROOT, "ArtSource", "previews")


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


def mat(name, color, metal=0.0, rough=0.35, emit=None, alpha=1.0, coat=0.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if coat:
        b.inputs["Coat Weight"].default_value = coat
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = 3.0
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
        m.surface_render_method = "BLENDED"
    m.diffuse_color = (*color, alpha)
    return m


def obj(name, bm, material):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = link(bpy.data.objects.new(name, me))
    ob.data.materials.append(material)
    return ob


def select_only(ob):
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def smooth(ob, angle=40):
    select_only(ob)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))


def bevel(ob, width, segments=3):
    md = ob.modifiers.new("Bevel", "BEVEL")
    md.width = width
    md.segments = segments
    md.limit_method = "ANGLE"
    select_only(ob)
    bpy.ops.object.modifier_apply(modifier=md.name)


def lathe(name, profile, material, seg=96, axis="Y"):
    """profile: (radius, depth) pairs, revolved around the depth axis (Y = facing the viewer)."""
    bm = bmesh.new()
    rings = []
    for r, d in profile:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            if axis == "Y":
                ring.append(bm.verts.new((r * math.cos(a), d, r * math.sin(a))))
            else:
                ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), d)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(seg):
            a, b = rings[k][i], rings[k][(i + 1) % seg]
            c, d = rings[k + 1][(i + 1) % seg], rings[k + 1][i]
            if a.co != d.co or b.co != c.co:
                bm.faces.new((a, b, c, d))
    if profile[0][0] > 1e-4:
        bm.faces.new(list(reversed(rings[0])))
    if profile[-1][0] > 1e-4:
        bm.faces.new(rings[-1])
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj(name, bm, material)
    smooth(ob, 50)
    return ob


def box(name, size, center, material, bev=0.0, seg=2):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = obj(name, bm, material)
    if bev > 0:
        bevel(ob, bev, seg)
    smooth(ob)
    return ob


def torus(name, major, minor, material, seg=96, mseg=20, center=(0, 0, 0), axis="Y"):
    bm = bmesh.new()
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for j in range(mseg):
            b = 2 * math.pi * j / mseg
            r = major + minor * math.cos(b)
            p = (r * math.cos(a), minor * math.sin(b), r * math.sin(a)) if axis == "Y" else (r * math.cos(a), r * math.sin(a), minor * math.sin(b))
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for i in range(seg):
        for j in range(mseg):
            bm.faces.new((rings[i][j], rings[(i + 1) % seg][j], rings[(i + 1) % seg][(j + 1) % mseg], rings[i][(j + 1) % mseg]))
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = obj(name, bm, material)
    smooth(ob, 80)
    return ob


def flat_poly(name, pts, depth, y, material):
    """Extrudes a 2D outline (x,z) along -Y by depth starting at y (front face at y - depth)."""
    bm = bmesh.new()
    front = [bm.verts.new((x, y - depth, z)) for x, z in pts]
    back = [bm.verts.new((x, y, z)) for x, z in pts]
    n = len(pts)
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(n):
        bm.faces.new((back[i], back[(i + 1) % n], front[(i + 1) % n], front[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj(name, bm, material)
    smooth(ob, 30)
    return ob


# ------------------------------------------------------------------ pocket watch

def build_watch():
    brass = mat("Brass", (0.86, 0.66, 0.34), metal=1.0, rough=0.22)
    brass_dark = mat("BrassDark", (0.55, 0.4, 0.2), metal=1.0, rough=0.38)
    enamel = mat("Enamel", (0.045, 0.055, 0.12), rough=0.18, coat=1.0)
    gold = mat("Gold", (1.0, 0.8, 0.45), metal=1.0, rough=0.18)
    ice = mat("Ice", (0.49, 0.96, 1.0), emit=(0.49, 0.96, 1.0))
    glass = mat("Glass", (0.9, 0.97, 1.0), rough=0.02, alpha=0.06)
    parts = []
    # case: rounded drum, back towards +Y, front rim at y = -0.17
    parts.append(lathe("Case", [(0.0, 0.2), (0.62, 0.21), (0.9, 0.18), (1.0, 0.1), (1.03, 0.0), (1.02, -0.09), (0.98, -0.15), (0.95, -0.17)], brass))
    parts.append(torus("Bezel", 0.955, 0.045, gold, center=(0, -0.18, 0)))
    parts.append(lathe("Dial", [(0.0, -0.165), (0.93, -0.165), (0.93, -0.15)], enamel))
    # chapter ring: 60 minute ticks, 12 bold hour batons
    for k in range(60):
        a = math.radians(90 - k * 6)
        bold = k % 5 == 0
        L, W = (0.13, 0.034) if bold else (0.05, 0.012)
        t = box(f"Tick{k:02d}", (W, 0.012, L), (0, 0, 0), gold if bold else brass, bev=0.004 if bold else 0, seg=1)
        t.data.transform(Matrix.Rotation(-a + math.pi / 2, 4, "Y"))
        r = 0.84 - L * 0.5
        t.data.transform(Matrix.Translation((r * math.cos(a), -0.172, r * math.sin(a))))
        parts.append(t)
    # inner track ring and the small-seconds sub dial
    parts.append(torus("Track", 0.66, 0.006, brass, mseg=8, center=(0, -0.17, 0)))
    parts.append(torus("SubRing", 0.17, 0.006, brass, mseg=8, center=(0, -0.17, -0.42)))
    for k in range(12):
        a = math.radians(90 - k * 30)
        t = box(f"SubTick{k:02d}", (0.008, 0.01, 0.035), (0, 0, 0), brass)
        t.data.transform(Matrix.Rotation(-a + math.pi / 2, 4, "Y"))
        t.data.transform(Matrix.Translation((0.14 * math.cos(a), -0.172, -0.42 + 0.14 * math.sin(a))))
        parts.append(t)
    # crown, stem and bow on top (+Z)
    parts.append(cyl_z("Stem", 0.07, 0.16, (0, 0, 1.1), brass))
    crown = cyl_z("Crown", 0.13, 0.13, (0, 0, 1.22), gold, verts=48)
    # knurling
    for k in range(24):
        a = 2 * math.pi * k / 24
        g = box(f"Knurl{k:02d}", (0.022, 0.022, 0.12), (0.132 * math.cos(a), 0.132 * math.sin(a), 1.22), gold)
        parts.append(g)
    parts.append(crown)
    parts.append(torus("Bow", 0.22, 0.035, brass, center=(0, 0, 1.47), axis="Y"))
    # hands (pivot at origin, pointing +Z = 12 o'clock); separate objects for animation
    parts.append(hand("HandHour", 0.42, 0.075, -0.19, gold))
    parts.append(hand("HandMinute", 0.66, 0.055, -0.2, gold))
    sec = hand("HandSecond", 0.8, 0.018, -0.21, ice, tail=0.18)
    parts.append(sec)
    parts.append(cyl_y("Hub", 0.05, 0.05, (0, -0.215, 0), gold))
    sub = hand("HandSmall", 0.13, 0.016, -0.18, ice, tail=0.03)
    sub.location = (0, 0, -0.42)
    parts.append(sub)
    # glass dome over the dial
    parts.append(lathe("Glass", [(0.93, -0.18), (0.8, -0.24), (0.55, -0.285), (0.0, -0.3)], glass, seg=96))
    return parts


def cyl_z(name, r, h, center, material, verts=40):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=h)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = obj(name, bm, material)
    bevel(ob, min(r, h) * 0.15, 2)
    smooth(ob)
    return ob


def cyl_y(name, r, h, center, material, verts=40):
    ob = cyl_z(name, r, h, (0, 0, 0), material, verts)
    ob.data.transform(Matrix.Rotation(math.radians(90), 4, "X"))
    ob.data.transform(Matrix.Translation(center))
    return ob


def hand(name, length, width, y, material, tail=0.1):
    """Leaf-shaped hand in the XZ plane at depth y, from -tail to +length along +Z."""
    pts = [(0, length), (width * 0.55, length * 0.72), (width, 0.06), (width * 0.6, -tail), (-width * 0.6, -tail), (-width, 0.06), (-width * 0.55, length * 0.72)]
    return flat_poly(name, [(x, z) for x, z in pts], 0.012, y + 0.006, material)


# ------------------------------------------------------------------ medals & lock

def build_coin(kind, icon=False):
    metals = {
        "gold": ((1.0, 0.77, 0.36), 0.18),
        "silver": ((0.86, 0.89, 0.95), 0.2),
        "bronze": ((0.78, 0.47, 0.27), 0.3),
        "none": ((0.05, 0.06, 0.11), 0.55),
    }
    col, rough = metals[kind]
    if icon:
        # small-size icons: more saturated, rougher metal so the colour survives downscaling
        col = {"gold": (1.0, 0.68, 0.18), "silver": (0.88, 0.92, 1.0), "bronze": (0.86, 0.44, 0.2), "none": col}[kind]
        rough = 0.42
    metal = mat("Coin_" + kind, col, metal=0.0 if kind == "none" else 1.0, rough=rough)
    deep = mat("CoinDeep_" + kind, tuple(c * 0.55 for c in col), metal=0.0 if kind == "none" else 1.0, rough=rough + 0.15)
    parts = []
    if kind == "none":
        parts.append(lathe("Socket", [(0.0, 0.02), (0.92, 0.02), (1.0, -0.02), (1.02, 0.02)], metal))
        parts += hourglass(mat("SocketLine", (0.12, 0.14, 0.24), rough=0.6), 0.004, -0.0)
        return parts
    # coin body with raised rim, reeded edge
    parts.append(lathe("Coin", [(0.0, 0.06), (0.8, 0.06), (0.86, 0.035), (0.9, -0.05), (0.97, -0.07), (1.0, -0.05), (1.0, 0.06), (0.0, 0.07)], metal))
    for k in range(120):
        a = 2 * math.pi * k / 120
        r = box(f"Reed{k:03d}", (0.022, 0.1, 0.018), (1.0 * math.cos(a), 0.0, 1.0 * math.sin(a)), deep)
        r.rotation_euler = (0, -a, 0)
        parts.append(r)
    parts.append(torus("InnerRing", 0.74, 0.012, metal, mseg=10, center=(0, 0.02, 0)))
    # 12 hour pips around the field
    for k in range(12):
        a = math.radians(90 - 30 * k)
        p = gem_dot(f"Pip{k:02d}", 0.028, (0.66 * math.cos(a), 0.035, 0.66 * math.sin(a)), metal)
        parts.append(p)
    parts += hourglass(metal, 0.05, 0.035)
    if kind == "gold":
        # the Time Thief gets a tiny crown of rays
        for k in range(7):
            a = math.radians(60 + k * 10)
            ray = box(f"Ray{k}", (0.016, 0.03, 0.1), (0.52 * math.cos(a), 0.035, 0.52 * math.sin(a)), metal)
            ray.rotation_euler = (0, -(a - math.pi / 2), 0)
            parts.append(ray)
    return parts


def gem_dot(name, r, center, material):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=8, radius=r)
    bmesh.ops.scale(bm, vec=Vector((1, 0.5, 1)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=Vector(center), verts=bm.verts)
    ob = obj(name, bm, material)
    smooth(ob, 80)
    return ob


def hourglass(material, relief, y):
    """Embossed hourglass emblem facing -Y."""
    w, h, neck = 0.3, 0.42, 0.045
    pts = [(-w, h), (w, h), (w, h - 0.06), (neck, 0.0), (w, -h + 0.06), (w, -h), (-w, -h), (-w, -h + 0.06), (-neck, 0.0), (-w, h - 0.06)]
    glass = flat_poly("Hourglass", pts, relief, y, material)
    top = box("CapTop", (0.74, relief, 0.07), (0, y - relief * 0.5, h + 0.06), material, bev=0.01, seg=1)
    bot = box("CapBot", (0.74, relief, 0.07), (0, y - relief * 0.5, -h - 0.06), material, bev=0.01, seg=1)
    return [glass, top, bot]


def build_lock():
    brass = mat("LockBrass", (0.86, 0.66, 0.34), metal=1.0, rough=0.28)
    steel = mat("LockSteel", (0.55, 0.6, 0.72), metal=1.0, rough=0.25)
    ink = mat("LockInk", (0.04, 0.05, 0.1), rough=0.6)
    body = box("Body", (1.1, 0.42, 0.9), (0, 0, -0.25), brass, bev=0.12, seg=4)
    shackle = torus("Shackle", 0.36, 0.085, steel, center=(0, 0, 0.2))
    # cut the lower half of the shackle torus away: keep verts with z >= 0.2 and add legs
    bpy.ops.object.select_all(action="DESELECT")
    select_only(shackle)
    bpy.ops.object.mode_set(mode="EDIT")
    bm = bmesh.from_edit_mesh(shackle.data)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < 0.2 - 1e-4], context="VERTS")
    bmesh.update_edit_mesh(shackle.data)
    bpy.ops.object.mode_set(mode="OBJECT")
    legs = [cyl_z(f"Leg{i}", 0.085, 0.35, (x, 0, 0.05), steel) for i, x in enumerate((-0.36, 0.36))]
    hole = cyl_y("Keyhole", 0.1, 0.05, (0, -0.21, -0.17), ink)
    slot = box("Slot", (0.07, 0.05, 0.22), (0, -0.21, -0.33), ink)
    return [body, shackle, hole, slot] + legs


# ------------------------------------------------------------------ rendering

def scene_setup(size, ortho_scale, cam_loc, cam_rot, transparent=True):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.film_transparent = transparent
    scene.view_settings.view_transform = "Standard"
    scene.render.image_settings.color_mode = "RGBA"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.03, 0.035, 0.07, 1)
    bg.inputs["Strength"].default_value = 1.0
    scene.world = world
    cd = bpy.data.cameras.new("Cam")
    cd.type = "ORTHO"
    cd.ortho_scale = ortho_scale
    cam = link(bpy.data.objects.new("Cam", cd))
    cam.location = cam_loc
    cam.rotation_euler = cam_rot
    scene.camera = cam
    for name, energy, rot, color in (("Key", 4.0, (60, 0, -35), (1, 0.93, 0.82)), ("Rim", 2.5, (110, 0, 150), (0.6, 0.75, 1.0)), ("Fill", 1.2, (30, 0, 40), (1, 1, 1))):
        L = bpy.data.lights.new(name, "SUN")
        L.energy = energy
        L.color = color
        lo = link(bpy.data.objects.new(name, L))
        lo.rotation_euler = tuple(math.radians(v) for v in rot)
    # softboxes: metal reads by what it reflects, so give it big bright shapes on a dark studio
    for name, energy, size, loc, color in (("Softbox", 900, 5, (-3, -4, 4), (1, 0.95, 0.88)),
                                           ("Strip", 500, 2.5, (4, -3, -1), (0.65, 0.8, 1.0)),
                                           ("Top", 400, 6, (0, -1, 6), (1, 1, 1))):
        A = bpy.data.lights.new(name, "AREA")
        A.energy = energy
        A.size = size
        A.color = color
        ao = link(bpy.data.objects.new(name, A))
        ao.location = loc
        ao.rotation_euler = (Vector((0, 0, 0)) - ao.location).to_track_quat("-Z", "Y").to_euler()
    return scene


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("[ui-assets] rendered", path)


def export_fbx(name, objects):
    os.makedirs(OUT_FBX, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT_FBX, name + ".fbx"), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "ArtSource", name + ".blend"), compress=True)


def do_watch():
    reset()
    parts = build_watch()
    export_fbx("PocketWatch", parts)
    # preview: three-quarter view with the hands at 10:08
    for p in parts:
        if p.name == "HandHour":
            p.rotation_euler = (0, math.radians(-304), 0)
        if p.name == "HandMinute":
            p.rotation_euler = (0, math.radians(-48), 0)
    scene_setup(800, 3.4, (1.6, -5, 0.6), (math.radians(84), 0, math.radians(17)), transparent=False)
    render(os.path.join(OUT_PREVIEW, "PocketWatch.png"))


def do_coins():
    os.makedirs(OUT_UI, exist_ok=True)
    for kind in ("gold", "silver", "bronze", "none"):
        reset()
        build_coin(kind)
        scene_setup(256, 2.18, (0, -5, 0), (math.radians(90), 0, 0))
        render(os.path.join(OUT_UI, f"medal_{kind}.png"))
    for kind in ("gold", "silver", "bronze"):
        reset()
        build_coin(kind, icon=True)
        scene_setup(160, 2.18, (0, -5, 0), (math.radians(90), 0, 0))
        render(os.path.join(OUT_UI, f"medal_{kind}_icon.png"))
    # one big coin per medal for the level-complete stamp (same saturated metal as the icons)
    for kind in ("gold", "silver", "bronze"):
        reset()
        build_coin(kind, icon=True)
        scene_setup(512, 2.18, (0, -5, 0), (math.radians(90), 0, 0))
        render(os.path.join(OUT_UI, f"medal_{kind}_large.png"))


def do_lock():
    reset()
    build_lock()
    scene_setup(256, 1.9, (0.8, -5, 0.5), (math.radians(84), 0, math.radians(9)))
    render(os.path.join(OUT_UI, "lock.png"))


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    groups = argv or ["watch", "coins", "lock"]
    if "watch" in groups:
        do_watch()
    if "coins" in groups:
        do_coins()
    if "lock" in groups:
        do_lock()
