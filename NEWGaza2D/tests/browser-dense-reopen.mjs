// Disposable WebGL browser only. Exercises a dense saved district after fresh-document reloads.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';

const b = await browser(390, 844), key = 'newgaza2d-save-v1';
const testUrl = process.env.NEWGAZA_TEST_URL ?? 'http://localhost:80/';
let original, scriptId;
const reloadDocument = async () => {
  const origin = await b.evaluate('performance.timeOrigin');
  await b.send('Page.reload');
  await b.until(`performance.timeOrigin > ${origin} && !!document.querySelector('#boot.done')`);
};
const waitForWorld = async (timeout = 210_000) => {
  const start = Date.now();
  while (Date.now() - start < timeout) {
    const report = await b.evaluate(`(()=>{
      const scene=window.__newgazaArtScene;
      const status=document.querySelector('#city-status');
      return {ready:!!scene?.worldReady && !!status?.hidden,renderer:scene?.game?.renderer?.type,
        plots:scene?.views?.size,groundStatus:status?.textContent??''};
    })()`);
    if (report.ready) return { ...report, elapsedMs: Date.now() - start };
    await b.wait(100);
  }
  throw new Error(`Dense district failed to load within ${timeout}ms: ${JSON.stringify(await b.evaluate(`({
    status:document.querySelector('#city-status')?.textContent,
    hidden:document.querySelector('#city-status')?.hidden,
    worldReady:window.__newgazaArtScene?.worldReady,
    renderer:window.__newgazaArtScene?.game?.renderer?.type
  })`))}`);
};

try {
  await b.send('Page.addScriptToEvaluateOnNewDocument', { source: `
    window.__denseGroundProgress=[];
    window.__denseGroundTimer=setInterval(()=>{
      const text=document.querySelector('#city-status')?.textContent?.trim()||'';
      if(/تجهيز أرض الحي/.test(text) && window.__denseGroundProgress[window.__denseGroundProgress.length-1]!==text)
        window.__denseGroundProgress.push(text);
    },25);
  ` }).then(result => { scriptId = result.identifier; });
  await b.send('Page.navigate', { url: testUrl });
  await b.until(`!!document.querySelector('#boot.done')`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);

  const fixture = await b.evaluate(`(async()=>{
    const {newGame}=await import('/src/engine.ts');
    const {BUILDINGS}=await import('/src/catalog.ts');
    const {roadsFor}=await import('/src/roads.ts');
    const {validateSave}=await import('/src/save.ts');
    const now=Date.now(),save=newGame(now),district=save.districts[0];
    save.currentDistrict=district.id;
    save.coins=500000;
    district.unlocked=true;district.difficulty='medium';district.difficultyChosen=true;
    district.claimed=false;district.celebrated=false;
    const types=['small_house','food_shop','cafe','mosque'];
    if(!types.every(id=>BUILDINGS.some(b=>b.id===id)))throw new Error('Dense reload fixture building type is missing');
    for(const [i,p] of district.plots.entries()){
      Object.assign(p,{status:i<140?'built':'rubble',buildingId:types[i%types.length],
        orientation:i%4,startedAt:0,endsAt:0,incomeAt:now});
    }
    for(const p of district.projects)Object.assign(p,{status:'complete',startedAt:0,endsAt:0,incomeAt:0,gemRewarded:true});
    district.pavedRoads=roadsFor(district.id).slice(0,Math.ceil(roadsFor(district.id).length*.7)).map(r=>r.id);
    validateSave(save);
    localStorage.setItem('${key}',JSON.stringify(save));
    return {district:district.id,built:district.plots.filter(p=>p.status==='built').length,
      total:district.plots.length,buildings:types.length,roads:district.pavedRoads.length};
  })()`);
  assert.equal(fixture.built, 140);

  const runs=[];
  for(let attempt=1;attempt<=1;attempt++){
    await reloadDocument();
    const report=await waitForWorld();
    assert.equal(report.renderer,2,'Must exercise WebGL, not the Canvas fallback');
    assert.equal(report.plots,144,'All plots must be restored');
    const progress=await b.evaluate('window.__denseGroundProgress');
    assert(progress.length>=2,`Ground progress must advance, got ${JSON.stringify(progress)}`);
    const scene=await b.evaluate(`(()=>{
      const s=window.__newgazaArtScene;
      const tiles=s.groundTextures.map(k=>{const c=s.textures.get(k).getSourceImage();return [c.width,c.height]});
      return {tiles,frame:s.frame,progress:window.__denseGroundProgress};
    })()`);
    assert(scene.tiles.length>0);
    assert(scene.tiles.every(([w,h])=>w<=2048&&h<=2048),'Ground tiles must stay within mobile texture limits');
    assert.equal(await b.evaluate(`JSON.parse(localStorage.getItem('${key}')).coins`),500000,'Reload must not reset saved currency');
    runs.push({attempt,elapsedMs:report.elapsedMs,renderer:report.renderer,plots:report.plots,progress:scene.progress});
    if(attempt===1)await b.screenshot('/tmp/newgaza-dense-reopen.png');
  }
  assert.equal(b.errors.length,0,JSON.stringify(b.errors));
  console.log('PASS: 93% saved city, WebGL terrain preparation progress, all 144 plots, and a fresh-document reload.',{fixture,runs});
} finally {
  try { await b.evaluate('clearInterval(window.__denseGroundTimer)'); } catch {}
  if(scriptId)await b.send('Page.removeScriptToEvaluateOnNewDocument',{identifier:scriptId});
  if(original!==undefined)await b.evaluate(original===null
    ?`localStorage.removeItem('${key}');void 0`
    :`localStorage.setItem('${key}',${JSON.stringify(original)});void 0`);
  try { await b.send('Page.navigate',{url:'about:blank'}); } catch {}
  b.close();
}
