import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1280, 840), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{const now=Date.now(),s=(await import('/src/engine.ts')).newGame(now);
    const d=s.districts[0];s.currentDistrict=d.id;d.camera={x:0,y:435,zoom:1.9};
    for(const p of d.plots)p.status='empty';
    for(const [id,bid,o]of [[26,'small_house',0],[27,'medium_house',0],[28,'housing_4',0],
      [34,'small_house',1],[35,'villa',0],[36,'housing_6',0]])Object.assign(d.plots[id],
      {status:'built',buildingId:bid,orientation:o,incomeAt:now});
    for(const[id,progress,o]of [[18,.1,0],[19,.85,1]])Object.assign(d.plots[id],
      {status:'building',buildingId:'small_house',orientation:o,startedAt:now-progress*600000,
      endsAt:now+(1-progress)*600000,incomeAt:0});
    (await import('/src/save.ts')).validateSave(s);localStorage.setItem('${key}',JSON.stringify(s));
    window.__footprintOldPage=true})()`);
  await b.send('Page.reload'); await b.until(`!window.__footprintOldPage`);
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.views?.get(26)?.yard`);
  const report = await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene,view=id=>{const v=s.views.get(id);return {
      scale:v.img.scaleX,width:v.img.displayWidth,yard:v.yard?.texture.key,
      yardWidth:v.yard?.displayWidth,yardScale:v.yard?.scaleX,front:v.yardFront?.texture.key,
      shadow:v.shadow?.scaleX,origin:v.img.originY}};
    return {small:view(26),medium:view(27),apartment:view(28),back:view(34),villa:view(35),
      large:view(36),foundation:view(18),finishing:view(19)};
  })()`);
  assert.ok(report.small.width < report.apartment.width * .85, JSON.stringify(report));
  assert.equal(report.small.scale, .72 * .62);
  assert.equal(report.back.scale, report.small.scale);
  assert.equal(report.foundation.scale, report.small.scale);
  assert.equal(report.finishing.scale, report.small.scale);
  for (const id of ['small','medium','back','villa','foundation','finishing']) assert.ok(report[id].yard, id);
  assert.equal(report.apartment.yard, undefined);
  assert.equal(report.large.yard, undefined);
  assert.equal(report.small.yardScale, .72);
  assert.equal(report.small.shadow, 1.1 * .62);
  assert.notEqual(report.small.yard, report.back.yard);
  await b.wait(200); await b.screenshot('/tmp/newgaza-courtyards.png');
  await b.evaluate(`(()=>{const s=window.__newgazaArtScene;
    const p=s.hooks.getState().districts[0].plots[26];p.orientation=1;s.sync()})()`);
  const rotated = await b.evaluate(`({scale:window.__newgazaArtScene.views.get(26).img.scaleX,key:window.__newgazaArtScene.views.get(26).yard.texture.key})`);
  assert.equal(rotated.scale, report.small.scale);
  assert.notEqual(rotated.key, report.small.yard);
  await b.screenshot('/tmp/newgaza-courtyards-rotated.png');
  await b.evaluate(`(()=>{const s=window.__newgazaArtScene;s.hooks.getState().districts[0].plots[26].status='empty';s.sync()})()`);
  assert.equal(await b.evaluate(`!!window.__newgazaArtScene.views.get(26).yard || !!window.__newgazaArtScene.views.get(26).yardFront || !!window.__newgazaArtScene.views.get(26).shadow`), false);
  await b.send('Emulation.setDeviceMetricsOverride', { width:390,height:844,deviceScaleFactor:1,mobile:true });
  await b.evaluate(`void window.__newgazaArtScene.cameras.main.setZoom(1.15).centerOn(0,448)`);
  await b.wait(150); await b.screenshot('/tmp/newgaza-courtyards-phone.png');
  // The unoccupied garden remains part of the selectable land, not dead space.
  const garden = await b.evaluate(`(()=>{
    const s=window.__newgazaArtScene,c=s.cameras.main,r=s.game.canvas.getBoundingClientRect();
    const wx=(6.58-7.5)*64,wy=(6.58+7.5)*32;
    const q=c.matrix.transformPoint(wx-c.scrollX,wy-c.scrollY);
    return {x:r.left+q.x*r.width/s.scale.width,y:r.top+q.y*r.height/s.scale.height,
      outsideHouse:Math.abs(wx-s.views.get(27).img.x)>s.views.get(27).img.displayWidth/2};
  })()`);
  assert.equal(garden.outsideHouse, true);
  await b.send('Input.dispatchMouseEvent', {type:'mousePressed',x:garden.x,y:garden.y,button:'left',clickCount:1});
  await b.send('Input.dispatchMouseEvent', {type:'mouseReleased',x:garden.x,y:garden.y,button:'left',clickCount:1});
  await b.until(`window.__newgazaArtScene.selected===27`);
  assert.equal(await b.evaluate(`document.documentElement.scrollWidth>window.innerWidth+2`), false);
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  console.log('Small/medium/villa vs apartment land use; consistent front/back and construction; cleared-yard cleanup; phone:', report);
} finally {
  if (original != null) await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(original)})`);
  b.close();
}
