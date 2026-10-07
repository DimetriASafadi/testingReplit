import test from 'node:test';
import assert from 'node:assert/strict';
import { CanvasContacts, releaseStaleTouches } from '../src/canvas-contacts';
const pointer = (id: number, identifier: number, wasTouch = true) => ({
  id, identifier, wasTouch, isDown: true,
  reset() { this.isDown = false; },
});
test('UI fingers and stale engine flags cannot become canvas pinch contacts', () => {
  const c = new CanvasContacts<ReturnType<typeof pointer>>();
  const real = pointer(3,30), ghost = pointer(1,10), ui = pointer(2,20);
  c.start(real);
  assert.deepEqual(c.live(new Set([30,20])), [real]);
  releaseStaleTouches([ghost,ui,real],new Set([30,20]));
  assert.equal(ghost.isDown,false); assert.equal(ui.isDown,true);
  assert.deepEqual(c.live(new Set([30])),[real]);
});
test('pinch release leaves one real contact; cancellation, reuse and UI reset discard old contacts', () => {
  const c = new CanvasContacts<ReturnType<typeof pointer>>();
  const a=pointer(1,11),b=pointer(2,22);
  c.start(a);c.start(b);
  assert.equal(c.live(new Set([11,22])).length,2);
  c.end(b);b.isDown=false;
  assert.deepEqual(c.live(new Set([11])),[a]);
  a.identifier=33;
  assert.equal(c.has(a),false); assert.deepEqual(c.live(new Set([33])),[]);
  c.start(a); c.clear();
  assert.deepEqual(c.live(),[]);
});
test('stale touch cleanup does not release a genuine live finger or mouse drag', () => {
  const touch=pointer(1,17),mouse=pointer(0,0,false),stale=pointer(2,18);
  releaseStaleTouches([touch,mouse,stale],new Set([17]));
  assert.equal(touch.isDown,true);assert.equal(mouse.isDown,true);assert.equal(stale.isDown,false);
});
test('newly starting touch identifiers may reuse a stranded UI slot identifier', () => {
  const old=pointer(1,51),held=pointer(2,52);
  releaseStaleTouches([old,held],new Set([51,52]),new Set([51]));
  assert.equal(old.isDown,false);assert.equal(held.isDown,true);
});
