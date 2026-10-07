import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b=await browser(1280,900),key='newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene`);
  original=await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{
    const now=Date.now(),s=(await import('/src/engine.ts')).newGame(now),d=s.districts[0];
    for(const district of s.districts)district.plots=district.plots.slice(0,64).map(({layoutId,...p})=>({...p,x:(p.id%8)*2,y:Math.floor(p.id/8)*2}));
    s.currentDistrict=d.id;s.sequence=3;s.coins=765432;
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',orientation:0,incomeAt:now});
    Object.assign(d.plots[1],{status:'building',buildingId:'small_house',startedAt:now-10000,endsAt:now+60000});
    const e=(await import('/src/catalog.ts')).EQUIPMENT;
    s.units=['excavator','bulldozer','truck'].map((kind,i)=>({id:'unit-'+(i+1),kind,level:1,purchasePrice:e[kind].cost}));
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__oldCityDocument=true;
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__oldCityDocument && !!document.querySelector('#boot.done') && window.__newgazaArtScene?.views.size===144`);
  const report=await b.evaluate(`(async()=>{
    const c=window.__newgazaArtScene,s=c.hooks.getState(),d=s.districts[0],l=(await import('/src/city-layout.ts')).cityLayout(d.id);
    const U=(await import('/src/art.ts')).U;
    c.cameras.main.setZoom(.32);c.cameras.main.centerOn(0,l.extent*U/2);
    return {plots:d.plots.length,views:c.views.size,blocks:l.blocks.length,
      groups:[...new Set(l.blocks.map(b=>b.plotIds.length))].sort(),extent:l.extent,
      coins:s.coins,oldBuilding:d.plots[1].status,terrain:c.terrainInfo};
  })()`);
  assert.equal(report.plots,144);assert.equal(report.views,144);
  assert.deepEqual(report.groups,[1,2,4]);assert.equal(report.coins,765432);
  assert.equal(report.oldBuilding,'building');assert.equal(report.terrain.water,false);
  await b.wait(1600);
  await b.screenshot('/tmp/newgaza-expanded-blocks-desktop.png');
  await b.evaluate(`window.__newgazaArtScene.hooks.onSelect(143)`);
  await b.click('[data-a="clear"]');
  assert.equal(await b.evaluate(`window.__newgazaArtScene.hooks.getState().jobs[0]?.plotId`),143,'edge plot supports real dispatch');
  await b.until(`window.__newgazaArtScene.fleetAnimation.actors.size===3`);
  assert.equal(await b.evaluate(`window.__newgazaArtScene.fleetAnimation.actors.size`),3,'the convoy renders in the expanded city');
  await b.click('[data-a="close-sheet"]');
  await b.evaluate(`window.__newgazaArtScene.hooks.onSelect(8)`);
  await b.click('[data-a="pickb"][data-id="small_house"]');
  await b.click('[data-a="place"]');
  assert.equal(await b.evaluate(`window.__newgazaArtScene.hooks.getState().districts[0].plots[8].status`),'building');
  const snapshot=await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene.hooks.getState();window.__oldCityDocument=true;
    return {coins:s.coins,job:s.jobs[0].id,ends:s.districts[0].plots[8].endsAt,x:s.districts[0].plots[143].x};
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__oldCityDocument && !!document.querySelector('#boot.done') && window.__newgazaArtScene?.views.size===144`);
  assert.deepEqual(await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene.hooks.getState();
    return {coins:s.coins,job:s.jobs[0].id,ends:s.districts[0].plots[8].endsAt,x:s.districts[0].plots[143].x};
  })()`),snapshot,'reload retains new layout, work, deadlines and money');
  await b.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:false});
  await b.evaluate(`window.__newgazaArtScene.hooks.onSelect(143)`);
  await b.wait(800);
  await b.screenshot('/tmp/newgaza-expanded-blocks-phone.png');
  assert.equal(await b.evaluate(`document.documentElement.scrollWidth<=innerWidth`),true);
  assert.equal(b.errors.length,0);
  console.log(JSON.stringify({report,dispatch:true,construction:true,reload:true,phone:true,errors:b.errors.length},null,2));
} finally {
  if(original!==undefined) {
    await b.evaluate(original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`);
    await b.send('Page.reload');
  }
  b.close();
}
