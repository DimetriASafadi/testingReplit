from pathlib import Path
import json
ROOT=Path(__file__).resolve().parents[1]
ART=ROOT/"public/art/realistic"
FF=ROOT/"art-src/four-face"
PREFIXES="work equip recycling glass cement steel asphalt food_factory water smallhouse house villa apt4 h6 complex modern trad tower tourist foodshop clothes shoe tmall mmall muni zoo falafel shawarma western luxury cafe kiosk wheat citrus olive veg straw cattle palms corn trees orn smarket toys juiceshop drinks juicef grapes".split()
assert len(PREFIXES)==48
NEW_W={"smarket":196,"toys":196,"juiceshop":196,"drinks":196,"juicef":196,"grapes":210}
FACES=["front","right","back","left"]  # TL,TR,BL,BR = 0,90,180,270
def front_key(p): return "farm_cattle" if p=="cattle" else p+"_front"
def manifest(): return json.loads((ROOT/"src/realistic-manifest.json").read_text())
