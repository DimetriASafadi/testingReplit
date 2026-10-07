import test from 'node:test';
import assert from 'node:assert/strict';
import { DISTRICTS } from '../src/catalog';
import { districtEnvironment } from '../src/district-environment';

test('every district has a distinct deterministic environment, with sea restricted to coastal identities', () => {
  const coast = new Set(['rimal', 'sheikh-ijlin', 'rashid']);
  const seeds = new Set<number>();
  for (const d of DISTRICTS) {
    const env = districtEnvironment(d.id);
    assert.equal(env.style === 'coastal', coast.has(d.id), d.id);
    assert.ok(env.farmland >= 0 && env.farmland <= 1);
    assert.ok(Number.isSafeInteger(env.seed)); seeds.add(env.seed);
    assert.deepEqual(districtEnvironment(d.id), env);
  }
  assert.equal(seeds.size, DISTRICTS.length);
  assert.throws(() => districtEnvironment('unknown'));
});
test('eastern farmland and central urban surroundings are distinct rather than the same coastal template', () => {
  assert.equal(districtEnvironment('shujaiya').style, 'agricultural');
  assert.ok(districtEnvironment('shujaiya').farmland >= .7);
  assert.equal(districtEnvironment('tuffah').vegetation, 'citrus');
  assert.equal(districtEnvironment('zeitoun').vegetation, 'olive');
  for (const id of ['old-city', 'daraj', 'nasr', 'tel-al-hawa', 'sabra', 'sheikh-radwan']) {
    assert.equal(districtEnvironment(id).style, 'urban');
  }
  assert.equal(districtEnvironment('old-city').historic, true);
});
