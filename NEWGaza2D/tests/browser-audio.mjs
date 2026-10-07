import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b=await browser(390,844),save='newgaza2d-save-v1',prefs='newgaza2d-audio-v1';
let original,oldPrefs;
const state=()=>b.evaluate(`(async()=>{const a=(await import('/src/game-audio.ts')).gameAudio;
  return{status:a.status,buffers:a.buffers.size,voices:[...a.voices.keys()],context:a.context?.state,
    master:a.master?.gain.value,prefs:a.prefs}})()`);
try{
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`!!document.querySelector('#boot.done')`);
  original=await b.evaluate(`localStorage.getItem('${save}')`);
  oldPrefs=await b.evaluate(`localStorage.getItem('${prefs}')`);
  await b.evaluate(`(async()=>{
    const n=Date.now(),s=(await import('/src/engine.ts')).newGame(n),d=s.districts[0],p=d.plots[27];
    s.currentDistrict=d.id;d.camera={x:(p.x-p.y)*64,y:(p.x+p.y+2)*32,zoom:1.8};
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',incomeAt:n});
    Object.assign(d.plots[28],{status:'building',buildingId:'small_house',startedAt:n-5000,endsAt:n+60000});
    Object.assign(d.plots[35],{status:'built',buildingId:'ornamental_trees',incomeAt:n});
    s.sequence=4;
    const {EQUIPMENT}=await import('/src/catalog.ts');
    s.units=['excavator','bulldozer','truck'].map((kind,i)=>({id:'unit-'+(i+1),kind,level:1,purchasePrice:EQUIPMENT[kind].cost}));
    s.jobs=[{id:'job-4',districtId:d.id,plotId:27,originPlotId:0,unitIds:s.units.map(u=>u.id),
      start:n-10000,arrival:n-3000,workEnd:n+57000,returnEnd:n+90000,cleared:false,value:2000}];
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${save}',JSON.stringify(s));localStorage.setItem('${prefs}',JSON.stringify({enabled:true,master:.7,effects:.8,ambience:.5}));
    window.__oldAudio=true;
  })()`);
  await b.send('Page.reload');await b.until(`!window.__oldAudio&&window.__newgazaArtScene?.views.size===144`);
  await b.click('.sound-toggle');
  await b.until(`(async()=>{const a=(await import('/src/game-audio.ts')).gameAudio;return a.buffers.size===10&&a.context.state==='running'})()`);
  await b.wait(450);
  let s=await state();assert.equal(s.buffers,10);assert.equal(s.context,'running');
  assert.ok(s.voices.some(k=>k.startsWith('engine:')));assert.ok(s.voices.some(k=>k.startsWith('plot:')));
  assert.ok(s.voices.length<=8);
  assert.equal(await b.evaluate(`document.querySelectorAll('.audio-row input').length`),3);
  await b.screenshot('/tmp/newgaza-audio-settings-phone.png');
  // Confirm decoded content is audible without depending on the headless machine's speakers.
  const rms=await b.evaluate(`(async()=>{const a=(await import('/src/game-audio.ts')).gameAudio,c=new OfflineAudioContext(1,22050,44100);
    const s=c.createBufferSource();s.buffer=a.buffers.get('excavator');s.connect(c.destination);s.start();
    const data=(await c.startRendering()).getChannelData(0);return Math.sqrt(data.reduce((sum,v)=>sum+v*v,0)/data.length)})()`);
  assert.ok(rms>.001);
  await b.evaluate(`window.__newgazaArtScene.cameras.main.setZoom(.35);void 0`);await b.wait(750);
  assert.equal((await state()).voices.length,0,'zoom far out silences the world');
  await b.evaluate(`window.__newgazaArtScene.cameras.main.setZoom(2);void 0`);await b.wait(450);
  assert.ok((await state()).voices.length>0);
  await b.click('[data-a="audio-toggle"]');await b.wait(300);
  s=await state();assert.equal(s.prefs.enabled,false);assert.equal(s.voices.length,0);assert.ok(s.master<.005);
  await b.evaluate(`const r=document.querySelector('[data-pref="master"]');r.value='28';r.dispatchEvent(new Event('input',{bubbles:true}))`);
  await b.click('[data-a="audio-toggle"]');await b.wait(250);
  assert.equal((await state()).prefs.master,.28);
  assert.equal(await b.evaluate(`JSON.parse(localStorage.getItem('${prefs}')).master`),.28);
  assert.equal(b.errors.length,0);
  console.log('PASS: all 10 files decode, real fleet/building playback, audible waveform, zoom silence, voice cap, mute, mobile controls and saved levels.');
}finally{
  if(original!==undefined){
    await b.evaluate(`${original===null?`localStorage.removeItem('${save}')`:`localStorage.setItem('${save}',${JSON.stringify(original)})`};
      ${oldPrefs===null?`localStorage.removeItem('${prefs}')`:`localStorage.setItem('${prefs}',${JSON.stringify(oldPrefs)})`}`);
    await b.send('Page.reload');
  }
  b.close();
}
