"""Build 11 farm quartets from upright botanical cutouts on a true 2:1 plane.

python3.13 tools/build_canonical_farms.py --kit ../generated-artifacts/farm-vegetation-kit.png
Subsequent runs use the retained small art-src/farm-kit cutouts. Runtime loads
only the finished WebPs and three tiny courtyard canopies, not individual crops.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageEnhance, ImageFilter
import argparse
import json
import math
import random
import shutil

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "public/art/realistic"
KIT = ROOT / "art-src/farm-kit"
W, H, CY = 256, 256, 144
NAMES = ["olive", "citrus", "palm", "cypress", "orn", "wheat",
         "corn", "veg", "straw", "vine", "tank", "cow"]
FARMS = ["wheat", "citrus", "olive", "veg", "straw", "cattle",
         "palms", "corn", "trees", "orn", "grapes"]
FACES = ["front", "right", "back", "left"]
SOIL = {"wheat": "#978a6c", "corn": "#8b8167", "citrus": "#8a816b",
        "olive": "#918773", "veg": "#817562", "straw": "#857360",
        "cattle": "#938b74", "palms": "#a0947c", "trees": "#8b856d",
        "orn": "#8d866d", "grapes": "#8b7d65"}


def rotate(u, v, turn):
    for _ in range(turn % 4):
        u, v = -v, u
    return u, v


def project(u, v, z=0, turn=0):
    u, v = rotate(u, v, turn)
    return W/2+64*(u-v), CY+32*(u+v)-z


def extract(path):
    sheet = Image.open(path).convert("RGBA")
    KIT.mkdir(parents=True, exist_ok=True)
    for index, name in enumerate(NAMES):
        col, row = index % 4, index // 4
        # The authoring sheet has taller tree cells, not equal-height rows.
        # Keep complete palm roots and don't include roots from the row above.
        bands=[0,.37,.67,1]
        cell = sheet.crop((round(col*sheet.width/4), round(bands[row]*sheet.height),
                           round((col+1)*sheet.width/4), round(bands[row+1]*sheet.height)))
        bbox = cell.getchannel("A").point(lambda a: 255 if a > 20 else 0).getbbox()
        if not bbox:
            raise ValueError(f"Missing botanical cutout: {name}")
        cell = cell.crop(bbox)
        cell.thumbnail((256, 256), Image.Resampling.LANCZOS)
        cell = ImageEnhance.Color(cell).enhance(.79)
        cell = ImageEnhance.Brightness(cell).enhance(.94)
        cell.save(KIT / f"{name}.png")


def soil(image, stem, turn):
    mask = Image.new("L", image.size)
    polygon = [project(-1, -1), project(1, -1), project(1, 1), project(-1, 1)]
    ImageDraw.Draw(mask).polygon(polygon, fill=255)
    texture = Image.open(OUT / "ground-earth.jpg").convert("RGB").resize((256, 256))
    texture = ImageEnhance.Color(texture).enhance(.35)
    texture = Image.blend(texture, Image.new("RGB", texture.size, SOIL[stem]), .64)
    plane = texture.transform(image.size, Image.Transform.AFFINE,
                              (1, 2, 128-W/2-2*CY, -1, 2, 128+W/2-2*CY),
                              Image.Resampling.BICUBIC).convert("RGBA")
    image.paste(plane, (0, 0), mask)
    d = ImageDraw.Draw(image)
    d.line(polygon+[polygon[0]], fill="#baae97", width=1)
    # All soil rows and service paths share the same camera projection.
    for i in range(12):
        v = -.86+i*.155
        d.line([project(-.91, v, turn=turn), project(.91, v, turn=turn)],
               fill="#7c725d", width=1)
    path = [project(u, v, turn=turn) for u, v in [(.77, -.96), (.93, -.96), (.93, .96), (.77, .96)]]
    d.polygon(path, fill="#b5a991")
    for v in [-.8, -.5, -.2, .1, .4, .7]:
        d.ellipse(box(project(.85, v, turn=turn), .6, .3), fill="#988b73")


def box(p, rx, ry):
    return p[0]-rx, p[1]-ry, p[0]+rx, p[1]+ry


def barn(image, turn):
    d = ImageDraw.Draw(image)
    corners = [(-.83, -.74), (.10, -.74), (.10, -.08), (-.83, -.08)]
    # Visibility depends on world-facing normals after rotation, not a mirrored
    # bitmap; masonry and corrugated roof use actual projected 3-D vertices.
    walls = []
    for i in range(4):
        a, b = corners[i], corners[(i+1) % 4]
        normal = rotate(b[1]-a[1], a[0]-b[0], turn)
        if normal[0]+normal[1] <= 0:
            continue
        quad = [project(*a, 0, turn), project(*b, 0, turn),
                project(*b, 13, turn), project(*a, 13, turn)]
        light = normal[1] > normal[0]
        d.polygon(quad, fill="#aa9e85" if light else "#827c69")
        d.line(quad+[quad[0]], fill="#716d5e", width=1)
        for z in [4, 8, 12]:
            d.line([project(*a, z, turn), project(*b, z, turn)], fill="#948a75")
        for t in [.15, .35, .55, .75]:
            u, v = a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t
            p = project(u, v, 9, turn)
            d.rectangle((p[0]-1, p[1], p[0]+1, p[1]+3), fill="#454e48")
    for side in [0, 2]:
        # Roof strips follow the building's local u axis in every orientation.
        edge_v = -.74 if side == 0 else -.08
        quad = [project(-.88, edge_v, 14, turn), project(.15, edge_v, 14, turn),
                project(.15, -.41, 20, turn), project(-.88, -.41, 20, turn)]
        d.polygon(quad, fill="#a6a797" if side == 0 else "#7e887e")
        for i in range(13):
            u = -.88+i*1.03/12
            d.line([project(u, edge_v, 14, turn), project(u, -.41, 20, turn)], fill="#737d73")


def render(stem, turn, assets):
    image = Image.new("RGBA", (W, H))
    soil(image, stem, turn)
    rng = random.Random(817+FARMS.index(stem)*71)
    nodes = []
    if stem in ["olive", "citrus", "palms", "trees", "orn"]:
        asset = {"palms": "palm", "trees": "cypress"}.get(stem, stem)
        count = 3 if stem == "palms" else 5 if stem in ["trees","orn"] else 4
        height = {"olive": 32, "citrus": 28, "palms": 47, "trees": 36, "orn": 21}[stem]
        for row in range(count):
            for col in range(count):
                u = -.73+col*(1.30/max(1, count-1))+rng.uniform(-.025, .025)
                v = -.73+row*(1.46/max(1, count-1))+rng.uniform(-.025, .025)
                nodes.append((u, v, asset, height*rng.uniform(.92, 1.05), 0))
    elif stem == "cattle":
        for u, v in [(-.65, .35), (-.2, .55), (.38, .5), (.48, -.45), (.25, .08)]:
            nodes.append((u, v, "cow", 8, 0))
        nodes.append((-.53, -.39, "__barn", 0, 0))
        nodes.append((-.77, .75, "tank", 7, 0))
    elif stem == "grapes":
        for row in range(6):
            v = -.76+row*.285
            for u in [-.73, -.05, .63]:
                nodes.append((u, v, "__post", 8, 0))
            for col in range(8):
                nodes.append((-.68+col*.18, v, "vine", 8, 5))
    else:
        asset = {"straw": "straw"}.get(stem, stem)
        height = {"wheat": 8, "corn": 13, "veg": 5.5, "straw": 5.5}[stem]
        rows,cols=(12,14) if stem=="wheat" else (10,12)
        for row in range(rows):
            for col in range(cols):
                nodes.append((-.83+col*1.48/(cols-1)+rng.uniform(-.02, .02),
                              -.84+row*1.64/(rows-1), asset, height*rng.uniform(.86, 1.06), 0))
    # Fixed light in screen/world coordinates; not rotated with the field.
    shadows = Image.new("RGBA", image.size)
    shadow = ImageDraw.Draw(shadows)
    for u, v, asset, height, z in nodes:
        if asset.startswith("__"):
            continue
        px, py = project(u, v, turn=turn)
        shadow.ellipse((px, py-.6, px+height*.38+2, py+height*.13+1), fill=(40, 39, 28, 50))
    image = Image.alpha_composite(image, shadows.filter(ImageFilter.GaussianBlur(.6)))
    # Painter order is determined AFTER rotating the world positions.
    nodes.sort(key=lambda node: sum(rotate(node[0], node[1], turn)))
    for u, v, asset, height, z in nodes:
        if asset == "__barn":
            barn(image, turn)
            continue
        if asset == "__post":
            d = ImageDraw.Draw(image)
            d.line([project(u, v, turn=turn), project(u, v, 8, turn)], fill="#8f8066", width=1)
            if u == -.73:
                d.line([project(u, v, 7, turn), project(.63, v, 7, turn)], fill="#666b54", width=1)
            continue
        source = assets[asset]
        if asset=="vine":
            # Trellis carries foliage, not a floating transplanted tree trunk.
            source=source.crop((0,0,source.width,round(source.height*.76)))
        size = (max(1, round(source.width*height/source.height)), max(1, round(height)))
        plant = source.resize(size, Image.Resampling.LANCZOS)
        px, py = project(u, v, z, turn)
        image.alpha_composite(plant, (round(px-plant.width/2), round(py-plant.height)))
    # A light low boundary, with an opening at the rotated service entrance.
    d = ImageDraw.Draw(image)
    for a, b in [((-.98, -.98), (.98, -.98)), ((-.98, -.98), (-.98, .98))]:
        d.line([project(*a, 1, turn), project(*b, 1, turn)], fill="#a89d85")
    top, bottom = image.getchannel("A").getbbox()[1::2]
    image = image.crop((0, top, W, bottom))
    return image, {"w": W, "h": image.height, "ay": round((CY-top)/image.height, 6),
                   "ground": {"w": W, "h": W/2}, "canonicalFarm": 1, "cameraGround": 1}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kit", type=Path)
    args = parser.parse_args()
    if args.kit:
        extract(args.kit)
    assets = {name: Image.open(KIT / f"{name}.png").convert("RGBA") for name in NAMES}
    manifest = json.loads((ROOT / "src/realistic-manifest.json").read_text())
    backup = ROOT.parent / "generated-artifacts/farm-art-before"
    if not backup.exists():
        backup.mkdir(parents=True)
        (backup / "manifest.json").write_text(json.dumps(manifest))
        for stem in FARMS:
            for face in FACES:
                key = "farm_cattle" if stem == "cattle" and face == "front" else f"{stem}_{face}"
                shutil.copyfile(OUT / "s" / f"{key}.webp", backup / f"{key}.webp")
    for stem in FARMS:
        for turn, face in enumerate(FACES):
            key = "farm_cattle" if stem == "cattle" and face == "front" else f"{stem}_{face}"
            image, meta = render(stem, (-turn) % 4, assets)
            image.save(OUT / "s" / f"{key}.webp", "WEBP", quality=94, method=6)
            manifest[key] = meta
    canopy = OUT / "canopies"
    canopy.mkdir(exist_ok=True)
    for name in ["olive", "citrus", "orn"]:
        source = assets[name]
        leaves = source.crop((0, 0, source.width, round(source.height*.69)))
        leaves.thumbnail((60, 60), Image.Resampling.LANCZOS)
        tile = Image.new("RGBA", (64, 64))
        tile.alpha_composite(leaves, ((64-leaves.width)//2, (64-leaves.height)//2))
        tile.save(canopy / f"{name}.webp", "WEBP", quality=94, method=6)
    for path in [ROOT / "src/realistic-manifest.json", OUT / "manifest.json"]:
        path.write_text(json.dumps(manifest))
    print("Exported 44 canonical farm views and 3 courtyard canopy cutouts.")


if __name__ == "__main__":
    main()
