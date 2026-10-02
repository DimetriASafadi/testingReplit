#!/usr/bin/env python3
"""Package the native project and offline basemap generator, excluding ignored caches."""
import pathlib
import hashlib
import json
import subprocess
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
paths = subprocess.check_output(
    ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "testingReplic"],
    cwd=root,
).decode().split("\0")
paths += ["tools/fetch_gaza_basemap.py", "tools/package_unity_source.py"]
paths += ["tools/join_unity_source.py"]
paths += ["tools/prepare_equipment_audio.py"]
paths += ["tools/export_equipment_fbx.py", "tools/render_equipment_proof.py",
          "tools/package_equipment_assets.py"]
paths += ["tools/generate_destroyed_assets.py", "tools/render_destroyed_assets.py",
          "tools/compose_destroyed_previews.py", "tools/package_destroyed_assets.py"]
paths += ["tools/generate_housing_assets.py", "tools/render_housing_assets.py",
          "tools/compose_housing_previews.py", "tools/package_housing_assets.py",
          "tools/render_construction_workers.py"]
paths += [str(path.relative_to(root)) for path in (root / "tools").glob("housing_*.py")]
paths += ["pyproject.toml", "uv.lock"]
paths += [str(path.relative_to(root)) for path in
          (root / "attached_assets/generated_audio").glob("new-gaza-*.mp3")]
destination = root / "exports/NewGaza-Unity-Source.zip"
destination.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for name in sorted(set(filter(None, paths))):
        path = root / name
        if path.is_file():
            archive.write(path, name)
with zipfile.ZipFile(destination) as archive:
    error = archive.testzip()
    if error:
        raise RuntimeError("Archive CRC failed: " + error)
    names = set(archive.namelist())
    required = {
        "testingReplic/Assets/NewGaza/Resources/GazaBasemap.json",
        "testingReplic/MapData/GazaBasemap-source.json.gz",
        "testingReplic/Assets/NewGaza/Resources/NewGazaSea.shader",
        "testingReplic/Assets/NewGaza/Runtime/CityAudio.cs",
        "testingReplic/Assets/NewGaza/World/EquipmentTrackRig.cs",
        "testingReplic/Assets/NewGaza/UI/CityHudAudio.cs",
        "testingReplic/Assets/NewGaza/Core/RoadEconomy.cs",
        "testingReplic/Assets/NewGaza/World/CityRoadNetwork.cs",
        "testingReplic/Assets/NewGaza/World/CityRoadView.cs",
        "testingReplic/Assets/NewGaza/UI/CityHudRoad.cs",
        "testingReplic/Tests/Roads/RoadsTests.csproj",
        "testingReplic/Tests/RoadView/RoadViewTests.csproj",
        "testingReplic/Assets/NewGaza/World/CityRuinProfiles.cs",
        "testingReplic/Tests/DestroyedAssets/DestroyedAssets.csproj",
        "testingReplic/DESTROYED-ASSETS.ar.md",
        "testingReplic/EQUIPMENT.ar.md",
        "testingReplic/Assets/NewGaza/Resources/NewGazaDust.shader",
        "testingReplic/Assets/NewGaza/World/EquipmentDustRig.cs",
        "testingReplic/Assets/NewGaza/World/CityFleetAppearance.cs",
        "testingReplic/Assets/NewGaza/World/CityFleetEffects.cs",
        "testingReplic/Tests/Equipment/EquipmentDustChecks.cs",
        "testingReplic/HOUSING.ar.md",
        "testingReplic/Assets/NewGaza/World/CityHousingProfiles.cs",
        "testingReplic/Assets/NewGaza/World/CityConstructionVisuals.cs",
        "testingReplic/Assets/NewGaza/World/CityConstructionCrew.cs",
        "testingReplic/Assets/NewGaza/World/CityConstructionWorkerRig.cs",
        "testingReplic/Tests/Construction/ConstructionTests.csproj",
        "testingReplic/Tests/ConstructionCrew/ConstructionCrewTests.csproj",
    }
    for name in ("YellowExcavator", "GreenTipper", "YellowBulldozer"):
        required.add("testingReplic/Assets/NewGaza/Art/EquipmentFBX/" + name + ".fbx")
    housing_masters = ("house_small_redtile", "house_cream_family", "house_modern_villa",
        "apartment_4floor_balcony", "apartment_6floor_balcony", "house_compound",
        "apartment_blueglass_midrise", "house_traditional_stonearches",
        "apartment_12floor_tower", "house_coastal_white_pool")
    for master in housing_masters:
        for stage in ("foundation", "frame", "finishing", "final"):
            key = master + "_" + stage
            required.add("testingReplic/Assets/NewGaza/Art/HousingFBX/" + key + ".fbx")
            for extension in (".obj", "_albedo.png", "_manifest.json"):
                required.add("testingReplic/Assets/NewGaza/Resources/Models/" + key + extension)
    ruin_keys = ("shujaiya", "tuffah", "sheikh_radwan", "daraj", "karama", "old_city",
                 "nasr", "sabra", "zeitoun", "rimal", "tel_al_hawa", "sheikh_ijlin",
                 "rashid", "mosque", "school", "clinic", "civic", "wall", "car",
                 "crater", "debris")
    for suffix in ruin_keys:
        key = "ruin_" + suffix
        required.add("testingReplic/Assets/NewGaza/Art/DestroyedFBX/" + key + ".fbx")
        for extension in (".obj", "_albedo.png", "_manifest.json"):
            required.add("testingReplic/Assets/NewGaza/Resources/Models/" + key + extension)
    required.update("testingReplic/Assets/NewGaza/Resources/Audio/" + key + ".wav" for key in
        ("excavator_engine", "truck_engine", "dozer_engine", "hydraulics", "tracks",
         "wind_high", "coastal_surf", "ui_click", "ui_confirm"))
    if not required <= names:
        raise RuntimeError("Missing native city resources: " + str(required - names))
    if any("/cache/" in name or "/bin/" in name or "/obj/" in name for name in names):
        raise RuntimeError("Generated caches must not be packaged")
    if any(name.startswith("testingReplic/Assets/NewGaza/") and name.endswith(".meta")
           for name in names):
        raise RuntimeError("NewGaza .meta files must be generated by the user's Unity editor")
print(f"{destination.relative_to(root)}: {destination.stat().st_size:,} bytes; {len(names)} files; CRC OK")
parts = []
with destination.open("rb") as source:
    for number in range(1, 100):
        payload = source.read(24 * 1024 * 1024)
        if not payload:
            break
        part = destination.with_name(destination.name + ".part" + str(number).zfill(2))
        part.write_bytes(payload)
        parts.append({"name": part.name, "sha256": hashlib.sha256(payload).hexdigest(),
                      "bytes": len(payload)})
manifest = {"bytes": destination.stat().st_size,
            "sha256": hashlib.sha256(destination.read_bytes()).hexdigest(), "parts": parts}
(root / "exports/NewGaza-Unity-Source.parts.json").write_text(
    json.dumps(manifest, indent=2), encoding="utf-8")
print(f"GitHub delivery: {len(parts)} verified archive parts, at most 24 MiB each.")