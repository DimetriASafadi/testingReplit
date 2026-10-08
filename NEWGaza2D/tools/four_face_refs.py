"""Build white-backed 512px front references for editImage (art-src/four-face/refs/<prefix>.png).
Prefers the unfeathered lot-cohesion baseline of the same corrected front pose."""
from PIL import Image
from four_face_common import *
for p in PREFIXES:
    k=front_key(p); src=ROOT/"art-src/lot-cohesion-baseline"/f"{k}.webp"
    if not src.exists(): src=ART/"s"/f"{k}.webp"
    im=Image.open(src).convert("RGBA"); s=min(440/im.width,440/im.height)
    im=im.resize((round(im.width*s),round(im.height*s)),Image.Resampling.LANCZOS)
    bg=Image.new("RGBA",(512,512),(244,241,234,255))
    bg.alpha_composite(im,((512-im.width)//2,(512-im.height)//2)); bg.convert("RGB").save(FF/"refs"/f"{p}.png")
print("refs",len(PREFIXES))
