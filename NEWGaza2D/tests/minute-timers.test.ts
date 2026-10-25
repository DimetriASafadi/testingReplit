import test from 'node:test';
import assert from 'node:assert/strict';
import { GameStore, SAVE_KEY } from '../src/engine';
import { BUILDINGS, building, projects } from '../src/catalog';
import { activityDuration, buildingIncomePeriod, setMinuteTestTimers } from '../src/game-timing';
import { validateSave } from '../src/save';
import { outgoingTravelDuration, returnTravelDuration } from '../src/fleet-motion';
import { projectIncome } from '../src/economy';
import { clearingSeconds } from '../src/construction-rules';

class MemoryStorage {
  value: string | null = null;
  getItem() { return this.value; }
  setItem(_key: string, value: string) { this.value = value; }
}
function setup() {
  const storage = new MemoryStorage(), store = new GameStore(storage);
  store.state.coins=1000000;store.state.districts[0].difficulty='easy';
  store.dispatch({ type: 'enter', districtId: 'shujaiya' });
  return { store, storage };
}
test('construction uses production times while unrelated projects and income retain their minute profile', () => {
  assert.equal(buildingIncomePeriod(), 60000);
  for (const b of BUILDINGS) {
    const { store } = setup(), p = store.state.districts[0].plots[0];
    store.dispatch({ type: 'place', plotId: p.id, buildingId: b.id, orientation: 0 });
    assert.equal(p.endsAt - p.startedAt, b.duration*1000);
    store.tick(p.endsAt - 1); assert.equal(p.status, 'building');
    store.tick(p.endsAt); assert.equal(p.status, 'built');
    if (b.income) {
      assert.throws(() => store.dispatch({ type: 'collectBuilding', plotId: p.id }), /دقيقة/);
      store.tick(p.incomeAt + 60000);
      const coins = store.state.coins;
      store.dispatch({ type: 'collectBuilding', plotId: p.id });
      assert.equal(store.state.coins - coins, Math.floor(b.income*.7));
    }
    validateSave(store.state);
  }
  for (const finale of [false, true]) {
    const { store } = setup(), d = store.state.districts[0];
    for (const def of projects(finale)) {
      assert.equal(activityDuration(def.duration * 1000), 60000);
      if (def.period) assert.equal(activityDuration(def.period * 1000), 60000);
      if (finale) continue; // Coastal catalog uses the same centralized override.
      store.dispatch({ type: 'project', projectId: def.id });
      const p = d.projects.find(x => x.id === def.id)!;
      assert.equal(p.endsAt - p.startedAt, 60000);
      store.tick(p.endsAt);
      if (def.income) {
        if (def.period) store.tick(p.incomeAt + 60000);
        const before = store.state.coins;
        store.dispatch({ type: 'collectProject', projectId: def.id });
        assert.equal(store.state.coins - before, projectIncome(d,def));
      }
    }
    validateSave(store.state);
  }
});
test('convoy travel follows route distance while clearing uses size, with offline settlement exactly once', () => {
  const { store, storage } = setup(), d = store.state.districts[0], p = d.plots[0];
  store.dispatch({ type: 'place', plotId: 0, buildingId: 'recycling', orientation: 0 });
  store.tick(p.endsAt);
  store.dispatch({ type: 'place', plotId: 1, buildingId: 'equipment_store', orientation: 0 });
  store.tick(d.plots[1].endsAt);
  for (const kind of ['excavator', 'bulldozer', 'truck'] as const) store.dispatch({ type: 'buy', kind });
  const target = d.plots.find(x => x.status === 'rubble')!;
  store.dispatch({ type: 'clear', plotId: target.id });
  const j = store.state.jobs[0];
  const travel = outgoingTravelDuration(d.plots[1], target);
  assert.equal(j.arrival - j.departureAt!, travel);
  assert.ok(travel < 60000);
  assert.equal(j.workEnd - j.arrival, clearingSeconds(building(target.buildingId).floors)*1000);
  assert.equal(j.toolsReturnEnd! - j.returnStartAt!, returnTravelDuration(d.plots[1], target, travel));
  const restored = new GameStore(storage, j.workEnd);
  assert.equal(restored.state.districts[0].plots[target.id].status, 'empty');
  assert.equal(restored.state.inventory.value, 0);
  restored.tick(j.returnEnd);
  const value = restored.state.inventory.value;
  assert.ok(value > 0); assert.equal(restored.state.jobs.length, 0);
  const again = new GameStore(storage, j.returnEnd + 60000);
  assert.equal(again.state.inventory.value, value);
  validateSave(again.state);
});
test('existing counters retain deadlines and earned income without resetting on reload', () => {
  setMinuteTestTimers(false);
  const { store, storage } = setup(), d = store.state.districts[0];
  store.dispatch({ type: 'place', plotId: 0, buildingId: 'food_shop', orientation: 0 });
  store.tick(d.plots[0].endsAt);
  store.dispatch({ type: 'project', projectId: 'water' });
  const now = store.state.lastSeen + 7200000 + 30000;
  // Keep a long in-progress utility fixture and two accrued commercial periods.
  const ps = d.projects.find(p => p.id === 'water')!;
  ps.startedAt = now - 30000; ps.endsAt = now + 3570000;
  store.state.lastSeen = now;
  d.plots[0].incomeAt = now - 7200000;
  storage.setItem(SAVE_KEY, JSON.stringify(store.state));
  setMinuteTestTimers(true);
  const migrated = new GameStore(storage, now);
  const mp = migrated.state.districts[0].projects.find(p => p.id === 'water')!;
  assert.equal(mp.endsAt - mp.startedAt, 3600000);
  assert.equal(mp.endsAt - now, 3570000);
  const coins = migrated.state.coins;
  migrated.dispatch({ type: 'collectBuilding', plotId: 0 });
  assert.equal(migrated.state.coins - coins, Math.floor(building('food_shop').income*.7));
  const reloaded = new GameStore(storage, now + 1000);
  assert.equal(reloaded.state.districts[0].projects.find(p => p.id === 'water')!.endsAt, mp.endsAt);
  validateSave(reloaded.state);
});
