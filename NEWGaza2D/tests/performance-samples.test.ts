import { test } from 'node:test';
import assert from 'node:assert/strict';
import { FrameWindow, progressFingerprint } from '../src/performance-samples';
import { newGame } from '../src/engine';

test('diagnostic frames reflect 30fps game steps, percentiles and bounded windows', () => {
  const frames = new FrameWindow();
  for (let i = 0; i < 150; i++) frames.add(1000 / 30);
  frames.add(200);
  frames.add(NaN); frames.add(-1); frames.add(0);
  const report = frames.report();
  assert.equal(report.frames, 151);
  assert.equal(report.p95Ms, 1000 / 30);
  assert.equal(report.maxMs, 200);
  assert.equal(report.over50Ms, 1);
  assert(report.fps! < 30 && report.fps! > 28);
  for (let i = 0; i < 2000; i++) frames.add(16);
  assert.equal(frames.report().frames, 1200);
  assert.equal(frames.report().dropped, 951);
  frames.reset();
  assert.equal(frames.report().fps, null);
  assert.equal(frames.report().p99Ms, null);
});

test('progress checksum is non-mutating, ignores moving clocks but catches lost plots/projects/roads', () => {
  const d = newGame(1000).districts[0];
  const original = JSON.stringify(d), checksum = progressFingerprint(d);
  assert.equal(JSON.stringify(d), original);
  d.plots[0].incomeAt = 999999;
  d.camera = { x: 10, y: 20, zoom: 1 };
  assert.equal(progressFingerprint(d), checksum);
  d.plots[0].orientation = 3;
  assert.notEqual(progressFingerprint(d), checksum);
  const afterRotation = progressFingerprint(d);
  d.projects[0].status = 'complete';
  assert.notEqual(progressFingerprint(d), afterRotation);
  const afterProject = progressFingerprint(d);
  d.pavedRoads = ['r2', 'r1'];
  assert.notEqual(progressFingerprint(d), afterProject);
  const afterRoad = progressFingerprint(d);
  d.pavedRoads.reverse();
  assert.equal(progressFingerprint(d), afterRoad);
});
