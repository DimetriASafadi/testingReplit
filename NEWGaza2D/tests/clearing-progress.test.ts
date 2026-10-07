import test from 'node:test';
import assert from 'node:assert/strict';
import { clearingProgress, clearingStage } from '../src/clearing-progress';
test('clearing artwork starts at arrival and reaches the nearly clean stage before engine completion',()=>{
  const job={arrival:50000,workEnd:110000};
  assert.equal(clearingProgress(job,0),0);
  assert.equal(clearingStage(clearingProgress(job,50000)),0);
  assert.deepEqual([60000,75000,90000,105000].map(t=>clearingStage(clearingProgress(job,t))),[1,2,3,4]);
  assert.equal(clearingStage(clearingProgress(job,120000)),4,'keep last artwork while awaiting authoritative completion');
});
test('saved wall-clock deadlines restore the same clearing stage, including delayed dispatch',()=>{
  const job={arrival:80000,workEnd:140000}, restored=JSON.parse(JSON.stringify(job));
  for(const t of [80000,91000,108000,123000,139000])
    assert.equal(clearingStage(clearingProgress(job,t)),clearingStage(clearingProgress(restored,t)));
  assert.equal(clearingProgress(job,65000),0);
});
