"""Offline camera alignment for open-land and retail artwork.

Fit the visible front ground edges, not the image bbox. Keep vertical lines
vertical, map the two ground directions to +/-0.5, and anchor at the measured
front vertex. No runtime warping. Run with python3.13 (Pillow toolchain).
Original published images/metadata are retained under generated-artifacts.
"""
from pathlib import Path
from statistics import median
import argparse
import hashlib
import io
import json
import math
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "public/art/realistic"
BASE = ROOT.parent / "generated-artifacts/camera-ground-baseline"
REPORT = BASE.parent / "camera-ground-report.json"
STEMS = {
    "rpark", "club", "zoo", "wheat", "citrus", "olive", "veg", "straw",
    "cattle", "palms", "corn", "trees", "orn", "grapes",
    "western", "luxury", "cafe", "falafel", "shawarma", "kiosk",
    "foodshop", "clothes", "shoe", "hshop", "smarket", "toys", "juiceshop",
}
FACES = ("front", "right", "back", "left")


def fit_line(points):
    slopes = [(y2-y)/(x2-x) for i, (x, y) in enumerate(points)
              for x2, y2 in points[i+1:] if x2-x >= 8]
    if not slopes:
        raise ValueError("No reliable ground edge")
    slope = median(slopes)
    intercept = median(y-slope*x for x, y in points)
    confidence = sum(abs(y-slope*x-intercept) < 2 for x, y in points)/len(points)
    return slope, intercept, confidence


def measure(image):
    w, h = image.size
    alpha = image.getchannel("A")
    points = []
    for x in range(w):
        ys = [y for y in range(h//3, h) if alpha.getpixel((x, y)) > 100]
        if ys:
            points.append((x, ys[-1]))
    left = fit_line([(x, y) for x, y in points if .12*w < x < .43*w])
    right = fit_line([(x, y) for x, y in points if .57*w < x < .88*w])
    ml, cl, ql = left
    mr, cr, qr = right
    if ml <= mr:
        raise ValueError("Ground edges do not form a forward-facing base")
    a = 1/(ml-mr)
    b = .5-a*ml
    fx = (cr-cl)/(ml-mr)
    fy = ml*fx+cl
    if not (.65 < a < 1.35 and abs(b) < .1 and min(ql, qr) >= .65 and .4 < fx/w < .6):
        raise ValueError("Occluded/irregular ground edge; not safe for automatic projection")
    return {"left": ml, "right": mr, "a": a, "b": b, "fx": fx, "fy": fy,
            "confidence": min(ql, qr)}


def reproject(image, calibration, width):
    a, b, fx, fy = (calibration[k] for k in ("a", "b", "fx", "fy"))
    dx = width/2-fx
    w, h = image.size
    corners = [a*y+b*x for x, y in [(0, 0), (w, 0), (0, h), (w, h)]]
    dy = 2-math.floor(min(corners))
    height = math.ceil(max(corners)+dy)+2
    # x'=x+dx; y'=a*y+b*x+dy. No x/y mixing in the x direction:
    # a vertical wall remains vertical, rather than leaning with a homography.
    out = image.transform((width, height), Image.Transform.AFFINE,
                          (1, 0, -dx, -b/a, 1/a, (b*dx-dy)/a),
                          Image.Resampling.BICUBIC)
    bbox = out.getchannel("A").getbbox()
    if not bbox:
        raise ValueError("Projection produced an empty image")
    top, bottom = bbox[1], bbox[3]
    out = out.crop((0, top, width, bottom))
    center = a*fy+b*fx+dy-top-width/4
    if not (0 < center < out.height):
        raise ValueError("Ground anchor is outside the sprite")
    return out, {"w": width, "h": out.height, "ay": round(center/out.height, 6), "cameraGround": 1,
                 "ground": {"w": width, "h": width/2}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Verify published outputs against retained sources")
    args = parser.parse_args()
    manifest = json.loads((ROOT / "src/realistic-manifest.json").read_text())
    if not BASE.exists():
        if args.check:
            raise SystemExit("Projection baseline is missing")
        if any(v.get("cameraGround") for v in manifest.values()):
            raise SystemExit("Cannot capture already projected artwork as a new source. Restore the retained baseline or supply freshly packed, unprojected artwork.")
        BASE.mkdir(parents=True)
        (BASE / "manifest.json").write_text(json.dumps(manifest))
        for stem in STEMS:
            for face in FACES:
                key = "farm_cattle" if stem == "cattle" and face == "front" else f"{stem}_{face}"
                (BASE / f"{key}.webp").write_bytes((ART / "s" / f"{key}.webp").read_bytes())
    source_manifest = json.loads((BASE / "manifest.json").read_text())
    outputs, report, skipped = {}, {}, {}
    for stem in sorted(STEMS):
        keys = ["farm_cattle" if stem == "cattle" and face == "front" else f"{stem}_{face}" for face in FACES]
        if all(manifest.get(key, {}).get("canonicalFarm") for key in keys):
            continue
        images = {key: Image.open(BASE / f"{key}.webp").convert("RGBA") for key in keys}
        try:
            calibrations = {key: measure(image) for key, image in images.items()}
        except ValueError as error:
            # Do not mix calibrated and uncalibrated faces of one building.
            skipped[stem] = str(error)
            continue
        width = math.ceil(max(2*max(v["fx"], images[k].width-v["fx"]) for k, v in calibrations.items()))+2
        for key in keys:
            image, meta = reproject(images[key], calibrations[key], width)
            if image.width > 512 or image.height > 512:
                raise ValueError(f"Mobile sprite bound exceeded: {key}")
            encoded = io.BytesIO()
            image.save(encoded, "WEBP", quality=94, method=6)
            outputs[key] = (encoded.getvalue(), meta)
            report[key] = {**calibrations[key], "source": source_manifest[key], "output": meta,
                           "sha256": hashlib.sha256(encoded.getvalue()).hexdigest()}
    if not outputs:
        raise SystemExit("No safely measurable image families")
    for key, (data, meta) in outputs.items():
        target = ART / "s" / f"{key}.webp"
        if args.check:
            if target.read_bytes() != data or manifest[key] != meta:
                raise SystemExit(f"Published image/metadata mismatch: {key}")
        else:
            target.write_bytes(data)
            manifest[key] = meta
    if not args.check:
        for path in [ROOT / "src/realistic-manifest.json", ART / "manifest.json"]:
            path.write_text(json.dumps(manifest))
        REPORT.write_text(json.dumps({"poses": report, "not_automatically_changed": skipped}, indent=2))
    print(f'{"Verified" if args.check else "Projected"} {len(outputs)} poses / {len(outputs)//4} families.')
    print(f"Unchanged irregular silhouettes (not guessed): {', '.join(skipped) or 'none'}")
    print(f"Sources and calibration report: {BASE}")


if __name__ == "__main__":
    main()
