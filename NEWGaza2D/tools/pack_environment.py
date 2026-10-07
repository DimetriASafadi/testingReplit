"""Crop the twelve alpha decals into small runtime WebPs with ground anchors."""
from pathlib import Path
import json
from PIL import Image, ImageFilter

root = Path(__file__).resolve().parents[1]
art = root / "public/art/realistic"
sheet = Image.open(art / "environment-sheet.png").convert("RGBA")
manifest = {}
for n in range(12):
    x, y = n % 4, n // 4
    cell = sheet.crop((round(x * sheet.width / 4), round(y * sheet.height / 3),
                       round((x + 1) * sheet.width / 4), round((y + 1) * sheet.height / 3)))
    bbox = cell.getchannel("A").point(lambda v: 255 if v > 12 else 0).getbbox()
    if not bbox:
        raise ValueError(f"Empty environment cell {n}")
    cell = cell.crop(bbox)
    w, h = cell.size
    cell = cell.resize((240, round(h * 240 / w)), Image.Resampling.LANCZOS)
    alpha = cell.getchannel("A")
    cell.putalpha(alpha.filter(ImageFilter.GaussianBlur(.4)))
    cell.save(art / f"environment-{n}.webp", quality=84, method=4)
    manifest[str(n)] = {"w": cell.width, "h": cell.height, "ay": .57}
(root / "src/environment-manifest.json").write_text(json.dumps(manifest))
print("Packed twelve integrated ground/curb/rubble decals.")
