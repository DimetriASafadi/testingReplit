/** Read-only scale contact sheet; bundle for a disposable local review page. */
import { buildingFootprint, buildingLotDimensions, buildingMaxHeight, buildingRenderScale } from '../../src/building-footprint';
import manifest from '../../src/realistic-manifest.json';
type Meta={w:number;h:number;ay:number;ground?:{w:number;h:number}};
const sprites=manifest as Record<string,Meta>;
const cameraReview=new URLSearchParams(location.search).has('camera');
const beforeSprites:Record<string,Meta>=cameraReview?await (await fetch('./__camera-meta.json')).json():sprites;
const cameraBeforeScale=(key:string)=>{
  const stem=key.replace(/_front$/,''),target=buildingLotDimensions(key);
  return Math.min(...['front','right','back','left'].map(face=>{
    const m=beforeSprites[`${stem}_${face}`],gw=m.ground?.w??m.w,gh=m.ground?.h??gw/2;
    return Math.min(target.width/gw,target.height/gh,
      buildingMaxHeight(key)/Math.max(1,m.h*m.ay-gh/2),240.64/gw,120.32/gh);
  }));
};
const oldScale=(key:string)=>{
  const stem=key==='farm_cattle'?'cattle':key.replace(/_front$/,'');
  return Math.min(...['front','right','back','left'].map(face=>{
    const m=sprites[stem==='cattle'&&face==='front'?'farm_cattle':`${stem}_${face}`];
    const gw=m.ground?.w??m.w,gh=m.ground?.h??gw/2;
    return Math.min(.72*buildingFootprint(key).factor,buildingMaxHeight(key)/m.h,179.2/gw,89.6/gh);
  }));
};
const rows=[
  ['rpark_front','western_front'],['olive_front','grapes_front'],
  ['smallhouse_front','apt4_front'],['club_front','zoo_front'],
];
const canvas=document.querySelector('canvas')!,ctx=canvas.getContext('2d')!;
canvas.width=800;canvas.height=920;ctx.fillStyle='#102439';ctx.fillRect(0,0,800,920);
for(let row=0;row<rows.length;row++)for(let item=0;item<2;item++){
  const key=rows[row][item];
  const image=new Image();image.src=`./art/realistic/s/${key}.webp`;await image.decode();
  const beforeImage=new Image();
  if(cameraReview){beforeImage.src=`./__camera-before/${key}.webp`;await beforeImage.decode();}
  for(let variant=0;variant<2;variant++){
    const m=cameraReview&&!variant?beforeSprites[key]:sprites[key];
    const x=(item*2+variant)*200,y=row*230,cx=x+100,cy=y+172,k=.65;
    ctx.fillStyle='#958e81';ctx.fillRect(x+3,y+3,194,224);
    ctx.fillStyle='#b3ad9c';ctx.strokeStyle='#e4d9c0';ctx.lineWidth=1;
    ctx.beginPath();ctx.moveTo(cx-128*k,cy);ctx.lineTo(cx,cy-64*k);
    ctx.lineTo(cx+128*k,cy);ctx.lineTo(cx,cy+64*k);ctx.closePath();ctx.fill();ctx.stroke();
    const scale=variant?buildingRenderScale(key,m):cameraReview?cameraBeforeScale(key):oldScale(key);
    ctx.drawImage(cameraReview&&!variant?beforeImage:image,cx-m.w*scale*k/2,cy-m.h*m.ay*scale*k,m.w*scale*k,m.h*scale*k);
    if(cameraReview){
      const half=(m.ground?.w??m.w)*scale*k/2;
      ctx.strokeStyle='#64eddf';ctx.lineWidth=1;ctx.beginPath();
      ctx.moveTo(cx-half,cy);ctx.lineTo(cx,cy+half/2);ctx.lineTo(cx+half,cy);ctx.stroke();
    }
    ctx.font='bold 14px system-ui';ctx.fillStyle='#fff';ctx.textAlign='center';
    ctx.fillText(`${key.replace('_front','')} — ${variant?'after':'before'}`,cx,y+22);
    ctx.font='12px system-ui';
    ctx.fillText(`ground ${((m.ground?.w??m.w)*scale).toFixed(0)} × ${((m.ground?.h??m.w/2)*scale).toFixed(0)} px`,cx,y+220);
  }
}
