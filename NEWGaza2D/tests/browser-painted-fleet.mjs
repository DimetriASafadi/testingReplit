// Inspect real Phaser artwork, with a disposable save isolated from the user's HTTPS origin.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1000,850), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{
    const now=Date.now(),s=(await import('/src/engine.ts')).newGame(now),d=s.districts[0];
    s.currentDistrict=d.id;s.sequence=4;
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',orientation:0,incomeAt:now});
    const catalog=(await import('/src/catalog.ts')).EQUIPMENT;
    s.units=['excavator','bulldozer','truck'].map((kind,i)=>({id:'unit-'+(i+1),kind,level:1,purchasePrice:catalog[kind].cost}));
    s.jobs=[{id:'job-4',districtId:d.id,plotId:27,unitIds:s.units.map(u=>u.id),start:now,arrival:now+60000,workEnd:now+120000,returnEnd:now+180000,cleared:false,value:30000,originPlotId:0}];
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__paintedOld=true;
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__paintedOld && !!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.fleetAnimation`);
  if(await b.evaluate(`!!document.querySelector('[data-a="hide-obj"]')`))await b.click('[data-a="hide-obj"]');
  await b.evaluate(`(()=>{
    const c=window.__newgazaArtScene;c.update=()=>{};c.sys.sceneUpdate=()=>{};c.sync=()=>{};c.cameras.main.setZoom(2.4);c.focusPlot(27);
  })()`);
  const report=[];
  for(const [stage,seconds] of [[0,3],[1,12],[2,28],[3,42],[4,55]]) {
    const r=await b.evaluate(`(()=>{
      const c=window.__newgazaArtScene,s=c.hooks.getState(),j=s.jobs[0],t=j.arrival+${seconds}*1000;
      c.syncClearing(s,t,true);c.fleetAnimation.update(s,t,3);
      const v=c.views.get(27);
      return{stage:c.clearingStages.get(27),key:v.key,texture:v.img.texture.key,
        anchor:[v.img.x,v.img.y],textureCount:c.textures.getTextureKeys().length,
        actors:[...c.fleetAnimation.actors].map(([id,a])=>({id,alpha:a.a.alpha,frame:a.a.frame.name,tip:a.tip,position:a.pos}))};
    })()`);
    assert.equal(r.stage,stage);assert.ok(r.actors.every(a=>a.alpha===1));
    report.push(r);
    await b.wait(120);await b.screenshot(`/tmp/newgaza-painted-stage-${stage}.png`);
  }
  assert.ok(report.slice(1).every(r=>r.texture.startsWith('clearing:')));
  assert.ok(report.every(r=>JSON.stringify(r.anchor)===JSON.stringify(report[0].anchor)));
  const budget=await b.evaluate(`(()=>{
    const c=window.__newgazaArtScene,s=c.hooks.getState(),t=s.jobs[0].arrival+55000;
    const textures=c.textures.getTextureKeys().length;
    for(let i=0;i<180;i++){c.syncClearing(s,t+i*10,true);c.fleetAnimation.update(s,t+i*10,3);}
    return{before:textures,after:c.textures.getTextureKeys().length};
  })()`);
  assert.equal(budget.before,budget.after,'no new textures allocated per work frame');
  for(const [name,phase] of [['dig',.13],['lift',.34],['dump',.76],['recover',.90]]) {
    await b.evaluate(`(()=>{
      const c=window.__newgazaArtScene,s=c.hooks.getState(),t=s.jobs[0].arrival+900+${phase}*7200;
      c.fleetAnimation.update(s,t,3);
    })()`);
    await b.wait(100);await b.screenshot(`/tmp/newgaza-painted-${name}.png`);
  }
  const travel = await b.evaluate(`(() => {
    const c=window.__newgazaArtScene,s=c.hooks.getState(),j=s.jobs[0];
    c.fleetAnimation.resetDistrict();
    const headings=new Set(); let street=0, portal=0, visible=0;
    for (const [start,end] of [[j.start,j.arrival],[j.workEnd,j.returnEnd]]) {
      for(let t=start;t<end;t+=100) {
        c.fleetAnimation.update(s,t,3);
        for(const a of c.fleetAnimation.actors.values()) if(a.a.visible) {
          visible++;
          if(a.a.alpha!==1)throw new Error('transparent moving body');
          if(a.a.mask)portal++;else street++;
          headings.add(a.a.frame.name);
          if(a.a.depth < -1000)throw new Error('vehicle below the road surface');
        }
      }
    }
    return {visible,street,portal,frames:headings.size};
  })()`);
  assert.ok(travel.visible>500 && travel.street>0 && travel.portal>0 && travel.frames>12);
  await b.evaluate(`(()=>{
    const c=window.__newgazaArtScene,s=c.hooks.getState();
    c.fleetAnimation.resetDistrict();
    for(let i=0;i<20;i++)c.fleetAnimation.update(s,s.jobs[0].arrival+2000+i*100,3);
  })()`);
  await b.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await b.wait(300);await b.evaluate(`window.__newgazaArtScene.focusPlot(27)`);await b.wait(650);await b.screenshot('/tmp/newgaza-painted-phone.png');
  assert.equal(b.errors.length,0,JSON.stringify(b.errors));
  console.log(JSON.stringify({report,budget,travel,errors:b.errors.length},null,2));
}finally {
  if(original!==undefined){
    await b.evaluate(original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`);
    await b.send('Page.reload');
  }
  b.close();
}
