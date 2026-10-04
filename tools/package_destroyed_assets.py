#!/usr/bin/env python3
"""Package original destroyed FBX masters, native resources and honest previews."""
import pathlib
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROJECT = ROOT / "testingReplic"
FBX = PROJECT / "Assets/NewGaza/Art/DestroyedFBX"
MODELS = PROJECT / "Assets/NewGaza/Resources/Models"
EXPORTS = ROOT / "exports/destroyed-assets"
KEYS = (
    "shujaiya", "tuffah", "sheikh_radwan", "daraj", "karama", "old_city",
    "nasr", "sabra", "zeitoun", "rimal", "tel_al_hawa", "sheikh_ijlin", "rashid",
    "mosque", "school", "clinic", "civic", "wall", "car", "crater", "debris",
    "context_collapse", "context_shell", "context_pancake", "context_masonry",
)
files = []
for suffix in KEYS:
    key = "ruin_" + suffix
    files.append((FBX / (key + ".fbx"), "FBX/" + key + ".fbx"))
    for extension in (".obj", "_albedo.png", "_manifest.json"):
        files.append((MODELS / (key + extension), "Unity-Resources/Models/" + key + extension))
for texture in FBX.glob("*.png"):
    files.append((texture, "FBX/" + texture.name))
files += [
    (PROJECT / "DESTROYED-ASSETS.ar.md", "README.ar.md"),
    (EXPORTS / "Manifest.json", "Manifest.json"),
    (ROOT / "tools/generate_destroyed_assets.py", "tools/generate_destroyed_assets.py"),
]
for name in ("Regional-East-Central.png", "Regional-Urban-Coast.png", "Destroyed-Objects.png"):
    files.append((EXPORTS / name, "Offline-Blender-Previews/" + name))
for source, _ in files:
    if not source.is_file() or source.stat().st_size == 0:
        raise FileNotFoundError(source)
destination = ROOT / "exports/NewGaza-Destroyed-FBX.zip"
with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for source, name in files:
        archive.write(source, name)
with zipfile.ZipFile(destination) as archive:
    if archive.testzip() is not None:
        raise RuntimeError("Destroyed asset archive failed CRC validation")
    if any(name.endswith(".meta") for name in archive.namelist()):
        raise RuntimeError("Never deliver generated .meta files")
    if len([name for name in archive.namelist() if name.endswith(".fbx")]) != 25:
        raise RuntimeError("All 25 real FBX masters must be delivered")
print(f"{destination.relative_to(ROOT)}: {destination.stat().st_size:,} bytes; 25 FBX; CRC OK")