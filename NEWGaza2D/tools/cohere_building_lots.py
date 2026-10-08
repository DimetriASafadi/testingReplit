"""Pack corrected poses, soften every completed sprite's ground seam, and export
ground-contact metadata. Never touch ruins, game saves or original authoring art.
Run AFTER grading/alignment; idempotent baselines prevent cumulative filtering.
"""
from pathlib import Path
import argparse
import json
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "public/art/realistic"
BASE = ROOT / "art-src/lot-cohesion-baseline"
REDRAW = ROOT / "art-src/lot-redraw"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--import-redraws", action="store_true")
args = parser.parse_args()
manifest = json.loads((ROOT / "src/realistic-manifest.json").read_text())
BASE.mkdir(parents=True, exist_ok=True)
if args.import_redraws:
    for source in [*sorted(REDRAW.glob("clean_*.png")),*sorted(REDRAW.glob("next_*.png"))]:
        key = source.stem.removeprefix("clean_").removeprefix("next_")
        if key not in manifest:
            raise ValueError(f"Unrecognized corrected pose: {key}")
        target = BASE / f"{key}.webp"
        if target.exists() and target.stat().st_mtime >= source.stat().st_mtime:
            continue
        # Preserve unmodified authored sources; only overwrite this stage's
        # baseline with newly generated, trimmed/graded art.
        image = Image.open(source).convert("RGBA")
        bbox = image.getchannel("A").point(lambda a: 255 if a > 20 else 0).getbbox()
        if not bbox:
            raise ValueError(f"Empty corrected pose: {key}")
        image = image.crop(bbox)
        width = 196
        scale = min(width / image.width, 330 / image.height)
        image = image.resize((round(image.width * scale), round(image.height * scale)), Image.Resampling.LANCZOS)
        # Match the current warm painted palette without restoring stale
        # global harmonization baselines or undoing planted-ground alignment.
        from PIL import ImageEnhance
        alpha = image.getchannel("A")
        rgb = ImageEnhance.Color(image.convert("RGB")).enhance(.87)
        rgb = Image.blend(rgb, Image.new("RGB", rgb.size, (166,155,135)), .035)
        image = rgb.convert("RGBA"); image.putalpha(alpha)
        image.save(BASE / f"{key}.webp", "WEBP", quality=88, method=6)
        (BASE / f"{key}.json").write_text(json.dumps({"w":image.width,"h":image.height,
            "ay":1-image.width/(4*image.height),"ground":{"w":image.width,"h":image.width/2}}))

count = 0
for path in sorted((ART / "s").glob("*.webp")):
    key = path.stem
    if not key.endswith(("_front", "_back", "_right", "_left")) and key != "farm_cattle":
        continue
    source = BASE / path.name
    metadata = BASE / f"{key}.json"
    if not source.exists():
        source.write_bytes(path.read_bytes())
        metadata.write_text(json.dumps(manifest[key]))
    m = json.loads(metadata.read_text())
    image = Image.open(source).convert("RGBA")
    w,h = image.size
    cy = m["ay"] * h
    gw = m.get("ground",{}).get("w",w)
    gh = m.get("ground",{}).get("h",gw/2)
    alpha = image.getchannel("A")
    soft = alpha.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(1.4))
    pixels = image.load()
    for y in range(max(0,round(cy)),h):
        for x in range(w):
            r,g,b,a = pixels[x,y]
            if not a:
                continue
            metric = abs(x-w/2)/(gw/2)+abs(y-cy)/(gh/2)
            edge = max(0,min(1,(1.065-metric)/.10))
            front = min(1,max(0,(y-cy)/(gh*.2)))
            # Feather only the ground-bearing lower edges. Roofs, windows,
            # foliage and upper architectural outlines remain crisp.
            feather = 1-front+front*soft.getpixel((x,y))/255
            new_alpha = round(a*edge*feather)
            # A little settled dust where the slab meets the district soil.
            dust = max(0,min(.22,(metric-.72)*.7))*front
            pixels[x,y]=(round(r*(1-dust)+147*dust),round(g*(1-dust)+139*dust),
                round(b*(1-dust)+120*dust),new_alpha)
    image.save(path,"WEBP",quality=88,method=4)
    manifest[key]={**m,"ground":{"w":gw,"h":gh}}
    count += 1
for p in [ROOT / "src/realistic-manifest.json",ART / "manifest.json"]:
    p.write_text(json.dumps(manifest))
print(f"Calibrated/feathered {count} finished poses; all sources and anchors preserved.")
