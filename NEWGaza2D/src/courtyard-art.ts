import { U } from './art';
import { YARD_W } from './lot-geometry';
import { courtyardZones } from './courtyard-layout';
import { fillPavers,drawShadeSeating,drawBench,drawParking,type Pt } from './parcel-dressing';
const project=([a,b]:Pt):Pt=>[YARD_W/2+(a-b)*U,380+(a+b)*U/2];
/** Baked into the existing yard texture, not additional scene objects. */
export function drawCourtyard(x:CanvasRenderingContext2D,m:(p:Pt)=>Pt,radius:number,edge:number,n:number,residential:boolean){
  for(const z of courtyardZones(radius,edge)){
    const poly:Pt[]=[[z.x0,z.y0],[z.x1,z.y0],[z.x1,z.y1],[z.x0,z.y1]].map(p=>m(p as Pt));
    if(z.kind==='garden'){
      x.save();x.beginPath();poly.forEach((p,i)=>{const[a,b]=project(p);i?x.lineTo(a,b):x.moveTo(a,b);});x.closePath();
      x.fillStyle=residential?'#7f8968':'#85886a';x.fill();x.strokeStyle='#c7bc9e';x.lineWidth=2;x.stroke();x.clip();
      // Muted ground speckle and planted rows, not a saturated flat green pad.
      for(let i=0;i<42;i++){const p=project(m([z.x0+(z.x1-z.x0)*((i*17%43)/43),z.y0+(z.y1-z.y0)*((i*29%41)/41)]));
        x.fillStyle=i%3?'rgba(53,68,39,.19)':'rgba(191,182,141,.22)';x.fillRect(p[0],p[1],3,1);}
      x.restore();
    }else{
      fillPavers(x,poly,'limestone',.85);
      const mid:Pt=[(z.x0+z.x1)/2,(z.y0+z.y1)/2];
      if(z.kind==='terrace'){
        if(z.x1-z.x0>.43&&z.y1-z.y0>.43)drawShadeSeating(x,m,mid);
        else if(z.x1-z.x0>.28)drawBench(x,m,mid);
      }else if(z.y1-z.y0>.34&&z.x1-z.x0>.6){
        drawParking(x,m,[z.x0+.08,mid[1]],[z.x1-.12,mid[1]],2,n);
      }
    }
  }
}
