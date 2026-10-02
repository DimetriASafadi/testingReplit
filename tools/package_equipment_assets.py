#!/usr/bin/env python3
"""Package the unbranded editable fleet masters and their audit, without Unity metadata."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parents[1]
asset_dir = root / "testingReplic/Assets/NewGaza/Art/EquipmentFBX"
expected = {"YellowExcavator.fbx", "GreenTipper.fbx", "YellowBulldozer.fbx"}
actual = {path.name for path in asset_dir.glob("*.fbx")}
if actual != expected:
    raise RuntimeError(f"Expected exactly the three fleet masters; got {actual}")
files = [path for path in asset_dir.rglob("*") if path.is_file()]
if any(path.suffix == ".meta" for path in files):
    raise RuntimeError("Do not package .meta files; Unity generates them on the user's machine.")
files += [root / "testingReplic/EQUIPMENT.ar.md",
          root / "exports/equipment/Equipment-FBX-Audit.json"]
if any(not path.is_file() for path in files):
    raise RuntimeError("Missing fleet documentation or validated FBX audit.")
destination = root / "exports/NewGaza-Equipment-FBX.zip"
with ZipFile(destination, "w", ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(files):
        name = path.relative_to(asset_dir) if path.is_relative_to(asset_dir) else Path(path.name)
        archive.write(path, name.as_posix())
with ZipFile(destination) as archive:
    error = archive.testzip()
    if error:
        raise RuntimeError(f"Archive CRC failed: {error}")
    if len([name for name in archive.namelist() if name.endswith(".fbx")]) != 3:
        raise RuntimeError("Fleet archive lost a model.")
print(f"{destination.relative_to(root)}: {destination.stat().st_size:,} bytes; CRC OK; no .meta")