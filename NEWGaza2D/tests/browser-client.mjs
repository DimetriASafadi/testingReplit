import { writeFileSync } from 'node:fs';
export async function browser(width=1440,height=900) {
  const pages=await (await fetch('http://localhost:9222/json/list')).json();
  const page=pages.find(p=>p.type==='page');
  const ws=new WebSocket(page.webSocketDebuggerUrl), pending=new Map(), errors=[];
  let seq=0;
  await new Promise((resolve,reject)=>{ws.onopen=resolve;ws.onerror=reject});
  ws.onmessage=e=>{
    const r=JSON.parse(e.data);
    if(r.id){const p=pending.get(r.id);pending.delete(r.id);r.error?p?.reject(r.error):p?.resolve(r.result)}
    if(r.method==='Runtime.exceptionThrown')errors.push(r.params.exceptionDetails);
  };
  const send=(method,params={})=>new Promise((resolve,reject)=>{
    const id=++seq;pending.set(id,{resolve,reject});ws.send(JSON.stringify({id,method,params}));
  });
  const evaluate=async expression=>{
    const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
    if(r.exceptionDetails)throw new Error(JSON.stringify(r.exceptionDetails));
    return r.result.value;
  };
  const wait=ms=>new Promise(r=>setTimeout(r,ms));
  const until=async expression=>{
    for(let i=0;i<100;i++){
      try { if(await evaluate(expression))return; }
      catch(e) {
        if(!/navigated|context.*destroyed|Cannot find context|closed/i.test(e?.message || ''))throw e;
      }
      await wait(150);
    }
    throw new Error('Timed out: '+expression);
  };
  await send('Page.enable');await send('Runtime.enable');
  await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});
  return {send,evaluate,wait,until,errors,close:()=>ws.close(),
    async click(selector) {
      await until(`!!document.querySelector(${JSON.stringify(selector)})`);
      const p=await evaluate(`(()=>{const e=document.querySelector(${JSON.stringify(selector)});e.scrollIntoView({block:'center'});const r=e.getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`);
      await send('Input.dispatchMouseEvent',{type:'mousePressed',button:'left',clickCount:1,...p});
      await send('Input.dispatchMouseEvent',{type:'mouseReleased',button:'left',clickCount:1,...p});
      await wait(650);
    },
    async screenshot(path){const r=await send('Page.captureScreenshot',{format:'png'});writeFileSync(path,Buffer.from(r.data,'base64'))},
  };
}
