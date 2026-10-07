import assert from 'node:assert/strict';
import { browser } from './browser-client.mjs';
const b=await browser(390,844);
try{
 await b.send('Emulation.setTouchEmulationEnabled',{enabled:true,maxTouchPoints:5});
 await b.send('Page.navigate',{url:'http://localhost:80/'});
 await b.until(`!!document.querySelector('[data-a="enter"]') && document.querySelector('#boot.done')`);
 await b.click('[data-a="enter"]');
 if(await b.evaluate(`!!document.querySelector('[data-a="hide-obj"]')`))await b.click('[data-a="hide-obj"]');
 const points=(a,z)=>[{x:a,y:420,id:1},{x:z,y:420,id:2}];
 await b.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:points(140,250)});
 await b.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:points(90,300)});
 await b.wait(100);
 await b.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
 await b.wait(700);
 const camera=()=>b.evaluate(`JSON.parse(localStorage.getItem('newgaza2d-save-v1')).districts[0].camera`);
 const before=await camera();assert(before?.zoom>.35);
 await b.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:190,y:450,id:1}]});
 await b.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x:270,y:480,id:1}]});
 await b.wait(100);
 await b.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
 await b.wait(700);
 const after=await camera();assert(after.x!==before.x || after.y!==before.y);
 assert.equal(await b.evaluate(`!!document.querySelector('[data-a="close-sheet"]')`),false);
 await b.screenshot('/tmp/newgaza-mobile-touch.png');
 assert.equal(b.errors.length,0);
 console.log('PASS mobile two-finger pinch, one-finger pan, saved camera and no accidental plot selection.');
}finally{b.close()}
