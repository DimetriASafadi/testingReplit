"""Split 2x2 sheets (TL front0, TR right90, BL back180, BR left270) into sprites.
Source: art-src/four-face/clean/<prefix>.png (RGBA, transparent). Never mirrors.
Default writes to art-src/four-face/staging + contact sheet. --publish --confirmed
copies to public/art/realistic/s and updates BOTH manifests.
  --keep-legacy  keep existing live front/back, only add right/left."""
import argparse,json,shutil,sys
from PIL import Image,ImageEnhance,ImageFilter,ImageDraw
from four_face_common import *
PREFIXES="school hospital mosque church club rpark".split()
ap=argparse.ArgumentParser();ap.add_argument("--only",nargs="*");ap.add_argument("--keep-legacy",action="store_true")
ap.add_argument("--publish",action="store_true");ap.add_argument("--confirmed",action="store_true");a=ap.parse_args()

import numpy as np
from collections import deque
def extract_faces(sh):
    """Connected-component foreground extraction over the entire sheet (no assumed quadrant borders).
    Components come from alpha>24 dilated to join antialiased gaps; four largest become TL,TR,BL,BR by centre.
    Small stray fragments within 24px of a face's bbox join it; real image-edge clipping is the only clip reject."""
    al=sh.getchannel("A");W,H=sh.size
    for dil in(7,5,3,1):
        lab,n,sizes=label(al,dil)
        bg=sorted(sizes,reverse=True)[:4]
        if len(bg)==4 and bg[3]>.05*sum(bg): break
    if not(len(bg)==4 and bg[3]>.05*sum(bg)): return cut_extract(sh)
    return finish(sh,lab,n,sizes)
def cut_extract(sh):
    """Fallback when neighbouring subjects touch: split along the sparsest row/column near the middle (adaptive, not fixed 512)."""
    W,H=sh.size;a=np.array(sh);fg=a[...,3]>24;errs=[]
    cy=H//2-48+int(np.argmin(fg.sum(1)[H//2-48:H//2+48]));cx=W//2-48+int(np.argmin(fg.sum(0)[W//2-48:W//2+48]))
    box={"front":(0,0,cx,cy),"right":(cx,0,W,cy),"back":(0,cy,cx,H),"left":(cx,cy,W,H)};faces={}
    for f,(x0,y0,x1,y1) in box.items():
        q=a[y0:y1,x0:x1].copy();im=Image.fromarray(q,"RGBA");bb=im.getchannel("A").point(lambda v:255 if v>24 else 0).getbbox()
        if not bb: errs.append(f"{f}: empty");continue
        if (x0+bb[0]<=0 or y0+bb[1]<=0 or x0+bb[2]>=W or y0+bb[3]>=H): errs.append(f"{f}: clipped at IMAGE edge")
        faces[f]=im.crop(bb);faces[f].off=(x0+bb[0],y0+bb[1])
    return faces,errs
def label(al,dil):
    m=np.array(al.point(lambda v:255 if v>24 else 0).filter(ImageFilter.MaxFilter(dil)) if dil>1 else al.point(lambda v:255 if v>24 else 0))[::2,::2]>0
    lab=np.zeros(m.shape,np.int32);n=0;sizes=[]
    for y0,x0 in zip(*np.nonzero(m)):
        if lab[y0,x0]: continue
        n+=1;lab[y0,x0]=n;q=deque([(y0,x0)]);c=0
        while q:
            y,x=q.popleft();c+=1
            for dy,dx in((1,0),(-1,0),(0,1),(0,-1)):
                yy,xx=y+dy,x+dx
                if 0<=yy<m.shape[0] and 0<=xx<m.shape[1] and m[yy,xx] and not lab[yy,xx]: lab[yy,xx]=n;q.append((yy,xx))
        sizes.append(c)
    return lab,n,sizes
def finish(sh,lab,n,sizes):
    W,H=sh.size;errs=[]
    if len(sizes)<4: return {},["fewer than four foreground components"]
    big=sorted(range(1,n+1),key=lambda i:-sizes[i-1])[:4]
    full=np.kron(lab,np.ones((2,2),np.int32))[:H,:W]
    full=np.pad(full,((0,H-full.shape[0]),(0,W-full.shape[1])))
    info=[]
    for i in big:
        ys,xs=np.nonzero(full==i);info.append((i,ys.mean(),xs.mean()))
    info.sort(key=lambda t:t[1]);top=sorted(info[:2],key=lambda t:t[2]);bot=sorted(info[2:],key=lambda t:t[2])
    order=top+bot;arr=np.array(sh);masks={}
    for f,(i,_,_) in zip(FACES,order):
        masks[f]=(full==i)
    for i in range(1,n+1):  # attach strays to nearest big component bbox within 24px
        if i in big: continue
        ys,xs=np.nonzero(full==i)
        if not len(ys): continue
        for f,(bi,_,_) in zip(FACES,order):
            by,bx=np.nonzero(masks[f]);
            if bx.min()-24<=xs.mean()<=bx.max()+24 and by.min()-24<=ys.mean()<=by.max()+24: masks[f]|=(full==i);break
    faces={}
    for f in FACES:
        out=arr.copy();out[~masks[f]]=0;im=Image.fromarray(out,"RGBA")
        bb=im.getchannel("A").point(lambda v:255 if v>24 else 0).getbbox()
        if not bb: errs.append(f"{f}: empty");continue
        if bb[0]<=0 or bb[1]<=0 or bb[2]>=W or bb[3]>=H: errs.append(f"{f}: clipped at IMAGE edge")
        faces[f]=im.crop(bb);faces[f].off=(bb[0],bb[1])
    return faces,errs
man=manifest();done=[];bad={}
def name(p,f): return "farm_cattle" if p=="cattle" and f=="front" else f"{p}_{f}"
for p in (a.only or PREFIXES):
    src=FF/"clean"/f"{p}-corrected.png"
    if not src.exists(): src=FF/"clean"/f"{p}.png"
    if not src.exists(): continue
    sh=Image.open(src).convert("RGBA");W2,H2=sh.size;faces={};errs=[];W=196;gh=98
    faces,errs=extract_faces(sh);single={}
    for f in FACES:  # optional single-pose overrides, only when supplied
        o=FF/"clean/single"/f"{p}_{f}.png"
        if o.exists():
            im=Image.open(o).convert("RGBA");bb=im.getchannel("A").point(lambda v:255 if v>24 else 0).getbbox()
            if not bb or bb[0]<=0 or bb[1]<=0 or bb[2]>=im.width or bb[3]>=im.height: errs.append(f"{f}: single override empty/clipped")
            else: single[f]=im.crop(bb);errs=[e for e in errs if not e.startswith(f+":")]
    for f,im in single.items():  # supplied single is a separate canvas: match the footprint width of the sheet's other faces (uniform scale)
        others=[v.width for g,v in faces.items() if g!=f]
        if others:
            k=(sum(others)/len(others))/im.width;im=im.resize((round(im.width*k),round(im.height*k)),Image.Resampling.LANCZOS)
        faces[f]=im;faces[f].off=(None,None);errs=[e for e in errs if not e.startswith(f+":")]
    if len(faces)==4:
        hs=[v.height*W/v.width for v in faces.values()]  # normalized height at pinned width
        mw=sum(v.width for v in faces.values())/4
        for f,v in faces.items():
            if not .85<=v.width/mw<=1.15: errs.append(f"{f}: width outlier {v.width/mw:.2f}x mean")
        if max(hs)/min(hs)>1.3: errs.append(f"height spread {max(hs)/min(hs):.2f}")
    if errs: bad[p]=errs;continue
    capk=min(1,512/max(W,max(round(v.height*W/v.width) for v in faces.values())))  # one common cap for all four faces
    out={}
    for f,v in faces.items():
        k=capk  # only shrinks uniformly if edge>512
        ow=v.width;off=getattr(v,'off',(None,None))
        w=round(W*k);h=round(v.height*W/v.width*k);v=v.resize((w,h),Image.Resampling.LANCZOS)  # uniform scale, never anisotropic
        gW=W*k;gH=gh*k
        al=v.getchannel("A");rgb=ImageEnhance.Color(v.convert("RGB")).enhance(.93)
        rgb=Image.blend(rgb,Image.new("RGB",rgb.size,(166,155,135)),.03);v=rgb.convert("RGBA");v.putalpha(al)
        w,h=v.size;ay=1-gH/2/h;cy=ay*h;
        v.save(FF/"staging/baseline"/f"{name(p,f)}.webp","WEBP",quality=92,method=6)  # graded, UNFEATHERED
        (FF/"staging/baseline"/f"{name(p,f)}.json").write_text(json.dumps({"w":w,"h":h,"ay":round(ay,6),"ground":{"w":gW,"h":gH}}));soft=al.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(1.4));px=v.load()
        for y in range(max(0,round(cy)),h):
            for x in range(w):
                r,g,b,aa=px[x,y]
                if not aa: continue
                m=abs(x-w/2)/(gW/2)+abs(y-cy)/(gH/2);edge=max(0,min(1,(1.065-m)/.10))
                fr=min(1,max(0,(y-cy)/(gH*.2)));fe=1-fr+fr*soft.getpixel((x,y))/255
                px[x,y]=(r,g,b,round(aa*edge*fe))
        v.save(FF/"staging"/f"{name(p,f)}.webp","WEBP",quality=88,method=6)
        (FF/"staging/cropinfo").mkdir(exist_ok=True);(FF/"staging/cropinfo"/f"{name(p,f)}.json").write_text(json.dumps({"sheet_bbox_origin":off,"scale":w/ow}))
        out[name(p,f)]={"w":w,"h":h,"ay":round(ay,6),"ground":{"w":gW,"h":gH}}
    done.append((p,out))
    # contact strip
    cs=Image.new("RGBA",(4*(W+20),330),(150,140,118,255));d=ImageDraw.Draw(cs)
    for i,f in enumerate(FACES):
        v=Image.open(FF/"staging"/f"{name(p,f)}.webp");cs.alpha_composite(v,(i*(W+20)+10,320-v.height))
    cs.convert("RGB").save(FF/"contact"/f"{p}.png")
if done:
    ps=[d[0] for d in done];tile=Image.open(FF/"contact"/f"{ps[0]}.png");T=Image.new("RGB",(tile.width,tile.height*len(ps)))
    for i,q in enumerate(ps): T.paste(Image.open(FF/"contact"/f"{q}.png"),(0,i*tile.height))
    T.save(FF/"contact"/"ALL.png")
for p,e in bad.items(): print("FAIL",p,e)
print("staged",[d[0] for d in done])
if a.publish:
    if not a.confirmed or bad: sys.exit("refuse: need --confirmed and zero failures")
    for p,out in done:
        for k,m in out.items():
            legacy=k in(front_key(p),f"{p}_back")
            if a.keep_legacy and legacy: continue
            shutil.copy(FF/"staging"/f"{k}.webp",ART/"s"/f"{k}.webp");man[k]=m
            B=ROOT/"art-src/lot-cohesion-baseline";B.mkdir(exist_ok=True)  # fresh graded unfeathered pixels
            for ext in("webp","json"): shutil.copy(FF/"staging/baseline"/f"{k}.{ext}",B/f"{k}.{ext}")
    for q in[ROOT/"src/realistic-manifest.json",ART/"manifest.json"]: q.write_text(json.dumps(man))
    print("published")
