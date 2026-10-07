import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';

const b = await browser(390, 844), key = 'newgaza2d-save-v1';
let original;
const selectPlot = async id => {
  await b.evaluate(`void window.__newgazaArtScene.hooks.onSelect(${id})`);
  await b.until(`!!document.querySelector('#sheet .sheet')`);
};
const overflow = () => b.evaluate(`(()=>{const els=[document.documentElement,...document.querySelectorAll('#sheet .sheet,#sheet .sheet-body,dialog[open] .dlg-body')];return els.map(e=>({name:e.className||e.tagName,client:e.clientWidth,scroll:e.scrollWidth})).filter(e=>e.scroll>e.client+2)})()`);
const count = () => b.evaluate(`(()=>{
  for(let page=0;page<50;page++){const prev=document.querySelector('#sheet [data-k="catalog-page-prev"]:not(:disabled)');if(!prev)break;prev.click();}
  const ids=new Set();
  for(let page=0;page<50;page++){
    document.querySelectorAll('#sheet [data-a="pickb"]').forEach(e=>ids.add(e.dataset.id));
    for(const e of document.querySelectorAll('#sheet [data-a="pickb"],#sheet [data-a="cat"],#sheet .page-controls button')){
      const r=e.getBoundingClientRect(),hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);
      if(r.top<0||r.bottom>innerHeight||!e.contains(hit))throw new Error('Catalog option is hidden: '+e.textContent);
      for(let parent=e.parentElement;parent;parent=parent.parentElement){
        const pr=parent.getBoundingClientRect();
        if(/hidden|auto|scroll|clip/.test(getComputedStyle(parent).overflowY) && (r.top<pr.top-1||r.bottom>pr.bottom+1))throw new Error('Catalog option is clipped: '+e.textContent);
      }
    }
    const next=document.querySelector('#sheet [data-a="catalog-page"][data-k="catalog-page-next"]:not(:disabled)');
    if(!next)break;next.click();
  }
  for(let page=0;page<50;page++){const prev=document.querySelector('#sheet [data-k="catalog-page-prev"]:not(:disabled)');if(!prev)break;prev.click();}
  return ids.size;
})()`);
const reveal = selector => b.evaluate(`(()=>{
  for(let page=0;page<50;page++){
    if(document.querySelector(${JSON.stringify(selector)}))return true;
    const next=document.querySelector('[data-k="catalog-page-next"]:not(:disabled),dialog[open] [data-k="dialog-page-next"]:not(:disabled)');
    if(!next)return false;next.click();
  }return false;
})()`);
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{const n=Date.now(),s=(await import('/src/engine.ts')).newGame(n),d=s.districts[0];
    s.currentDistrict=d.id;d.camera={x:0,y:435,zoom:1.3};
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',orientation:0,incomeAt:n});
    d.plots[27].status='empty';
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__mobileOld=true})()`);
  await b.send('Page.reload'); await b.until(`!window.__mobileOld`);
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.hooks`);
  const defs = await b.evaluate(`(async()=> (await import('/src/catalog.ts')).BUILDINGS.map(x=>({id:x.id,category:x.category,cost:x.cost})))()`);
  await selectPlot(27);
  assert.equal(await count(), defs.length, 'All buildings discoverable by default');
  for (const category of [...new Set(defs.map(x => x.category))]) {
    await b.click(`[data-a="cat"][data-id="${category}"]`);
    assert.equal(await count(), defs.filter(x => x.category === category).length, category);
    assert.deepEqual(await overflow(), [], `${category}: no horizontal clipping`);
  }
  await b.click('[data-a="cat"][data-id="all"]');
  await reveal(`[data-a="pickb"][data-id="${defs.at(-1).id}"]`);
  await b.click(`[data-a="pickb"][data-id="${defs.at(-1).id}"]`);
  assert.ok(await b.evaluate(`!!document.querySelector('[data-a="place"]')`), 'Last catalog entry can be selected');
  await selectPlot(27);
  const reports = [];
  for (const [width, height] of [[390,844],[320,568],[844,390]]) {
    await b.send('Emulation.setDeviceMetricsOverride', {width,height,deviceScaleFactor:1,mobile:false});
    await selectPlot(27); await b.click('[data-a="cat"][data-id="all"]');
    await b.wait(350);
    assert.equal(await count(), defs.length);
    assert.deepEqual(await overflow(), [], `${width}x${height}: no clipping`);
    await reveal('[data-a="pickb"][data-id="small_house"]');
    await b.click('[data-a="pickb"][data-id="small_house"]');
    const rect = await b.evaluate(`(()=>{const e=document.querySelector('[data-a="place"]'),r=e.getBoundingClientRect();return{x:r.x,y:r.y,bottom:r.bottom,width:r.width,height:r.height}})()`);
    assert.ok(rect.y >= 0 && rect.bottom <= height && rect.x >= 0 && rect.x + rect.width <= width, `Build action remains on-screen at ${width}x${height}: ${JSON.stringify(rect)}`);
    assert.equal(await b.evaluate(`(()=>{const e=document.querySelector('[data-a="place"]'),r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2))})()`), true, 'Build button is not clipped or covered');
    assert.ok(rect.height >= 40, 'Touch-friendly build action');
    await b.screenshot(`/tmp/newgaza-build-${width}x${height}.png`);
    reports.push({width,height,build:rect});
    await b.click('[data-a="back-catalog"]');
    assert.equal(await b.evaluate(`!!document.querySelector('.has-pick')`), false);
    assert.equal(await count(), defs.length);
    for (const id of ['fleet','mats','projects','progress','save']) {
      await b.click(`[data-a="dlg"][data-id="${id}"]`);
      assert.deepEqual(await overflow(), [], `${id} remains usable at ${width}x${height}`);
      assert.deepEqual(await b.evaluate(`Array.from(document.querySelectorAll('dialog[open] button[data-a]')).filter(e=>{
        const r=e.getBoundingClientRect();return r.top<0||r.bottom>innerHeight||!e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2))
      }).map(e=>e.textContent)`), [], `${id}: all displayed actions visible without scrolling`);
      await b.click('[data-a="close-dlg"]');
      await b.until(`!document.querySelector('dialog[open]')`);
    }
  }
  await reveal('[data-a="pickb"][data-id="small_house"]');
  await b.click('[data-a="pickb"][data-id="small_house"]');
  await b.click('[data-a="rot"]');
  const before = await b.evaluate(`window.__newgazaArtScene.hooks.getState().coins`);
  await b.click('[data-a="place"]');
  const built = await b.evaluate(`(()=>{const s=window.__newgazaArtScene.hooks.getState(),p=s.districts[0].plots[27];return{coins:s.coins,id:p.buildingId,status:p.status,orientation:p.orientation}})()`);
  assert.deepEqual(built, {coins:before-2500,id:'small_house',status:'building',orientation:1});
  await b.until(`document.querySelector('#sheet').textContent.includes('قيد الإنشاء')`);
  const saved = await b.evaluate(`JSON.parse(localStorage.getItem('${key}')).districts[0].plots[27]`);
  assert.equal(saved.buildingId, 'small_house');
  assert.equal(saved.orientation, 1);
  await b.evaluate(`(()=>{window.__catalogPanel=document.querySelector('.sheet');const e=document.querySelector('.sheet-body');e.scrollTop=e.scrollHeight})()`);
  const scroll = await b.evaluate(`document.querySelector('.sheet-body').scrollTop`);
  await b.wait(1600);
  assert.ok(Math.abs(await b.evaluate(`document.querySelector('.sheet-body').scrollTop`) - scroll) <= 2, 'Live construction updates preserve scroll');
  assert.equal(await b.evaluate(`__catalogPanel===document.querySelector('.sheet') && __catalogPanel.getAnimations().every(a=>a.playState!=='running')`),true,'Live progress preserves the panel and does not replay its opening animation');
  await b.send('Emulation.setDeviceMetricsOverride', {width:320,height:568,deviceScaleFactor:1,mobile:false});
  await b.click('[data-a="dlg"][data-id="fleet"]');
  for (const kind of ['excavator','bulldozer','truck']) {
    await reveal(`[data-a="buy"][data-id="${kind}"]`);
    await b.click(`[data-a="buy"][data-id="${kind}"]`);
    assert.deepEqual(await overflow(), [], 'Purchased equipment controls fit a small phone');
  }
  await b.click('[data-a="close-dlg"]');
  await selectPlot(28);
  assert.equal(await b.evaluate(`(()=>{const e=document.querySelector('[data-a="clear"]'),r=e.getBoundingClientRect();return e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2))})()`),true,'Notifications do not intercept the clear action');
  await b.click('[data-a="clear"]');
  assert.equal(await b.evaluate(`window.__newgazaArtScene.hooks.getState().jobs.length`),1);
  assert.equal(await b.evaluate(`window.__newgazaArtScene.hooks.getState().jobs[0].unitIds.length`),3);
  assert.ok(await b.evaluate(`document.querySelector('#sheet').textContent.includes('متبق')`), 'Current clearing phase and countdown are visible');
  await b.evaluate(`document.querySelector('.sheet-body').scrollTop=45`);
  const activeScroll = await b.evaluate(`document.querySelector('.sheet-body').scrollTop`);
  await b.wait(1700);
  assert.ok(Math.abs(await b.evaluate(`document.querySelector('.sheet-body').scrollTop`)-activeScroll)<=2, 'Live machinery countdown preserves scroll');
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  console.log('All catalog entries, six categories, last entry selection, phone/landscape actions, real rotated build/save, live scroll and all menus:', {count:defs.length,reports});
} finally {
  if (original !== undefined) await b.evaluate(original == null ? `localStorage.removeItem('${key}')` : `localStorage.setItem('${key}',${JSON.stringify(original)})`);
  b.close();
}
