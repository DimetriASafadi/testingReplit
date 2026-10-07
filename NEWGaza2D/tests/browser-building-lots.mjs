import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b=await browser(1280,900),key='newgaza2d-save-v1';
let original;
try{
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`!!document.querySelector('#boot.done')`);
  original=await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{
    const {newGame}=await import('/src/engine.ts'),{BUILDINGS}=await import('/src/catalog.ts');
    const n=Date.now(),s=newGame(n),d=s.districts[0];s.currentDistrict=d.id;
    for(const p of d.plots)Object.assign(p,{status:'empty',buildingId:'small_house'});
    BUILDINGS.forEach((def,i)=>[0,1].forEach(orientation=>{
      Object.assign(d.plots[i*2+orientation],{status:'built',buildingId:def.id,orientation,
        startedAt:n-100000,endsAt:n-1000,incomeAt:n});
    }));
    d.camera={x:0,y:950,zoom:.7};
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__oldLots=true;
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__oldLots&&window.__newgazaArtScene?.views.size===144`);
  const report=await b.evaluate(`(async()=>{
    const scene=window.__newgazaArtScene,{builtKey,SPR}=await import('/src/realart.ts'),
      {builtScale}=await import('/src/art-scale.ts'),{buildingMaxHeight}=await import('/src/building-footprint.ts'),
      {PLOT,U}=await import('/src/art.ts'),{CITY_SHADOW}=await import('/src/building-shadow.ts');
    const state=scene.hooks.getState(),d=state.districts.find(d=>d.id===state.currentDistrict);
    return {sun:CITY_SHADOW,plots:d.plots.slice(0,84).map(p=>{
      const raw=builtKey(p.buildingId,p.orientation),m=SPR[raw],scale=builtScale(raw),v=scene.views.get(p.id);
      return{id:p.buildingId,orientation:p.orientation,groundWidth:m.ground.w*scale,groundDepth:m.ground.h*scale,
        maxWidth:PLOT*U*2,maxDepth:PLOT*U,height:m.h*scale,cap:buildingMaxHeight(raw),
        equalScale:Math.abs(v.img.scaleX-scale)<.0001,shadow:v.shadow.texture.key,
        shadowScale:v.shadow.scaleX,origin:[v.shadow.originX,v.shadow.originY]};
    })};
  })()`);
  assert.equal(report.plots.length,84);
  assert.ok(report.sun.dx>0&&report.sun.dy>0);
  for(const p of report.plots){
    assert.ok(p.groundWidth<=p.maxWidth+.01&&p.groundDepth<=p.maxDepth+.01,JSON.stringify(p));
    assert.ok(p.height<=p.cap+.01&&p.equalScale,JSON.stringify(p));
    assert.ok(p.shadow.startsWith('building-shadow:'));
    assert.ok(p.origin.every(v=>v>=0&&v<=1));
  }
  await b.screenshot('/tmp/newgaza-all-building-lots.png');
  // Inspect both faces together at a readable zoom, including the reported shops
  // and the formerly frontal municipal building.
  for(const [group,ids] of [['retail',['shoe_shop','food_shop','cafe','municipality']],['housing',['small_house','medium_house','villa','modern_housing']]]){
    await b.evaluate(`(async()=>{
      const scene=window.__newgazaArtScene,s=scene.hooks.getState(),d=s.districts.find(d=>d.id===s.currentDistrict),n=Date.now();
      for(const p of d.plots)Object.assign(p,{status:'empty',buildingId:'small_house'});
      const nearby=[...d.plots].sort((a,b)=>Math.hypot(a.x-15,a.y-15)-Math.hypot(b.x-15,b.y-15)).slice(0,8);
      ${JSON.stringify(ids)}.forEach((id,i)=>[0,1].forEach(orientation=>{
        const p=nearby[i*2+orientation];Object.assign(p,{status:'built',buildingId:id,orientation,startedAt:n-1000,endsAt:n-1,incomeAt:n});
      }));
      const avg=nearby.reduce((v,p)=>({x:v.x+p.x+1,y:v.y+p.y+1}),{x:0,y:0});
      scene.cameras.main.setZoom(1.7);scene.cameras.main.centerOn((avg.x-avg.y)*64/8,(avg.x+avg.y)*32/8-75);
    })()`);
    await b.wait(600);await b.screenshot('/tmp/newgaza-'+group+'-paired-lots.png');
  }
  assert.equal(b.errors.length,0,JSON.stringify(b.errors));
  console.log('PASS: all 42 buildings/84 orientations fit ground and height bounds; uniform sun, silhouette shadows and real scene scale; paired galleries captured.');
}finally{
  if(original!==undefined){
    await b.evaluate(original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`);
    await b.send('Page.reload');
  }
  b.close();
}
