import test from 'node:test';
import assert from 'node:assert/strict';
import { spatialMix, selectVoices, type AudioSource } from '../src/audio-mix';
const source:AudioSource={id:'machine',clip:'excavator',x:0,y:0,volume:.3};
test('ground audio becomes silent far out and louder on zooming toward the same source',()=>{
  assert.equal(spatialMix(source,{x:0,y:0,zoom:.35}).gain,0);
  assert.equal(spatialMix(source,{x:0,y:0,zoom:.65}).gain,0);
  assert.ok(spatialMix(source,{x:0,y:0,zoom:1.5}).gain>spatialMix(source,{x:0,y:0,zoom:1}).gain);
  assert.equal(spatialMix(source,{x:0,y:0,zoom:2.2}).gain,.3);
});
test('offscreen sources fade and pan by ground position, with a softer distant filter',()=>{
  const close=spatialMix(source,{x:0,y:0,zoom:2});
  const far=spatialMix({...source,x:500},{x:0,y:0,zoom:2});
  assert.ok(far.gain<close.gain);assert.ok(far.cutoff<close.cutoff);assert.ok(far.pan>0);
  assert.equal(spatialMix({...source,x:1000},{x:0,y:0,zoom:2}).gain,0);
});
test('voice budget remains bounded and effects/ambience settings are independent',()=>{
  const sources=Array.from({length:144},(_,i)=>({...source,id:String(i),x:i,ambient:i%2===0}));
  assert.equal(selectVoices(sources,{x:0,y:0,zoom:2},1,1).length,8);
  assert.ok(selectVoices(sources,{x:0,y:0,zoom:2},0,1).every(s=>!s.ambient));
  assert.ok(selectVoices(sources,{x:0,y:0,zoom:2},1,0).every(s=>s.ambient));
  assert.deepEqual(selectVoices(sources,{x:0,y:0,zoom:.35},1,1),[]);
});
