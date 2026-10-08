"""Catalog reference sheet: every prefix front + floors + parcel scale -> art-src/four-face/contact/CATALOG.png"""
from PIL import Image,ImageDraw
import re
from four_face_common import *
fl={}
for m in re.finditer(r"\['(\w+)',\[([^\]]*)\]",""): pass
txt=(ROOT/"src/catalog.ts").read_text()
KEY=dict(re.findall(r"(\w+): '(\w+)'",(ROOT/"src/realart.ts").read_text().split("const KEY")[1].split("};")[0]))
rows=re.findall(r"\['(?:equipment|industry|housing|farm|commerce|leisure)',\[([^\]]*)\],\[[^\]]*\],\[([\d,]*)\]",txt)
for ids,fs in rows:
    for i,f in zip(re.findall(r"'(\w+)'",ids),fs.split(',')): fl[KEY[i]]=int(f)
man=manifest();cols=7;cw,chh=230,300
S=Image.new("RGB",(cols*cw,6*chh),(150,140,118));d=ImageDraw.Draw(S)
for n,p in enumerate(PREFIXES):
    x,y=(n%cols)*cw,(n//cols)*chh;im=Image.open(ART/"s"/f"{front_key(p)}.webp").convert("RGBA")
    S.paste(im,(x+(cw-im.width)//2,y+chh-30-im.height),im);d.text((x+6,y+chh-22),f"{p} floors={fl[p]} {im.width}x{im.height}",fill=(255,255,255))
S.save(FF/"contact"/"CATALOG.png");print(fl)
