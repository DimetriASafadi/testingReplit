#!/usr/bin/env python3
"""Articulate every variant from the inspected uploaded FBX; never edit its source."""
import json
import math
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from uploaded_equipment_geometry import (
    PALETTE, cut, cylinder, empty, join, materials, mesh_bounds, paint, parent_at, track_loop,
)

ROOT = Path(__file__).resolve().parents[1]
INSPECTION = ROOT / "exports/uploaded-equipment-inspection"
OUT = ROOT / "exports/uploaded-equipment"
CONFIG = [
    ("Excavator_Large", "excavator", [0], (.065,.584), (-.175,.743), (-.245,.558)),
    ("Tipper_Loaded_Large", "truck", [1,12,13,14,15,16,17], None, None, None),
    ("Bulldozer_Large", "dozer", [2,18], None, None, None),
    ("Tipper_Empty_Medium", "truck", [3], None, None, None),
    ("Excavator_Medium", "excavator", [4], (.050,.224), (-.164,.318), (-.190,.179)),
    ("Bulldozer_Medium", "dozer", [5], None, None, None),
    ("Bulldozer_Rear_Part", "tracked_part", [6], None, None, None),
    ("Excavator_Base_Part", "tracked_part", [7], None, None, None),
    ("Bulldozer_Front_Part", "blade_part", [8], None, None, None),
    ("Tipper_Bed_Part", "bed_part", [9], None, None, None),
    ("Truck_Cab_Part", "cab_part", [10], None, None, None),
    ("Excavator_Mini", "excavator", [11], (.039,.061), (-.038,.108), (-.075,.019)),
]


def key_rotation(obj, axis, values):
    for frame, value in values:
        obj.rotation_euler[axis] = math.radians(value)
        obj.keyframe_insert(data_path="rotation_euler", frame=frame)
    obj["ControlAxis_Blender"] = "XYZ"[axis]
    obj["DemonstrationRangeDegrees"] = [min(v for _, v in values), max(v for _, v in values)]


def scene_objects(root):
    result = [root]
    for child in root.children:
        result.extend(scene_objects(child))
    return result


def build_tracks(base, root, low, high, palette):
    xc = (low[0] + high[0]) / 2
    width = high[0] - low[0]
    left, rest = cut(base, (1,0,0), (xc-width*.20,0,0), "LeftTrack", palette["Steel_Dark"])
    middle, right = cut(rest, (1,0,0), (xc+width*.20,0,0), "RightTrack", palette["Steel_Dark"])
    for label, part in (("Left", left), ("Right", right)):
        if not part:
            continue
        box = mesh_bounds(part)
        paint(part, palette, "Steel_Dark", "track")
        pivot = (box[0] + box[1]) / 2
        part.name = label + "_Track_Casing"
        parent_at(part, root, pivot)
        assembly = empty(label + "_Track", root, pivot)
        track_loop(assembly, box, label + "_Track", palette)
    if middle:
        middle.name = "Chassis"
        paint(middle, palette, "Steel_Dark")
        parent_at(middle, root, (low + high) / 2)


def build_excavator(source, root, low, high, palette, image, config):
    _, _, _, boom_yz, elbow_yz, bucket_yz = config
    xc = (low[0] + high[0]) / 2
    length, height = high[1]-low[1], high[2]-low[2]
    boom_p = Vector((xc, *boom_yz))
    elbow_p = Vector((xc, *elbow_yz))
    bucket_p = Vector((xc, *bucket_yz))
    # Do not apply the diagonal arm cut to the tracks. The original collage is
    # welded: a single half-space otherwise assigns front track fragments to the bucket.
    front_arm, rear = cut(source, (0,1,0), (0,boom_p.y-length*.23,0),
                          "ArmFront", palette["Paint_Yellow"])
    base_height = low[2]+height*.34
    lower, rear_upper = cut(rear, (0,0,1), (0,0,base_height),
                            "ProtectUndercarriage", palette["Steel_Dark"])
    upper_arm, upper_hull = cut(rear_upper, (0,1,.35*length/height),
                    boom_p + Vector((0,length*.035,0)), "ArmHull", palette["Paint_Yellow"])
    arm = join([front_arm,upper_arm],"Arm")
    hull = join([lower,upper_hull],"Hull")
    stick_bucket, boom = cut(arm, (0,1,.12*length/height), elbow_p, "Elbow", palette["Paint_Yellow"])
    bucket, stick = cut(stick_bucket, (0,0,1), bucket_p + Vector((0,0,height*.018)),
                        "BucketWrist", palette["Steel_Blade"])
    if not all((hull, boom, bucket, stick)):
        raise RuntimeError(root.name + ": articulation region produced an empty part")
    base, upper = cut(hull, (0,0,1), (0,0,base_height), "Slew", palette["Steel_Dark"])
    if not base or not upper:
        raise RuntimeError(root.name + ": no base/upper-body separation")
    base_box = mesh_bounds(base)
    # Support is the actual track casing, not the lower bucket in the source collage.
    support = base_box[0][2]
    root["source_pivot"] = [xc, (base_box[0][1]+base_box[1][1])/2, support]
    root.location = root["source_pivot"]
    build_tracks(base, root, *base_box, palette)
    slew_p = (xc, (base_box[0][1]+base_box[1][1])/2, base_height)
    turret = empty("Upper_Slew", root, slew_p)
    paint(upper, palette, "Paint_Yellow", "body", image,
          lambda p: low[2]+height*.47 < p.z < low[2]+height*.75 and
          p.y < high[1]-length*.10)
    upper.name = "Cab_And_Counterweight"
    parent_at(upper, turret, slew_p)
    joints = []
    for name, obj, parent, pivot, material in (
        ("Boom", boom, turret, boom_p, "Paint_Yellow"),
        ("Stick", stick, None, elbow_p, "Paint_Yellow"),
        ("Bucket", bucket, None, bucket_p, "Steel_Blade"),
    ):
        if parent is None:
            parent = joints[-1]
        obj.name = name
        paint(obj, palette, material)
        parent_at(obj, parent, pivot)
        radius = height*.022
        cylinder(name+"_Hinge_Pin", obj, pivot-Vector((height*.10,0,0)),
                 pivot+Vector((height*.10,0,0)), radius, palette["Steel_Dark"])
        joints.append(obj)
    boom, stick, bucket = joints
    bpy.context.view_layer.update()
    park = 0
    for angle in np.linspace(0, -.6, 61):
        boom.rotation_euler.x = float(angle)
        bpy.context.view_layer.update()
        minimum = min((bucket.matrix_world @ v.co).z for v in bucket.data.vertices)
        if minimum >= support + height*.025:
            park = math.degrees(angle)
            break
    else:
        raise RuntimeError(root.name + ": could not lift the bucket clear of track support")
    key_rotation(turret, 2, [(1,0),(25,-15),(49,15),(73,0),(97,0),(121,0),(145,0)])
    key_rotation(boom, 0, [(1,park),(25,park-9),(49,park-16),(73,park-6),(97,park),(121,park),(145,park)])
    key_rotation(stick, 0, [(1,0),(25,9),(49,-10),(73,6),(97,0),(121,0),(145,0)])
    key_rotation(bucket, 0, [(1,0),(25,-12),(49,-20),(73,10),(97,0),(121,0),(145,0)])
    root["ParkedBucketLiftDegrees"] = park
    return [
        {"name":"Boom_Lift", "a":turret, "ap":boom_p+Vector((0,length*.07,-height*.05)),
         "b":boom, "bp":boom_p+Vector((0,-length*.16,height*.26)), "radius":height*.018},
        {"name":"Stick_Extension", "a":boom,
         "ap":boom_p+(elbow_p-boom_p)*.50+Vector((0,0,height*.16)),
         "b":stick,"bp":elbow_p+(bucket_p-elbow_p)*.25+Vector((0,0,height*.06)),
         "radius":height*.013},
        {"name":"Bucket_Curl", "a":stick,
         "ap":elbow_p+(bucket_p-elbow_p)*.56+Vector((0,-length*.018,height*.018)),
         "b":bucket,"bp":bucket_p+Vector((0,-length*.03,-height*.025)),
         "radius":height*.011},
    ]


def wheel_parts(source, low, high, palette, axles):
    """Extract joined scanned wheels at six bounded tire regions, with capped cuts."""
    width, length, height = high-low
    xc = (low[0]+high[0])/2
    wheel_band, source = cut(source, (0,0,1), (0,0,low[2]+height*.32),
                            "WheelBand", palette["Steel_Dark"])
    if not wheel_band:
        return source, []
    outer_left, central = cut(wheel_band, (1,0,0), (low[0]+width*.25,0,0),
                              "WheelLeft", palette["Steel_Dark"])
    inner, outer_right = cut(central, (1,0,0), (high[0]-width*.25,0,0),
                             "WheelRight", palette["Steel_Dark"])
    leftovers = [source, inner]
    wheels = []
    for side, strip in (("Left",outer_left),("Right",outer_right)):
        if not strip:
            continue
        for index, relative in enumerate(axles):
            center_y = low[1]+length*relative
            front, rest = cut(strip, (0,1,0), (0,center_y-length*.085,0),
                              side+"WheelFront", palette["Rubber_Tires"])
            leftovers.append(front)
            wheel, strip = cut(rest, (0,1,0), (0,center_y+length*.085,0),
                               side+"WheelBack", palette["Rubber_Tires"])
            if wheel:
                wheel.name = f"Wheel_{side}_{index+1:02d}"
                wheels.append(wheel)
        leftovers.append(strip)
    return join(leftovers, "Cab_And_Chassis"), wheels


def build_truck(source, extras, root, low, high, palette, image, partial=False):
    width, length, height = high-low
    xc = (low[0]+high[0])/2
    if partial:
        hull, wheels = wheel_parts(source, low, high, palette, [.24,.79])
        bed = None
    else:
        front, rear = cut(source, (0,1,0), (0,low[1]+length*.30,0),
                          "BedFront", palette["Steel_Dark"])
        rear_base, bed = cut(rear, (0,0,1), (0,0,low[2]+height*.40),
                              "BedFloor", palette["Paint_Green"])
        hull = join([front,rear_base], "Cab_And_Chassis")
        if extras:
            wheels = extras
        else:
            hull, wheels = wheel_parts(hull, low, high, palette, [.16,.765,.925])
    def cab_material(face):
        p,n = face.center,face.normal
        in_cab = p.y < low[1]+length*(.95 if partial else .30)
        if not in_cab:
            return "Steel_Dark"
        if (low[2]+height*.43<p.z<low[2]+height*.71 and abs(n.z)<.55 and
                (abs(n.x)>.45 or p.y<low[1]+length*.10)):
            return "Glass_Tinted"
        return "Paint_OffWhite" if p.z>low[2]+height*.62 else "Paint_Green"
    # Remove raised front badge relief as well as its logo-bearing texture colors.
    for vertex in hull.data.vertices:
        p=vertex.co
        if (abs(p.x-xc)<width*.09 and p.y<low[1]+length*.065 and
                low[2]+height*.25<p.z<low[2]+height*.41):
            p.y=low[1]+length*.04
    hull.data.update()
    paint(hull, palette, "Paint_Green", "body", image,region_material=cab_material)
    hull.name = "Cab_And_Chassis"
    parent_at(hull, root, root["source_pivot"])
    for i, wheel in enumerate(wheels):
        box = mesh_bounds(wheel)
        pivot = (box[0]+box[1])/2
        wheel.name = f"Wheel_{'Left' if pivot[0]<xc else 'Right'}_{i:02d}"
        paint(wheel, palette, "Rubber_Tires", "wheel")
        parent_at(wheel, root, pivot)
        key_rotation(wheel, 0, [(1,0),(97,0),(121,0),(145,-360)])
        wheel["Control"] = "Roll about local X; steering may be added to a parent pivot."
    if bed:
        cargo = None
        if root.name.startswith("Tipper_Loaded"):
            bed, cargo = cut(bed, (0,0,1), (0,0,low[2]+height*.71),
                              "Cargo", palette["Steel_Dark"])
        bed.name = "Dump_Bed"
        paint(bed, palette, "Paint_Green")
        pivot = Vector((xc,high[1]-length*.075,low[2]+height*.40))
        parent_at(bed, root, pivot)
        key_rotation(bed, 0, [(1,0),(25,-12),(49,-38),(73,-18),(97,0),(121,0),(145,0)])
        if cargo:
            cargo.name = "Cargo_Removable"
            # Original textured stone is separate; it is not rigidly attached to the bed.
            parent_at(cargo, root, pivot)
            cargo["Control"] = "Separate static cargo. Hide/remove before the tipping demonstration; no physics claimed."
            for frame, hidden in [(1,False),(24,False),(25,True),(96,True),(97,False),(145,False)]:
                cargo.hide_render = hidden
                cargo.keyframe_insert(data_path="hide_render", frame=frame)
            cargo.hide_render = False
        return [{"name":"Bed_Lift", "a":root, "ap":pivot+Vector((0,-length*.40,-height*.09)),
                 "b":bed, "bp":pivot+Vector((0,-length*.26,height*.04)), "radius":height*.016}]
    return []


def build_dozer(source, root, low, high, palette, image, partial=False, rear_only=False):
    width, length, height = high-low
    xc = (low[0]+high[0])/2
    blade = None
    if not rear_only:
        blade, source = cut(source, (0,1,0), (0,low[1]+length*.27,0),
                             "BladeLift", palette["Paint_Yellow"])
    if partial:
        hull = source
    else:
        base, hull = cut(source, (0,0,1), (0,0,low[2]+height*.37),
                         "TrackBase", palette["Steel_Dark"])
        if base:
            build_tracks(base, root, *mesh_bounds(base), palette)
    if hull:
        hull.name = "Cab_And_Hull"
        paint(hull, palette, "Paint_Yellow", "body", image,
              lambda p: p.z > low[2]+height*.52)
        parent_at(hull, root, root["source_pivot"])
    if blade:
        blade.name = "Blade"
        paint(blade, palette, "Steel_Blade")
        lift_p = Vector((xc,low[1]+length*.43,low[2]+height*.22))
        carrier = empty("Blade_Lift", root, lift_p)
        pivot = Vector((xc,low[1]+length*.20,low[2]+height*.14))
        parent_at(blade, carrier, pivot)
        key_rotation(carrier, 0, [(1,0),(25,-5),(49,-10),(73,-4),(97,0),(121,0),(145,0)])
        key_rotation(blade, 0, [(1,0),(25,4),(49,8),(73,-2),(97,0),(121,0),(145,0)])
        return [{"name":"Blade_Hydraulic_"+side, "a":root,
                 "ap":Vector((xc+offset,low[1]+length*.58,low[2]+height*.46)),
                 "b":blade,"bp":Vector((xc+offset,low[1]+length*.22,low[2]+height*.22)),
                 "radius":height*.018}
                for side,offset in (("Left",-width*.25),("Right",width*.25))]
    return []


def bake_hydraulics(definitions, root, palette):
    for definition in definitions:
        a, b = definition["a"], definition["b"]
        ap = Vector(definition["ap"])-Vector(a["source_pivot"])
        bp = Vector(definition["bp"])-Vector(b["source_pivot"])
        bpy.context.scene.frame_set(1)
        bpy.context.view_layer.update()
        wa, wb = a.matrix_world @ ap, b.matrix_world @ bp
        length = (wb-wa).length
        tube_length = length*.55
        radius = definition["radius"]
        tube = cylinder(definition["name"]+"_Tube", root, wa, wb, radius, palette["Paint_Yellow"])
        rod = cylinder(definition["name"]+"_Rod", root, wa, wb, radius*.64, palette["Chrome_Hydraulic"])
        for frame in range(1,146):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            wa, wb = a.matrix_world @ ap, b.matrix_world @ bp
            direction = wb-wa
            if direction.length <= tube_length*.8:
                raise RuntimeError(root.name+": hydraulic exceeds its constructed stroke")
            direction.normalize()
            for obj, start, end in (
                (tube,wa,wa+direction*tube_length),
                (rod,wa+direction*tube_length*.75,wb),
            ):
                obj.location = root.matrix_world.inverted() @ ((start+end)/2)
                local_direction = root.matrix_world.inverted().to_3x3() @ (end-start)
                obj.rotation_euler = local_direction.to_track_quat("Z","Y").to_euler()
                obj.scale.z = (end-start).length
                for path in ("location","rotation_euler","scale"):
                    obj.keyframe_insert(data_path=path,frame=frame)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT/"FBX").mkdir(exist_ok=True)
    (OUT/"Textures").mkdir(exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(INSPECTION/"source-components.blend"))
    components = {int(o.name.rsplit("_",1)[1]):o for o in bpy.context.scene.objects
                  if o.type=="MESH" and o.name.startswith("SourceComponent_")}
    image = next(i for i in bpy.data.images if "basecolor" in i.name)
    palette = materials(OUT/"Textures")
    for obj in components.values():
        obj.data.transform(obj.matrix_world)
        obj.matrix_world = Matrix.Identity(4)
    roots, report = [], []
    scene = bpy.context.scene
    scene.frame_start, scene.frame_end = 1,145
    scene.render.fps = 24
    for config in CONFIG:
        name, kind, ids, *_ = config
        sources = [components[i] for i in ids]
        if kind == "truck" and len(sources)>1:
            source, extras = sources[0], sources[1:]
            lows_highs = [mesh_bounds(o) for o in sources]
            low = np.min([x[0] for x in lows_highs],axis=0)
            high = np.max([x[1] for x in lows_highs],axis=0)
        else:
            source, extras = join(sources,name+"_Source"), []
            low, high = mesh_bounds(source)
        pivot = ((low[0]+high[0])/2,(low[1]+high[1])/2,low[2])
        root = empty(name,None,pivot)
        root["VariantKind"] = kind
        root["SourceComponentIds"] = ids
        root["SourceUnits"] = "Source relative scale retained; dimensions are not surveyed real machine sizes."
        if kind == "excavator":
            hydraulics = build_excavator(source,root,low,high,palette,image,config)
        elif kind in ("truck","cab_part"):
            hydraulics = build_truck(source,extras,root,low,high,palette,image,kind=="cab_part")
        elif kind in ("dozer","blade_part","tracked_part"):
            hydraulics = build_dozer(source,root,low,high,palette,image,
                                     kind=="blade_part",kind=="tracked_part")
        else:
            base, bed = cut(source,(0,0,1),(0,0,low[2]+(high[2]-low[2])*.39),
                             "PartialBed",palette["Paint_Green"])
            if base:
                base.name="Partial_Chassis"
                paint(base,palette,"Steel_Dark")
                parent_at(base,root,pivot)
            bed.name="Dump_Bed"
            paint(bed,palette,"Paint_Green")
            parent_at(bed,root,((low[0]+high[0])/2,high[1]-(high[1]-low[1])*.07,
                                low[2]+(high[2]-low[2])*.39))
            key_rotation(bed,0,[(1,0),(25,-12),(49,-35),(73,-15),(97,0),(121,0),(145,0)])
            hydraulics=[]
        bake_hydraulics(hydraulics,root,palette)
        roots.append(root)
        scene.frame_set(1)
        bpy.context.view_layer.update()
        nodes = scene_objects(root)
        for node in nodes[1:]:
            role = node.name.rsplit(".",1)[0] if node.name.rsplit(".",1)[-1].isdigit() else node.name
            node["Role"] = role
            node.name = root.name+"__"+role
        report.append({
            "name":name,"kind":kind,"source_component_ids":ids,
            "source_bounds":{"min":low.tolist(),"max":high.tolist()},
            "nodes":[{"name":o.name,"parent":o.parent.name if o.parent else None,
                      "type":o.type,"role":o.get("Role"),
                      "vertices":len(o.data.vertices) if o.type=="MESH" else 0,
                      "control_axis":o.get("ControlAxis_Blender")} for o in nodes],
            "parked_bucket_lift_degrees":root.get("ParkedBucketLiftDegrees"),
        })
        print("RIG_BUILT",name,len(nodes),flush=True)
    # Save the edit-ready original arrangement, not twelve overlapping normalized roots.
    scene.frame_set(1)
    bpy.context.view_layer.update()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/"All_Variants_Articulated.blend"),compress=True)
    for root in roots:
        bpy.ops.object.select_all(action="DESELECT")
        nodes=scene_objects(root)
        for obj in nodes:
            obj.select_set(True)
        position=root.location.copy()
        root.location=(0,0,0)
        scene.frame_set(1)
        bpy.context.view_layer.update()
        bpy.ops.export_scene.fbx(
            filepath=str(OUT/"FBX"/(root.name+".fbx")),use_selection=True,
            object_types={"EMPTY","MESH"},apply_unit_scale=True,
            use_custom_props=True,
            axis_forward="-Z",axis_up="Y",use_mesh_modifiers=True,
            mesh_smooth_type="FACE",path_mode="COPY",embed_textures=True,
            bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=False,
            bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,
        )
        root.location=position
        print("FBX_EXPORTED",root.name,flush=True)
    report={"source_unchanged":True,"source_component_count":19,"variants":report,
            "fps":24,"clip_ranges":{"Articulation":[1,97],"Track_Or_Wheel_Travel":[121,145]},
            "materials":{k:{"rgba":list(v[0]),"metallic":v[1],"roughness":v[2]} for k,v in PALETTE.items()},
            "limitations":["AI-reconstructed shapes are not CAD mechanisms.",
                "Partial source variants stay partial; no missing vehicles are invented.",
                "Source scale retained; no measured dimensions inferred.",
                "Cargo is removable and has Blender preview-only visibility keys; FBX does not export visibility.",
                "No Unity or device execution was performed."]}
    (OUT/"Rig_Manifest.json").write_text(json.dumps(report,indent=2))
    print("ALL_VARIANTS_COMPLETE",len(roots),flush=True)


if __name__=="__main__":
    main()