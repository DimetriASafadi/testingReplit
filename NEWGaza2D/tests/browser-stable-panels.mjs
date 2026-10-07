// Real ticking UI: panel, buttons, images and focus must survive countdown updates.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b = await browser(390,844), key = 'newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene`);
  original = await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{
    const now=Date.now(),s=(await import('/src/engine.ts')).newGame(now),d=s.districts[0];
    s.currentDistrict=d.id;s.sequence=4;
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',orientation:0,incomeAt:now});
    Object.assign(d.plots[1],{status:'building',buildingId:'small_house',orientation:0,startedAt:now-30000,endsAt:now+90000});
    const e=(await import('/src/catalog.ts')).EQUIPMENT;
    s.units=['excavator','bulldozer','truck'].map((kind,i)=>({id:'unit-'+(i+1),kind,level:1,purchasePrice:e[kind].cost}));
    s.jobs=[{id:'job-4',districtId:d.id,plotId:27,unitIds:s.units.map(u=>u.id),start:now-60000,arrival:now-10000,workEnd:now+50000,returnEnd:now+110000,cleared:false,value:30000,originPlotId:0}];
    Object.assign(d.projects[0],{status:'building',startedAt:now-10000,endsAt:now+80000});
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__oldPanelDocument=true;
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__oldPanelDocument && !!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.fleetAnimation`);
  await b.evaluate(`(()=>{
    window.__newgazaArtScene.hooks.onSelect(27);
    window.__panel=document.querySelector('.sheet');
    window.__close=document.querySelector('[data-a="close-sheet"]');
    window.__timer=document.querySelector('.sheet .note .num');
    window.__time=__timer.textContent;__close.focus();
    window.__panelChanges=[];
    window.__observer=new MutationObserver(ms=>__panelChanges.push(...ms.map(m=>m.type)));
    __observer.observe(__panel,{subtree:true,childList:true,characterData:true,attributes:true});
  })()`);
  await b.wait(2400);
  const rubble=await b.evaluate(`({
    samePanel:__panel===document.querySelector('.sheet'),
    sameButton:__close===document.querySelector('[data-a="close-sheet"]'),
    sameTimer:__timer===document.querySelector('.sheet .note .num'),
    focus:document.activeElement===__close, ticking:__time!==__timer.textContent,
    noRemounts:!__panelChanges.includes('childList')
  })`);
  for(const [k,v] of Object.entries(rubble))assert.equal(v,true,'rubble '+k);
  await b.screenshot('/tmp/newgaza-stable-countdown-phone.png');
  await b.evaluate(`(()=>{
    __observer.disconnect();window.__newgazaArtScene.hooks.onSelect(1);
    window.__buildPanel=document.querySelector('.sheet');
    window.__buildTimer=document.querySelector('.sheet .kv .num');
    window.__buildTime=__buildTimer.textContent;
  })()`);
  await b.wait(2200);
  assert.equal(await b.evaluate(`__buildPanel===document.querySelector('.sheet') && __buildTimer===document.querySelector('.sheet .kv .num') && __buildTime!==__buildTimer.textContent`),true,'construction countdown preserves nodes');
  // Completion still refreshes the content, without closing/remounting the sheet.
  await b.evaluate(`window.__newgazaArtScene.hooks.getState().districts[0].plots[1].endsAt=Date.now()+600`);
  await b.wait(1400);
  assert.equal(await b.evaluate(`__buildPanel===document.querySelector('.sheet') && !document.querySelector('.sheet').textContent.includes('قيد الإنشاء')`),true,'completion updates the existing panel');
  await b.click('[data-a="close-sheet"]');
  assert.equal(await b.evaluate(`!document.querySelector('.sheet')`),true,'explicit close still works');
  await b.click('[data-a="dlg"][data-id="fleet"]');
  await b.evaluate(`window.__purchaseImage=document.querySelector('dialog img[data-veh]')`);
  await b.wait(1200);
  assert.equal(await b.evaluate(`!!__purchaseImage && __purchaseImage===document.querySelector('dialog img[data-veh]')`),true,'purchase art remains mounted during background updates');
  await b.click('[data-a="dialog-tab"][data-id="2"]');
  await b.evaluate(`(()=>{
    window.__dlg=document.querySelector('dialog');window.__body=__dlg.querySelector('.dlg-body');
    window.__image=__dlg.querySelector('img[data-veh]');
    window.__dialogTimer=__dlg.querySelector('.item.building p .num');
    window.__dialogTime=__dialogTimer.textContent;
    __body.scrollTop=120;window.__scroll=__body.scrollTop;
    __dlg.querySelector('[data-a="close-dlg"]').focus();
  })()`);
  await b.wait(2300);
  const fleet=await b.evaluate(`({
    open:__dlg.open,sameBody:__body===__dlg.querySelector('.dlg-body'),
    sameImage:__image===__dlg.querySelector('img[data-veh]'),
    sameTimer:__dialogTimer===__dlg.querySelector('.item.building p .num'),
    ticking:__dialogTime!==__dialogTimer.textContent,
    scroll:__scroll===__body.scrollTop,focus:document.activeElement===__dlg.querySelector('[data-a="close-dlg"]')
  })`);
  for(const [k,v] of Object.entries(fleet))assert.equal(v,true,'fleet '+k);
  await b.click('[data-a="close-dlg"]');
  assert.equal(await b.evaluate(`!document.querySelector('dialog').open`),true);
  await b.click('[data-a="dlg"][data-id="projects"]');
  await b.evaluate(`window.__projectBody=document.querySelector('.dlg-body');window.__projectTimer=document.querySelector('.item.building .num');window.__projectTime=__projectTimer.textContent`);
  await b.wait(2200);
  assert.equal(await b.evaluate(`__projectBody===document.querySelector('.dlg-body') && __projectTimer===document.querySelector('.item.building .num') && __projectTime!==__projectTimer.textContent`),true,'projects preserve countdown nodes');
  assert.equal(b.errors.length,0);
  console.log(JSON.stringify({rubble,fleet,construction:true,projects:true,completion:true,explicitClose:true,errors:b.errors.length},null,2));
} finally {
  if(original!==undefined) {
    await b.evaluate(`${original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`}`);
    await b.send('Page.reload');
  }
  b.close();
}
