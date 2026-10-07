import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1280, 840), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{const s=(await import('/src/engine.ts')).newGame(Date.now());
    s.currentDistrict=s.districts[0].id;
    for(const d of s.districts){d.unlocked=true;d.claimed=true;d.camera=null;}
    (await import('/src/save.ts')).validateSave(s);localStorage.setItem('${key}',JSON.stringify(s));
    window.__environmentOldPage=true})()`);
  await b.send('Page.reload'); await b.until(`!window.__environmentOldPage`);
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.groundKey`);
  const ids = await b.evaluate(`window.__newgazaArtScene.hooks.getState().districts.map(d=>d.id)`);
  const reports = [];
  for (const id of ids) {
    const report = await b.evaluate(`(()=>{const s=window.__newgazaArtScene,st=s.hooks.getState();
      st.currentDistrict=${JSON.stringify(id)};s.sync();
      const source=s.textures.get(s.groundKey).getSourceImage(),data=source.getContext('2d').getImageData(0,0,source.width,source.height).data;
      let cyan=0,green=0,count=0,hash=0;
      for(let y=0;y<source.height;y+=12)for(let x=0;x<source.width;x+=12){
        const n=(y*source.width+x)*4;if(data[n+3]<160)continue;
        const r=data[n],g=data[n+1],b=data[n+2];count++;
        if(r<110&&g-r>15&&b-r>15)cyan++;
        if(g>r+3&&r>b+10)green++;
        hash=(hash*31+r*3+g*5+b*7)>>>0;
      }
      const keys=Object.keys(s.textures.list);
      return {id:${JSON.stringify(id)},...s.terrainInfo,cyan:cyan/count,green:green/count,hash,
        groundKeys:keys.filter(k=>k.startsWith('ground:')),roadKeys:keys.filter(k=>k.startsWith('roads:'))};
    })()`);
    const coastal = ['rimal','sheikh-ijlin','rashid'].includes(id);
    assert.equal(report.water, coastal, JSON.stringify(report));
    assert.ok(coastal ? report.cyan > .08 : report.cyan < .01, JSON.stringify(report));
    assert.equal(report.groundKeys.length, 1, JSON.stringify(report));
    assert.equal(report.roadKeys.length, 1, JSON.stringify(report));
    reports.push(report);
    if (['shujaiya','old-city','rashid','zeitoun'].includes(id)) {
      await b.wait(1100); await b.screenshot(`/tmp/newgaza-environment-${id}.png`);
    }
  }
  assert.equal(new Set(reports.map(r=>r.hash)).size, ids.length);
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  console.log('All district terrain profiles, actual water pixels and bounded active canvas textures:', reports);
} finally {
  if (original != null) await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(original)})`);
  b.close();
}
