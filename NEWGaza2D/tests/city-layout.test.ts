import test from 'node:test';
import assert from 'node:assert/strict';
import { cityLayout, LOT_COUNT } from '../src/city-layout';
import { DISTRICTS, EQUIPMENT } from '../src/catalog';
import { GameStore, SAVE_KEY, newGame, districtProgress } from '../src/engine';
import { validateSave } from '../src/save';
import { fleetRoute, routeLength, routePoint } from '../src/fleet-motion';

test('expanded districts have stable unique plots, mixed 1/2/4-lot blocks and no streets between adjoining lots', () => {
  const signatures = new Set<string>();
  for (const d of DISTRICTS) {
    const l = cityLayout(d.id);
    assert.equal(l.cells.length, LOT_COUNT);
    assert.equal(new Set(l.cells.map(p => `${p.x}:${p.y}`)).size, LOT_COUNT);
    assert.deepEqual(new Set(l.blocks.map(b => b.plotIds.length)), new Set([1,2,4]));
    assert.ok(l.extent > 28);
    assert.equal(cityLayout(d.id), l, 'reloads use the same deterministic layout');
    signatures.add(JSON.stringify(l.cells));
    for (const b of l.blocks) {
      assert.ok(!l.roadX.some(x => x > b.x0 && x < b.x1));
      assert.ok(!l.roadY.some(y => y > b.y0 && y < b.y1));
      const cells = b.plotIds.map(id => l.cells[id]);
      for (const p of cells) {
        assert.ok(p.x >= b.x0 && p.x + 2 <= b.x1 + .00001);
        assert.ok(p.y >= b.y0 && p.y + 2 <= b.y1 + .00001);
      }
    }
  }
  assert.equal(signatures.size, DISTRICTS.length);
});

test('legacy expansion retains money, buildings, job deadlines, equipment and earned access by identity', () => {
  const now = 150000, state = newGame(now);
  for (const d of state.districts) {
    d.plots = d.plots.slice(0,64).map(({ layoutId: _, ...p }) => ({ ...p, x: (p.id%8)*2, y: Math.floor(p.id/8)*2 }));
  }
  const d = state.districts[0];
  state.coins = 875321; state.currentDistrict = d.id;
  d.claimed = true; state.districts[1].unlocked = true;
  Object.assign(d.plots[0], { status:'built', buildingId:'recycling', orientation:1, incomeAt:100000 });
  Object.assign(d.plots[1], { status:'building', buildingId:'small_house', startedAt:100000, endsAt:220000 });
  state.sequence = 4;
  state.units = (['excavator','bulldozer','truck'] as const).map((kind,i) => ({ id:`unit-${i+1}`,kind,level:2,purchasePrice:EQUIPMENT[kind].cost }));
  state.jobs = [{ id:'job-4', districtId:d.id, plotId:27, originPlotId:0, unitIds:state.units.map(u=>u.id),
    start:100000, arrival:120000, workEnd:180000, returnEnd:220000, cleared:false, value:30000 }];
  validateSave(state);
  const old = structuredClone(state);
  let raw = JSON.stringify(state);
  const storage = { getItem:()=>raw, setItem:(_key:string,value:string)=>{raw=value} };
  const store = new GameStore(storage,now);
  validateSave(store.state);
  assert.equal(store.state.coins,old.coins);
  assert.deepEqual(store.state.units.map(({ depotIds: _, home: __, ...u }) => u),old.units);
  assert.ok(store.state.units.every(u => u.depotIds?.[d.id] === 0));
  assert.deepEqual(store.state.jobs,old.jobs);
  assert.equal(store.state.districts[0].claimed,true);
  assert.equal(store.state.districts[1].unlocked,true);
  assert.equal(store.state.districts[2].unlocked,false);
  for (let i=0;i<64;i++) {
    const { x:_,y:__,layoutId:___,...p } = store.state.districts[0].plots[i];
    const { x:____,y:_____,...previous } = old.districts[0].plots[i];
    assert.deepEqual(p,previous);
  }
  assert.equal(store.state.districts[0].plots[143].status,'rubble');
  const reloaded = new GameStore(storage,now);
  assert.deepEqual(reloaded.state,store.state,'a second reload never re-expands or resets');
  assert.equal(JSON.parse(raw).districts[0].plots.length,144);
  assert.equal(SAVE_KEY,'newgaza2d-save-v1');
});

test('expanded progress requires rebuilt parcels and validates new edge plots', () => {
  const s=newGame(100000),d=s.districts[0];
  assert.ok(districtProgress(d)<2);
  for(const p of d.plots) p.status='empty';
  for(const p of d.projects) p.status='complete';
  assert.ok(districtProgress(d)<100);
  validateSave(s);
  d.plots[143].x+=.01;
  assert.throws(()=>validateSave(s));
});

test('an earlier expanded layout upgrades without losing edge-plot construction', () => {
  const s=newGame(100000);
  for(const d of s.districts) {
    const previous=cityLayout(d.id,1);
    for(const p of d.plots) {p.x=previous.cells[p.id].x;p.y=previous.cells[p.id].y;}
  }
  Object.assign(s.districts[0].plots[143],{status:'building',buildingId:'small_house',startedAt:90000,endsAt:200000});
  validateSave(s);
  let raw=JSON.stringify(s);
  const store=new GameStore({getItem:()=>raw,setItem:(_k,v)=>{raw=v}},100000);
  validateSave(store.state);
  const p=store.state.districts[0].plots[143],cell=cityLayout(s.districts[0].id).cells[143];
  assert.equal(p.status,'building');assert.equal(p.endsAt,200000);
  assert.equal(p.x,cell.x);assert.equal(p.y,cell.y);
});

test('new-block routes stay out of unrelated building interiors for both factory orientations', () => {
  const d=newGame(100000).districts[0],source=d.plots[0];
  for(const orientation of [0,1] as const) {
    source.orientation=orientation;
    for(const id of [1,27,63,64,91,143]) for(const kind of ['excavator','bulldozer','truck'] as const) {
      const target=d.plots[id],route=fleetRoute(source,target,kind);
      const length=routeLength(route);
      for(let distance=0;distance<length;distance+=.12) {
        const [x,y]=routePoint(route,distance);
        const collision=d.plots.find(p=>p.id!==source.id && p.id!==target.id &&
          x>p.x+.28 && x<p.x+1.72 && y>p.y+.28 && y<p.y+1.72);
        assert.equal(collision,undefined,`route to ${id} crosses another plot`);
      }
    }
  }
});
