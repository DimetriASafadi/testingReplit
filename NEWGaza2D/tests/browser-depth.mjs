import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(1280, 840), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate', { url: 'http://localhost:80/' });
  await b.until(`!!document.querySelector('#boot.done')`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{const {newGame}=await import('/src/engine.ts');
    const s=newGame(Date.now()),d=s.districts[0];s.currentDistrict=d.id;
    for(const p of d.plots)p.status='empty';
    Object.assign(d.plots[27],{status:'built',buildingId:'housing_6',orientation:0,incomeAt:s.lastSeen});
    d.camera={x:35,y:430,zoom:2.7};
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__depthOldDocument=true})()`);
  await b.send('Page.reload'); await b.until(`!window.__depthOldDocument`);
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.fleetAnimation`);
  const report = await b.evaluate(`(async()=>{
    const s=window.__newgazaArtScene;s.scene.pause();
    const f=s.fleetAnimation,meta=(await import('/src/machinery-manifest.json')).default;
    const unit={id:'depth-probe',kind:'truck',level:1,purchasePrice:0},a=f.actor(unit);
    a.heading=0;const pos=[7.86,6.55],frame=meta.clips['truck:drive'][0];
    f.pose(a,unit,frame,frame,0,pos);const fixed=a.a.depth,wall=s.views.get(27).img;
    s.children.depthSort();
    const correctedIndex=s.children.list.indexOf(a.a),wallIndex=s.children.list.indexOf(wall);
    a.a.setDepth(a.a.y+.4);a.b.setDepth(a.a.y+.401);s.children.depthSort();
    window.__depthProbe={a,f,unit,frame,pos,fixed,wall};
    return {fixed,old:a.a.depth,wall:wall.depth,correctedIndex,wallIndex,
      oldIndex:s.children.list.indexOf(a.a)};
  })()`);
  assert.ok(report.old > report.wall && report.fixed < report.wall, JSON.stringify(report));
  assert.ok(report.oldIndex > report.wallIndex && report.correctedIndex < report.wallIndex);
  await b.screenshot('/tmp/newgaza-depth-old.png');
  await b.evaluate(`(()=>{const p=window.__depthProbe;
    p.f.pose(p.a,p.unit,p.frame,p.frame,0,p.pos);p.f.scene.children.depthSort()})()`);
  await b.screenshot('/tmp/newgaza-depth-fixed.png');
  const foreground = await b.evaluate(`(()=>{const p=window.__depthProbe;
    p.f.pose(p.a,p.unit,p.frame,p.frame,0,[7.86,8.1]);
    p.f.scene.children.depthSort();return p.a.a.depth>p.wall.depth})()`);
  assert.equal(foreground, true);
  assert.equal(b.errors.length, 0, JSON.stringify(b.errors));
  console.log('Immediate chassis/foreground ordering in actual Phaser display list; correctly returns to foreground:', report);
} finally {
  if (original != null) await b.evaluate(`localStorage.setItem('${key}',${JSON.stringify(original)})`);
  b.close();
}
