"""Generate transparent launcher assets from the supplied, background-removed logo.

Run with python3.13 from the workspace root. Android adaptive foregrounds keep
the complete logo inside the circular safe area; their system background is navy.
"""
from collections import deque
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
from PIL import Image
import numpy as np

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / "NEWGaza2D/public"
RES = ROOT / "NEWGaza2D/android/app/src/main/res"
MASTER = PUBLIC / "art/branding/new-gaza-icon-transparent.png"


def prepare():
    image = Image.open(MASTER).convert("RGBA")
    pixels = np.array(image)
    height, width = pixels.shape[:2]
    rgb = pixels[:, :, :3].astype(np.int16)
    # Remove connected JPEG-white remnants without erasing enclosed flag whites.
    white = (rgb.min(axis=2) > 231) & (np.ptp(rgb, axis=2) < 25)
    traversable = white | (pixels[:, :, 3] < 8)
    seen = np.zeros((height, width), dtype=bool)
    queue = deque()
    for x in range(width):
        for y in (0, height - 1):
            if traversable[y, x]:
                seen[y, x] = True
                queue.append((x, y))
    for y in range(height):
        for x in (0, width - 1):
            if traversable[y, x] and not seen[y, x]:
                seen[y, x] = True
                queue.append((x, y))
    while queue:
        x, y = queue.popleft()
        for xx, yy in ((x-1, y), (x+1, y), (x, y-1), (x, y+1)):
            if 0 <= xx < width and 0 <= yy < height and not seen[yy, xx] and traversable[yy, xx]:
                seen[yy, xx] = True
                queue.append((xx, yy))
    pixels[seen, 3] = 0
    image = Image.fromarray(pixels)
    image = image.crop(image.getbbox())
    image.thumbnail((960, 960), Image.Resampling.LANCZOS)
    master = Image.new("RGBA", (1024, 1024))
    master.alpha_composite(image, ((1024-image.width)//2, (1024-image.height)//2))
    master.save(MASTER, optimize=True)

    def square(size, content=1.0):
        result = Image.new("RGBA", (size, size))
        extent = max(1, round(size * content))
        logo = master.resize((extent, extent), Image.Resampling.LANCZOS)
        result.alpha_composite(logo, ((size-extent)//2, (size-extent)//2))
        return result

    for density, size in (("mdpi",48), ("hdpi",72), ("xhdpi",96), ("xxhdpi",144), ("xxxhdpi",192)):
        folder = RES / f"mipmap-{density}"
        folder.mkdir(parents=True, exist_ok=True)
        for name in ("ic_launcher", "ic_launcher_round"):
            square(size).save(folder / f"{name}.png", optimize=True)
        foreground_size = round(size * 108 / 48)
        square(foreground_size, .47).save(folder / "ic_launcher_foreground.png", optimize=True)

    for size in (32, 180, 192, 512):
        square(size).save(PUBLIC / f"icon-{size}.png", optimize=True)

    output = ROOT / "generated-artifacts/new-gaza-icons"
    output.mkdir(parents=True, exist_ok=True)
    for size in (16,32,48,64,72,96,128,144,180,192,256,384,512,1024):
        square(size).save(output / f"new-gaza-{size}.png", optimize=True)
    square(512).save(output / "play-store-512.png", optimize=True)
    with ZipFile(output.parent / "new-gaza-icons.zip", "w", ZIP_DEFLATED) as archive:
        for file in sorted(output.glob("*.png")):
            archive.write(file, file.name)
    assert master.getextrema()[3] == (0, 255)
    print("Generated transparent master, 5 Android densities, adaptive foregrounds, web icons and ZIP.")


if __name__ == "__main__":
    prepare()
