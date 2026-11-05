import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import test from 'node:test';
const cli=fileURLToPath(new URL('../cli.mjs',import.meta.url));
const now='2026-11-02T00:00:00Z';
const plan={remaining:50,unit:'credits',resetAt:'2026-11-07T00:00:00Z'};
function run(input,args=['--now='+now]){return spawnSync(process.execPath,[cli,...args],{input:typeof input==='string'?input:JSON.stringify(input),encoding:'utf8',timeout:3000});}
test('offline plan computes a same-unit daily budget and never claims provider availability',()=>{
 const r=run(plan);assert.equal(r.status,0,r.stderr);const out=JSON.parse(r.stdout);assert.equal(out.dailyBudget,10);assert.equal(out.unit,'credits');assert.equal(out.availabilityVerified,false);assert.match(out.basis,/self-entered/);
});
test('all supported input units retain their identity without conversion',()=>{
 for(const unit of ['credits','tokens','requests','percent']){const r=run({...plan,unit});assert.equal(r.status,0,r.stderr);assert.equal(JSON.parse(r.stdout).unit,unit);}
});
test('invalid balances units payloads and unknown fields fail without echoing input',()=>{
 for(const input of [{...plan,remaining:-1},{...plan,unit:'money'},{...plan,unit:'percent',remaining:101},{...plan,remaining:'Infinity'},{...plan,email:'private@example.invalid'},[],null,'not json','x'.repeat(16385)]){
  const r=run(input);assert.equal(r.status,1);assert.equal(r.stdout,'');assert.match(r.stderr,/Plan unavailable/);assert.ok(!r.stderr.includes('private@example.invalid'));
 }
});
test('ambiguous malformed normalized or expired reset timestamps reject',()=>{
 for(const resetAt of ['2026-11-07T00:00:00','2026-11-07T00:00:00+00:00','2026-02-30T00:00:00Z',now,'2026-11-01T00:00:00Z'])assert.equal(run({...plan,resetAt}).status,1);
});
test('explicit UTC clock validation and fractional timestamps are deterministic',()=>{
 for(const value of ['bad','2026-11-02T00:00:00','2026-02-30T00:00:00Z'])assert.equal(run(plan,['--now='+value]).status,1);
 assert.equal(run({...plan,resetAt:'2026-11-07T00:00:00.000Z'}).status,0);
});
test('help and command-line errors have no side effects or input echo',()=>{
 assert.equal(run('', ['--help']).status,0);assert.equal(run(plan,['--help','--now='+now]).status,1);assert.equal(run(plan,['--output=/tmp/private']).status,1);assert.equal(run(plan,['--now='+now,'extra']).status,1);
});
