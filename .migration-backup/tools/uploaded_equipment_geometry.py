"""Geometry and hierarchy utilities for non-destructive uploaded machinery edits."""
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector


PALETTE = {
    "Paint_Yellow": ((.88, .49, .035, 1), .32, .39),
    "Paint_Green": ((.035, .24, .105, 1), .26, .43),
    "Paint_OffWhite": ((.78, .79, .72, 1), .20, .44),
    "Steel_Dark": ((.045, .054, .063, 1), .80, .36),
    "Steel_Blade": ((.38, .42, .48, 1), .72, .35),
    "Chrome_Hydraulic": ((.61, .65, .70, 1), .98, .17),
    "Rubber_Tires": ((.019, .023, .028, 1), .0, .86),
    "Glass_Tinted": ((.025, .08, .12, 1), .0, .14),
}


def materials(texture_dir):
    """Portable image-based diffuse colors; explicit PBR values are also recorded."""
    result = {}
    for name, (color, metallic, roughness) in PALETTE.items():
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        material.diffuse_color = color
        bsdf = material.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        image = bpy.data.images.new(name + "_BaseColor", width=8, height=8)
        image.generated_color = color
        image.filepath_raw = str(texture_dir / (name + "_BaseColor.png"))
        image.file_format = "PNG"
        image.save()
        image.pack()
        node = material.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = image
        material.node_tree.links.new(node.outputs["Color"], bsdf.inputs["Base Color"])
        material["Metallic"] = metallic
        material["Roughness"] = roughness
        result[name] = material
    return result


def mesh_bounds(obj):
    coordinates = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", coordinates)
    points = coordinates.reshape(-1, 3)
    return points.min(axis=0), points.max(axis=0)


def cut(obj, normal, point, name, cap_material):
    """Split a mesh on a real plane, interpolate UVs and close just the cut loops."""
    halves = []
    for side in (True, False):
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        result = bmesh.ops.bisect_plane(
            bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
            dist=1e-7, plane_co=point, plane_no=normal,
            clear_outer=side, clear_inner=not side,
        )
        boundary = [e for e in result["geom_cut"]
                    if isinstance(e, bmesh.types.BMEdge) and e.is_valid and e.is_boundary]
        slot = len(obj.data.materials)
        if boundary:
            filled = bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
            for face in filled["faces"]:
                face.material_index = slot
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        data = bpy.data.meshes.new(name + ("_Negative" if side else "_Positive"))
        bm.to_mesh(data)
        bm.free()
        if len(data.polygons):
            piece = bpy.data.objects.new(data.name, data)
            bpy.context.collection.objects.link(piece)
            for material in obj.data.materials:
                data.materials.append(material)
            data.materials.append(cap_material)
            halves.append(piece)
        else:
            bpy.data.meshes.remove(data)
            halves.append(None)
    bpy.data.objects.remove(obj, do_unlink=True)
    return halves


def join(objects, name):
    objects = [o for o in objects if o is not None]
    if not objects:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    if len(objects) > 1:
        bpy.ops.object.join()
    obj = objects[0]
    obj.name = name
    return obj


def parent_at(obj, parent, pivot):
    """Geometry is still in source coordinates. Move origin without moving surfaces."""
    pivot = Vector(pivot)
    obj.data.transform(Matrix.Translation(-pivot))
    obj.parent = parent
    obj.location = pivot - Vector(parent["source_pivot"])
    obj.rotation_euler = (0, 0, 0)
    obj["source_pivot"] = list(pivot)
    return obj


def empty(name, parent, pivot):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.empty_display_type = "ARROWS"
    obj.empty_display_size = .015
    obj["source_pivot"] = list(pivot)
    if parent:
        obj.parent = parent
        obj.location = Vector(pivot) - Vector(parent["source_pivot"])
    else:
        obj.location = pivot
    return obj


def paint(obj, palette, default, mode="paint", source_image=None, window_region=None,
          region_material=None):
    """Replace logo-bearing colors, retaining glass/metal regions by measured UV color."""
    slots = list(palette)
    data = obj.data
    if len(data.polygons) == 0:
        return
    uv = data.uv_layers.active
    pixels = np.asarray(source_image.pixels[:], dtype=np.float32).reshape(
        source_image.size[1], source_image.size[0], 4) if source_image and uv else None
    low, high = mesh_bounds(obj)
    wheel_center = (low+high)/2
    wheel_radius = max(high[1]-low[1], high[2]-low[2])/2
    indices = []
    for face in data.polygons:
        name = default
        center = face.center
        if mode == "track":
            name = "Steel_Dark"
        elif mode == "wheel":
            name = "Rubber_Tires"
            if ((center.y-wheel_center[1])**2+(center.z-wheel_center[2])**2 <
                    (wheel_radius*.52)**2 and abs(face.normal.x)>.35):
                name = "Steel_Blade"
        elif mode == "body" and pixels is not None:
            coords = [uv.data[i].uv for i in face.loop_indices]
            u, v = np.mean(coords, axis=0)
            color = pixels[int(v % 1 * pixels.shape[0]), int(u % 1 * pixels.shape[1]), :3]
            r, g, b = color
            if region_material:
                name = region_material(face)
            elif (window_region and window_region(center) and
                  abs(face.normal.z)<.55 and b > r*.85 and max(color)<.55):
                name = "Glass_Tinted"
        indices.append(slots.index(name))
    data.materials.clear()
    for name in slots:
        data.materials.append(palette[name])
    for face, index in zip(data.polygons, indices):
        face.material_index = index
        face.use_smooth = True
    data.update()


def cylinder(name, parent, start, end, radius, material):
    direction = Vector(end) - Vector(start)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=radius, depth=1)
    obj = bpy.context.object
    obj.name = name
    obj.data.materials.append(material)
    for face in obj.data.polygons:
        face.use_smooth = True
    obj.parent = parent
    obj.location = (Vector(start) + Vector(end)) / 2 - Vector(parent["source_pivot"])
    obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
    obj.scale.z = direction.length
    obj["source_pivot"] = list((Vector(start) + Vector(end)) / 2)
    return obj


def track_loop(parent, box, name, palette):
    """Keep the scanned casing, add independently editable moving steel track shoes."""
    low, high = box
    yc, zc = (low[1] + high[1]) / 2, (low[2] + high[2]) / 2
    radius = (high[2] - low[2]) * .46
    half_straight = max((high[1] - low[1]) / 2 - radius, radius * .1)
    circumference = 4 * half_straight + 2 * math.pi * radius
    count = max(24, min(48, round(circumference / max(radius * .36, .001))))
    pitch = circumference / count
    xc = (low[0] + high[0]) / 2

    def point(distance):
        d = distance % circumference
        straight = 2 * half_straight
        arc = math.pi * radius
        if d < straight:
            return yc - half_straight + d, zc + radius, 0
        d -= straight
        if d < arc:
            angle = math.pi / 2 - d / radius
            return yc + half_straight + radius * math.cos(angle), zc + radius * math.sin(angle), angle - math.pi / 2
        d -= arc
        if d < straight:
            return yc + half_straight - d, zc - radius, -math.pi
        d -= straight
        angle = -math.pi / 2 - d / radius
        return yc - half_straight + radius * math.cos(angle), zc + radius * math.sin(angle), angle - math.pi / 2

    mesh = bpy.data.meshes.new(name + "_ShoeMesh")
    width = max(high[0] - low[0], .004)
    x, y, z = width * .54, pitch * .43, radius * .075
    mesh.from_pydata([(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),
                     (-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)],
                    [], [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)])
    mesh.materials.append(palette["Steel_Blade"])
    nodes = []
    for index in range(count):
        shoe = bpy.data.objects.new(f"{name}_Shoe_{index:02d}", mesh)
        bpy.context.collection.objects.link(shoe)
        shoe.parent = parent
        for frame in (1, 97):
            yy, zz, angle = point(index * pitch)
            shoe.location = Vector((xc, yy, zz)) - Vector(parent["source_pivot"])
            shoe.rotation_euler.x = angle
            shoe.keyframe_insert(data_path="location", frame=frame)
            shoe.keyframe_insert(data_path="rotation_euler", frame=frame)
        previous_angle = point(index * pitch)[2]
        for frame in range(121, 146):
            yy, zz, angle = point((index + (frame - 121) / 24) * pitch)
            while angle - previous_angle > math.pi:
                angle -= 2 * math.pi
            while angle - previous_angle < -math.pi:
                angle += 2 * math.pi
            previous_angle = angle
            shoe.location = Vector((xc, yy, zz)) - Vector(parent["source_pivot"])
            shoe.rotation_euler.x = angle
            shoe.keyframe_insert(data_path="location", frame=frame)
            shoe.keyframe_insert(data_path="rotation_euler", frame=frame)
        nodes.append(shoe)
    return nodes