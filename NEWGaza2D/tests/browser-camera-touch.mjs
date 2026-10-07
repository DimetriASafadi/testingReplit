import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b=await browser(390,844),key='newgaza2d-save-v1';
let original;
const touch=(type,points)=>b.send('Input.dispatchTouchEvent',{type,touchPoints:points.map(p=>({...p,radiusX:4,radiusY:4,force:1}))});
const tap=async selector=>{
  await b.until(`!!document.querySelector(${JSON.stringify(selector)})`);
  const p=await b.evaluate(`(()=>{const r=document.querySelector(${JSON.stringify(selector)}).getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2,id:31}})()`);
  await touch('touchStart',[p]);await touch('touchEnd',[]);await b.wait(300);
};
const camera=()=>b.evaluate(`(()=>{const c=window.__newgazaArtScene.cameras.main;return{x:c.scrollX,y:c.scrollY,z:c.zoom}})()`);
const pan=async()=>{
  const before=await camera();
  await touch('touchStart',[{x:110,y:420,id:51}]);
  await touch('touchMove',[{x:165,y:445,id:51}]);
  await touch('touchMove',[{x:200,y:460,id:51}]);
  await touch('touchEnd',[]);
  const after=await camera();
  assert.ok(Math.abs(before.x-after.x)>20,'single finger must pan');
  assert.ok(Math.abs(before.z-after.z)<.00001,'single finger must not zoom');
};
try{
  await b.send('Emulation.setTouchEmulationEnabled',{enabled:true,maxTouchPoints:3});
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`window.__newgazaArtScene && document.querySelector('#boot.done')`);
  original=await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{const n=Date.now(),s=(await import('/src/engine.ts')).newGame(n),d=s.districts[0];
    s.currentDistrict=d.id;d.camera={x:0,y:700,zoom:1.3};
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',incomeAt:n});d.plots[27].status='empty';
    localStorage.setItem('${key}',JSON.stringify(s));window.__oldTouch=true})()`);
  await b.send('Page.reload');await b.until(`!window.__oldTouch&&window.__newgazaArtScene?.views.size===144`);
  await b.evaluate(`document.querySelector('[data-a="hide-obj"]')?.click();window.__newgazaArtScene.hooks.onSelect(27)`);
  await b.wait(700);
  const category=await b.evaluate(`(async()=>(await import('/src/catalog.ts')).BUILDINGS.find(b=>b.id==='small_house').category)()`);
  await tap(`[data-a="cat"][data-id="${category}"]`);
  await tap('[data-a="pickb"][data-id="small_house"]');
  await tap('[data-a="place"]');
  assert.equal(await b.evaluate(`window.__newgazaArtScene.hooks.getState().districts[0].plots[27].status`),'building');
  await tap('#sheet .sheet-head button');
  // Reproduce engine slots stranded by removed UI targets, not just ordinary taps.
  await b.evaluate(`(()=>{const sc=window.__newgazaArtScene;
    for(const [i,p] of [sc.input.pointer1,sc.input.pointer2].entries()){
      p.isDown=true;p.wasTouch=true;p.active=true;p.identifier=i===0?51:901;
    }})()`);
  await pan();
  const before=await camera();
  await touch('touchStart',[{x:90,y:430,id:61},{x:250,y:430,id:62}]);
  await touch('touchMove',[{x:60,y:430,id:61},{x:280,y:430,id:62}]);
  assert.ok((await camera()).z>before.z+.1,'two fingers must zoom');
  // CDP touchMove lists changed fingers, not all live fingers. Use real DOM
  // touchend/move events to represent lifting just one finger.
  await b.evaluate(`(()=>{
    const sc=window.__newgazaArtScene,canvas=sc.game.canvas,ps=sc.contacts.live();
    const make=(p,x,y)=>new Touch({identifier:p.identifier,target:canvas,clientX:x,clientY:y,pageX:x,pageY:y});
    const keep=make(ps[0],60,430),end=make(ps[1],280,430);
    canvas.dispatchEvent(new TouchEvent('touchend',{bubbles:true,cancelable:true,touches:[keep],targetTouches:[keep],changedTouches:[end]}));
  })()`);
  const one=await camera();
  await b.evaluate(`(()=>{
    const sc=window.__newgazaArtScene,canvas=sc.game.canvas,p=sc.contacts.live()[0];
    const t=new Touch({identifier:p.identifier,target:canvas,clientX:120,clientY:470,pageX:120,pageY:470});
    canvas.dispatchEvent(new TouchEvent('touchmove',{bubbles:true,cancelable:true,touches:[t],targetTouches:[t],changedTouches:[t]}));
  })()`);
  const moved=await camera();
  assert.ok(Math.abs(moved.x-one.x)>10,'remaining finger resumes pan');
  assert.ok(Math.abs(moved.z-one.z)<.00001,'remaining finger does not zoom');
  await touch('touchEnd',[]);
  await pan();
  await touch('touchStart',[{x:140,y:430,id:71}]);await touch('touchCancel',[]);
  await pan();
  assert.equal(b.errors.length,0);
  await b.screenshot('/tmp/newgaza-camera-touch.png');
  console.log('PASS: touch build UI, stranded slots, single-finger pan, pinch, pinch-to-pan and touch cancellation.');
}finally{
  await touch('touchEnd',[]).catch(()=>{});
  if(original!==undefined){
    await b.evaluate(original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`);
    await b.send('Page.reload');
  }
  await b.send('Emulation.setTouchEmulationEnabled',{enabled:false});b.close();
}
