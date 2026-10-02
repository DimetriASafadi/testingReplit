#!/usr/bin/env python3
"""Package all 40 housing FBXs and their native runtime resources without .meta."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

from render_housing_assets import MASTERS, STAGES

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "testingReplic"
ART = PROJECT / "Assets/NewGaza/Art/HousingFBX"
MODELS = PROJECT / "Assets/NewGaza/Resources/Models"
OUTPUT = ROOT / "exports/housing-assets"


def main():
    files = []
    for master in MASTERS:
        for stage in STAGES:
            key = master + "_" + stage
            files.append((ART / (key + ".fbx"), "FBX/" + key + ".fbx"))
            for extension in (".obj", "_albedo.png", "_manifest.json"):
                files.append((MODELS / (key + extension), "Unity-Resources/Models/" + key + extension))
    files += [(path, "FBX/" + path.name) for path in ART.glob("*.png")]
    files += [
        (PROJECT / "HOUSING.ar.md", "README.ar.md"),
        (OUTPUT / "Manifest.json", "Manifest.json"),
        (OUTPUT / "Preview-Audit.json", "Preview-Audit.json"),
        (ROOT / "tools/generate_housing_assets.py", "tools/generate_housing_assets.py"),
    ]
    for helper in (ROOT / "tools").glob("housing_*.py"):
        files.append((helper, "tools/" + helper.name))
    for name in ("Housing-Reference-Families.png", "Housing-Construction-Stages.png"):
        files.append((OUTPUT / name, "Offline-Blender-Previews/" + name))
    for name in ("Construction-Workers-Source.gif", "Construction-Workers-Source.png"):
        files.append((OUTPUT / name, "Source-Executed-Crew-Previews/" + name))
    files.append((OUTPUT / "Construction-Crew-Frames.json", "Source-Executed-Crew-Previews/Frames.json"))
    for source, _ in files:
        if not source.is_file() or source.stat().st_size == 0:
            raise FileNotFoundError(source)
    destination = ROOT / "exports/NewGaza-Housing-FBX.zip"
    with ZipFile(destination, "w", ZIP_DEFLATED, compresslevel=9) as archive:
        for source, name in files:
            archive.write(source, name)
    with ZipFile(destination) as archive:
        if archive.testzip():
            raise RuntimeError("Housing archive failed CRC validation")
        if any(name.endswith(".meta") for name in archive.namelist()):
            raise RuntimeError("Unity must generate new .meta files locally")
        if sum(name.endswith(".fbx") for name in archive.namelist()) != 40:
            raise RuntimeError("All ten archetypes and four construction forms are required")
    print(f"{destination.relative_to(ROOT)}: {destination.stat().st_size:,} bytes; 40 FBX; CRC OK")


if __name__ == "__main__":
    main()