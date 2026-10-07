"""Match sprite/terrain lighting without sacrificing independently playable plots.

Local baselines are ignored authoring inputs; re-running is deterministic, not
another destructive filter pass. Runtime assets remain compact WebP/JPEG.
"""
from pathlib import Path
import argparse
import shutil
from PIL import Image, ImageEnhance, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "public/art/realistic"
BASELINE = ROOT / "art-src/style-baseline"
LUT = [round(17 + 221 * ((v / 255) ** 0.98)) for v in range(256)]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--refresh-baseline", action="store_true",
                    help="Use only after packing newly authored, ungraded source art.")
REFRESH = parser.parse_args().refresh_baseline


def harmonize(path, terrain=False):
    relative = path.relative_to(ART)
    source = BASELINE / relative
    source.parent.mkdir(parents=True, exist_ok=True)
    if REFRESH or not source.exists():
        shutil.copy2(path, source)
    original = Image.open(source).convert("RGBA")
    alpha = original.getchannel("A")
    rgb = original.convert("RGB")
    # A consistent light/ink range, rather than photographic crushed blacks.
    rgb = ImageEnhance.Color(rgb).enhance(0.87 if not terrain else 0.91)
    rgb = rgb.point(LUT * 3)
    # Reduce photographic micro-noise; keep painted architectural edges legible.
    rgb = Image.blend(rgb, rgb.filter(ImageFilter.GaussianBlur(0.48)), 0.34)
    if not terrain or "water" not in path.name:
        dust = Image.new("RGB", rgb.size, (166, 155, 135))
        rgb = Image.blend(rgb, dust, 0.035 if not terrain else 0.025)
    if path.suffix == ".webp":
        rgba = rgb.convert("RGBA")
        rgba.putalpha(alpha)  # Do not change the ground anchor/footprint.
        rgba.save(path, "WEBP", quality=85, method=6)
    else:
        rgb.save(path, "JPEG", quality=84, optimize=True)


for image in sorted((ART / "s").glob("*.webp")):
    harmonize(image)
for image in sorted(ART.glob("ground-*.jpg")):
    harmonize(image, terrain=True)
print("Matched sprite and terrain light range; preserved every footprint and alpha.")
