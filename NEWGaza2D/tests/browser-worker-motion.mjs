// Separate local CDP profile; never uses the player's live-link save.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1280, 900), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{
    const now=Date.now(),s=(await import('/src/engine.ts')).newGame(now),d=s.districts[0];
    s.currentDistrict=d.id;d.camera={x:0,y:500,zoom:1.8};
    for(const [id,buildingId,orientation] of [[26,'small_house',0],[27,'small_house',1],[28,'housing_6',0],[35,'cement',1]])
      Object.assign(d.plots[id],{status:'building',buildingId,orientation,startedAt:now-810000,endsAt:now+990000,incomeAt:0});
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__workerOld=true;
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__workerOld && !!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.siteAnimation`);
  await b.evaluate(`void window.__newgazaArtScene.focusPlot(28)`);
  await b.wait(900);
  if (await b.evaluate(`!!document.querySelector('[data-a="hide-obj"]')`)) await b.click('[data-a="hide-obj"]');
  const result = await b.evaluate(`(()=>{
    const c=window.__newgazaArtScene,sa=c.siteAnimation,plots=c.hooks.getState().districts[0].plots;
    const now=Date.now(),seen=new Map();
    const scan=()=>{
      for(const [id,v] of sa.views){
        if(!v.workers.length)continue;
        const p=plots.find(p=>p.id===id),center=sa.iso(p.x+1,p.y+1);
        v.workers.forEach((l,i)=>{
          const k=id+':'+i,a=seen.get(k)||{x:[],y:[],front:false,back:false,roles:new Set()};
          a.x.push(l.w.x);a.y.push(l.w.y);a.front ||= l.w.depth>center.y+8;a.back ||= l.w.depth<center.y+8;
          a.roles.add(l.w.texture.key.split(':')[1]);seen.set(k,a);
        });
      }
    };
    for(let t=0;t<45000;t+=250){sa.update(plots,now+t);scan();}
    const warmed=c.textures.getTextureKeys().length;
    for(let t=0;t<45000;t+=250)sa.update(plots,now+t);
    const records=[...seen].map(([id,a])=>({id,dx:Math.max(...a.x)-Math.min(...a.x),dy:Math.max(...a.y)-Math.min(...a.y),front:a.front,back:a.back,roles:[...a.roles]}));
    return{records,warmed,after:c.textures.getTextureKeys().length,sites:sa.views.size};
  })()`);
  assert.equal(result.sites, 4);
  for (const w of result.records) {
    assert.ok(w.dx > 40 && w.dy > 20, `Worker ${w.id} visibly traverses the site`);
    assert.ok(w.front && w.back, `Worker ${w.id} depth follows its foot behind and in front of the building`);
    for (const role of ['walk','carry','hammer']) assert.ok(w.roles.includes(role), `${w.id}: ${role}`);
  }
  assert.equal(result.warmed,result.after,'Repeated motion reuses worker textures');
  await b.wait(1000); await b.screenshot('/tmp/newgaza-workers-desktop.png');
  await b.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await b.evaluate(`void window.__newgazaArtScene.cameras.main.setZoom(2)`);
  await b.evaluate(`void window.__newgazaArtScene.focusPlot(28)`);
  await b.wait(1200); await b.screenshot('/tmp/newgaza-workers-phone.png');
  assert.equal(b.errors.length,0,JSON.stringify(b.errors));
  console.log('Actual small houses (front/rear), apartment and industrial crews move/work on every side, change depth immediately and reuse textures:',result);
} finally {
  if(original!==undefined) {
    await b.evaluate(original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`);
    await b.send('Page.reload');
  }
  b.close();
}
