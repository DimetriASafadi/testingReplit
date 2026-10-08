// Isolated browser profile only. Does not alter any player's save.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(390, 844);
try {
  await b.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 3 });
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until("document.querySelector('#boot.done')");
  await b.evaluate(`(async()=>{
    const E=await import('/src/engine.ts'), C=await import('/src/catalog.ts'), R=await import('/src/realart.ts');
    const s=E.newGame();s.currentDistrict='shujaiya';s.coins=99000000;
    const d=s.districts.find(d=>d.id==='shujaiya');
    C.BUILDINGS.forEach((def,i)=>{
      const p=d.plots[i],o=[0,2,1,3][i%4];
      Object.assign(p,{status:'built',buildingId:def.id,orientation:R.SPR[R.builtKey(def.id,o)]?o:0,incomeAt:s.lastSeen});
    });
    Object.assign(d.plots[42],{status:'empty',orientation:0});
    localStorage.setItem(E.SAVE_KEY,JSON.stringify(s));
  })()`);
  await b.send('Page.reload');
  await b.until("window.__newgazaArtScene?.worldReady");
  const inventory = await b.evaluate(`(async()=>{
    const C=await import('/src/catalog.ts'),R=await import('/src/realart.ts');
    return C.BUILDINGS.map(d=>({id:d.id,keys:[0,2,1,3].map(o=>R.builtKey(d.id,o)),
      full:[0,2,1,3].every(o=>!!R.SPR[R.builtKey(d.id,o)])}));
  })()`);
  assert.equal(inventory.filter(x => x.full).length, 36);
  const house = inventory.find(x => x.keys[0] === 'smallhouse_front');
  await b.evaluate("window.__newgazaArtScene.hooks.onSelect(42)");
  await b.click('[data-a="cat"][data-id="housing"]');
  await b.click(`[data-a="pickb"][data-id="${house.id}"]`);
  const pose = () => b.evaluate("document.querySelector('.ori')?.textContent");
  assert.equal(await pose(), 'أمام');
  for (const label of ['يمين', 'خلف', 'يسار', 'أمام']) {
    await b.click('[data-a="rot"]'); assert.equal(await pose(), label);
  }
  await b.click('[data-a="rot"]'); // build the right face
  await b.click('[data-a="place"]');
  await b.evaluate(`(()=>{
    const sc=window.__newgazaArtScene,s=sc.hooks.getState(),p=s.districts.find(d=>d.id===s.currentDistrict).plots[42];
    p.startedAt=Date.now()-50000;p.endsAt=Date.now()+70000;
    const v=sc.views.get(p.id);sc.cameras.main.setZoom(1.8).centerOn(v.img.x,v.img.y-30);
  })()`);
  await b.wait(1200);
  const snapshot = () => b.evaluate(`(()=>{
    const sc=window.__newgazaArtScene,s=sc.hooks.getState(),p=s.districts.find(d=>d.id===s.currentDistrict).plots[42],v=sc.views.get(42);
    return {o:p.orientation,start:p.startedAt,end:p.endsAt,coins:s.coins,width:v.img.displayWidth,texture:v.img.texture.key};
  })()`);
  const start = await snapshot();
  for (const o of [1, 3, 0, 2]) {
    await b.click('[data-a="rotate-building"]');
    const s = await snapshot();
    assert.equal(s.o, o); assert.equal(s.start, start.start); assert.equal(s.end, start.end);
    assert.equal(s.coins, start.coins);
    assert.ok(await b.evaluate("!!document.querySelector('.sheet')"), 'dialog remains open');
  }
  await b.screenshot('/tmp/four-faces-construction.png');
  await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene.hooks.getState();s.districts.find(d=>d.id===s.currentDistrict).plots[42].endsAt=Date.now()-1;
  })()`);
  await b.until(`window.__newgazaArtScene.hooks.getState().districts.find(d=>d.id==='shujaiya').plots[42].status==='built'`);
  const widths = [];
  for (const o of [1, 3, 0, 2]) {
    await b.click('[data-a="rotate-building"]'); const s = await snapshot();
    assert.equal(s.o, o); assert.ok(s.texture.endsWith(house.keys[[0,2,1,3].indexOf(o)]), s.texture);
    widths.push(s.width);
  }
  assert.ok(Math.max(...widths) - Math.min(...widths) < .001, 'same physical size');
  await b.send('Emulation.setDeviceMetricsOverride', { width: 844, height: 390, deviceScaleFactor: 1, mobile: true });
  await b.wait(1000);
  assert.ok(await b.evaluate(`(()=>{
    const r=document.querySelector('[data-a="rotate-building"]').getBoundingClientRect();
    return r.top>=0&&r.bottom<=innerHeight&&r.left>=0&&r.right<=innerWidth;
  })()`), 'landscape action visible');
  await b.screenshot('/tmp/four-faces-landscape.png');
  await b.send('Page.reload');
  await b.until("window.__newgazaArtScene?.worldReady");
  assert.equal((await snapshot()).o, 2);
  const trad = inventory.find(x => x.keys[0] === 'trad_front');
  await b.evaluate(`(()=>{
    const sc=window.__newgazaArtScene,s=sc.hooks.getState(),p=s.districts.find(d=>d.id===s.currentDistrict).plots.find(p=>p.buildingId==='${trad.id}');
    p.orientation=0;sc.hooks.onSelect(p.id);
  })()`);
  assert.ok(await b.evaluate("document.querySelector('.sheet').textContent.includes('لم تُعتمد بعد')"));
  await b.click('[data-a="rotate-building"]');
  assert.equal(await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene.hooks.getState();return s.districts.find(d=>d.id===s.currentDistrict).plots.find(p=>p.buildingId==='${trad.id}').orientation;
  })()`), 1);
  assert.deepEqual(b.errors, []);
  console.log('PASS: 36 true quartets; phone previews; construction/built rotation; constant size; timers/costs; saved right pose; landscape controls; explicit two-face pending art.');
} finally { b.close(); }
