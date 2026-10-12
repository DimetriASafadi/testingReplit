import { cityLayout } from './city-layout';
import type { DistrictState, Plot, RoadWork } from './model';
export type Point = [number,number];
export interface RoadSegment {id:string;a:Point;b:Point}
const cache = new Map<string,RoadSegment[]>();
/** Cut at intersections first, then subdivide to no more than a parcel length. */
export function roadsFor(id:string) {
 let found=cache.get(id);if(found)return found;
 const l=cityLayout(id),roads:RoadSegment[]=[];
 for(const axis of ['x','y'] as const){
  const fixed=axis==='x'?l.roadX:l.roadY,cross=axis==='x'?l.roadY:l.roadX;
  fixed.forEach((f,i)=>{for(let c=0;c<cross.length-1;c++){
   const count=Math.ceil((cross[c+1]-cross[c])/2);
   for(let n=0;n<count;n++){
    const p=cross[c]+(cross[c+1]-cross[c])*n/count,q=cross[c]+(cross[c+1]-cross[c])*(n+1)/count;
    roads.push({id:`${axis}-${i}-${c}-${n}`,a:axis==='x'?[f,p]:[p,f],b:axis==='x'?[f,q]:[q,f]});
   }
  }});
 }
 cache.set(id,roads);return roads;
}
export const roadMid = (r:RoadSegment):Point => [(r.a[0]+r.b[0])/2,(r.a[1]+r.b[1])/2];
/** A street is one continuous grid line, not an individual selectable slice. */
export function streetFor(id:string,roadId:string) {
 const r=roadsFor(id).find(r=>r.id===roadId);if(!r)return [];
 const vertical=r.a[0]===r.b[0];
 return roadsFor(id).filter(q=>vertical?q.a[0]===q.b[0]&&q.a[0]===r.a[0]:q.a[1]===q.b[1]&&q.a[1]===r.a[1]);
}
export function workRoad(id:string,w:Pick<RoadWork,'roadId'|'roadIds'>):RoadSegment {
 const all=w.roadIds?streetFor(id,w.roadId):roadsFor(id).filter(r=>r.id===w.roadId);
 return {id:w.roadId,a:all[0].a,b:all[all.length-1].b};
}
export function roadAt(id:string,x:number,y:number) {
 return roadsFor(id).find(r=> Math.abs(r.a[0]-r.b[0])<.001
  ? Math.abs(x-r.a[0])<.36 && y>=r.a[1] && y<=r.b[1]
  : Math.abs(y-r.a[1])<.36 && x>=r.a[0] && x<=r.b[0]);
}
export function pavingRoute(d:DistrictState,p:Plot,r:RoadSegment):Point[] {
 const l=cityLayout(d.id),nearest=(arr:number[],v:number)=>arr.reduce((a,b)=>Math.abs(b-v)<Math.abs(a-v)?b:a);
 const block=l.blocks.find(b=>b.plotIds.includes(p.id));
 const sx=nearest(l.roadX,p.x+2),sy=block?.roadY??nearest(l.roadY,p.y+1);
 // Follow the parcel seam to the block street: a direct shortcut from an
 // interior factory to the nearest road would cross its neighbours.
 const horizontal=Math.abs(r.a[1]-r.b[1])<.001;
 const out:Point[]=[[p.x+1,p.y+1],[p.x+2,p.y+1],[p.x+2,sy],[sx,sy]];
 if(horizontal)out.push([sx,r.a[1]],r.a);else out.push([r.a[0],sy],r.a);
 return out.filter((v,i,a)=>i===0||Math.hypot(v[0]-a[i-1][0],v[1]-a[i-1][1])>.001);
}
export function pathLength(points:Point[]) {return points.slice(1).reduce((n,p,i)=>n+Math.hypot(p[0]-points[i][0],p[1]-points[i][1]),0)}
export function along(points:Point[],fraction:number):{p:Point;angle:number}{
 let left=Math.max(0,Math.min(1,fraction))*pathLength(points);
 for(let i=1;i<points.length;i++){const a=points[i-1],b=points[i],len=Math.hypot(b[0]-a[0],b[1]-a[1]);if(left<=len||i===points.length-1){const t=len?Math.min(1,left/len):1;return{p:[a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t],angle:Math.atan2(b[1]-a[1],b[0]-a[0])}}left-=len;}
 return{p:points[0],angle:0};
}
