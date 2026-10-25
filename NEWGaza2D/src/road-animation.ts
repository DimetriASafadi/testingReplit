import Phaser from 'phaser';
import type { DistrictState } from './model';
import { along, roadsFor, workRoad, type RoadSegment } from './roads';
import { U } from './art';
import { paverTexture, tentTexture, TENT_ORIGIN, PAVER_ORIGIN, PAVER_SCALE } from './expansion-art';
import { districtProgress } from './engine';
import type { FleetSound } from './game-audio';
import { cityLayout } from './city-layout';
import { pavingPlan, runOutline, nodeOutlines, ROAD_HALF, type PavingPlan } from './road-paving';
import { RoadPreparationAnimation } from './road-preparation-animation';
import { RoadSurfaceCache, type SurfaceBrush } from './road-surface-cache';

const iso=(x:number,y:number)=>({x:(x-y)*U,y:(x+y)*U/2});
type P=[number,number];
/** Half lane width in lot units; screed trails the sprite's ground origin by SCREED lot units. */
const HALF=.30,SCREED=.36,HOPPER=.34;
const hash=(i:number)=>{const s=Math.sin(i*127.1+311.7)*43758.5453;return s-Math.floor(s);};

/** Cached finished coating plus bounded active actors; never repaint the city ground per tick.
 *  Active work: fresh unmarked mat only BEHIND the screed (granular near screed, compacted further back),
 *  heat haze, auger spray, moving tread cleats, exhaust, cones. Lane paint only on finished roads. */
export class RoadAnimation {
 private pavement:Phaser.GameObjects.Image[]=[];
 private pavedKeys:string[]=[];
 private work?:Phaser.GameObjects.Graphics;
 private fx?:Phaser.GameObjects.Graphics;
 private signature='';
 private did='';
 private actors=new Map<string,{img:Phaser.GameObjects.Image;under:Phaser.GameObjects.Graphics;over:Phaser.GameObjects.Graphics}>();
 private tents:Phaser.GameObjects.Image[]=[];
 private nextProgress=0;
 private tentAlpha=1;
 private preparation?:RoadPreparationAnimation;
 private fresh:RoadSurfaceCache;
 sounds:FleetSound[]=[];
  constructor(private scene:Phaser.Scene){this.fresh=new RoadSurfaceCache(scene,-798925);}
 reset(){this.fresh.reset();this.preparation?.destroy();this.preparation=undefined;this.clearCoat();this.work?.destroy();this.fx?.destroy();for(const a of this.actors.values()){a.img.destroy();a.under.destroy();a.over.destroy();}for(const t of this.tents)t.destroy();this.actors.clear();this.tents=[];this.did='';this.signature='';this.sounds=[];this.nextProgress=0;}
 private clearCoat(){for(const img of this.pavement)img.destroy();for(const key of this.pavedKeys)this.scene.textures.remove(key);this.pavement=[];this.pavedKeys=[];}
 private quad(r:RoadSegment,f0:number,f1:number,w=HALF){
  const dx=r.b[0]-r.a[0],dy=r.b[1]-r.a[1],len=Math.hypot(dx,dy),ox=-dy/len*w,oy=dx/len*w;
  const s=[r.a[0]+dx*f0,r.a[1]+dy*f0],e=[r.a[0]+dx*f1,r.a[1]+dy*f1];
  return [iso(s[0]+ox,s[1]+oy),iso(e[0]+ox,e[1]+oy),iso(e[0]-ox,e[1]-oy),iso(s[0]-ox,s[1]-oy)];
 }
 /** Finished coat: one cached canvas texture, repainted only when the paved signature changes. */
 private paintCoat(d:DistrictState){
  const l=cityLayout(d.id),max=l.extent,K=.5;
  const ox=iso(-1,max+1).x,oy=iso(-1,-1).y,w=Math.ceil((iso(max+1,-1).x-ox)*K),h=Math.ceil((iso(max+1,max+1).y-oy)*K);
  const plan:PavingPlan=pavingPlan(l.roadX,l.roadY,roadsFor(d.id),new Set(d.pavedRoads??[]));
  const pt=(x:number,y:number):[number,number]=>{const q=iso(x,y);return[(q.x-ox)*K,(q.y-oy)*K];};
  const polys=[...plan.runs.map(runOutline),...plan.nodes.flatMap(n=>nodeOutlines(n))];
  const trace=(ctx:CanvasRenderingContext2D,ps:[number,number][][])=>{ctx.beginPath();for(const poly of ps)poly.forEach(([x,y],i)=>{const[a,b]=pt(x,y);i?ctx.lineTo(a,b):ctx.moveTo(a,b);});};
  const fillEach=(ctx:CanvasRenderingContext2D,ps:[number,number][][],col:string)=>{ctx.fillStyle=col;for(const poly of ps){trace(ctx,[poly]);ctx.closePath();ctx.fill();}};
   if(!polys.length){this.clearCoat();return;}
   // CPU scratch only: the full neighborhood must never be a single GPU texture.
   const cv=document.createElement('canvas');cv.width=w;cv.height=h;const c=cv.getContext('2d')!;
  // 1. dusty shoulder: opaque union on a scratch canvas, blurred, composited once at low alpha (no double-alpha edges)
  const sc=document.createElement('canvas');sc.width=w;sc.height=h;const s2=sc.getContext('2d')!;
   s2.lineJoin='round';s2.lineWidth=11;s2.strokeStyle='#a8916c';
  for(const poly of polys){trace(s2,[poly]);s2.closePath();s2.stroke();}
   fillEach(s2,polys,'#a8916c');
   // Blur the opaque union once, not a separate expensive filter for every edge.
   c.globalAlpha=.42;c.filter='blur(5px)';c.drawImage(sc,0,0);c.filter='none';c.globalAlpha=1;sc.width=sc.height=0;
  // 2. asphalt: opaque, so overlapping run/patch polygons are seamless
  fillEach(c,polys,'#353c3d');
  // 3. aggregate grain clipped to the surface, world-hashed so it never restarts per segment
  c.save();trace(c,polys.map(p=>[...p,p[0]]));c.clip('nonzero');
  for(let i=0;i<w*h/90;i++){const x=hash(i)*w,y=hash(i+7.3)*h;c.fillStyle=i%3?'rgba(110,115,108,.22)':'rgba(16,20,22,.28)';c.fillRect(x,y,1,1);}
  c.restore();
  // 4. worn edge lines only along straight sides; stop before curb returns
  c.lineCap='round';
  for(const r of plan.runs){const P=(s:number,o:number)=>r.axis==='x'?pt(r.fixed+o,s):pt(s,r.fixed+o);
   for(const o of [-ROAD_HALF+.03,ROAD_HALF-.03])for(const [e0,e1] of r.edges){const a=P(e0,o),b=P(e1,o);c.strokeStyle='rgba(190,182,154,.55)';c.lineWidth=.9;c.beginPath();c.moveTo(...a);c.lineTo(...b);c.stroke();}
   for(const [a0,b0] of r.dashes){const a=P(a0,0),b=P(b0,0);c.strokeStyle='rgba(222,209,164,.82)';c.lineWidth=1.1;c.beginPath();c.moveTo(...a);c.lineTo(...b);c.stroke();}}
   this.clearCoat();
   for(let y=0;y<h;y+=1024)for(let x=0;x<w;x+=1024){
    const key=`paved:${d.id}:${x}:${y}`,tw=Math.min(1024,w-x),th=Math.min(1024,h-y);
    const tex=this.scene.textures.createCanvas(key,tw,th)!;
    tex.getContext().drawImage(cv,x,y,tw,th,0,0,tw,th);tex.refresh();
    this.pavedKeys.push(key);this.pavement.push(this.scene.add.image(ox+x/K,oy+y/K,key).setOrigin(0,0).setScale(1/K).setDepth(-799000));
   }
   cv.width=cv.height=0;
 }
 /** Hot fresh mat from road start to the screed edge (fraction fs). No paint. */
  private freshMat(g:SurfaceBrush,r:RoadSegment,fs:number){
  if(fs<=0)return;
  const dx=r.b[0]-r.a[0],dy=r.b[1]-r.a[1],len=Math.hypot(dx,dy),nx=-dy/len,ny=dx/len;
  // graded base ahead remains visible; mat itself: compacted (cool) behind, loose (hot) near screed
  const bands=10;
  for(let b=0;b<bands;b++){
   const f0=fs*b/bands,f1=fs*(b+1)/bands,age=(fs-f1)*len; // lot units behind screed
   const hot=Math.max(0,1-age/1.6);
   const c=Phaser.Display.Color.Interpolate.ColorWithColor(new Phaser.Display.Color(44,49,51),new Phaser.Display.Color(26,28,29),100,Math.round(hot*100));
   g.fillStyle(Phaser.Display.Color.GetColor(c.r,c.g,c.b),1).fillPoints(this.quad(r,f0,f1),true);
  }
  g.lineStyle(1,0x0d0f10,.55).strokePoints(this.quad(r,0,fs),true);
  // aggregate: dense coarse grains near screed, thinning to fine speckle where rolled
   const n=Math.ceil(len*70);
  for(let i=0;i<n;i++){
    const t=hash(i);if(t>fs)continue;
    const o=(hash(i+999)-.5)*2*HALF*.92,age=(fs-t)*len,hot=Math.max(0,1-age/1.3);
   if(hash(i+77)>.35+hot*.6)continue;
   const p=iso(r.a[0]+dx*t+nx*o,r.a[1]+dy*t+ny*o),s=.8+hot*1.1;
   g.fillStyle(hash(i+5)>.5?0x5c605c:0x0b0c0d,.25+hot*.45).fillRect(p.x,p.y,s*1.3,s);
  }
  // screed edge joint line
  const e1=iso(r.a[0]+dx*fs+nx*HALF,r.a[1]+dy*fs+ny*HALF),e2=iso(r.a[0]+dx*fs-nx*HALF,r.a[1]+dy*fs-ny*HALF);
  g.lineStyle(1.4,0x4a4e4c,.8).lineBetween(e1.x,e1.y,e2.x,e2.y);
   // Moving steam remains in the bounded machine effects below, not in every finished section.
 }
 private cones(g:Phaser.GameObjects.Graphics,r:RoadSegment){
  const dx=r.b[0]-r.a[0],dy=r.b[1]-r.a[1],len=Math.hypot(dx,dy),nx=-dy/len,ny=dx/len;
  for(const f of [-.06,1.06])for(const s of [-1,0,1]){
   const p=iso(r.a[0]+dx*f+nx*s*HALF*.8,r.a[1]+dy*f+ny*s*HALF*.8);
   g.fillStyle(0x1b1d1c,.3).fillEllipse(p.x+1.5,p.y+.8,6,2.4);
   g.fillStyle(0x2a2a28,1).fillRect(p.x-2.6,p.y-.8,5.2,1.6);
   g.fillStyle(0xe2672b,1).fillTriangle(p.x-2,p.y,p.x+2,p.y,p.x,p.y-7);
   g.fillStyle(0xf1ece0,1).fillRect(p.x-1.1,p.y-4.2,2.2,1.1);
  }
 }
 update(d:DistrictState,now:number){
  if(this.did!==d.id){
   this.reset();this.did=d.id;this.work=this.scene.add.graphics().setDepth(-798900);this.fx=this.scene.add.graphics().setDepth(4e5);
   const max=cityLayout(d.id).extent;
    for(let i=0;i<28;i++){
     const t=(i+1)/29,point=i%2?iso(-3.5-(i%3)*1.6,t*max):iso(t*max,-4-(i%3)*1.4);
    const img=this.scene.add.image(point.x,point.y,tentTexture(this.scene,i)).setOrigin(TENT_ORIGIN.x,TENT_ORIGIN.y).setScale(.85+(i%3)*.08).setDepth(point.y);
    this.tents.push(img);
   }
  }
  const sig=(d.pavedRoads??[]).join('|');
  if(sig!==this.signature){this.signature=sig;this.paintCoat(d);}
  if(now>=this.nextProgress){this.nextProgress=now+500;this.tentAlpha=Math.pow(1-districtProgress(d)/100,1.2);}
  const target=this.tentAlpha;
  for(const t of this.tents)t.setAlpha(Math.abs(t.alpha-target)<.005?target:t.alpha+(target-t.alpha)*.045);
   this.preparation??=new RoadPreparationAnimation(this.scene);
   this.preparation.update(d,now);
   const live=new Set<string>();this.sounds=[...this.preparation.sounds];
   this.work!.clear();this.fx!.clear();this.fresh.begin();
  for(const w of d.roadWorks??[]){
   if(now>=w.returnEnd)continue;
   if(!roadsFor(d.id).some(r=>r.id===w.roadId))continue;
    const departure=w.preparation?.aggregateEnd??w.start,arrival=w.preparation?.asphaltStart??w.arrival;
    if(now<departure)continue;
   const r=workRoad(d.id,w);live.add(w.machineId);
    const working=now>=arrival&&now<w.workEnd;
   const len=Math.hypot(r.b[0]-r.a[0],r.b[1]-r.a[1]);
   // machine travels from (screed at road start) to (screed at road end) so mat fully covers the segment
    const f=Math.max(0,Math.min(1,(now-arrival)/(w.workEnd-arrival)));
   const ux=(r.b[0]-r.a[0])/len,uy=(r.b[1]-r.a[1])/len;
   const workStart:P=[r.a[0]+ux*SCREED,r.a[1]+uy*SCREED],workFinish:P=[r.b[0]+ux*SCREED,r.b[1]+uy*SCREED];
    const m=now<arrival?along([...w.route,workStart],(now-departure)/(arrival-departure)):working?{p:[workStart[0]+ux*f*len,workStart[1]+uy*f*len] as P,angle:Math.atan2(uy,ux)}:along([workFinish,r.b,...w.route.slice().reverse()],(now-w.workEnd)/(w.returnEnd-w.workEnd));
    if(now<arrival)this.cones(this.work!,r);
    const pending=new Set(w.roadIds??[w.roadId]);
    if(working || now>=w.workEnd && ![...pending].every(id=>(d.pavedRoads??[]).includes(id))){
     for(const part of roadsFor(d.id).filter(q=>pending.has(q.id))){
       const start=Math.hypot(part.a[0]-r.a[0],part.a[1]-r.a[1]),size=Math.hypot(part.b[0]-part.a[0],part.b[1]-part.a[1]);
       const fs=Math.max(0,Math.min(1,(f*len-start)/size));
       if(fs>0)this.fresh.paint(`${w.machineId}:${part.id}`,part,String(Math.round(fs*size*U)),now,g=>this.freshMat(g,part,fs));
     }
     if(working)this.cones(this.work!,r);
   }
   const p=iso(...m.p),key=paverTexture(this.scene,m.angle);
    const view=this.scene.cameras.main.worldView;
    if(p.x<view.x-100||p.x>view.right+100||p.y<view.y-100||p.y>view.bottom+100){
     const old=this.actors.get(w.machineId);if(old){old.img.setVisible(false);old.under.clear();old.over.clear();}
     this.sounds.push({id:w.machineId,kind:'paver',x:p.x,y:p.y,working});continue;
    }
   const ca=Math.cos(m.angle),sa=Math.sin(m.angle);
   let a=this.actors.get(w.machineId);
   if(!a){a={img:this.scene.add.image(p.x,p.y,key).setOrigin(PAVER_ORIGIN.x,PAVER_ORIGIN.y),under:this.scene.add.graphics(),over:this.scene.add.graphics()};this.actors.set(w.machineId,a);}
   const vib=working?Math.sin(now/38)*.45:Math.sin(now/90)*.2;
    a.img.setTexture(key).setScale(PAVER_SCALE).setPosition(p.x,p.y+vib).setDepth(p.y+8).setAlpha(1).setVisible(true);
   // grounded contact shadow (sun upper-left -> shadow falls down-right), under body
   const u=a.under;u.clear().setDepth(p.y+7);
   u.fillStyle(0x15130f,.32).fillEllipse(p.x+6,p.y+4,62*PAVER_SCALE/.62,20*PAVER_SCALE/.62);
   u.fillStyle(0x15130f,.25).fillEllipse(p.x+2,p.y+1,40*PAVER_SCALE/.62,12*PAVER_SCALE/.62);
   // tread cleats rolling on both track lines (visible at the ground edge of the body)
   const speed=working?.0009:.004,phase=(now*speed)%.12;
   for(const side of [-1,1])for(let s=-.32+phase;s<.3;s+=.12){
    const gx=m.p[0]+ca*s-sa*side*.24,gy=m.p[1]+sa*s+ca*side*.24,q=iso(gx,gy);
    u.fillStyle(0x23221f,.85).fillRect(q.x-1.6,q.y-.6,3.2,1.4);
   }
   const o=a.over;o.clear().setDepth(p.y+9);
   if(working){
    // auger spreading hot material across screed width (rear)
    const bx=m.p[0]-ca*SCREED*.82,by=m.p[1]-sa*SCREED*.82;
    for(let k=0;k<9;k++){
     const t=(k/8-.5)*2*HALF,ph=((now/260)+k*.37)%1,q=iso(bx-sa*t*(1-ph*.2)-ca*ph*.05,by+ca*t*(1-ph*.2)-sa*ph*.05);
     o.fillStyle(k%2?0x101112:0x3b3e3c,.9).fillCircle(q.x,q.y-ph*1.5,1.1+(1-ph)*.6);
    }
    // conveyor feed: dark chunks dropping into hopper front
    const hx=m.p[0]+ca*HOPPER,hy=m.p[1]+sa*HOPPER;
    for(let k=0;k<4;k++){const ph=((now/420)+k/4)%1,q=iso(hx-ca*ph*.3,hy-sa*ph*.3);o.fillStyle(0x121314,.85).fillRect(q.x-1,q.y-9+ph*3,2,1.6);}
    // steam off the screed
    for(let k=0;k<6;k++){const ph=((now/1400)+k/6)%1,q=iso(bx+(hash(k)-.5)*.4*sa,by-(hash(k)-.5)*.4*ca);
     this.fx!.fillStyle(0xf0ece4,.22*(1-ph)).fillEllipse(q.x+ph*6,q.y-4-ph*22,5+ph*10,3+ph*6);}
   }
   // exhaust puffs from the stack (rises above body)
   for(let k=0;k<3;k++){const ph=((now/(working?700:1100))+k/3)%1;this.fx!.fillStyle(0x4a4844,.28*(1-ph)).fillCircle(p.x+3+ph*7,p.y-46*PAVER_SCALE/.62-ph*16,2+ph*4);}
   this.sounds.push({id:w.machineId,kind:'paver',x:p.x,y:p.y,working});
  }
   this.fresh.end();
   for(const[id,a]of this.actors)if(!live.has(id)){a.img.destroy();a.under.destroy();a.over.destroy();this.actors.delete(id);}
 }
}
