// Disposable CDP profile only. All three scenes use identical camera coordinates and zoom.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1440, 960), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  if (await b.evaluate(`!!document.querySelector('[data-a="enter"]')`)) await b.click('[data-a="enter"]');
  original = await b.evaluate(`JSON.parse(localStorage.getItem('${key}'))`);
  const first = structuredClone(original), d = first.districts[0], now = Date.now();
  first.currentDistrict = d.id; first.lastSeen = now; first.jobs = [];
  d.camera = { x: 0, y: 450, zoom: 1.2 };
  const house = ['small_house', 'housing_4', 'medium_house', 'villa', 'housing_6', 'small_house',
    'traditional_housing', 'modern_housing', 'housing_4', 'villa', 'housing_tower', 'small_house'];
  const load = async s => {
    await b.evaluate(`(async () => {(await import('/src/save.ts')).validateSave(${JSON.stringify(s)}); localStorage.setItem('${key}',${JSON.stringify(JSON.stringify(s))});})()`);
    await b.send('Page.reload'); await b.until(`!!window.__newgazaArtScene?.fleetAnimation`);
    await b.wait(1000);
    if (await b.evaluate(`!!document.querySelector('[data-a="hide-obj"]')`)) await b.click('[data-a="hide-obj"]');
  };
  for (const p of d.plots) Object.assign(p, { status: 'rubble', startedAt: 0, endsAt: 0, incomeAt: 0 });
  await load(first); await b.screenshot('/tmp/newgaza-proportions-ruined.png');
  const mixed = structuredClone(first), md = mixed.districts[0];
  md.projects.find(p => p.id === 'road').status = 'complete';
  for (let i = 0; i < md.plots.length; i++) if (i % 3 !== 0) Object.assign(md.plots[i], {
    status: 'built', buildingId: house[i % house.length], orientation: i % 2, incomeAt: now,
  });
  await load(mixed); await b.screenshot('/tmp/newgaza-proportions-mixed.png');
  const built = structuredClone(mixed), bd = built.districts[0];
  for (let i = 0; i < bd.plots.length; i++) Object.assign(bd.plots[i], {
    status: 'built', buildingId: house[i % house.length], orientation: i % 2, incomeAt: now,
  });
  await load(built); await b.screenshot('/tmp/newgaza-proportions-built.png');
  const stats = await b.evaluate(`(() => {
    const c=window.__newgazaArtScene, v=[...c.views.values()], road=[];
    for(let x=2;x<=14;x+=2) for(let y=2;y<=14;y+=2)
      for(const [dx,dy] of [[0,0],[1,0],[0,1]]) {
        const xx=x+dx, yy=y+dy, wx=(xx-yy)*64, wy=(xx+yy)*32;
        if(c.cameras.main.worldView.contains(wx,wy)) road.push([wx,wy]);
      }
    const covered=(wx,wy,old)=>{
      for(const {img} of v) {
        const sc=old?.88:img.scaleX, left=img.x-sc*img.width/2, top=img.y-sc*img.displayOriginY;
        if(wx<left || wx>=left+sc*img.width || wy<top || wy>=top+sc*img.height) continue;
        const px=Math.floor((wx-img.x)/sc+img.displayOriginX), py=Math.floor((wy-img.y)/sc+img.displayOriginY);
        if((c.textures.getPixelAlpha(px,py,img.texture.key)??0)>90) return true;
      }
      return false;
    };
    return { samples:road.length, newOpen:road.filter(p=>!covered(...p,false)).length,
      oldOpen:road.filter(p=>!covered(...p,true)).length,
      maxBuildingHeight:Math.max(...v.map(({img})=>img.displayHeight)) };
  })()`);
  assert.ok(stats.samples > 60);
  assert.ok(stats.newOpen >= stats.oldOpen, `Reconstructed roads lost visible space: ${JSON.stringify(stats)}`);
  assert.ok(stats.maxBuildingHeight <= 187.01, JSON.stringify(stats));
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  await b.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await b.wait(600); await b.screenshot('/tmp/newgaza-proportions-phone.png');
  assert.equal(await b.evaluate(`document.documentElement.scrollWidth <= 390`), true);
  console.log('Matched ruined, mixed and rebuilt city views, road visibility:', stats);
} finally {
  if (original) await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(JSON.stringify(original))})`);
  b.close();
}
