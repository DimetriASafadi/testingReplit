import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';

const b = await browser(1280,900), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', {url:'http://localhost:80/'});
  await b.until(`window.__newgazaArtScene && document.querySelector('#boot.done')`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{
    const now=Date.now(),s=(await import('/src/engine.ts')).newGame(now),d=s.districts[0];
    s.currentDistrict=d.id;
    d.plots.forEach(p=>p.status='empty');
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',incomeAt:now});
    for(const [id,name,o] of [[27,'ornamental_trees',0],[28,'ornamental_trees',1],[35,'protective_trees',0],[36,'citrus',1]]){
      Object.assign(d.plots[id],{status:'built',buildingId:name,orientation:o,incomeAt:now});
    }
    const p=d.plots[27];d.camera={x:(p.x-p.y)*64,y:(p.x+p.y+3)*32,zoom:1.6};
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__oldGroundDocument=true;
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__oldGroundDocument && window.__newgazaArtScene?.views.size===144`);
  await b.evaluate(`document.querySelector('[data-a="hide-obj"]')?.click()`);
  const report = await b.evaluate(`(async()=>{
    const {SPR,builtKey}=await import('/src/realart.ts');
    const {builtScale}=await import('/src/art-scale.ts');
    const {BUILDINGS}=await import('/src/catalog.ts');
    const issues=[];
    for(const b of BUILDINGS)for(const o of [0,1]){
      const key=builtKey(b.id,o),m=SPR[key],s=builtScale(key);
      const w=m.ground?.w??m.w,h=m.ground?.h??w/2;
      if(w*s>179.201||h*s>89.601)issues.push(key);
    }
    return {issues,scenery:Object.values(SPR).filter(m=>m.ground).length,
      ornamental:[SPR.orn_front,SPR.orn_back],
      renderer:window.__newgazaArtScene.views.get(27).img.texture.key};
  })()`);
  assert.deepEqual(report.issues,[]);
  assert.equal(report.scenery,24);
  for(const m of report.ornamental){
    assert.ok(Math.abs(m.ground.w/m.ground.h-2)<.03);
    assert.ok(m.ay>0&&m.ay<1);
  }
  await b.screenshot('/tmp/newgaza-aligned-scenery-desktop.png');
  await b.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:false});
  await b.evaluate(`(()=>{const sc=window.__newgazaArtScene,p=sc.hooks.getState().districts[0].plots[27];sc.cameras.main.setZoom(2).centerOn((p.x-p.y)*64,(p.x+p.y+2)*32-35)})()`);
  await b.wait(250);
  await b.screenshot('/tmp/newgaza-aligned-ornamental-phone.png');
  await b.evaluate(`(()=>{const sc=window.__newgazaArtScene,p=sc.hooks.getState().districts[0].plots[27];
    p.status='building';p.startedAt=Date.now()-60000;p.endsAt=Date.now()+30000;sc.sync()})()`);
  assert.ok(await b.evaluate(`window.__newgazaArtScene.views.get(27).img.texture.key.startsWith('construction:ornamental_trees')`));
  assert.equal(b.errors.length,0);
  console.log(JSON.stringify({...report,construction:true,errors:0},null,2));
} finally {
  if(original!==undefined){
    await b.evaluate(`${original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`}`);
    await b.send('Page.reload');
  }
  b.close();
}
