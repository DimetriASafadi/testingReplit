// Disposable browser fixture. Compare idle/travel/work hot paths without changing real saves.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(390,844), key='newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`!!document.querySelector('#boot.done')`);
  original=await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{const n=Date.now(),s=(await import('/src/engine.ts')).newGame(n);
    s.currentDistrict=s.districts[0].id;const d=s.districts[0];
    d.camera={x:0,y:448,zoom:1.2};
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',incomeAt:n});
    const equipment=(await import('/src/catalog.ts')).EQUIPMENT;
    s.sequence=4;s.units=['excavator','bulldozer','truck'].map((kind,i)=>({id:'unit-'+(i+1),kind,level:1,purchasePrice:equipment[kind].cost}));
    s.jobs=[];(await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__perfOld=true})()`);
  await b.send('Page.reload');await b.until(`!window.__perfOld`);
  await b.until(`!!window.__newgazaArtScene?.fleetAnimation?.doors?.views?.size`);
  await b.send('Emulation.setCPUThrottlingRate',{rate:4});
  await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene;window.__perfStats={};
    const wrap=(o,k,name)=>{const f=o[k];o[k]=function(...a){const t=performance.now();
      try{return f.apply(this,a)}finally{const d=performance.now()-t,r=window.__perfStats;
        if(r.enabled){const q=r[name]??(r[name]={calls:0,total:0,max:0});q.calls++;q.total+=d;q.max=Math.max(q.max,d)}}}};
    wrap(s,'revealWorkingFleet','occlusion');wrap(s.fleetAnimation,'update','fleet');
    wrap(s.textures,'getPixelAlpha','pixelReads');wrap(s.sys.displayList,'depthSort','sort');
    wrap(s.game.renderer,'render','render');
    let last=0;s.events.on('postupdate',()=>{const n=performance.now(),r=window.__perfStats;
      if(r.enabled&&last)r.frames.push(n-last);last=n});
  })()`);
  const reports={};
  for(const phase of ['idle','travel','work']){
    await b.evaluate(`(()=>{
      const s=window.__newgazaArtScene,st=s.hooks.getState(),d=st.districts[0],n=Date.now();
      st.jobs=${phase==='idle'?'[]':`[{id:'job-4',districtId:d.id,plotId:27,originPlotId:0,
        unitIds:st.units.map(u=>u.id),value:1000,cleared:false,
        start:n-${phase==='travel'?60000:90000},arrival:n+${phase==='travel'?20000:-30000},
        workEnd:n+90000,returnEnd:n+180000}]`};
      s.sync();window.__perfStats={enabled:false};
    })()`);
    await b.wait(1000);
    await b.evaluate(`window.__perfStats={enabled:true,frames:[]}`);
    await b.wait(6000);
    reports[phase]=await b.evaluate(`(()=>{
      const r=window.__perfStats;r.enabled=false;const a=r.frames.sort((a,b)=>a-b);
      delete r.frames;return {...r,frames:a.length,p50:a[Math.floor(a.length*.5)],
        p95:a[Math.floor(a.length*.95)],over50:a.filter(x=>x>50).length,
        renderer:window.__newgazaArtScene.game.renderer.type,
        actors:window.__newgazaArtScene.fleetAnimation.actors.size};
    })()`);
  }
  assert.equal(reports.work.actors,3);
  if (await b.evaluate(`!!window.__newgazaArtScene.spriteOpacity`)) {
    assert.equal(reports.work.pixelReads?.calls ?? 0,0,'No canvas pixel readbacks while working');
    assert.equal(reports.travel.pixelReads?.calls ?? 0,0,'No canvas pixel readbacks while travelling');
  }
  assert.equal(b.errors.length,0,JSON.stringify(b.errors));
  console.log(JSON.stringify(reports,null,2));
  await b.screenshot('/tmp/newgaza-fleet-performance.png');
}finally{
  await b.send('Emulation.setCPUThrottlingRate',{rate:1});
  if(original!=null)await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(original)})`);
  b.close();
}
