import test from 'node:test';
import assert from 'node:assert/strict';
import { GameStore, newGame } from '../src/engine';
import { beginAd, markAdEarned, recoverAd, settleEarnedAd } from '../src/ads/ledger';
import { ADS } from '../src/ads/config';
import { validateSave } from '../src/save';
import { WHEEL_COOLDOWN, WHEEL_SPIN_MS } from '../src/rewards';
import { webAdProvider } from '../src/ads/web';

function fixture() {
  let value: string | null = null, writes = 0, failAt = Infinity;
  const storage = { getItem: () => value, setItem: (_k: string, v: string) => { if (++writes === failAt) throw Error('quota'); value = v; } };
  const store = new GameStore(storage, Date.now() + 100000);
  return { store, storage, failAfter(n: number) { failAt = writes + n; }, raw: () => value! };
}
test('legacy saves remain valid and interrupted ad requests never create rewards', () => {
  const { store, storage } = fixture(), coins = store.state.coins;
  const id = store.beginRewardedAd('coins');
  assert.throws(() => store.beginRewardedAd('gems'), /قيد المعالجة/);
  const reloaded = new GameStore(storage, store.state.lastSeen);
  assert.equal(reloaded.state.coins, coins); assert.equal(reloaded.state.ads?.pending, undefined);
  assert.throws(() => reloaded.earnedRewardedAd(id), /صلاحية/);
  validateSave(reloaded.state);
});
test('only earned requests credit once and cooldown applies across placements', () => {
  const { store, storage } = fixture(), coins = store.state.coins;
  const id = store.beginRewardedAd('coins');
  store.earnedRewardedAd(id); store.earnedRewardedAd(id);
  assert.equal(store.state.coins, coins + ADS.rewards.coins);
  assert.throws(() => store.beginRewardedAd('gems'), /ثانية/);
  const restored = new GameStore(storage, store.state.lastSeen); restored.earnedRewardedAd(id);
  assert.equal(restored.state.coins, coins + ADS.rewards.coins);
  restored.tick(restored.state.ads!.nextAt);
  const next = restored.beginRewardedAd('gems'); restored.earnedRewardedAd(next);
  assert.equal(restored.state.gems, ADS.rewards.gems);
  validateSave(restored.state);
});
test('durable earned marker recovers exactly once after credit write failure', () => {
  const f = fixture(), coins = f.store.state.coins, id = f.store.beginRewardedAd('coins');
  f.failAfter(2); // earned marker succeeds, wallet credit fails.
  assert.throws(() => f.store.earnedRewardedAd(id));
  assert.equal(f.store.state.coins, coins);
  assert.equal(JSON.parse(f.raw()).ads.pending.status, 'earned');
  const r = new GameStore(f.storage, f.store.state.lastSeen);
  assert.equal(r.state.coins, coins + ADS.rewards.coins);
  assert.equal(new GameStore(f.storage, r.state.lastSeen).state.coins, r.state.coins);
  validateSave(r.state);
});
test('cancelled and foreign requests cannot steal another placement', () => {
  const { store } = fixture(), before = store.state.coins, id = store.beginRewardedAd('coins');
  assert.throws(() => store.earnedRewardedAd('ad-999'));
  store.cancelRewardedAd(id); assert.throws(() => store.earnedRewardedAd(id));
  assert.equal(store.state.coins, before); assert.equal(store.state.gems, 0);
});
test('bonus spin is durable, single-use and leaves free-wheel deadline unchanged', () => {
  const { store, storage } = fixture();
  store.dispatch({ type: 'spin' }); const freeAt = store.state.wheel!.nextAt;
  assert.throws(() => store.beginRewardedAd('wheel'), /دوران/);
  store.tick(store.state.lastSeen + WHEEL_SPIN_MS);
  const id = store.beginRewardedAd('wheel'); store.earnedRewardedAd(id);
  const restored = new GameStore(storage, store.state.lastSeen);
  assert.equal(restored.state.ads!.spinCredits, 1);
  restored.dispatch({ type: 'spin' });
  assert.equal(restored.state.wheel!.nextAt, freeAt); assert.equal(restored.state.wheel!.bonus, true);
  assert.equal(restored.state.ads!.spinCredits, 0); validateSave(restored.state);
  restored.tick(restored.state.wheel!.startedAt + WHEEL_SPIN_MS);
  assert.throws(() => restored.dispatch({ type: 'spin' }), /انتظر/);
  restored.tick(freeAt); restored.dispatch({ type: 'spin' });
  assert.equal(restored.state.wheel!.nextAt, freeAt + WHEEL_COOLDOWN);
  validateSave(new GameStore(storage, restored.state.lastSeen).state);
});
test('malformed ad state and bonus wheel records are rejected without replacing saves', () => {
  for (const ads of [{ nextAt: -1, spinCredits: 0 }, { nextAt: 0, spinCredits: 2 },
    { nextAt: 0, spinCredits: 0, pending: { id: 'ad-9', placement: 'money', status: 'earned', createdAt: 0 } }]) {
    const s = newGame(); (s as any).ads = ads; assert.throws(() => validateSave(s));
  }
  const s = newGame(), id = beginAd(s, 'gems', s.lastSeen); markAdEarned(s, id);
  recoverAd(s, s.lastSeen); assert.equal(s.gems, ADS.rewards.gems);
  assert.equal(settleEarnedAd(s, s.lastSeen), false);
});

type Fn = (e: any) => void;
function fakeGPT(empty = false, unsupported = false) {
  const listeners = new Map<string, Set<Fn>>(), slot = { addService() { return slot; } };
  let destroyed = 0;
  const fire = (event: string, extra = {}) => { for (const fn of listeners.get(event) ?? []) fn({ slot, ...extra }); };
  const pub = { addEventListener: (n: string, fn: Fn) => { if (!listeners.has(n)) listeners.set(n, new Set()); listeners.get(n)!.add(fn); },
    removeEventListener: (n: string, fn: Fn) => { listeners.get(n)?.delete(fn); } };
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { onLine: true } });
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {
    setTimeout: (fn: () => void, ms: number) => setTimeout(fn, ms === 750 ? 1 : ms),
    googletag: { cmd: { push: (fn: () => void) => fn() }, enums: { OutOfPageFormat: { REWARDED: 1 } },
      defineOutOfPageSlot: () => unsupported ? null : slot, pubads: () => pub, enableServices() {},
      display() { if (empty) fire('slotRenderEnded', { isEmpty: true }); else fire('rewardedSlotReady', { makeRewardedVisible: () => true }); },
      destroySlots() { destroyed++; } },
  } });
  Object.defineProperty(globalThis, 'document', { configurable: true, value: {
    createElement: () => ({ remove() {} }), head: { append: (e: any) => queueMicrotask(() => e.onload()) },
  } });
  return { fire, count: () => [...listeners.values()].reduce((n, s) => n + s.size, 0), destroyed: () => destroyed };
}
test('GPT adapter ignores unrelated slots, video-complete and duplicate grant events; cleans up after reordered close', async () => {
  const g = fakeGPT(); let grants = 0;
  const ad = await webAdProvider.prepare('ad-1', 'coins', () => grants++);
  g.fire('rewardedSlotGranted'); assert.equal(grants, 0); // Cannot earn before show.
  const result = ad.show();
  g.fire('rewardedSlotGranted', { slot: {} }); g.fire('rewardedSlotVideoCompleted');
  assert.equal(grants, 0);
  g.fire('rewardedSlotClosed'); g.fire('rewardedSlotGranted'); g.fire('rewardedSlotGranted');
  assert.equal(await result, 'earned'); assert.equal(grants, 1);
  assert.equal(g.count(), 0); assert.equal(g.destroyed(), 1);
});
test('GPT close without grant never pays, no-fill/unsupported/offline fail explicitly', async () => {
  let grants = 0; let g = fakeGPT(); const ad = await webAdProvider.prepare('ad-2', 'gems', () => grants++);
  const result = ad.show(); g.fire('rewardedSlotClosed'); assert.equal(await result, 'cancelled'); assert.equal(grants, 0);
  g = fakeGPT(true); await assert.rejects(webAdProvider.prepare('ad-3', 'gems', () => grants++), /NO_FILL/); assert.equal(g.count(), 0);
  fakeGPT(false, true); await assert.rejects(webAdProvider.prepare('ad-4', 'gems', () => grants++), /UNSUPPORTED/);
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { onLine: false } });
  await assert.rejects(webAdProvider.prepare('ad-5', 'gems', () => grants++), /OFFLINE/); assert.equal(grants, 0);
});
