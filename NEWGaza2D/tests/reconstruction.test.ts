import test from 'node:test';
import assert from 'node:assert/strict';
import { GameStore, newGame, SAVE_KEY } from '../src/engine';
import { building } from '../src/catalog';
import { actualWorkDuration, clearingSeconds, STREET_WORK_MS } from '../src/construction-rules';
import { roadsFor } from '../src/roads';
import { validateSave } from '../src/save';
import { warehouseCapacity } from '../src/warehouse-capacity';

function fixture(){
 let raw:string|null=null;const storage={getItem:()=>raw,setItem:(_k:string,v:string)=>{raw=v;}};
 const store=new GameStore(storage,Date.now()+100000);store.dispatch({type:'enter',districtId:'shujaiya'});
 const d=store.state.districts[0];d.difficulty='easy';
 for(const [id,type]of [[0,'recycling'],[1,'equipment_store'],[2,'asphalt']] as const)Object.assign(d.plots[id],{status:'built',buildingId:type});
 return {store,d,storage};
}
test('explicit production building and clearing bands are independent of income test mode',()=>{
 for(const [id,seconds] of [['small_house',300],['medium_house',600],['housing_complex',1800],['wood_factory',1800],['asphalt',1800],['recycling',120],['equipment_store',120]] as const)assert.equal(building(id).duration,seconds);
 assert.equal(clearingSeconds(2),120);assert.equal(clearingSeconds(6),180);
 const d=newGame().districts[0];d.difficulty='hard';assert.equal(actualWorkDuration(d,STREET_WORK_MS),624000);
});
test('capacity rejects a fourth kind without charge, second warehouse adds capacity',()=>{
 const {store,d}=fixture();
 for(let i=0;i<3;i++)store.dispatch({type:'buy',kind:'bulldozer'});
 const coins=store.state.coins;assert.throws(()=>store.dispatch({type:'buy',kind:'bulldozer'}),/سعة/);assert.equal(store.state.coins,coins);
 Object.assign(store.state.districts[0].plots[3],{status:'built',buildingId:'equipment_store'});
 store.dispatch({type:'buy',kind:'bulldozer'});
 assert.deepEqual(warehouseCapacity(store.state,store.state.districts[0],'bulldozer'),{used:4,total:6});
 validateSave(store.state);
});
test('eight minute stages persist without replay and reserve dozer against clearing',()=>{
 const {store,d,storage}=fixture();
 for(const kind of ['excavator','bulldozer','truck'] as const)store.dispatch({type:'buy',kind});
 store.dispatch({type:'buyPaver'});store.dispatch({type:'pave',roadId:roadsFor(d.id)[0].id});
 const w=structuredClone(d.roadWorks![0]),p=w.preparation!;
 assert.equal(p.aggregateEnd-w.arrival+w.workEnd-p.asphaltStart,480000);
 assert.ok(p.sandEnd>w.arrival&&p.aggregateEnd>p.sandEnd&&p.asphaltStart>p.aggregateEnd);
 assert.throws(()=>store.dispatch({type:'clear',plotId:3}));
 const restored=new GameStore(storage,p.sandEnd+1000);
 assert.deepEqual(restored.state.districts[0].roadWorks![0],w);
 validateSave(restored.state);
 restored.tick(w.returnEnd);
 assert.equal(restored.state.districts[0].roadWorks!.length,0);
 const earned=restored.state.coins;const again=new GameStore(storage,w.returnEnd+1000);assert.equal(again.state.coins,earned);
});
test('legacy nine-project claimed districts keep access without flag reward duplication',()=>{
 const s=newGame(Date.now()+100000);s.districts[0].claimed=true;s.districts[1].unlocked=true;
 for(const d of s.districts)d.projects=d.projects.filter(p=>p.id!=='flag');
 let raw=JSON.stringify(s);const store=new GameStore({getItem:()=>raw,setItem:(_k,v)=>raw=v},s.lastSeen);
 const flag=store.state.districts[0].projects.at(-1)!;
 assert.equal(flag.status,'complete');assert.equal(flag.gemRewarded,true);assert.equal(store.state.gems,0);
 validateSave(JSON.parse(raw));
});
