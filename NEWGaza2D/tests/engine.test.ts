import test from 'node:test';
import { setMinuteTestTimers } from '../src/game-timing';
setMinuteTestTimers(false);
import assert from 'node:assert/strict';
import { GameStore, newGame, rubbleYield, SAVE_KEY } from '../src/engine';
import { BUILDINGS, DISTRICTS, projects } from '../src/catalog';
import { validateSave } from '../src/save';
import { districtOverview, MIN_CAMERA_ZOOM } from '../src/camera-overview';
import { CONVOY_GAP, convoyMotion, factoryWindows, fleetRoute, gateDemand, routeLength, returnTravelDuration } from '../src/fleet-motion';
import type { EquipmentKind, Job } from '../src/model';
import { ORIENTATION_CYCLE, nextOrientation, rotateLotPoint, isOrientation } from '../src/orientation';
import { buildingSaleValue } from '../src/building-disposal';
import { PROJECT_GEMS, DISTRICT_GEMS, HALF_TIME_GEMS } from '../src/gems';
import { activityDuration } from '../src/game-timing';

test('gems reward each project once, including offline completions and legacy progress', () => {
  const storage = new MemoryStorage(), now = Date.now() + 600000;
  const saved = newGame(now);
  const first = saved.districts[0].projects[0];
  first.status = 'complete'; first.startedAt = now - 60000; first.endsAt = now - 1;
  delete (saved as Partial<typeof saved>).gems;
  storage.setItem(SAVE_KEY, JSON.stringify(saved));
  const store = new GameStore(storage, now);
  assert.equal(store.state.gems, PROJECT_GEMS);
  store.tick(now + 1000);
  assert.equal(new GameStore(storage, now + 2000).state.gems, PROJECT_GEMS);
  store.dispatch({ type: 'enter', districtId: 'shujaiya' });
  const farm = store.state.districts[0].projects.find(p => p.id === 'farm')!;
  Object.assign(farm, { status: 'building', startedAt: now, endsAt: now + 3000 });
  store.tick(now + 3000);
  assert.equal(store.state.gems, PROJECT_GEMS * 2);
  store.dispatch({ type: 'collectProject', projectId: farm.id });
  store.dispatch({ type: 'project', projectId: farm.id });
  store.tick(farm.endsAt);
  assert.equal(store.state.gems, PROJECT_GEMS * 2);
  validateSave(store.state);
});

test('half-time building costs two gems, keeps money cost and persists its deadline', () => {
  for (const minute of [true, false]) {
    setMinuteTestTimers(minute);
    const { store, storage } = setup();
    store.state.gems = HALF_TIME_GEMS;
    const def = BUILDINGS.find(b => b.category === 'housing')!;
    const before = store.state.coins;
    store.dispatch({ type: 'place', plotId: 0, buildingId: def.id, orientation: 2, accelerate: true });
    const plot = store.state.districts[0].plots[0];
    assert.equal(plot.endsAt - plot.startedAt, activityDuration(def.duration * 1000) / 2);
    assert.equal(store.state.gems, 0);
    assert.equal(store.state.coins, before - def.cost);
    const restored = new GameStore(storage, plot.startedAt);
    assert.equal(restored.state.districts[0].plots[0].endsAt, plot.endsAt);
    restored.tick(plot.endsAt - 1);
    assert.equal(restored.state.districts[0].plots[0].status, 'building');
    restored.tick(plot.endsAt);
    assert.equal(restored.state.districts[0].plots[0].status, 'built');
    assert.equal(restored.state.gems, 0);
  }
  setMinuteTestTimers(false);
});

test('failed accelerated builds never deduct coins or gems; ordinary construction needs no gems', () => {
  const { store } = setup(), def = BUILDINGS[0];
  const before = store.state.coins;
  assert.throws(() => store.dispatch({ type: 'place', plotId: 0, buildingId: def.id, orientation: 0, accelerate: true }), /جوهرتين/);
  assert.equal(store.state.coins, before); assert.equal(store.state.gems, 0);
  store.state.gems = HALF_TIME_GEMS; store.state.coins = 0;
  assert.throws(() => store.dispatch({ type: 'place', plotId: 0, buildingId: def.id, orientation: 0, accelerate: true }), /الرصيد غير كافٍ/);
  assert.equal(store.state.gems, HALF_TIME_GEMS);
  store.state.coins = before; store.state.gems = 0;
  store.dispatch({ type: 'place', plotId: 0, buildingId: def.id, orientation: 0 });
  const plot = store.state.districts[0].plots[0];
  assert.equal(plot.endsAt - plot.startedAt, activityDuration(def.duration * 1000));
});

test('district gem reward is granted only on explicit first claim', () => {
  const { store, storage } = setup();
  const d = store.state.districts[0];
  for (const p of d.plots) p.status = 'empty';
  for (const p of d.projects) { p.status = 'complete'; p.gemRewarded = true; }
  store.dispatch({ type: 'claim' });
  assert.equal(store.state.gems, DISTRICT_GEMS);
  assert.throws(() => store.dispatch({ type: 'claim' }), /بالفعل/);
  assert.equal(new GameStore(storage, store.state.lastSeen).state.gems, DISTRICT_GEMS);
});

test('corrupt gem balances and reward markers are rejected', () => {
  for (const gems of [-1, 1.5, Infinity, '2']) {
    const state = newGame();
    assert.throws(() => validateSave({ ...state, gems }));
  }
  const state = newGame();
  Object.assign(state.districts[0].projects[0], { gemRewarded: 'yes' });
  assert.throws(() => validateSave(state));
});

class MemoryStorage {
  data = new Map<string,string>();
  getItem(k:string) { return this.data.get(k) ?? null }
  setItem(k:string,v:string) { this.data.set(k,v) }
}
test('overview zoom fits a large district on phones and survives save/reload below the old limit', () => {
  for (const [width,height,insets] of [
    [390,844,{top:112,right:14,bottom:112,left:14}],
    [844,390,{top:64,right:164,bottom:14,left:14}],
    [320,568,{top:112,right:14,bottom:112,left:14}],
  ] as const) {
    const worldSize = 48 * 64, o = districtOverview(worldSize,width,height,insets);
    assert.ok(o.zoom >= MIN_CAMERA_ZOOM && o.zoom < .15);
    assert.ok((2*worldSize+128)*o.zoom <= width-insets.left-insets.right);
    assert.ok((worldSize+256)*o.zoom <= height-insets.top-insets.bottom);
    const storage = new MemoryStorage(), store = new GameStore(storage);
    store.dispatch({type:'enter',districtId:'shujaiya'});
    store.dispatch({type:'camera',camera:{x:0,y:1000,zoom:o.zoom}});
    const restored = new GameStore(storage);
    assert.equal(restored.state.districts[0].camera?.zoom,o.zoom);
    validateSave(restored.state);
    assert.throws(()=>store.dispatch({type:'camera',camera:{x:0,y:0,zoom:.001}}));
  }
});
test('four orientations retain legacy front/back numbering and rotate without mirroring', () => {
  assert.deepEqual(ORIENTATION_CYCLE, [0, 2, 1, 3]);
  assert.deepEqual(ORIENTATION_CYCLE.map(o => nextOrientation(o)), [2, 1, 3, 0]);
  assert.deepEqual(ORIENTATION_CYCLE.map(o => rotateLotPoint([.25, .8], o)),
    [[.25, .8], [.8, -.25], [-.25, -.8], [-.8, .25]]);
  for (const o of ORIENTATION_CYCLE) assert.equal(isOrientation(o), true);
  for (const o of [-1, 4, 1.5, '2', null]) assert.equal(isOrientation(o), false);
});
test('all building types place/save/restore four faces; rotation preserves costs and construction deadlines', () => {
  for (const b of BUILDINGS) {
    const storage = new MemoryStorage(), store = new GameStore(storage);
    store.dispatch({ type: 'enter', districtId: 'shujaiya' });
    store.state.coins = 99_000_000;
    const p = store.state.districts[0].plots[0];
    store.dispatch({ type: 'place', plotId: p.id, buildingId: b.id, orientation: 0 });
    const coins = store.state.coins, start = p.startedAt, end = p.endsAt;
    for (const orientation of [2, 1, 3, 0] as const) {
      store.dispatch({ type: 'rotateBuilding', plotId: p.id });
      assert.equal(p.orientation, orientation, b.id);
      assert.equal(p.startedAt, start); assert.equal(p.endsAt, end);
      assert.equal(store.state.coins, coins);
      const restored = new GameStore(storage);
      assert.equal(restored.state.districts[0].plots[p.id].orientation, orientation);
      validateSave(restored.state);
    }
    store.tick(end);
    const incomeAt = p.incomeAt;
    store.dispatch({ type: 'rotateBuilding', plotId: p.id });
    assert.equal(p.status, 'built');
    assert.equal(p.orientation, 2);
    assert.equal(p.incomeAt, incomeAt);
  }
});
test('factory orientation cannot change under an active convoy, but unlocks once it returns', () => {
  const { store } = ready();
  const district = store.state.districts[0], source = district.plots[0];
  const target = district.plots.find(p => p.status === 'rubble')!;
  store.dispatch({ type: 'clear', plotId: target.id });
  const job = store.state.jobs[0], raw = JSON.stringify(store.state);
  assert.throws(() => store.dispatch({ type: 'rotateBuilding', plotId: source.id }), /عودة آليات/);
  assert.equal(JSON.stringify(store.state), raw);
  store.tick(job.returnEnd + 1);
  store.dispatch({ type: 'rotateBuilding', plotId: source.id });
  assert.equal(store.state.districts[0].plots[source.id].orientation, 2);
});
function setup() {
  const storage = new MemoryStorage(), store = new GameStore(storage);
  store.dispatch({ type:'enter', districtId:'shujaiya' });
  return { storage, store };
}

test('every building can be sold at half price or removed, during construction and after completion', () => {
  for (const def of BUILDINGS) for (const completed of [false, true]) for (const type of ['sellBuilding','removeBuilding'] as const) {
    const { storage, store } = setup();
    const before = store.state.coins;
    store.dispatch({ type:'place', plotId:0, buildingId:def.id, orientation:3 });
    let p = store.state.districts[0].plots[0];
    assert.equal(p.paidCost, def.cost);
    if (completed) store.tick(p.endsAt);
    const location = [p.id,p.x,p.y,p.layoutId];
    store.dispatch({ type, plotId:0, expected:{buildingId:p.buildingId,startedAt:p.startedAt} });
    p = store.state.districts[0].plots[0];
    assert.equal(store.state.coins, before-def.cost+(type==='sellBuilding'?Math.floor(def.cost/2):0));
    assert.deepEqual([p.id,p.x,p.y,p.layoutId],location);
    assert.equal(p.status,'empty');
    assert.equal(p.startedAt,0); assert.equal(p.endsAt,0); assert.equal(p.incomeAt,0);
    assert.equal(p.paidCost,undefined);
    const restored = new GameStore(storage,store.state.lastSeen+1);
    assert.equal(restored.state.districts[0].plots[0].status,'empty');
    assert.equal(restored.state.coins,store.state.coins);
    assert.throws(()=>store.dispatch({type,plotId:0}),/للمباني فقط/);
    store.dispatch({type:'place',plotId:0,buildingId:def.id,orientation:0});
    validateSave(store.state);
  }
});

test('sale respects recorded paid price; legacy buildings remain sellable and bad costs are rejected', () => {
  const {store}=setup();
  store.dispatch({type:'place',plotId:0,buildingId:'recycling',orientation:0});
  const p=store.state.districts[0].plots[0], def=BUILDINGS.find(b=>b.id==='recycling')!;
  assert.equal(buildingSaleValue(p),def.cost/2);
  delete p.paidCost;
  assert.equal(buildingSaleValue(p),def.cost/2);
  p.paidCost=101;
  const before=store.state.coins;
  store.dispatch({type:'sellBuilding',plotId:0});
  assert.equal(store.state.coins,before+50);
  for (const cost of [-1,Infinity,1.5]) {
    const invalid=structuredClone(store.state); invalid.districts[0].plots[0].paidCost=cost;
    assert.throws(()=>validateSave(invalid));
  }
});

test('disposal requires explicit income collection, rejects changed targets and preserves unrelated buildings', () => {
  for (const type of ['sellBuilding','removeBuilding'] as const) {
    const {storage,store}=setup(), def=BUILDINGS.find(b=>b.income>0)!;
    store.dispatch({type:'place',plotId:0,buildingId:def.id,orientation:0});
    const p=store.state.districts[0].plots[0];
    store.tick(p.endsAt+2*3600000+1000);
    const unrelated=JSON.stringify(store.state.districts[0].plots[1]);
    assert.throws(()=>store.dispatch({type,plotId:0,expected:{buildingId:def.id,startedAt:p.startedAt+1}}),/تغيّر المبنى/);
    const before=store.state.coins;
    assert.throws(()=>store.dispatch({type,plotId:0}),/اجمع دخل المبنى أولًا/);
    assert.equal(store.state.coins,before);
    store.dispatch({type:'collectBuilding',plotId:0});
    store.dispatch({type,plotId:0});
    assert.equal(store.state.coins,before+def.income+(type==='sellBuilding'?Math.floor(def.cost/2):0));
    assert.equal(JSON.stringify(store.state.districts[0].plots[1]),unrelated);
    store.tick(store.state.lastSeen+7200000);
    assert.equal(new GameStore(storage,store.state.lastSeen).state.coins,store.state.coins);
    assert.equal(store.state.coins,before+def.income+(type==='sellBuilding'?Math.floor(def.cost/2):0));
    const rubble=store.state.districts[0].plots.find(p=>p.status==='rubble')!;
    assert.throws(()=>store.dispatch({type,plotId:rubble.id}),/للمباني فقط/);
  }
});

test('factories with outbound, working or returning teams cannot be sold or removed', () => {
  for (const type of ['sellBuilding','removeBuilding'] as const) for (const phase of ['travel','work','return']) {
    const {store}=ready();
    const target=store.state.districts[0].plots.find(p=>p.status==='rubble')!;
    store.dispatch({type:'clear',plotId:target.id});
    const job=store.state.jobs[0];
    store.tick(phase==='travel'?job.start:phase==='work'?job.arrival+1:job.workEnd+1);
    const before=store.exportSave();
    assert.throws(()=>store.dispatch({type,plotId:job.originPlotId}),/عودة جميع آليات/);
    assert.equal(store.exportSave(),before);
    validateSave(store.state);
    store.tick(job.returnEnd+1);
    store.dispatch({type,plotId:job.originPlotId});
    assert.equal(store.state.units.length,3);
    assert.ok(store.state.units.every(u=>u.depotIds?.shujaiya===undefined));
    validateSave(store.state);
  }
});

test('removing a depot rebalances idle machines, preserves all equipment and restores a valid save', () => {
  const {store,storage}=ready();
  const next=addDepot(store);
  store.dispatch({type:'sellBuilding',plotId:0});
  assert.ok(store.state.units.every(u=>u.depotIds?.shujaiya===next));
  validateSave(store.state);
  store.dispatch({type:'removeBuilding',plotId:next});
  let restored=new GameStore(storage,store.state.lastSeen);
  assert.equal(restored.state.units.length,3);
  assert.ok(restored.state.units.every(u=>u.depotIds?.shujaiya===undefined));
  assert.throws(()=>restored.dispatch({type:'clear',plotId:restored.state.districts[0].plots.find(p=>p.status==='rubble')!.id}));
  const replacement=addDepot(restored);
  restored=new GameStore(storage,restored.state.lastSeen);
  assert.ok(restored.state.units.every(u=>u.depotIds?.shujaiya===replacement));
  validateSave(restored.state);
});

test('reset commits a valid fresh save and later ticks cannot restore old progress', () => {
  const { storage, store } = setup();
  const previous = store.state;
  const now = previous.lastSeen + 1000;
  assert.equal(store.reset(now), true);
  assert.notEqual(store.state, previous);
  assert.equal(store.state.currentDistrict, null);
  assert.equal(store.state.jobs.length, 0);
  assert.equal(store.state.coins, newGame(now).coins);
  store.tick(now + 3000);
  const restored = new GameStore(storage, now + 4000);
  assert.equal(restored.state.currentDistrict, null);
  assert.equal(restored.state.coins, newGame(now).coins);
  restored.dispatch({ type: 'enter', districtId: 'shujaiya' });
  assert.equal(restored.state.districts[0].plots.length, 144);
  validateSave(JSON.parse(storage.getItem(SAVE_KEY)!));
});

test('failed reset preserves both the live progress and its stored copy', () => {
  const { storage, store } = setup();
  const state = store.state, raw = storage.getItem(SAVE_KEY);
  storage.setItem = () => { throw new Error('Quota exceeded'); };
  assert.equal(store.reset(state.lastSeen + 1000), false);
  assert.equal(store.state, state);
  assert.equal(storage.getItem(SAVE_KEY), raw);
  assert.match(store.storageError!, /لم نغيّر تقدمك/);
});
function ready() {
  const result = setup(), { store } = result;
  store.dispatch({ type:'place', plotId:0, buildingId:'recycling', orientation:0 });
  store.tick(store.state.lastSeen + 120000);
  for (const kind of ['excavator','bulldozer','truck'] as const) store.dispatch({ type:'buy',kind });
  return result;
}
test('return travel matches departure ground speed at near/far sites and every upgrade and factory face', () => {
  for (const orientation of ORIENTATION_CYCLE) for (const level of [1,2,4]) for (const far of [false,true]) {
    const {store,storage}=ready(), d=store.state.districts[0], source=d.plots[0];
    source.orientation=orientation;
    for (const u of store.state.units) u.level=level;
    const targets=d.plots.filter(p=>p.status==='rubble');
    targets.sort((a,b)=>(Math.abs(a.x-source.x)+Math.abs(a.y-source.y))-(Math.abs(b.x-source.x)+Math.abs(b.y-source.y)));
    const target=far?targets.at(-1)!:targets[0];
    store.dispatch({type:'clear',plotId:target.id});
    const j=store.state.jobs[0], outTime=j.arrival-j.departureAt!, backTime=j.returnEnd-j.returnStartAt!;
    assert.equal(j.workEnd-j.arrival,60000);
    assert.equal(backTime,returnTravelDuration(source,target,outTime));
    const maxLength=(back:boolean)=>Math.max(...(['excavator','bulldozer','truck'] as const).map(k=>routeLength(fleetRoute(source,target,k,back))))+2*CONVOY_GAP;
    const outSpeed=maxLength(false)/(outTime-750),backSpeed=maxLength(true)/(backTime-550-250);
    assert.ok(Math.abs(backSpeed/outSpeed-1)<.001,'matching distance-per-time, not a fixed return delay');
    assert.ok(backTime<outTime*1.2,'return is comparable, not disproportionately slow');
    const before=structuredClone(j), restored=new GameStore(storage,store.state.lastSeen+1);
    assert.deepEqual(restored.state.jobs[0],before,'restoring a mission must not alter its timeline');
    store.tick(j.returnEnd-1);assert.equal(store.state.jobs.length,1);
    store.tick(j.returnEnd);assert.equal(store.state.jobs.length,0);
    assert.ok(store.state.inventory.value>0);
    validateSave(store.state);
  }
});
function addDepot(store: GameStore, finish = true) {
  const plot = store.state.districts[0].plots.find(p => p.status === 'empty')!;
  store.dispatch({ type: 'place', plotId: plot.id, buildingId: 'recycling', orientation: 1 });
  if (finish) store.tick(plot.endsAt);
  return plot.id;
}
function buyTeam(store: GameStore) {
  for (const kind of ['excavator','bulldozer','truck'] as const) store.dispatch({ type:'buy', kind });
}
test('two recycling factories share complete teams and use their own independent portals', () => {
  const { store, storage } = ready();
  buyTeam(store);
  const second = addDepot(store), d = store.state.districts[0];
  for (const id of [0, second]) {
    const team = store.state.units.filter(u => u.depotIds?.[d.id] === id);
    assert.equal(team.length, 3);
    assert.equal(new Set(team.map(u => u.kind)).size, 3);
  }
  const targets = d.plots.filter(p => p.status === 'rubble').slice(0,2);
  for (const p of targets) store.dispatch({ type:'clear', plotId:p.id });
  assert.deepEqual(new Set(store.state.jobs.map(j => j.originPlotId)), new Set([0,second]));
  for (const j of store.state.jobs) {
    assert.ok(j.unitIds.every(id => store.state.units.find(u => u.id === id)!.depotIds![d.id] === j.originPlotId));
    for (const id of j.unitIds) {
      const u = store.state.units.find(u => u.id === id)!;
      const source = d.plots[j.originPlotId], target = d.plots[j.plotId];
      assert.equal(convoyMotion(j, source, target, u.kind, j.returnEnd - 100).visibility, 0);
    }
  }
  validateSave(store.state);
  const restored = new GameStore(storage, store.state.lastSeen);
  assert.deepEqual(restored.state.units, store.state.units);
  assert.deepEqual(restored.state.jobs, store.state.jobs);
});
test('a newly completed depot redistributes idle teams without changing an active trip', () => {
  const { store } = ready();
  buyTeam(store);
  const second = addDepot(store, false);
  // Complete the new factory during the mission, without assuming a slow return.
  store.tick(store.state.districts[0].plots[second].endsAt - 30000);
  const target = store.state.districts[0].plots.find(p => p.status === 'rubble')!;
  store.dispatch({ type:'clear', plotId:target.id });
  const job = structuredClone(store.state.jobs[0]);
  assert.ok(store.state.units.every(u => u.depotIds?.shujaiya === 0));
  store.tick(store.state.districts[0].plots[second].endsAt);
  assert.ok(store.state.jobs.some(j => j.id === job.id));
  const active = store.state.jobs.find(j => j.id === job.id)!;
  assert.equal(active.originPlotId, job.originPlotId);
  assert.equal(active.arrival, job.arrival);
  assert.equal(active.returnEnd, job.returnEnd);
  assert.ok(job.unitIds.every(id => store.state.units.find(u => u.id === id)!.depotIds?.shujaiya === 0));
  assert.equal(store.state.units.filter(u => u.depotIds?.shujaiya === second).length, 3);
  validateSave(store.state);
});
test('three depots distribute uneven equipment counts by kind, with stable idle assignments', () => {
  const { store } = ready();
  addDepot(store); addDepot(store);
  for (let i=0; i<3; i++) buyTeam(store);
  store.dispatch({ type:'buy', kind:'truck' });
  const d = store.state.districts[0], depots = d.plots.filter(p => p.status === 'built' && p.buildingId === 'recycling');
  for (const kind of ['excavator','bulldozer','truck'] as const) {
    const counts = depots.map(p => store.state.units.filter(u => u.kind === kind && u.depotIds?.[d.id] === p.id).length);
    assert.ok(Math.max(...counts) - Math.min(...counts) <= 1);
  }
  const units = structuredClone(store.state.units);
  store.dispatch({ type:'map' }); store.dispatch({ type:'enter', districtId:d.id });
  assert.deepEqual(store.state.units, units);
  validateSave(store.state);
});
test('legacy fleet saves acquire balanced depot homes and keep returning jobs at their original depot', () => {
  const { store, storage } = ready();
  buyTeam(store);
  const second = addDepot(store);
  store.dispatch({ type:'clear', plotId:2 });
  const job = structuredClone(store.state.jobs[0]);
  for (const u of store.state.units) delete u.depotIds;
  storage.setItem(SAVE_KEY, store.exportSave());
  const restored = new GameStore(storage, store.state.lastSeen);
  assert.deepEqual(restored.state.jobs[0], job);
  for (const id of job.unitIds) assert.equal(restored.state.units.find(u => u.id === id)!.depotIds?.shujaiya, job.originPlotId);
  assert.equal(restored.state.units.filter(u => u.depotIds?.shujaiya === second).length, 3);
  validateSave(restored.state);
  const invalid = structuredClone(restored.state);
  invalid.units[0].depotIds!.shujaiya = 2;
  assert.throws(() => validateSave(invalid));
});
test('each district keeps a separate depot assignment for the shared campaign fleet', () => {
  const { store, storage } = ready();
  buyTeam(store); addDepot(store);
  const previous = store.state.units.map(u => u.depotIds!.shujaiya);
  const first = store.state.districts[0];
  for (const p of first.plots) if (p.status === 'rubble') p.status = 'empty';
  for (const p of first.projects) p.status = 'complete';
  store.dispatch({ type:'claim' });
  store.dispatch({ type:'enter', districtId:'tuffah' });
  store.dispatch({ type:'place', plotId:0, buildingId:'recycling', orientation:0 });
  store.tick(store.state.districts[1].plots[0].endsAt);
  assert.ok(store.state.units.every(u => u.depotIds?.tuffah === 0));
  assert.deepEqual(store.state.units.map(u => u.depotIds!.shujaiya), previous);
  validateSave(store.state);
  assert.deepEqual(new GameStore(storage, store.state.lastSeen).state.units, store.state.units);
});
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
  assert.equal(state.districts.length,14);
  assert.equal(state.districts.filter(d=>d.unlocked).length,1);
  assert.equal(state.districts[0].plots.length,144);
  assert.equal(state.districts[0].plots.filter(p=>p.status==='rubble').length,133);
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
test('former district saves expand once without losing earned final-chapter access', () => {
  for (const finalClaimed of [false, true]) {
    const storage = new MemoryStorage(), now = Date.now() + 600000;
    const old = newGame(now);
    old.districts = old.districts.filter(d => d.id !== 'shati');
    for (const [i,d] of old.districts.entries()) {
      d.unlocked = true; d.claimed = i < 12 || finalClaimed;
    }
    old.currentDistrict = 'rashid';
    old.coins = 1234567;
    old.districts[12].camera = { x: 50, y: 60, zoom: .8 };
    validateSave(old);
    const prior = structuredClone(old.districts);
    storage.setItem(SAVE_KEY, JSON.stringify(old));
    const migrated = new GameStore(storage, now);
    assert.equal(migrated.state.districts.length, 14);
    assert.equal(migrated.state.coins, old.coins);
    assert.equal(migrated.state.currentDistrict, 'rashid');
    assert.deepEqual(migrated.state.districts.filter(d => d.id !== 'shati'), prior);
    assert.equal(migrated.state.districts.find(d => d.id === 'shati')!.unlocked, true);
    assert.equal(migrated.state.districts.find(d => d.id === 'shati')!.claimed, false);
    assert.equal(migrated.state.districts.at(-1)!.id, 'rashid');
    validateSave(migrated.state);
    assert.deepEqual(new GameStore(storage, now).state, migrated.state);
  }
  const storage = new MemoryStorage(), old = newGame();
  old.districts = old.districts.filter(d => d.id !== 'shati');
  storage.setItem(SAVE_KEY, JSON.stringify(old));
  const restored = new GameStore(storage, old.lastSeen);
  assert.equal(restored.state.districts.find(d => d.id === 'shati')!.unlocked, false);
  const damaged = structuredClone(old);
  damaged.districts[12].id = 'unknown';
  assert.throws(() => validateSave(damaged));
});

test('complete fourteen-district campaign via real actions and timers, hourly trade and final reward', () => {
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
    for(const def of projects(district.id==='rashid')) {
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
