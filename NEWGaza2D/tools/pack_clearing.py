"""Pack newly painted 2D clearing stages. No Unity/models or 3D rendering inputs."""
from pathlib import Path
import json
from PIL import Image, ImageEnhance
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
source = Image.open(ROOT / "art-src/drawn-clearing/rubble-stages.png").convert("RGBA")
out = ROOT / "public/art/clearing"
out.mkdir(parents=True, exist_ok=True)
manifest = {}
for row in range(2):
    for col, stage in enumerate(("dense", "half", "few")):
        cell = source.crop((round(col*source.width/3), round(row*source.height/2),
                            round((col+1)*source.width/3), round((row+1)*source.height/2)))
        alpha = cell.getchannel("A")
        box = alpha.point(lambda v: 255 if v > 30 else 0).getbbox()
        if not box:
            raise ValueError(f"Empty generated cell {row}/{col}")
        cell = cell.crop(box)
        cell = cell.resize((220, round(cell.height*220/cell.width)), Image.Resampling.LANCZOS)
        # Match the city's dusty greige palette; avoid a yellow cut-out soil platform.
        cell = ImageEnhance.Color(cell).enhance(.4)
        rgba = np.asarray(cell).copy()
        rgba[:,:,1] = np.clip(rgba[:,:,1].astype(int)+3,0,255)
        rgba[:,:,2] = np.clip(rgba[:,:,2].astype(int)+7,0,255)
        if stage != "dense":
            yy,xx = np.indices((cell.height,cell.width))
            foot_y = cell.height-cell.width/4
            radius = np.abs(xx-cell.width/2)/(cell.width/2)+np.abs(yy-foot_y)/(cell.width/4)
            feather = np.clip((1.06-radius)/.12,0,1)
            # Feather just the ground perimeter, never the raised pile's upper silhouette.
            feather[yy < foot_y-cell.width/4] = 1
            rgba[:,:,3] = (rgba[:,:,3]*feather).astype('uint8')
        cell = Image.fromarray(rgba)
        key = f"pile-{row}-{stage}"
        # Footprint is a 2:1 diamond: its centre is a quarter-width above the ground's lower tip.
        foot = cell.height - cell.width / 4
        manifest[key] = {"w": cell.width, "h": cell.height, "ay": round(foot/cell.height,6)}
        cell.save(out / f"{key}.webp", "WEBP", quality=91, method=6)
(ROOT / "src/clearing-manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
print(json.dumps(manifest,indent=2))
