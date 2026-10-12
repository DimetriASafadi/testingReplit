import Phaser from 'phaser';
import type { DistrictState } from './model';
import { along, roadsFor, type RoadSegment } from './roads';
import { U } from './art';
import { paverTexture, tentTexture, TENT_ORIGIN, PAVER_ORIGIN, PAVER_SCALE } from './expansion-art';
import { districtProgress } from './engine';
import type { FleetSound } from './game-audio';
import { cityLayout } from './city-layout';

const iso=(x:number,y:number)=>({x:(x-y)*U,y:(x+y)*U/2});
/** Cached road coating plus bounded active actors; never repaint the city ground per tick. */
export class RoadAnimation {
 private pavement?:Phaser.GameObjects.Graphics;
 private work?:Phaser.GameObjects.Graphics;
 private signature='';
 private did='';
 private actors=new Map<string,Phaser.GameObjects.Image>();
 private tents:Phaser.GameObjects.Image[]=[];
 private lastPaint=0;
 private nextProgress=0;
 private tentAlpha=1;
 sounds:FleetSound[]=[];
 constructor(private scene:Phaser.Scene){}
 reset(){this.pavement?.destroy();this.work?.destroy();for(const a of this.actors.values())a.destroy();for(const t of this.tents)t.destroy();this.actors.clear();this.tents=[];this.did='';this.signature='';this.sounds=[];}
 private coat(g:Phaser.GameObjects.Graphics,r:RoadSegment,f=1){
  const dx=r.b[0]-r.a[0],dy=r.b[1]-r.a[1],len=Math.hypot(dx,dy),ox=-dy/len*.30,oy=dx/len*.30;
  const end=[r.a[0]+dx*f,r.a[1]+dy*f];
  const pts=[iso(r.a[0]+ox,r.a[1]+oy),iso(end[0]+ox,end[1]+oy),iso(end[0]-ox,end[1]-oy),iso(r.a[0]-ox,r.a[1]-oy)];
  g.fillStyle(0x333b3d,1).fillPoints(pts,true);
  g.lineStyle(1.2,0xbeb99e,.7).strokePoints(pts,true);
  for(let i=.15;i<f;i+=.28){const a=iso(r.a[0]+dx*i,r.a[1]+dy*i),b=iso(r.a[0]+dx*Math.min(f,i+.12),r.a[1]+dy*Math.min(f,i+.12));g.lineStyle(1.6,0xe3d5a3,.8).lineBetween(a.x,a.y,b.x,b.y);}
  // Fine, deterministic aggregate texture rather than a flat vector slab.
  for(let i=0;i<42*f;i++){const t=((i*37)%43)/43*f,o=((i*13)%17)/17-.5;const p=iso(r.a[0]+dx*t+ox*o*1.7,r.a[1]+dy*t+oy*o*1.7);g.fillStyle(i%3?0x69716a:0x151c21,.28).fillRect(p.x,p.y,1.2,1);}
 }
 update(d:DistrictState,now:number){
  if(this.did!==d.id){
   this.reset();this.did=d.id;this.pavement=this.scene.add.graphics().setDepth(-799000);this.work=this.scene.add.graphics().setDepth(-798900);
   const max=cityLayout(d.id).extent;
   for(let i=0;i<14;i++){
    const t=(i+1)/15,point=i%2?iso(-3.5-(i%3)*1.6,t*max):iso(t*max,-4-(i%3)*1.4);
    const key=tentTexture(this.scene,i);
    const img=this.scene.add.image(point.x,point.y,key).setOrigin(TENT_ORIGIN.x,TENT_ORIGIN.y).setScale(.85+(i%3)*.08).setDepth(point.y);
    this.tents.push(img);
   }
  }
  const sig=(d.pavedRoads??[]).join('|');
  if(sig!==this.signature){this.signature=sig;this.pavement!.clear();const ids=new Set(d.pavedRoads);for(const r of roadsFor(d.id))if(ids.has(r.id))this.coat(this.pavement!,r);}
  if(now>=this.nextProgress){this.nextProgress=now+500;this.tentAlpha=Math.pow(1-districtProgress(d)/100,1.2);}
  const target=this.tentAlpha;
  for(const t of this.tents)t.setAlpha(Math.abs(t.alpha-target)<.005?target:t.alpha+(target-t.alpha)*.045);
  const live=new Set<string>();this.sounds=[];
  const paint=now>=this.lastPaint;if(paint){this.lastPaint=now+120;this.work!.clear();}
  for(const w of d.roadWorks??[]){
   if(now>=w.returnEnd)continue;
   const r=roadsFor(d.id).find(r=>r.id===w.roadId)!;live.add(w.machineId);
   const working=now>=w.arrival&&now<w.workEnd;
   const f=Math.max(0,Math.min(1,(now-w.arrival)/(w.workEnd-w.arrival)));
   const m=now<w.arrival?along(w.route,(now-w.start)/(w.arrival-w.start)):working?along([r.a,r.b],f):along([r.b,...w.route.slice().reverse()],(now-w.workEnd)/(w.returnEnd-w.workEnd));
   if(paint&&working)this.coat(this.work!,r,f);
   const p=iso(...m.p),key=paverTexture(this.scene,m.angle);
   if(paint){this.work!.fillStyle(0x222521,.22).fillEllipse(p.x+9,p.y+9,65,22);}
   let actor=this.actors.get(w.machineId);
   if(!actor){actor=this.scene.add.image(p.x,p.y,key).setOrigin(PAVER_ORIGIN.x,PAVER_ORIGIN.y);this.actors.set(w.machineId,actor);}
   actor.setTexture(key).setScale(PAVER_SCALE).setPosition(p.x,p.y+(working?Math.sin(now/65)*.35:0)).setDepth(p.y+8).setAlpha(1);
   if(paint&&working){this.work!.lineStyle(3,0xf2ca54,.95).lineBetween(p.x-12,p.y+12,p.x+12,p.y+12);}
   this.sounds.push({id:w.machineId,kind:'paver',x:p.x,y:p.y,working});
  }
  for(const[id,a]of this.actors)if(!live.has(id)){a.destroy();this.actors.delete(id);}
 }
}
