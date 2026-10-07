"""Pack generated raw sprites (art-src/raw) + parent ruins sheet into trimmed, scaled WebP sprites and a manifest.
Anchor = centre of the ground footprint diamond (bbox bottom minus w/4), so plots sit on the ground, not by building centre."""
import json, glob, os
from PIL import Image
OUT='public/art/realistic/s'; man={}
def save(key,im,W,maxh=460):
    bb=im.getbbox(); im=im.crop(bb); w,h=im.size
    s=min(W/w,maxh/h); im=im.resize((max(1,round(w*s)),max(1,round(h*s))),Image.LANCZOS); w,h=im.size
    im.save(f'{OUT}/{key}.webp',quality=82,method=6)
    man[key]={'w':w,'h':h,'ay':round(1-(w/4)/h,4) if h>w/4 else 0.5}
for f in sorted(glob.glob('art-src/raw/*.png')):
    k=os.path.basename(f)[:-4]
    im=Image.open(f).convert('RGBA')
    W=210 if k.split('_')[0] in ('farm','wheat','citrus','olive','veg','straw','palms','corn','cattle','trees','orn','zoo') else 196
    if k.startswith('tower'): W=150
    if k.startswith('shop_c'): W=196
    if k.startswith('apt') or k.startswith('house_c'): W=190
    save(k,im,W)
sh=Image.open('public/art/realistic/ruins-sheet.png').convert('RGBA'); cw,ch=sh.width/4,sh.height/3
for i in range(12):
    x0=round(i%4*cw); y0=round(i//4*ch); pad=60
    c=sh.crop((x0,max(0,y0-pad),x0+round(cw),min(sh.height,y0+round(ch)+pad)))
    # keep only the dominant contiguous vertical run of content (drops slivers of neighbouring cells)
    a=c.getchannel('A').point(lambda v:255 if v>24 else 0); rows=[a.crop((0,r,a.width,r+1)).histogram()[255] for r in range(a.height)]
    runs=[];st=None
    for r,v in enumerate(rows+[0]):
        if v>2 and st is None: st=r
        if v<=2 and st is not None: runs.append((st,r,sum(rows[st:r])));st=None
    b=max(runs,key=lambda t:t[2]); c=c.crop((0,b[0],c.width,b[1]))
    save(f'ruin{i}',c,200,300)
json.dump(man,open('public/art/realistic/manifest.json','w')); json.dump(man,open('src/realistic-manifest.json','w'))
print(len(man),'sprites'); os.system('du -sh '+OUT)
