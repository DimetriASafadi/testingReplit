#!/usr/bin/env python3
"""Restore the complete downloadable Unity ZIP from the verified GitHub parts."""
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
manifest = json.loads((ROOT / "exports/NewGaza-Unity-Source.parts.json").read_text())
target = ROOT / "exports/NewGaza-Unity-Source.zip"
digest = hashlib.sha256()
with target.open("wb") as output:
    for part in manifest["parts"]:
        payload = (ROOT / "exports" / part["name"]).read_bytes()
        if hashlib.sha256(payload).hexdigest() != part["sha256"]:
            raise RuntimeError("Corrupt archive part: " + part["name"])
        digest.update(payload)
        output.write(payload)
if digest.hexdigest() != manifest["sha256"] or target.stat().st_size != manifest["bytes"]:
    raise RuntimeError("Restored archive failed verification")
print(f"{target.relative_to(ROOT)}: {target.stat().st_size:,} bytes; SHA-256 verified")