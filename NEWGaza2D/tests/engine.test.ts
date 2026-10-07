import test from 'node:test';
import assert from 'node:assert/strict';
import { GameStore, newGame, rubbleYield, SAVE_KEY } from '../src/engine';
import { BUILDINGS, DISTRICTS, projects } from '../src/catalog';
import { validateSave } from '../src/save';
import { CONVOY_GAP, convoyMotion, factoryWindows, fleetRoute, gateDemand, routeLength } from '../src/fleet-motion';
import type { EquipmentKind, Job } from '../src/model';

class MemoryStorage {
  data = new Map<string,string>();
  getItem(k:string) { return this.data.get(k) ?? null }
  setItem(k:string,v:string) { this.data.set(k,v) }
}
function setup() {
  const storage = new MemoryStorage(), store = new GameStore(storage);
  store.dispatch({ type:'enter', districtId:'shujaiya' });
  return { storage, store };
}
function ready() {
  const result = setup(), { store } = result;
  store.dispatch({ type:'place', plotId:0, buildingId:'recycling', orientation:0 });
  store.tick(store.state.lastSeen + 120000);
  for (const kind of ['excavator','bulldozer','truck'] as const) store.dispatch({ type:'buy',kind });
  return result;
}
test('factory exit/entry convoy has physical spacing, hidden storage and compatible old deadlines', () => {
  const state = newGame(100000), source = state.districts[0].plots[0], target = state.districts[0].plots[27];
  const kinds: EquipmentKind[] = ['excavator', 'bulldozer', 'truck'];
  const j: Job = { id: 'job-4', districtId: state.districts[0].id, plotId: target.id,
    unitIds: ['unit-1', 'unit-2', 'unit-3'], start: 100000, arrival: 120000,
    workEnd: 180000, returnEnd: 270000, cleared: false, value: 30000, originPlotId: source.id };
  for (const orientation of [0, 1] as const) {
    source.orientation = orientation;
    for (const kind of kinds) assert.equal(convoyMotion(j, source, target, kind, j.start).visibility, 0);
    assert.equal(gateDemand(j, source, target, j.start).open, true);
    assert.equal(gateDemand(j, source, target, j.arrival + 3000).open, false);
    const out = kinds.map(k => convoyMotion(j, source, target, k, j.start + 7000));
    for (const m of out) assert.ok(m.visibility > 0);
    for (let i = 1; i < out.length; i++) assert.ok(Math.hypot(out[i].position[0] - out[i - 1].position[0], out[i].position[1] - out[i - 1].position[1]) >= CONVOY_GAP / Math.SQRT2 - .01);
    for (const kind of kinds) {
      assert.equal(convoyMotion(j, source, target, kind, j.arrival).stage, 'work');
      assert.equal(convoyMotion(j, source, target, kind, j.returnEnd - 249).visibility, 0);
      assert.ok(routeLength(fleetRoute(source, target, kind)) > 0);
    }
    const entering = kinds.map(k => convoyMotion(j, source, target, k, j.returnEnd - 19000));
    for (let i = 1; i < entering.length; i++) assert.ok(Math.hypot(entering[i].position[0] - entering[i - 1].position[0], entering[i].position[1] - entering[i - 1].position[1]) >= CONVOY_GAP / Math.SQRT2 - .01);
  }
});
test('multiple teams reserve the factory portal, save queues and settle once after actual return', () => {
  const { store, storage } = ready();
  for (const kind of ['excavator','bulldozer','truck'] as const) store.dispatch({ type: 'buy', kind });
  const targets = store.state.districts[0].plots.filter(p => p.status === 'rubble').slice(0, 2);
  for (const target of targets) store.dispatch({ type: 'clear', plotId: target.id });
  assert.equal(store.state.jobs.length, 2);
  const d = store.state.districts[0], source = d.plots[0];
  const windows = store.state.jobs.flatMap(j => factoryWindows(j, source, d.plots[j.plotId]));
  for (let i = 0; i < windows.length; i++) for (let k = i + 1; k < windows.length; k++)
    assert.ok(windows[i].end <= windows[k].start || windows[k].end <= windows[i].start, JSON.stringify(windows));
  validateSave(store.state);
  const restored = new GameStore(storage);
  assert.deepEqual(restored.state.jobs.map(j => [j.departureAt,j.returnStartAt,j.returnEnd]),
    store.state.jobs.map(j => [j.departureAt,j.returnStartAt,j.returnEnd]));
  const end = Math.max(...store.state.jobs.map(j => j.returnEnd));
  const before = store.state.inventory.value;
  store.tick(end); const after = store.state.inventory.value;
  assert.ok(after > before); assert.equal(store.state.jobs.length, 0);
  store.tick(end + 5000); assert.equal(store.state.inventory.value, after);
  const invalid = structuredClone(restored.state);
  invalid.jobs[0].departureAt = invalid.jobs[0].arrival;
  assert.throws(() => validateSave(invalid));
});
test('fresh campaign: million once, thirteen districts, dense ruins, no placed recycler', () => {
  const state = newGame();
  validateSave(state);
  assert.equal(state.coins,1000000);
  assert.equal(state.districts.length,13);
  assert.equal(state.districts.filter(d=>d.unlocked).length,1);
  assert.equal(state.districts[0].plots.filter(p=>p.status==='rubble').length,53);
  assert.equal(state.units.length,0);
});
test('factory onboarding, clean land, money-only prices, two orientations and reload', () => {
  const {store,storage}=setup();
  assert.throws(()=>store.dispatch({type:'buy',kind:'truck'}));
  assert.throws(()=>store.dispatch({type:'place',plotId:2,buildingId:'small_house',orientation:0}));
  store.dispatch({type:'place',plotId:0,buildingId:'small_house',orientation:1});
  assert.equal(store.state.coins,997500);
  assert.equal(store.state.districts[0].plots[0].orientation,1);
  const loaded=new GameStore(storage);
  assert.equal(loaded.state.coins,997500);
  loaded.tick(loaded.state.districts[0].plots[0].endsAt);
  assert.equal(loaded.state.districts[0].plots[0].status,'built');
  assert.equal(loaded.state.districts[0].plots[0].orientation,1);
});
test('all catalog entries support building and income, costs use money only', () => {
  for(const def of BUILDINGS) {
    const {store}=setup();
    store.dispatch({type:'place',plotId:0,buildingId:def.id,orientation:0});
    assert.equal(store.state.coins,1000000-def.cost);
    store.tick(store.state.districts[0].plots[0].endsAt+3600000);
    if(def.income) {
      store.dispatch({type:'collectBuilding',plotId:0});
      assert.equal(store.state.coins,1000000-def.cost+def.income);
      assert.throws(()=>store.dispatch({type:'collectBuilding',plotId:0}));
    }
    validateSave(store.state);
  }
});
test('clear sequence: travel -> sixty seconds work -> empty plot -> returned saleable materials', () => {
  const {store,storage}=ready(), coins=store.state.coins;
  store.dispatch({type:'clear',plotId:2});
  const j={...store.state.jobs[0]};
  assert.equal(j.workEnd-j.arrival,60000);
  assert.throws(()=>store.dispatch({type:'clear',plotId:2}));
  assert.throws(()=>store.dispatch({type:'clear',plotId:3}));
  store.tick(j.arrival-1);
  assert.equal(store.state.districts[0].plots[2].status,'rubble');
  store.tick(j.workEnd);
  assert.equal(store.state.districts[0].plots[2].status,'empty');
  assert.equal(store.state.inventory.value,0);
  const loaded=new GameStore(storage,j.workEnd+1000);
  assert.equal(loaded.state.jobs[0].cleared,true);
  loaded.tick(j.returnEnd);
  assert.equal(loaded.state.jobs.length,0);
  assert.equal(loaded.state.coins,coins);
  assert.equal(loaded.state.inventory.value,rubbleYield(j.value).value);
  const amount=loaded.state.inventory.value;
  loaded.dispatch({type:'sell'});
  assert.equal(loaded.state.coins,coins+amount);
  assert.throws(()=>loaded.dispatch({type:'sell'}));
  loaded.tick(j.returnEnd+100000);
  assert.equal(loaded.state.inventory.value,0);
  validateSave(loaded.state);
});
test('offline completion settles exactly once, busy machinery held through return', () => {
  const {store,storage}=ready();
  store.dispatch({type:'clear',plotId:2});
  assert.throws(()=>store.dispatch({type:'upgrade',unitId:store.state.units[0].id}));
  const end=store.state.jobs[0].returnEnd;
  const first=new GameStore(storage,end+1000), second=new GameStore(storage,end+2000);
  assert.equal(first.state.inventory.value,second.state.inventory.value);
  assert.equal(second.state.jobs.length,0);
});
test('upgrades are per machine, three times, each half original price', () => {
  const {store}=ready(), u=store.state.units[0], start=store.state.coins;
  for(let i=0;i<3;i++)store.dispatch({type:'upgrade',unitId:u.id});
  assert.equal(store.state.coins,start-u.purchasePrice*1.5);
  assert.equal(store.state.units[0].level,4);
  assert.equal(store.state.units[1].level,1);
  assert.throws(()=>store.dispatch({type:'upgrade',unitId:u.id}));
  store.dispatch({type:'buy',kind:'excavator'});
  assert.equal(store.state.units.at(-1)!.level,1);
});
test('parallel independent teams cannot share equipment', () => {
  const {store}=ready();
  for(const kind of ['excavator','bulldozer','truck'] as const)store.dispatch({type:'buy',kind});
  store.dispatch({type:'clear',plotId:2});store.dispatch({type:'clear',plotId:3});
  assert.equal(new Set(store.state.jobs.flatMap(j=>j.unitIds)).size,6);
  validateSave(store.state);
});
test('infrastructure prerequisites and real-time repeatable investments', () => {
  const {store}=setup();
  assert.throws(()=>store.dispatch({type:'project',projectId:'housing'}));
  store.dispatch({type:'project',projectId:'farm'});
  const p=store.state.districts[0].projects.find(p=>p.id==='farm')!;
  assert.equal(p.endsAt-p.startedAt,300000);
  assert.throws(()=>store.dispatch({type:'collectProject',projectId:'farm'}));
  store.tick(p.endsAt);store.dispatch({type:'collectProject',projectId:'farm'});
  assert.equal(store.state.coins,1001000);
  assert.throws(()=>store.dispatch({type:'collectProject',projectId:'farm'}));
  store.dispatch({type:'project',projectId:'farm'});
  assert.equal(store.state.coins,1000500);
});
test('district completion reward only once, ordered unlocking, finale present', () => {
  const {store,storage}=setup();
  assert.throws(()=>store.dispatch({type:'enter',districtId:'tuffah'}));
  assert.throws(()=>store.dispatch({type:'claim'}));
  const d=store.state.districts[0];
  for(const p of d.plots)p.status='empty';
  for(const p of d.projects)p.status='complete';
  store.dispatch({type:'claim'});
  assert.equal(store.state.coins,1100000);
  assert.equal(store.state.districts[1].unlocked,true);
  assert.throws(()=>store.dispatch({type:'claim'}));
  validateSave(store.state);
  assert.equal(new GameStore(storage).state.districts[1].unlocked,true);
  assert.equal(DISTRICTS.at(-1)!.id,'rashid');
  assert.equal(projects(true).find(p=>p.id==='road')!.cost,150000);
});
test('camera save, monotonic wall clock and failed actions never spend', () => {
  const {store,storage}=setup();
  store.dispatch({type:'camera',camera:{x:50,y:60,zoom:.8}});
  const time=store.state.lastSeen;
  store.tick(time-100000);
  assert.equal(store.state.lastSeen,time);
  assert.deepEqual(new GameStore(storage).state.districts[0].camera,{x:50,y:60,zoom:.8});
  const before=store.state.coins;
  assert.throws(()=>store.dispatch({type:'place',plotId:0,buildingId:'bad',orientation:0}));
  assert.equal(store.state.coins,before);
});
test('corruption is explicit and original data is preserved, no silent reset', () => {
  const storage=new MemoryStorage();
  for(const raw of ['bad JSON','null',JSON.stringify({...newGame(),coins:-1})]) {
    storage.setItem(SAVE_KEY,raw);
    assert.throws(()=>new GameStore(storage));
    assert.equal(storage.getItem(SAVE_KEY),raw);
  }
});
test('storage errors are surfaced without claiming saved success', () => {
  const storage={getItem:()=>null,setItem:()=>{throw new Error('quota')}};
  const store=new GameStore(storage);
  assert.match(store.storageError!,/لم يُحفظ/);
  assert.equal(JSON.parse(store.exportSave()).coins,1000000);
});
test('complete thirteen-district campaign via real actions and timers, hourly trade and final reward', () => {
  const {store}=ready();
  for (const [i,district] of DISTRICTS.entries()) {
    store.dispatch({type:'enter',districtId:district.id});
    if(i>0) {
      store.dispatch({type:'place',plotId:0,buildingId:'recycling',orientation:(i%2) as 0|1});
      store.tick(store.state.districts[i].plots[0].endsAt);
    }
    for(const plot of store.state.districts[i].plots.filter(p=>p.status==='rubble')) {
      store.dispatch({type:'clear',plotId:plot.id});
      store.tick(store.state.jobs[0].returnEnd);
    }
    store.dispatch({type:'sell'});
    for(const def of projects(i===12)) {
      store.dispatch({type:'project',projectId:def.id});
      const p=store.state.districts[i].projects.find(p=>p.id===def.id)!;
      store.tick(p.endsAt);
      if(def.income && !def.period)store.dispatch({type:'collectProject',projectId:def.id});
      if(def.period) {
        assert.throws(()=>store.dispatch({type:'collectProject',projectId:def.id}));
        store.tick(store.state.lastSeen+def.period*1000);
        const balance=store.state.coins;
        store.dispatch({type:'collectProject',projectId:def.id});
        assert.equal(store.state.coins,balance+def.income);
      }
    }
    const balance=store.state.coins;
    store.dispatch({type:'claim'});
    assert.equal(store.state.coins,balance+district.reward);
    validateSave(store.state);
  }
  assert(store.state.districts.every(d=>d.claimed));
});
