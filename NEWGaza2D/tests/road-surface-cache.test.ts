import test from 'node:test';
import assert from 'node:assert/strict';
import { RoadSurfaceCache, RoadMatHandover, SurfaceUploadBudget, surfaceBounds } from '../src/road-surface-cache';
import { roadsFor } from '../src/roads';
import { DISTRICTS } from '../src/catalog';

const road = {id:'test',a:[0,0] as [number,number],b:[2,0] as [number,number]};
function fixture(budget?: SurfaceUploadBudget) {
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
  return {cache:new RoadSurfaceCache(scene,-100,budget),stats,textures,view};
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
test('successive paver completions do not repaint other jobs or resurrect a returning machine road',()=>{
  const {cache,stats,textures}=fixture(), handover=new RoadMatHandover();
  const draws=new Map<string,number>();
  function frame(coating:boolean,jobs:{owner:string;working:boolean;pending:boolean;stamp:string}[],now:number) {
    cache.begin();handover.begin(coating);
    for(const j of jobs)if(handover.needs(j.owner,j.working,j.pending))
      cache.paint(j.owner+':section',road,j.stamp,now,()=>draws.set(j.owner,(draws.get(j.owner)??0)+1),j.owner);
    if(coating)cache.retain(handover.holding);
    handover.end();cache.end();
  }
  const job=(owner:string,working=true,pending=false,stamp='half')=>({owner,working,pending,stamp});
  frame(false,[job('A'),job('B'),job('C')],0);
  const original=new Map(textures);
  frame(true,[job('A',false,true,'complete'),job('B'),job('C')],300);
  frame(false,[job('A',false,false,'complete'),job('B'),job('C')],600);
  assert.equal(textures.size,2);
  // A is still returning when B finishes. The old global coat flag used to
  // repaint A's entire road here, although its surface was already retired.
  for(let i=0;i<40;i++)frame(true,[job('A',false,true,'complete'),job('B',false,true,'complete'),job('C')],900+i*34);
  assert.equal(stats.created,3);assert.equal(textures.size,2);
  assert.equal(draws.get('A'),2);assert.equal(draws.get('B'),2);assert.equal(draws.get('C'),1);
  for(const [key,texture] of textures)assert.equal(texture,original.get(key));
  frame(false,[job('A',false,false),job('B',false,false),job('C')],3000);
  assert.equal(textures.size,1);
  cache.reset();handover.reset();assert.equal(textures.size,0);
});
test('handover holds only owned mat, even if the completed job disappears during the coat queue',()=>{
  const {cache,stats,textures}=fixture(), handover=new RoadMatHandover();
  cache.begin();handover.begin(false);
  assert.equal(handover.needs('A:1',true,false),true);
  cache.paint('A:1:part',road,'complete',0,()=>stats.draws++,'A:1');handover.end();cache.end();
  for(let i=0;i<5;i++){
    cache.begin();handover.begin(true);cache.retain(handover.holding);handover.end();cache.end();
  }
  assert.equal(stats.draws,1);assert.equal(textures.size,1);
  cache.begin();handover.begin(false);handover.end();cache.end();assert.equal(textures.size,0);
  handover.begin(true);
  assert.equal(handover.needs('A:1',false,true),false);
  assert.equal(handover.needs('A:2',true,false),true,'a reused machine starts a distinct job');
});
test('preparation and asphalt share a fair bounded update budget, without blank allocations or stale progress queues',()=>{
  const budget=new SurfaceUploadBudget(1), prep=fixture(budget), mat=fixture(budget);
  const draws:string[]=[];
  for(let frame=0;frame<120;frame++){
    budget.begin();
    const before=prep.stats.uploads+mat.stats.uploads;
    prep.cache.begin();mat.cache.begin();
    // Earlier callbacks must not starve later machines.
    for(const [name,fixture] of [['sand',prep],['asphalt',mat]] as const)
      for(let section=0;section<3;section++)fixture.cache.paint(`${name}:${section}`,road,String(frame),frame*250,
        ()=>draws.push(`${name}:${section}:${frame}`));
    prep.cache.end();mat.cache.end();
    assert.equal(prep.stats.uploads+mat.stats.uploads-before,1);
    if(frame===0)assert.equal(prep.stats.created+mat.stats.created,1);
  }
  assert.equal(prep.stats.created+mat.stats.created,6);
  assert.equal(prep.stats.uploads,60);assert.equal(mat.stats.uploads,60);
  assert.ok(draws.includes('asphalt:0:3'),'deferred work uses current progress, not frame zero');
  // A removed/offscreen request cannot block later work indefinitely.
  for(let frame=0;frame<4;frame++){
    budget.begin();mat.cache.begin();
    mat.cache.paint('remaining',road,'final',100000+frame*250,()=>draws.push('remaining'));mat.cache.end();
  }
  assert.ok(draws.includes('remaining'));
  prep.cache.reset();mat.cache.reset();budget.reset();
  assert.equal(prep.textures.size+mat.textures.size,0);
});
