import { test } from 'node:test';
import assert from 'node:assert/strict';

test('opt-in diagnostics stay bounded, exclude background gaps and never touch saves', () => {
  const descriptors = new Map<string, PropertyDescriptor | undefined>();
  const install = (key: string, value: unknown) => {
    descriptors.set(key, Object.getOwnPropertyDescriptor(globalThis, key));
    Object.defineProperty(globalThis, key, { configurable: true, value, writable: true });
  };
  const storage = new Map<string, string>([
    ['newgaza2d-save-v1', 'user-save-do-not-change'], ['newgaza2d-performance-enabled', 'true'],
  ]);
  const writes: string[] = [], events = new Map<string, () => void>();
  let now = 100, interval: (() => void) | undefined, frame: (() => void) | undefined, detachCount = 0;
  const nodes = new Map<string, any>();
  const root = {
    style: { cssText: '' }, dir: '', id: '', innerHTML: '',
    querySelector: (selector: string) => {
      if (!nodes.has(selector)) nodes.set(selector, { value: '', focus() {}, select() {} });
      return nodes.get(selector);
    },
    showModal() {}, close() {}, remove() {}, addEventListener() {},
  };
  const doc = { hidden: false, querySelector: () => null, createElement: () => root,
    body: { append() {} }, addEventListener: (name: string, cb: () => void) => events.set(name, cb) };
  try {
    install('window', { setInterval: (cb: () => void) => { interval = cb; return 1; },
      addEventListener: (name: string, cb: () => void) => events.set(name, cb) });
    install('clearInterval', () => { interval = undefined; });
    install('document', doc);
    install('location', { search: '' });
    install('navigator', { userAgent: 'test-WebView', hardwareConcurrency: 4 });
    install('performance', { now: () => now, timeOrigin: 1000 });
    install('devicePixelRatio', 2); install('innerWidth', 390); install('innerHeight', 844);
    install('localStorage', {
      getItem: (key: string) => storage.get(key) ?? null,
      setItem: (key: string, value: string) => { writes.push(key); storage.set(key, value); },
    });
    const path = require.resolve('../src/performance-diagnostics');
    let diagnostics = require(path) as typeof import('../src/performance-diagnostics');
    diagnostics.connectDiagnostics(() => ({ renderer: 'WebGL', built: 140 }), callback => {
      frame = callback; return () => { detachCount++; frame = undefined; };
    });
    frame!(); now += 33; frame!();
    now += 33; frame!(); interval!();
    let sessions = JSON.parse(storage.get('newgaza2d-performance-v1')!);
    assert.equal(sessions[0].samples[0].frameWindow.frames, 2);
    assert.equal(sessions[0].samples[0].heapUsedBytes, null);
    assert.equal(sessions[0].environment.deviceMemoryGb, null);
    doc.hidden = true; events.get('visibilitychange')!();
    now += 120000; frame!(); doc.hidden = false; events.get('visibilitychange')!();
    frame!(); now += 34; frame!(); interval!();
    sessions = JSON.parse(storage.get('newgaza2d-performance-v1')!);
    assert.equal(sessions[0].samples.at(-1).frameWindow.maxMs, 34);
    for (let i = 0; i < 100; i++) { now += 5000; interval!(); diagnostics.diagnosticEvent('status'); }
    interval!();
    sessions = JSON.parse(storage.get('newgaza2d-performance-v1')!);
    assert.equal(sessions[0].samples.length, 60);
    assert.equal(sessions[0].events.length, 60);
    diagnostics.openDiagnostics();
    nodes.get('#perf-toggle').onclick();
    assert.equal(detachCount, 1);
    assert.equal(interval, undefined);
    assert.equal(storage.get('newgaza2d-performance-enabled'), 'false');
    assert(writes.every(key => key.startsWith('newgaza2d-performance')));
    assert.equal(storage.get('newgaza2d-save-v1'), 'user-save-do-not-change');
    // Subsequent disabled startup must attach no frame listener or timer.
    delete require.cache[path];
    diagnostics = require(path);
    diagnostics.connectDiagnostics(() => ({}), () => { throw Error('disabled subscribed'); });
    assert.equal(interval, undefined);
    // Eight independent document starts retain only six sessions.
    storage.set('newgaza2d-performance-enabled', 'true');
    for (let i = 0; i < 8; i++) { delete require.cache[path]; require(path); }
    assert.equal(JSON.parse(storage.get('newgaza2d-performance-v1')!).length, 6);
  } finally {
    for (const [key, descriptor] of descriptors) {
      if (descriptor) Object.defineProperty(globalThis, key, descriptor);
      else Reflect.deleteProperty(globalThis, key);
    }
  }
});
