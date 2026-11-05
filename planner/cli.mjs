#!/usr/bin/env node
import {readSync} from 'node:fs';
import {calculateDailyBudget,validateQuotaPlan} from './src/quota-plan.ts';

function utc(value){
 if(typeof value!=='string'||!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z$/.test(value))throw Error('Invalid UTC');
 const stamp=Date.parse(value);
 if(!Number.isFinite(stamp)||new Date(stamp).toISOString().replace('.000Z','Z')!==value.replace('.000Z','Z'))throw Error('Invalid UTC');
 return stamp;
}
try{
 if(process.argv.includes('--help')){
  if(process.argv.length!==3)throw Error('Arguments');
  console.log('Quorilo Reset Planner (private release candidate)\nRead a JSON plan from stdin. Optional --now=YYYY-MM-DDTHH:mm:ssZ.\nFields: remaining, unit (credits/tokens/requests/percent), resetAt (explicit UTC), optional daily.\nOffline arithmetic using your input only; no provider access, forecasts, quota conversion or telemetry.');
 }else{
  if(process.argv.length>3||process.argv[2]&&!process.argv[2].startsWith('--now='))throw Error('Arguments');
  const now=process.argv[2]?utc(process.argv[2].slice(6)):Date.now();
  const bytes=Buffer.alloc(16385);let size=0;
  while(size<bytes.length){const n=readSync(0,bytes,size,bytes.length-size,null);if(!n)break;size+=n;}
  if(size>16384)throw Error('Input bound');
  const input=JSON.parse(bytes.subarray(0,size).toString('utf8'));
  if(!input||typeof input!=='object'||Array.isArray(input)||Object.keys(input).some(k=>!['remaining','unit','resetAt','daily'].includes(k)))throw Error('Fields');
  if(!['credits','tokens','requests','percent'].includes(input.unit))throw Error('Unit');
  utc(input.resetAt);
  const errors=validateQuotaPlan(input,now);
  if(Object.keys(errors).length)throw Error('Invalid plan');
  const dailyBudget=calculateDailyBudget(input,now);
  if(!Number.isFinite(dailyBudget))throw Error('Invalid result');
  console.log(JSON.stringify({basis:'self-entered plan; not a provider observation',unit:input.unit,remaining:Number(input.remaining),resetAt:input.resetAt,asOf:new Date(now).toISOString(),dailyBudget,availabilityVerified:false}));
 }
}catch{
 // No input snippets, filesystem paths, identities or raw parser errors.
 console.error('Plan unavailable. Use --help and a valid bounded plan with an explicit UTC future reset.');
 process.exitCode=1;
}
