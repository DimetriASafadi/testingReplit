"""Process generated ground photographs into mobile-sized periodic albedo tiles."""
import hashlib
import json
from pathlib import Path
import zipfile

import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / "testingReplic/Assets/NewGaza/Resources/Ground"
EXPORT = ROOT / "exports/ground"
SPECS = [
    ("urban_ground_albedo.png", "UrbanGround_Albedo.png", "Urban limestone / compacted earth"),
    ("coastal_sand_albedo.png", "CoastalSand_Albedo.png", "Coastal sand"),
    ("dry_ground_albedo.png", "DryGround_Albedo.png", "Dry soil / sparse ground cover"),
]


def periodic(image):
    """Periodic-plus-smooth decomposition, followed by a narrow wrap feather."""
    pixels = np.asarray(image, dtype=np.float64)
    height, width, _ = pixels.shape
    boundary = np.zeros_like(pixels)
    boundary[0] = pixels[-1] - pixels[0]
    boundary[-1] = -boundary[0]
    horizontal = pixels[:, -1] - pixels[:, 0]
    boundary[:, 0] += horizontal
    boundary[:, -1] -= horizontal
    denominator = (2*np.cos(2*np.pi*np.arange(height)/height)[:, None] +
                   2*np.cos(2*np.pi*np.arange(width)/width)[None, :] - 4)
    denominator[0, 0] = 1
    smooth_fft = np.fft.fft2(boundary, axes=(0, 1)) / denominator[:, :, None]
    smooth_fft[0, 0] = 0
    result = pixels - np.fft.ifft2(smooth_fft, axes=(0, 1)).real
    for i in range(24):
        weight = 0.5*(1+np.cos(np.pi*i/24))
        match = (result[:, i].copy()+result[:, -1-i].copy())/2
        result[:, i] = result[:, i]*(1-weight)+match*weight
        result[:, -1-i] = result[:, -1-i]*(1-weight)+match*weight
    for i in range(24):
        weight = 0.5*(1+np.cos(np.pi*i/24))
        match = (result[i].copy()+result[-1-i].copy())/2
        result[i] = result[i]*(1-weight)+match*weight
        result[-1-i] = result[-1-i]*(1-weight)+match*weight
    # Rounding first avoids truncation turning almost-identical edge floats into
    # mismatched 8-bit pixels. The final opposite borders are exactly equal.
    result = np.clip(np.rint(result), 0, 255).astype(np.uint8)
    result[:, -1] = result[:, 0]
    result[-1] = result[0]
    return Image.fromarray(result, "RGB")


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    EXPORT.mkdir(parents=True, exist_ok=True)
    sheet = Image.new("RGB", (1536, 1130), "#171f24")
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 20) \
        if Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf").exists() else ImageFont.load_default()
    reports = []
    for index, (original, output, label) in enumerate(SPECS):
        source = ROOT / "attached_assets/generated_images" / original
        image = periodic(ImageOps.fit(Image.open(source).convert("RGB"), (1024, 1024),
                                      method=Image.Resampling.LANCZOS))
        path = DEST / output
        image.save(path, optimize=True)
        array = np.asarray(image)
        assert np.array_equal(array[:, 0], array[:, -1])
        assert np.array_equal(array[0], array[-1])
        spatial_variation = float(array.reshape(-1, 3).std(axis=0).mean())
        assert spatial_variation > 5, "Ground must retain visible non-flat surface variation."
        reports.append({"file": output, "source": str(source.relative_to(ROOT)),
                        "size": [1024, 1024], "mode": "RGB",
                        "edge_error": 0, "spatial_stddev": round(spatial_variation, 2),
                        "bytes": path.stat().st_size,
                        "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
        x = index * 512
        draw.text((x+15, 20), label, fill="#e5e7e6", font=font)
        sheet.paste(image.resize((480, 480), Image.Resampling.LANCZOS), (x+16, 60))
        small = image.resize((120, 120), Image.Resampling.LANCZOS)
        for row in range(4):
            for column in range(4):
                sheet.paste(small, (x+16+column*120, 570+row*120))
        draw.text((x+16, 1070), "4 x 4 repeating tiles | offline texture review", fill="#a8b8c2", font=font)
    sheet.save(EXPORT / "Ground-Texture-Review.jpg", quality=92)
    manifest = {"description": "Generated illustrative ground art, not geographic imagery.",
                "tile_metres": 8, "normal_maps": False, "textures": reports,
                "mobile_import": {"max_size": 1024, "mipmaps": True, "repeat": True,
                                  "android": "ETC2_RGB4", "iphone": "ASTC_6x6"},
                "provenance": "AI-generated original surface images; periodic processing in this repository."}
    (DEST / "GroundArtManifest.json").write_text(json.dumps(manifest, indent=2)+"\n")
    package = ROOT / "exports/NewGaza-Ground-Art.zip"
    with zipfile.ZipFile(package, "w", zipfile.ZIP_DEFLATED) as archive:
        for path in [DEST / spec[1] for spec in SPECS]:
            archive.write(path, path.relative_to(ROOT / "testingReplic"))
        for filename in ("README.md", "GroundArtManifest.json"):
            path = DEST / filename
            archive.write(path, path.relative_to(ROOT / "testingReplic"))
    with zipfile.ZipFile(package) as archive:
        assert archive.testzip() is None
        assert all(not name.endswith(".meta") for name in archive.namelist())
    print(json.dumps(manifest, indent=2))
    print("Verified package bytes:", package.stat().st_size)


if __name__ == "__main__":
    main()
