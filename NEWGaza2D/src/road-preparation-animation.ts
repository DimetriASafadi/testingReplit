import Phaser from 'phaser';
import type { DistrictState } from './model';
import { along, roadsFor, workRoad, type Point, type RoadSegment } from './roads';
import { U } from './art';
import meta from './machinery-manifest.json';
import { machineryDepth } from './machinery-depth';
import type { FleetSound } from './game-audio';
import { RoadSurfaceCache, type SurfaceBrush, type SurfaceUploadBudget } from './road-surface-cache';

const iso=([x,y]:Point)=>({x:(x-y)*U,y:(x+y)*U/2});
const clamp=(f:number)=>Math.max(0,Math.min(1,f));
const hash=(n:number)=>{const x=Math.sin(n*127.1+311.7)*43758.5453;return x-Math.floor(x);};
/** Ground preparation remains below vehicles and buildings. Only the bounded
 * active jobs are drawn; aggregate stays visible until asphalt covers it. */
export class RoadPreparationAnimation {
  private ground:RoadSurfaceCache;
  private actors=new Map<string,{body:Phaser.GameObjects.Image;dust:Phaser.GameObjects.Graphics}>();
  sounds:FleetSound[]=[];
  constructor(private scene:Phaser.Scene,budget?:SurfaceUploadBudget){this.ground=new RoadSurfaceCache(scene,-798950,budget);}
  reset(){this.ground.reset();for(const a of this.actors.values()){a.body.destroy();a.dust.destroy();}this.actors.clear();this.sounds=[];}
  destroy(){this.reset();}
  private coat(g:SurfaceBrush,r:RoadSegment,lo:number,hi:number,aggregate:boolean){
    if(hi<=lo)return;
    const dx=r.b[0]-r.a[0],dy=r.b[1]-r.a[1],len=Math.hypot(dx,dy),nx=-dy/len,ny=dx/len;
    const p=(f:number,o:number)=>iso([r.a[0]+dx*f+nx*o,r.a[1]+dy*f+ny*o]);
    g.fillStyle(aggregate?0x979486:0xb6a382,1).fillPoints([p(lo,-.30),p(hi,-.30),p(hi,.30),p(lo,.30)],true);
    for(let i=0;i<Math.min(600,Math.ceil(len*80));i++){
      const t=hash(i)*1;if(t<lo||t>hi)continue;
      const q=p(t,(hash(i+433)-.5)*.57);
      g.fillStyle(aggregate?(i%2?0xbcb7a6:0x6d7069):0x8c795c,.45).fillEllipse(q.x,q.y,aggregate?2:1.2,aggregate?1.2:.7);
    }
  }
  update(d:DistrictState,now:number){
    this.ground.begin();this.sounds=[];const live=new Set<string>();
    for(const w of d.roadWorks??[]){
      const prep=w.preparation;if(!prep||now>=w.workEnd)continue;
      const r=workRoad(d.id,w),len=Math.hypot(r.b[0]-r.a[0],r.b[1]-r.a[1]);
      const sand=clamp((now-w.arrival)/(prep.sandEnd-w.arrival));
      const aggregate=clamp((now-prep.sandEnd)/(prep.aggregateEnd-prep.sandEnd));
      const pending=new Set(w.roadIds??[w.roadId]);
      for(const part of roadsFor(d.id).filter(q=>pending.has(q.id))){
        const start=Math.hypot(part.a[0]-r.a[0],part.a[1]-r.a[1]),size=Math.hypot(part.b[0]-part.a[0],part.b[1]-part.a[1]);
        const s=clamp((sand*len-start)/size), a=clamp(((1-aggregate)*len-start)/size);
        if(s>0||a<1)this.ground.paint(`${w.machineId}:${part.id}`,part,`${Math.round(s*size*U)}:${Math.round(a*size*U)}`,now,g=>{
          this.coat(g,part,0,s,false);this.coat(g,part,a,1,true);
        });
      }
      if(now>=prep.dozerReturnEnd)continue;
      live.add(prep.unitId);
      const working=now>=w.arrival&&now<prep.aggregateEnd;
      const move=now<w.arrival?along(prep.route,(now-w.start)/(w.arrival-w.start))
        :now<prep.sandEnd?along([r.a,r.b],sand)
        :now<prep.aggregateEnd?along([r.b,r.a],aggregate)
        :along(prep.route.slice().reverse(),(now-prep.aggregateEnd)/(prep.dozerReturnEnd-prep.aggregateEnd));
      const q=iso(move.p),view=this.scene.cameras.main.worldView;
      if(q.x<view.x-100||q.x>view.right+100||q.y<view.y-100||q.y>view.bottom+100){
        const old=this.actors.get(prep.unitId);if(old){old.body.setVisible(false);old.dust.clear();}
        this.sounds.push({id:prep.unitId,kind:'bulldozer',x:q.x,y:q.y,working});continue;
      }
      let a=this.actors.get(prep.unitId);
      if(!a){a={body:this.scene.add.image(0,0,'machine:bulldozer','drive_0_0').setOrigin(meta.foot[0]/meta.size,meta.foot[1]/meta.size).setScale(.66),dust:this.scene.add.graphics()};this.actors.set(prep.unitId,a);}
      const heading=((Math.round(move.angle/(2*Math.PI)*meta.headings)%meta.headings)+meta.headings)%meta.headings;
      a.body.setVisible(true).setFrame(`drive_${heading}_${Math.floor(now/160)%2}`).setPosition(q.x,q.y+(working?Math.sin(now/80)*.25:0))
        .setDepth(machineryDepth('bulldozer',move.angle,q.y,meta.size,meta.ortho,.66));
      a.dust.clear().setDepth(a.body.depth+.2);
      if(working){
        const front=iso([move.p[0]+Math.cos(move.angle)*.28,move.p[1]+Math.sin(move.angle)*.28]);
        for(let i=0;i<9;i++){
          const phase=(now/1300+i/9)%1;
          a.dust.fillStyle(now<prep.sandEnd?0xb7a17e:0xaaa696,.2*(1-phase))
            .fillEllipse(front.x+(hash(i)-.5)*18+phase*7,front.y-phase*10,3+phase*11,2+phase*5);
        }
      }
      this.sounds.push({id:prep.unitId,kind:'bulldozer',x:q.x,y:q.y,working});
    }
    this.ground.end();
    for(const[id,a]of this.actors)if(!live.has(id)){a.body.destroy();a.dust.destroy();this.actors.delete(id);}
  }
}
