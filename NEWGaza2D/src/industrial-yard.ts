// Industrial contents baked into a shared ground texture, never live vehicles
// or per-plant scene objects. Ground positions rotate; verticals and sun do not.
import { U } from './art';
import { YARD_W } from './lot-geometry';
import { industrialKit,industrialSites } from './industrial-layout';
type Pt=[number,number];
const pr=([u,v]:Pt,z=0):Pt=>[YARD_W/2+(u-v)*U,380+(u+v)*U/2-z];

export function drawIndustrialYard(x:CanvasRenderingContext2D,m:(p:Pt)=>Pt,id:string,radius:number,edge:number){
  const kit=industrialKit(id),sites=industrialSites(radius,edge,kit);
  const point=(u:number,v:number,z=0)=>pr(m([u,v]),z);
  const poly=(pts:Pt[],color:string)=>{
    x.beginPath();pts.forEach((p,i)=>i?x.lineTo(...p):x.moveTo(...p));x.closePath();x.fillStyle=color;x.fill();
    x.strokeStyle='rgba(67,66,55,.38)';x.lineWidth=.65;x.stroke();
  };
  const box=(u:number,v:number,w:number,d:number,h:number,color:string,top:string)=>{
    const ground:Pt[]=[[u-w/2,v-d/2],[u+w/2,v-d/2],[u+w/2,v+d/2],[u-w/2,v+d/2]];
    const a=ground.map(p=>pr(m(p))),b=ground.map(p=>pr(m(p),h));
    poly(a.map(p=>[p[0]+3,p[1]+2] as Pt),'rgba(36,35,29,.17)');
    for(let i=0;i<4;i++){
      const j=(i+1)%4,aa=m(ground[i]),bb=m(ground[j]);
      if(bb[1]-aa[1]+aa[0]-bb[0]>0)poly([a[i],a[j],b[j],b[i]],color);
    }
    poly(b,top);
  };
  const tank=(u:number,v:number,s:number,h:number)=>{
    const [px,py]=point(u,v),rx=s*U*.70,ry=rx*.48;
    x.fillStyle='rgba(36,39,34,.18)';x.beginPath();x.ellipse(px+3,py+2,rx+2,ry+1,0,0,7);x.fill();
    const g=x.createLinearGradient(px-rx,0,px+rx,0);g.addColorStop(0,'#747d77');g.addColorStop(.4,'#b4bbb0');g.addColorStop(1,'#66766f');
    x.fillStyle=g;x.fillRect(px-rx,py-h,2*rx,h);x.beginPath();x.ellipse(px,py,rx,ry,0,0,7);x.fill();
    x.fillStyle='#a5b3aa';x.beginPath();x.ellipse(px,py-h,rx,ry,0,0,7);x.fill();
    x.strokeStyle='#687970';x.lineWidth=.7;
    for(const z of [h*.3,h*.7]){x.beginPath();x.ellipse(px,py-z,rx,ry,0,0,Math.PI);x.stroke();}
    x.fillStyle='#586c64';x.fillRect(px-1,py-h-2,2,3);
  };
  // Shallow drainage channels and white service markings use actual lot axes.
  for(const u of [-edge+.07,edge-.07]){
    const a=point(u,-edge+.12),b=point(u,edge-.12);
    x.strokeStyle='#777e74';x.lineWidth=1;x.beginPath();x.moveTo(...a);x.lineTo(...b);x.stroke();
  }
  const lane=Math.max(radius+.02,.52);
  if(lane<edge-.05){
    for(const u of [-.36,.36]){
      const a=point(u,lane),b=point(u,edge-.05);
      x.strokeStyle='#d0c395';x.lineWidth=1.3;x.beginPath();x.moveTo(...a);x.lineTo(...b);x.stroke();
    }
  }
  // Rear corners only: clear drive-through access even when the depot door is
  // viewed from a different face. Small furnishings never invade the core.
  sites.sort((a,b)=>{const p=m([a.u,a.v]),q=m([b.u,b.v]);return p[0]+p[1]-q[0]-q[1];});
  sites.forEach(({u,v,size:s},index)=>{
    const h=Math.min(13,s*42);
    if(kit==='water'||kit==='beverage'){
      tank(u,v,s*.78,h);
      const a=point(u-s*.3,v+s*.25,2),b=point(u+s*.3,v+s*.25,2);
      x.strokeStyle=kit==='water'?'#53828b':'#879690';x.lineWidth=2;x.beginPath();x.moveTo(...a);x.lineTo(...b);x.stroke();
    }else if(kit==='lumber'||kit==='metal'){
      for(let i=0;i<4;i++){
        box(u,v+(i%2)*s*.04,s,s*.60,1.5+i*1.8,kit==='lumber'?'#84633e':'#667272',kit==='lumber'?'#baa174':'#97a09a');
      }
      for(let i=0;i<4;i++){
        const a=point(u-s*.5,v+s*.30,1+i*1.8),b=point(u+s*.5,v+s*.30,1+i*1.8);
        x.strokeStyle=kit==='lumber'?'#6f5739':'#4d5c59';x.lineWidth=.6;
        x.beginPath();x.moveTo(...a);x.lineTo(...b);x.stroke();
      }
      for(const f of [-.28,.28]){
        const a=point(u+s*f,v-s*.3,8),b=point(u+s*f,v+s*.3,8);
        x.strokeStyle='#615e4d';x.lineWidth=1;x.beginPath();x.moveTo(...a);x.lineTo(...b);x.stroke();
      }
    }else if(kit==='glass'){
      box(u,v,s,s*.7,3,'#687f7c','#a4bcb2');
      for(let i=0;i<4;i++){
        const vv=v-s*.25+i*s*.13;
        poly([point(u-s*.4,vv,3),point(u+s*.4,vv,3),point(u+s*.4,vv+s*.12,h),point(u-s*.4,vv+s*.12,h)],i%2?'#93b9b4':'#b6c9bb');
      }
    }else if(kit==='aggregate'){
      const base=Array.from({length:10},(_,i)=>point(u+Math.cos(i/10*Math.PI*2)*s*.5,v+Math.sin(i/10*Math.PI*2)*s*.45));
      poly(base,'#9c9278');
      for(let i=0;i<10;i++)poly([base[i],base[(i+1)%10],point(u,v,h*.65)],i<5?'#ac9e7e':'#847d67');
      box(u-s*.55,v,s*.06,s,4,'#757e72','#a1a595');
    }else if(kit==='workers'){
      box(u,v,s,s*.35,3,'#78766a','#b9ad90');
    }else{
      const recycling=kit==='recycling';
      box(u,v,s,s*.72,h*.64,recycling?'#646e59':kit==='equipment'?'#717d73':'#7f8e7e',
        recycling?'#8d9576':kit==='equipment'?'#9caa9a':'#b0b998');
      const a=point(u-s*.5,v, h*.64),b=point(u+s*.5,v,h*.64);
      x.strokeStyle=index%2?'#c6ba8f':'#687966';x.lineWidth=1;x.beginPath();x.moveTo(...a);x.lineTo(...b);x.stroke();
    }
  });
}
