"""Align planted scenery's visible ground base to the game's 2:1 diamond.

Keep the upper tree/crop artwork at its original height. Reproject the lower
ground/fascia, and anchor at the actual side corners, not bbox-bottom - width/4.
Input backups make reruns idempotent; original artwork is never overwritten.
"""
from pathlib import Path
import argparse
import json
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "public/art/realistic"
BACKUP = ROOT / "art-src/ground-alignment-baseline"
STEMS = {"farm", "wheat", "citrus", "olive", "veg", "straw",
         "palms", "corn", "cattle", "trees", "orn", "zoo"}
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--refresh-baseline", action="store_true",
                    help="Only after packing and grading newly authored, unaligned art.")
refresh = parser.parse_args().refresh_baseline
manifest = json.loads((ROOT / "src/realistic-manifest.json").read_text())

for path in sorted((ART / "s").glob("*.webp")):
    key = path.stem
    if key.split("_")[0] not in STEMS:
        continue
    original = BACKUP / path.name
    if refresh or not original.exists():
        BACKUP.mkdir(parents=True, exist_ok=True)
        original.write_bytes(path.read_bytes())
    image = Image.open(original).convert("RGBA")
    w, h = image.size
    alpha = image.getchannel("A")
    spans = []
    for y in range(h // 2, h - 4):
        xs = [x for x in range(w) if alpha.getpixel((x, y)) > 160]
        if xs:
            spans.append((xs[-1] - xs[0] + 1, y))
    widest = max(width for width, _ in spans)
    rows = sorted(y for width, y in spans if width >= widest - 2)
    cy = rows[len(rows) // 2]
    depth = round(w / 4)
    out = Image.new("RGBA", (w, cy + depth))
    out.paste(image.crop((0, 0, w, cy)), (0, 0))
    ground = image.crop((0, cy, w, h)).resize((w, depth), Image.Resampling.LANCZOS)
    out.paste(ground, (0, cy))
    out.save(path, "WEBP", quality=88, method=6)
    manifest[key] = {"w": w, "h": out.height, "ay": round(cy / out.height, 6),
                     "ground": {"w": w, "h": depth * 2}}
    print(f"{key}: ground center {cy}, base {w}x{depth * 2}")

for path in [ROOT / "src/realistic-manifest.json", ART / "manifest.json"]:
    path.write_text(json.dumps(manifest))
