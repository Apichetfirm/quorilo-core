import test from 'node:test';
import assert from 'node:assert/strict';
import {calculateDailyBudget,validateQuotaPlan} from '../src/quota-plan.ts';

const now=Date.parse('2026-10-15T00:00:00Z');
const valid={remaining:'50',unit:'credits',resetAt:'2026-10-16T00:00:00Z'};
test('balances are validated in their own units',()=>{
  assert.deepEqual(validateQuotaPlan(valid,now),{});
  assert.deepEqual(validateQuotaPlan({...valid,remaining:'0'},now),{});
  assert.equal(validateQuotaPlan({...valid,remaining:''},now).remaining,'required');
  for(const value of ['-1','Infinity','NaN','abc'])assert.equal(validateQuotaPlan({...valid,remaining:value},now).remaining,'balance');
  assert.equal(validateQuotaPlan({...valid,unit:'percent',remaining:'100.01'},now).remaining,'percent');
  assert.deepEqual(validateQuotaPlan({...valid,unit:'percent',remaining:'100'},now),{});
  assert.deepEqual(validateQuotaPlan({...valid,unit:'tokens',remaining:'50000'},now),{});
});
test('percent balances reject empty, negative, non-finite, and over-100 values',()=>{
  for(const remaining of ['', '   ', '-1', 'Infinity', '-Infinity', 'NaN', '101', '100.01'])
    assert.ok(validateQuotaPlan({...valid,unit:'percent',remaining},now).remaining,`expected ${JSON.stringify(remaining)} to be rejected`);
  assert.deepEqual(validateQuotaPlan({...valid,unit:'percent',remaining:'0'},now),{});
  assert.deepEqual(validateQuotaPlan({...valid,unit:'percent',remaining:'100'},now),{});
  assert.equal(validateQuotaPlan({...valid,unit:'percent',remaining:'   '},now).remaining,'required');
  assert.equal(validateQuotaPlan({...valid,unit:'percent',remaining:'-1'},now).remaining,'percent');
});
test('a percent daily budget is limited to 0 through 100',()=>{
  for(const daily of ['', '-1', 'Infinity', 'NaN', '101'])
    assert.ok(validateQuotaPlan({...valid,unit:'percent',daily},now).daily,`expected daily ${JSON.stringify(daily)} to be rejected`);
  assert.equal(validateQuotaPlan({...valid,unit:'percent',daily:'-1'},now).daily,'percent');
  assert.deepEqual(validateQuotaPlan({...valid,unit:'percent',daily:'0'},now),{});
  assert.deepEqual(validateQuotaPlan({...valid,unit:'percent',daily:'100'},now),{});
  assert.deepEqual(validateQuotaPlan({...valid,unit:'credits',daily:'500'},now),{});
});
test('missing, invalid, current and expired reset times cannot create a valid plan',()=>{
  for(const value of ['', 'not-a-date','2026-10-15T00:00:00Z','2026-10-14T00:00:00Z'])assert.equal(validateQuotaPlan({...valid,resetAt:value},now).resetAt,'future');
});
test('percent balances cannot produce a plan for a past reset date',()=>{
  for(const resetAt of ['2026-10-14T00:00:00Z','2026-10-15T00:00:00Z'])
    assert.equal(validateQuotaPlan({...valid,unit:'percent',remaining:'50',resetAt},now).resetAt,'future');
});
test('invalid percent balances and expired windows never produce a daily budget',()=>{
  for(const remaining of ['', '-1', 'Infinity', 'NaN', '100.01'])
    assert.equal(calculateDailyBudget({...valid,unit:'percent',remaining},now),null);
  assert.equal(calculateDailyBudget({...valid,unit:'percent',remaining:'50',resetAt:'2026-10-14T00:00:00Z'},now),null);
  assert.equal(calculateDailyBudget({...valid,unit:'percent',remaining:'50'},now),50);
});
