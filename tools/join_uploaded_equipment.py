#!/usr/bin/env python3
"""Restore the GitHub-delivered FBX/Blender bundle and verify SHA-256."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
folder = root / "exports"
manifest = json.loads((folder / "Uploaded-Equipment-All-Variants.parts.json").read_text())
output = folder / manifest["archive"]
temporary = output.with_suffix(output.suffix + ".assembling")
digest = hashlib.sha256()
try:
    with temporary.open("wb") as target:
        for part in manifest["parts"]:
            path = folder / part["name"]
            if path.stat().st_size != part["bytes"]:
                raise ValueError(f"Incorrect part size: {path.name}")
            part_digest = hashlib.sha256()
            with path.open("rb") as source:
                while data := source.read(1024 * 1024):
                    part_digest.update(data)
                    digest.update(data)
                    target.write(data)
            if part_digest.hexdigest() != part["sha256"]:
                raise ValueError(f"Part checksum failed: {path.name}")
    if temporary.stat().st_size != manifest["bytes"] or digest.hexdigest() != manifest["sha256"]:
        raise ValueError("Reconstructed archive checksum failed")
    temporary.replace(output)
finally:
    temporary.unlink(missing_ok=True)
print(f"Verified: {output}")