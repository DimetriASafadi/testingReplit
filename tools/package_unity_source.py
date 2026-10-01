#!/usr/bin/env python3
"""Package the native project and offline basemap generator, excluding ignored caches."""
import pathlib
import subprocess
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
paths = subprocess.check_output(
    ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "testingReplic"],
    cwd=root,
).decode().split("\0")
paths += ["tools/fetch_gaza_basemap.py", "tools/package_unity_source.py"]
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
    }
    if not required <= names:
        raise RuntimeError("Missing native city resources: " + str(required - names))
    if any("/cache/" in name or "/bin/" in name or "/obj/" in name for name in names):
        raise RuntimeError("Generated caches must not be packaged")
print(f"{destination.relative_to(root)}: {destination.stat().st_size:,} bytes; {len(names)} files; CRC OK")