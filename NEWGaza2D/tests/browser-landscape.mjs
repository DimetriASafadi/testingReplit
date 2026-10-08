import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';

const b = await browser(844, 390), failures = [], reports = [];
const sizes = process.env.LANDSCAPE_SIZES ? JSON.parse(process.env.LANDSCAPE_SIZES) :
  [[568,320],[667,375],[844,390],[932,430],[1024,768],[1280,720],[390,844]];
const dialogs = process.env.LANDSCAPE_DIALOGS ? JSON.parse(process.env.LANDSCAPE_DIALOGS) :
  ['fleet','mats','projects','progress','save','audio'];
const resize = async (width,height) => {
  await b.send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
  await b.wait(400);
};
const check = async (selector, label) => {
  const bad = await b.evaluate(`(()=>{
    return [...document.querySelectorAll(${JSON.stringify(selector)})].filter(e=>e.getClientRects().length).flatMap(e=>{
      const r=e.getBoundingClientRect(), hit=document.elementFromPoint(r.x+r.width/2,r.y+r.height/2);
      let reason=r.top<0||r.left<0||r.right>innerWidth+1||r.bottom>innerHeight+1?'viewport':'';
      if(!e.contains(hit))reason+=' covered';
      for(let p=e.parentElement;p;p=p.parentElement){
        const pr=p.getBoundingClientRect(),css=getComputedStyle(p);
        if(/hidden|auto|scroll|clip/.test(css.overflowY)&&(r.top<pr.top-1||r.bottom>pr.bottom+1))reason+=' clipped';
      }
      return reason?[{text:e.textContent.trim().slice(0,60),reason,rect:{x:r.x,y:r.y,w:r.width,h:r.height}}]:[];
    });
  })()`);
  if (bad.length) failures.push({label,bad});
};
const camera = () => b.evaluate(`(()=>{const c=window.__newgazaArtScene.cameras.main;return {x:c.midPoint.x,y:c.midPoint.y,z:c.zoom}})()`);
try {
  await b.send('Emulation.setTouchEmulationEnabled',{enabled:true,maxTouchPoints:3});
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until("document.querySelector('#boot.done')");
  for(const [w,h] of sizes) {
    await resize(w,h);
    await check('[data-a="enter"]',`${w}x${h} map entry`);
  }
  await b.evaluate(`(async()=>{
    const s=(await import('/src/engine.ts')).newGame(Date.now()),d=s.districts[0];
    s.currentDistrict=d.id;d.camera={x:0,y:1000,zoom:1};
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',incomeAt:s.lastSeen});
    d.plots[27].status='empty';
    localStorage.setItem('newgaza2d-save-v1',JSON.stringify(s));window.__landscapeSeed=true;
  })()`);
  await b.send('Page.reload');
  await b.until("!window.__landscapeSeed && document.querySelector('#boot.done') && window.__newgazaArtScene?.worldReady");
  for(const [w,h] of sizes) {
    const before=await camera();
    await resize(w,h);
    const after=await camera();
    if(Math.hypot(after.x-before.x,after.y-before.y)>.1||after.z!==before.z)
      failures.push({label:`${w}x${h} preserves camera`,before,after});
    await check('.dock button,.zoom button,.sound-toggle,.hud-top button',`${w}x${h} HUD`);
    await b.evaluate('window.__newgazaArtScene.hooks.onSelect(27)');
    await b.wait(550);
    await check('[data-a="cat"],[data-a="pickb"],[data-a="catalog-page"],[data-a="close-sheet"]',`${w}x${h} catalog`);
    await b.click('[data-a="pickb"]');
    await check('[data-a="rot"],[data-a="place"],[data-a="close-sheet"]',`${w}x${h} building actions`);
    await check('.zoom button,.sound-toggle',`${w}x${h} auxiliary controls with sheet`);
    if(w===844)await b.screenshot('/tmp/newgaza-landscape-build.png');
    // A rotation with a selected building must not dismiss the sheet.
    await resize(h,w);
    assert.ok(await b.evaluate('!!document.querySelector(\'[data-a="place"]\')'));
    await resize(w,h);
    assert.ok(await b.evaluate('!!document.querySelector(\'[data-a="place"]\')'));
    await b.click('[data-a="close-sheet"]');
    for(const id of dialogs) {
      await b.evaluate(`document.querySelector('[data-a="dlg"][data-id="${id}"]').click()`);
      await b.wait(350);
      await check('dialog[open] [data-a="close-dlg"],dialog[open] .window-tabs button,dialog[open] .dlg-footer button',`${w}x${h} ${id}`);
      if(id==='fleet')await check('dialog[open] [data-a="buy"]',`${w}x${h} visible purchase`);
      const ref=await b.evaluate('window.__landscapeDialog=document.querySelector("dialog[open]");true');
      await resize(h,w);await resize(w,h);
      assert.ok(ref && await b.evaluate('window.__landscapeDialog===document.querySelector("dialog[open]")'));
      if(w===844 && id==='fleet')await b.screenshot('/tmp/newgaza-landscape-fleet.png');
      await b.click('[data-a="close-dlg"]');
    }
    if(w===844 && h===390) {
      const beforeDrag = await camera();
      await b.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:250,y:240,id:1}]});
      await b.wait(100);
      for(const x of [280,310,340]) {
        await b.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x,y:250,id:1}]});
        await b.wait(80);
      }
      await b.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
      await b.wait(150);
      const afterDrag = await camera();
      assert.equal(afterDrag.z,beforeDrag.z,'one finger must not become pinch zoom after rotation');
      assert.ok(Math.hypot(afterDrag.x-beforeDrag.x,afterDrag.y-beforeDrag.y)>30);
    }
    reports.push({width:w,height:h});
  }
  assert.equal(b.errors.length,0,JSON.stringify(b.errors));
  console.log(JSON.stringify({reports,failures},null,2));
  assert.equal(failures.length,0,'landscape controls must remain visible and reachable');
} finally { b.close(); }
