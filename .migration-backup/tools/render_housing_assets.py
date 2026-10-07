#!/usr/bin/env python3
"""Render the delivered housing FBXs, not substitute geometry or Unity screenshots."""
import argparse
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MASTERS = (
    "house_small_redtile", "house_cream_family", "house_modern_villa",
    "apartment_4floor_balcony", "apartment_6floor_balcony", "house_compound",
    "apartment_blueglass_midrise", "house_traditional_stonearches",
    "apartment_12floor_tower", "house_coastal_white_pool",
)
STAGES = ("foundation", "frame", "finishing", "final")
STAGE_EXAMPLES = ("house_cream_family", "apartment_4floor_balcony", "apartment_12floor_tower")


def main():
    sys.path.insert(0, str(ROOT / "tools"))
    import render_destroyed_assets as renderer
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--samples", type=int, default=16)
    parser.add_argument("--keys", nargs="*")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    renderer.FBX_ROOT = ROOT / "testingReplic/Assets/NewGaza/Art/HousingFBX"
    renderer.OUTPUT = ROOT / "exports/housing-assets/previews"
    keys = args.keys or list(dict.fromkeys(
        [master + "_final" for master in MASTERS] +
        [master + "_" + stage for master in STAGE_EXAMPLES for stage in STAGES]))
    output = ROOT / "exports/housing-assets/Preview-Audit.json"
    previous = json.loads(output.read_text()) if args.keys and output.is_file() else []
    by_key = {entry["key"]: entry for entry in previous}
    for key in keys:
        entry = renderer.render(key, args.samples)
        entry["sourceSha256"] = hashlib.sha256((ROOT / entry["source"]).read_bytes()).hexdigest()
        by_key[key] = entry
    result = list(by_key.values())
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Verified and rendered {len(result)} delivered FBX files using Cycles CPU.")


if __name__ == "__main__":
    main()