import test from 'node:test';
import assert from 'node:assert/strict';
import { projectIncome } from '../src/economy';
import { GameStore } from '../src/engine';
import { BUILDINGS, building, projects } from '../src/catalog';
import { buildingIncomePeriod, activityDuration, setMinuteTestTimers } from '../src/game-timing';
import { districtIncome } from '../src/district-income';
import { validateSave } from '../src/save';
import { buildingIncomeDetails } from '../src/building-income';
import { buildingAccruedIncome } from '../src/building-disposal';

class Storage {
  value: string | null = null;
  getItem() { return this.value; }
  setItem(_key: string, value: string) { this.value = value; }
}
for (const testMode of [true, false]) {
  test(`displayed building payout equals single, bulk and disposal calculation (${testMode ? 'minute' : 'normal'})`, () => {
    setMinuteTestTimers(testMode);
    const now = Date.now() + 600000, storage = new Storage(), store = new GameStore(storage, now);
    store.dispatch({ type: 'enter', districtId: 'shujaiya' });
    const d = store.state.districts[0], plot = d.plots[0], period = buildingIncomePeriod();
    Object.assign(plot, { status: 'built', buildingId: 'food_shop', startedAt: now - period * 6,
      endsAt: now - period * 5, incomeAt: now - period * 3.25 });
    const shown = buildingIncomeDetails(plot, now);
    assert.equal(shown.periods, 1);
    assert.equal(shown.amount, building(plot.buildingId).income);
    assert.ok(shown.nextAt <= now);
    assert.equal(buildingIncomeDetails(plot, now + period * 100).amount, shown.amount);
    assert.equal(districtIncome(d, now).total, shown.amount);
    assert.equal(buildingAccruedIncome(plot, now), shown.amount);
    const before = store.state.coins;
    store.dispatch({ type: 'collectBuilding', plotId: plot.id });
    assert.equal(store.state.coins - before, shown.amount);
    assert.equal(buildingIncomeDetails(plot, now).amount, 0);
    assert.equal(buildingIncomeDetails(plot, now).nextAt, now + period);
    assert.equal(buildingIncomeDetails(plot, now + period - 1).amount, 0);
    assert.equal(buildingIncomeDetails(plot, now + period).amount, Math.floor(shown.perPeriod*.45));
    assert.equal(buildingIncomeDetails({ ...plot, status: 'building' }, now + period).amount, 0);
    validateSave(store.state);
  });
}
for (const testMode of [true, false]) {
  test(`collect all is district-scoped, persistent and exactly once (${testMode ? 'minute' : 'normal'} timers)`, () => {
    setMinuteTestTimers(testMode);
    const now = Date.now() + 600000, storage = new Storage(), store = new GameStore(storage, now);
    store.dispatch({ type: 'enter', districtId: 'shujaiya' });
    const d = store.state.districts[0], other = store.state.districts[1];
    const period = buildingIncomePeriod();
    const home = building('food_shop');
    for (const [i, age] of [[0, 3.25], [1, .5]] as const) Object.assign(d.plots[i], {
      status: 'built', buildingId: home.id, startedAt: now - period * 5, endsAt: now - period * 4,
      incomeAt: now - period * age,
    });
    Object.assign(d.plots[2], { status: 'building', buildingId: home.id, startedAt: now - 1000, endsAt: now + 60000, incomeAt: 0 });
    Object.assign(other.plots[0], { status: 'built', buildingId: home.id, startedAt: now - period * 5, endsAt: now - period * 4, incomeAt: now - period * 3 });
    const definitions = projects(false);
    for (const id of ['power', 'services', 'commerce', 'farm', 'industry']) {
      const p = d.projects.find(p => p.id === id)!;
      const def = definitions.find(p => p.id === id)!;
      Object.assign(p, { status: def.income > 0 && !def.period ? 'ready' : 'complete',
        startedAt: now - period * 5, endsAt: now - period * 4,
        incomeAt: id === 'commerce' ? now - activityDuration(def.period * 1000) * 2.5 : now - period * 4 });
    }
    const inventory = structuredClone(store.state.inventory), outside = structuredClone(other);
    validateSave(store.state);
    const before = store.state.coins, available = districtIncome(d, now);
    const expected = home.income + ['commerce', 'farm', 'industry'].reduce((sum, id) => {
      const def = definitions.find(p => p.id === id)!; return sum + projectIncome(d,def);
    }, 0);
    assert.equal(available.total, expected);
    assert.equal(available.count, 4);
    store.dispatch({ type: 'collectAllIncome' });
    assert.equal(store.state.coins, before + expected);
    assert.equal(d.plots[0].incomeAt, now);
    assert.equal(d.projects.find(p => p.id === 'commerce')!.incomeAt, now);
    assert.equal(d.projects.find(p => p.id === 'farm')!.status, 'complete');
    assert.equal(d.projects.find(p => p.id === 'industry')!.status, 'complete');
    assert.deepEqual(store.state.districts[1], outside);
    assert.deepEqual(store.state.inventory, inventory);
    assert.equal(districtIncome(d, now).total, 0);
    assert.throws(() => store.dispatch({ type: 'collectAllIncome' }), /لا يوجد دخل جاهز/);
    const restored = new GameStore(storage, now);
    assert.equal(restored.state.coins, before + expected);
    assert.throws(() => restored.dispatch({ type: 'collectBuilding', plotId: 0 }), /يجهز الدخل/);
    restored.tick(now + period / 2);
    assert.equal(districtIncome(restored.state.districts[0], restored.state.lastSeen).plots.length, 1);
    restored.dispatch({ type: 'map' });
    assert.throws(() => restored.dispatch({ type: 'collectAllIncome' }), /اختر حيًا/);
    validateSave(restored.state);
  });
}
test('only economic producers earn income; saved homes cannot pay through any collection path', () => {
  const producers = [
    'glass','cement','steel','asphalt','food_factory',
    'wheat','citrus','olive','vegetables','strawberry','cattle','palms','corn',
    'food_shop','clothes_shop','shoe_shop','traditional_mall','modern_mall',
    'falafel','shawarma','western_food','luxury_restaurant','cafe','snack_kiosk',
    'sports_club','recreation_park',
  ];
  assert.deepEqual(BUILDINGS.filter(b => b.income > 0).map(b => b.id).sort(), producers.sort());
  setMinuteTestTimers(true);
  const now = Date.now() + 600000, storage = new Storage(), store = new GameStore(storage, now);
  store.dispatch({ type: 'enter', districtId: 'shujaiya' });
  const d = store.state.districts[0], homes = BUILDINGS.filter(b => b.category === 'housing');
  for (const [i, def] of homes.entries()) Object.assign(d.plots[i], {
    status: 'built', buildingId: def.id, startedAt: now - 360000, endsAt: now - 300000, incomeAt: now - 240000,
  });
  const before = store.state.coins;
  storage.setItem('newgaza2d-save-v1', JSON.stringify(store.state));
  const restored = new GameStore(storage, now + 1000);
  for (const [i, def] of homes.entries()) {
    const plot = restored.state.districts[0].plots[i];
    assert.equal(plot.buildingId, def.id);
    assert.equal(buildingIncomeDetails(plot, restored.state.lastSeen).amount, 0);
    assert.equal(buildingAccruedIncome(plot, restored.state.lastSeen), 0);
    assert.throws(() => restored.dispatch({ type: 'collectBuilding', plotId: plot.id }), /لا ينتج دخلًا/);
  }
  assert.equal(districtIncome(restored.state.districts[0], restored.state.lastSeen).total, 0);
  assert.throws(() => restored.dispatch({ type: 'collectAllIncome' }), /لا يوجد دخل جاهز/);
  assert.equal(restored.state.coins, before);
  const plot = restored.state.districts[0].plots[0];
  const sale = Math.floor(building(plot.buildingId).cost / 2);
  restored.dispatch({ type: 'sellBuilding', plotId: plot.id });
  assert.equal(restored.state.coins, before + sale);
});
