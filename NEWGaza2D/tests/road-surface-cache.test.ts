import test from 'node:test';
import assert from 'node:assert/strict';
import { RoadSurfaceCache, surfaceBounds } from '../src/road-surface-cache';
import { roadsFor } from '../src/roads';
import { DISTRICTS } from '../src/catalog';

const road = {id:'test',a:[0,0] as [number,number],b:[2,0] as [number,number]};
function fixture() {
  const textures=new Map<string,unknown>();
  const stats={draws:0,uploads:0,created:0,destroyed:0};
  const view={x:-1000,y:-1000,right:1000,bottom:1000};
  const scene:any={
    cameras:{main:{worldView:view}},
    textures:{
      createCanvas(key:string,width:number,height:number) {
        stats.created++;
        const texture={width,height,getContext:()=>({setTransform(){},clearRect(){}}),
          refresh(){stats.uploads++;}};
        textures.set(key,texture);return texture;
      },
      remove(key:string){assert.ok(textures.delete(key),'texture exists until destroyed');},
    },
    add:{image(){
      const image={setOrigin(){return image;},setDepth(){return image;},setVisible(){return image;},
        destroy(){stats.destroyed++;}};
      return image;
    }},
  };
  return {cache:new RoadSurfaceCache(scene,-100),stats,textures,view};
}
test('all districts use mobile-safe short road surface bounds',()=>{
  for(const d of DISTRICTS)for(const r of roadsFor(d.id)){
    const b=surfaceBounds(r);assert.ok(b.width<=256&&b.height<=256);
    assert.ok(b.width>0&&b.height>0);
  }
});
test('static road does not repaint or allocate textures during 1800 animation frames',()=>{
  const {cache,stats,textures}=fixture();
  for(let frame=0;frame<1800;frame++){
    cache.begin();cache.paint('job:section',road,'complete',frame*1000/30,()=>stats.draws++);cache.end();
  }
  assert.deepEqual(stats,{draws:1,uploads:1,created:1,destroyed:0});
  assert.equal(textures.size,1);
  cache.begin();cache.end();
  assert.equal(textures.size,0);assert.equal(stats.destroyed,1);
});
test('changing surface uploads at most five times a second, without allocating per progress step',()=>{
  const {cache,stats}=fixture();
  for(let ms=0;ms<60000;ms+=20) {
    cache.begin();cache.paint('job:section',road,String(ms),ms,()=>stats.draws++);cache.end();
  }
  assert.equal(stats.draws,300);assert.equal(stats.uploads,300);assert.equal(stats.created,1);
  cache.reset();assert.equal(stats.destroyed,1);
});
test('offscreen sections wait for the camera, then reconcile to current progress',()=>{
  const {cache,stats,view}=fixture();
  view.x=2000;view.right=3000;
  cache.begin();cache.paint('job:section',road,'old',0,()=>stats.draws++);cache.end();
  assert.equal(stats.created,0);
  view.x=-1000;view.right=1000;
  cache.begin();cache.paint('job:section',road,'current',1000,()=>stats.draws++);cache.end();
  assert.equal(stats.draws,1);
  // Returning to the same progress must not trigger a catch-up repaint.
  cache.begin();cache.paint('job:section',road,'current',2000,()=>stats.draws++);cache.end();
  assert.equal(stats.draws,1);
});
test('switching jobs/districts releases every old surface; repeated reset is safe',()=>{
  const {cache,stats,textures}=fixture();
  for(let cycle=0;cycle<100;cycle++){
    cache.begin();
    for(let j=0;j<20;j++)cache.paint(`${cycle}:${j}`,road,'1',cycle*1000,()=>stats.draws++);
    cache.end();assert.equal(textures.size,20);
    cache.reset();cache.reset();assert.equal(textures.size,0);
  }
  assert.equal(stats.created,stats.destroyed);
});
test('finished-coat handover retains the old mat without drawing or uploading it again',()=>{
  const {cache,stats,textures}=fixture();
  cache.begin();cache.paint('finishing',road,'1',0,()=>stats.draws++);cache.end();
  for(let i=0;i<20;i++){cache.begin();cache.retain();cache.end();}
  assert.equal(textures.size,1);assert.equal(stats.draws,1);assert.equal(stats.uploads,1);
  cache.begin();cache.end();assert.equal(textures.size,0);assert.equal(stats.destroyed,1);
});
test('a system clock adjustment does not freeze the visible surface cache',()=>{
  const {cache,stats}=fixture();
  cache.begin();cache.paint('job',road,'1',1000,()=>stats.draws++);cache.end();
  cache.begin();cache.paint('job',road,'2',500,()=>stats.draws++);cache.end();
  assert.equal(stats.draws,2);
});
