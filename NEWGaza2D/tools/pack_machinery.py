"""Pack CPU renders into three mobile-size alpha atlases and shared pose metadata."""
from pathlib import Path
import hashlib
import json
import math
from PIL import Image, ImageEnhance

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "art-src/machinery-frames"
OUT = ROOT / "public/art/machinery"
OUT.mkdir(parents=True, exist_ok=True)
data = json.loads((SRC / "manifest.json").read_text())
size, cols = data["size"], 8
manifest = {**data, "revision": "", "atlases": {}}
lut = [round(17 + 221 * ((v / 255) ** .98)) for v in range(256)]
digest = hashlib.sha256()
for kind in ("excavator", "truck", "bulldozer"):
    keys = [k for k in data["frames"] if k.startswith(kind + "-")]
    atlas = Image.new("RGBA", (cols * size, math.ceil(len(keys) / cols) * size))
    frames = {}
    for n, key in enumerate(keys):
        path = SRC / (key + ".png")
        im = Image.open(path).convert("RGBA")
        if im.size != (size, size):
            raise ValueError(f"Invalid render size {key}")
        alpha = im.getchannel("A")
        rgb = ImageEnhance.Color(im.convert("RGB")).enhance(.87).point(lut * 3)
        im = rgb.convert("RGBA")
        im.putalpha(alpha)
        x, y = n % cols * size, n // cols * size
        atlas.paste(im, (x, y))
        frames[key] = {"frame": {"x": x, "y": y, "w": size, "h": size},
                       "rotated": False, "trimmed": False,
                       "spriteSourceSize": {"x": 0, "y": 0, "w": size, "h": size},
                       "sourceSize": {"w": size, "h": size}}
        digest.update(path.read_bytes())
        if key == kind + "-drive-0":
            im.save(OUT / (kind + "-preview.webp"), quality=87, method=4)
    image_name = kind + ".webp"
    atlas.save(OUT / image_name, quality=83, method=4)
    (OUT / (kind + ".json")).write_text(json.dumps({"frames": frames, "meta": {
        "image": image_name, "size": {"w": atlas.width, "h": atlas.height}, "scale": "1"}}))
    manifest["atlases"][kind] = {"w": atlas.width, "h": atlas.height, "count": len(keys)}
    if max(atlas.size) > 4096:
        raise ValueError("Atlas exceeds conservative mobile texture limit")
manifest["revision"] = digest.hexdigest()[:12]
(ROOT / "src/machinery-manifest.json").write_text(json.dumps(manifest))
print("Packed", len(data["frames"]), "rig-consistent machine frames; three alpha atlases.")
