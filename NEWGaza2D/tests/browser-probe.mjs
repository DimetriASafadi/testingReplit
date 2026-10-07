import { browser } from './browser-client.mjs';
const b=await browser();
try{
 await b.send('Page.navigate',{url:'http://localhost:80/'});
 await b.until(`!!document.querySelector('[data-a="enter"]') && document.querySelector('#boot.done')`);
 await b.click('[data-a="enter"]');
 await b.wait(2000);
 await b.screenshot('/tmp/newgaza-city.png');
 console.log(await b.evaluate(`({body:document.body.innerText.slice(-2300),game:typeof window.Phaser,keys:window.Phaser?Object.keys(window.Phaser).filter(x=>x.includes('GAME')):[],canvas:document.querySelector('canvas')?.getBoundingClientRect().toJSON()})`));
 console.log('runtime errors',b.errors);
}finally{b.close()}
