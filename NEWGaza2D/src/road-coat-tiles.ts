/** Dirty raster tiles only. A new street must not rebuild unrelated city roads.
 * Pending tiles survive replanning, including partially applied plans. */
export const COAT_TILE_SIZE = 256;
export interface CoatBounds { x:number; y:number; right:number; bottom:number }
type Point=readonly [number,number];
export interface CoatShape { token:string; bounds:CoatBounds; footprints?:readonly (readonly Point[])[] }
export const touchesCoatTile=(a:CoatBounds,b:CoatBounds)=>
  a.x<=b.right&&a.right>=b.x&&a.y<=b.bottom&&a.bottom>=b.y;
/** Polygon/rectangle overlap, including shoulders. A diagonal street's bounding
 * rectangle covers much of the city; it is NOT its dirty painting footprint. */
export function coatShapeTouches(s:CoatShape,tile:CoatBounds){
  if(!touchesCoatTile(s.bounds,tile))return false;
  if(!s.footprints)return true;
  const b={x:tile.x-22,y:tile.y-22,right:tile.right+22,bottom:tile.bottom+22};
  return s.footprints.some(poly=>{
    if(!poly.length)return false;
    const inside=(x:number,y:number)=>{let yes=false;
      for(let i=0,j=poly.length-1;i<poly.length;j=i++){
        const a=poly[i],c=poly[j];
        if((a[1]>y)!==(c[1]>y)&&x<(c[0]-a[0])*(y-a[1])/(c[1]-a[1])+a[0])yes=!yes;
      }return yes;};
    if(inside(b.x,b.y)||inside(b.right,b.y)||inside(b.x,b.bottom)||inside(b.right,b.bottom))return true;
    return poly.some((a,i)=>{
      const c=poly[(i+1)%poly.length],dx=c[0]-a[0],dy=c[1]-a[1];let lo=0,hi=1;
      const p=[-dx,dx,-dy,dy],q=[a[0]-b.x,b.right-a[0],a[1]-b.y,b.bottom-a[1]];
      for(let k=0;k<4;k++){if(p[k]===0){if(q[k]<0)return false;continue;}
        const t=q[k]/p[k];if(p[k]<0)lo=Math.max(lo,t);else hi=Math.min(hi,t);if(lo>hi)return false;}
      return true;
    });
  });
}
export class RoadCoatTiles {
  private desired=new Map<string,CoatShape>();
  private pending=new Map<string,{x:number;y:number}>();
  get size(){return this.pending.size;}
  reset(){this.desired.clear();this.pending.clear();}
  reconcile(shapes:CoatShape[],width:number,height:number){
    const next=new Map(shapes.map(s=>[s.token,s]));
    const dirty:CoatShape[]=[];
    for(const [token,b] of this.desired)if(!next.has(token))dirty.push(b);
    for(const [token,b] of next)if(!this.desired.has(token))dirty.push(b);
    for(const shape of dirty){
      const b=shape.bounds;
      const x0=Math.max(0,Math.floor(b.x/COAT_TILE_SIZE)*COAT_TILE_SIZE);
      const y0=Math.max(0,Math.floor(b.y/COAT_TILE_SIZE)*COAT_TILE_SIZE);
      for(let y=y0;y<height&&y<=b.bottom;y+=COAT_TILE_SIZE)
        for(let x=x0;x<width&&x<=b.right;x+=COAT_TILE_SIZE)
          if(coatShapeTouches(shape,{x,y,right:x+COAT_TILE_SIZE,bottom:y+COAT_TILE_SIZE}))
            this.pending.set(`${x}:${y}`,{x,y});
    }
    this.desired=next;
  }
  next(){return this.pending.values().next().value as {x:number;y:number}|undefined;}
  applied(x:number,y:number){this.pending.delete(`${x}:${y}`);}
}
