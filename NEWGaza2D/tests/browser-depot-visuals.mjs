// Real Phaser renderer in an isolated local browser profile; restore its original save.
import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b=await browser(1000,850),key='newgaza2d-save-v1';
let original;
try {
  await b.send('Page.navigate',{url:'http://localhost:80/'});
  await b.until(`!!document.querySelector('#boot.done') && !!window.__newgazaArtScene`);
  original=await b.evaluate(`localStorage.getItem('${key}')`);
  await b.evaluate(`(async()=>{
    const now=Date.now(),s=(await import('/src/engine.ts')).newGame(now),d=s.districts[0];
    s.currentDistrict=d.id;d.camera={x:0,y:35,zoom:2.6};s.sequence=4;
    Object.assign(d.plots[0],{status:'built',buildingId:'recycling',orientation:0,incomeAt:now});
    const equipment=(await import('/src/catalog.ts')).EQUIPMENT;
    s.units=['excavator','bulldozer','truck'].map((kind,i)=>({id:'unit-'+(i+1),kind,level:1,purchasePrice:equipment[kind].cost}));
    s.jobs=[{id:'job-4',districtId:d.id,plotId:27,unitIds:s.units.map(u=>u.id),start:now,arrival:now+60000,workEnd:now+120000,returnEnd:now+180000,cleared:false,value:30000,originPlotId:0}];
    (await import('/src/save.ts')).validateSave(s);
    localStorage.setItem('${key}',JSON.stringify(s));window.__depotOld=true;
  })()`);
  await b.send('Page.reload');
  await b.until(`!window.__depotOld && !!document.querySelector('#boot.done') && !!window.__newgazaArtScene?.fleetAnimation`);
  await b.wait(400);
  if(await b.evaluate(`!!document.querySelector('[data-a="hide-obj"]')`))await b.click('[data-a="hide-obj"]');
  await b.evaluate(`(()=>{
    const fa=window.__newgazaArtScene.fleetAnimation;fa.__originalUpdate=fa.update;fa.update=()=>{};
  })()`);
  for(const orientation of [0,1]) {
    await b.evaluate(`(()=>{
      const c=window.__newgazaArtScene,s=c.hooks.getState();s.districts[0].plots[0].orientation=${orientation};
      c.sync();c.fleetAnimation.resetDistrict();c.cameras.main.setZoom(2.6);c.focusPlot(0);
    })()`);
    await b.wait(600);
    const result=await b.evaluate(`(async()=>{
      const c=window.__newgazaArtScene,fa=c.fleetAnimation,s=c.hooks.getState(),d=s.districts[0],j=s.jobs[0];
      const motion=await import('/src/fleet-motion.ts');
      let opaque=true,masked=false,unmasked=false;
      for(let t=j.start;t<j.returnEnd;t+=200) {
        fa.__originalUpdate.call(fa,s,t,3);
        for(const a of fa.actors.values()){opaque &&= a.a.alpha===1;masked ||= !!a.a.mask;unmasked ||= !a.a.mask;}
      }
      let exitAt=j.start,enterAt=j.workEnd,exitError=Infinity,enterError=Infinity;
      const p=motion.depotPortal(d.plots[0]),desired=[p.threshold[0]+p.outward[0]*.35,p.threshold[1]+p.outward[1]*.35];
      for(let t=j.start;t<j.returnEnd;t+=30) {
        const m=motion.convoyMotion(j,d.plots[0],d.plots[27],'excavator',t);
        const e=Math.hypot(m.position[0]-desired[0],m.position[1]-desired[1]);
        if(t<j.arrival && e<exitError){exitAt=t;exitError=e;}
        if(t>j.workEnd && e<enterError){enterAt=t;enterError=e;}
      }
      fa.resetDistrict();fa.__originalUpdate.call(fa,s,j.start-1,3);
      const v=fa.doors.views.get(0),commands=v.maskShape.commandBuffer.length;
      for(let i=0;i<20;i++)fa.doors.passage(d.plots[0]);
      return{opaque,masked,unmasked,exitAt,enterAt,commands,after:v.maskShape.commandBuffer.length};
    })()`);
    assert.ok(result.opaque && result.masked && result.unmasked,'Opaque bodies with doorway clipping only near factory');
    assert.equal(result.commands,result.after,'Passage lookup does not redraw static geometry');
    await b.screenshot(`/tmp/newgaza-depot-${orientation}-closed.png`);
    for(const [phase,t]of[['exit',result.exitAt],['entry',result.enterAt]]){
      await b.evaluate(`(()=>{
        const c=window.__newgazaArtScene,fa=c.fleetAnimation;fa.resetDistrict();fa.__originalUpdate.call(fa,c.hooks.getState(),${t},3);
      })()`);
      await b.wait(100);await b.screenshot(`/tmp/newgaza-depot-${orientation}-${phase}.png`);
    }
    console.log('Factory orientation',orientation,result);
  }
  await b.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await b.wait(300);await b.screenshot('/tmp/newgaza-depot-phone.png');
  assert.equal(b.errors.length,0,JSON.stringify(b.errors));
}finally{
  if(original!==undefined){
    await b.evaluate(original===null?`localStorage.removeItem('${key}')`:`localStorage.setItem('${key}',${JSON.stringify(original)})`);
    await b.send('Page.reload');
  }
  b.close();
}
