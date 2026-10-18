import test from "node:test";
import assert from "node:assert/strict";
import worker,{IgdbCoordinator,cacheKey,coordinatorName,publicPolicy} from "../src/index.mjs";
import {validateDeployment} from "../scripts/check-deployment.mjs";
const COORDINATOR=coordinatorName(),POLICY=publicPolicy();

const game={id:1,name:"Synthetic game",game_type:{type:"Main Game"},videos:[{id:10,game:1,name:"Trailer",video_id:"abcdefghijk"}]};
const token=()=>Response.json({access_token:"synthetic_app_token",expires_in:3600,token_type:"bearer"});
const req=(q={title:"Game"},headers={})=>new Request("https://fixture.test/v1/game-videos/query",{method:"POST",headers:{"Content-Type":"application/json",...headers},body:JSON.stringify(q)});
const internal=q=>new Request("https://igdb.internal/query",{method:"POST",body:JSON.stringify(q)});
function host(){
  const values=new Map(),storage={get:async k=>values.get(k),put:async(k,v)=>{values.set(k,structuredClone(v));},delete:async k=>values.delete(k),list:async({prefix,limit})=>new Map([...values].filter(([k])=>k.startsWith(prefix)).sort().slice(0,limit))};
  const env={IGDB_ENABLED:"true",IGDB_CLIENT_ID:"synthetic_client",IGDB_CLIENT_SECRET:"synthetic_secret"};
  const state={storage};let coordinator=new IgdbCoordinator(state,env),names=[];
  env.IGDB_COORDINATOR={idFromName:n=>{names.push(n);return n;},get:()=>({fetch:(u,i)=>coordinator.fetch(new Request(u,i))})};
  return {env,values,names,coordinator,restart(){coordinator=new IgdbCoordinator(state,env);return coordinator;}};
}
async function upstream(handler,action){const old=fetch;globalThis.fetch=handler;try{return await action();}finally{globalThis.fetch=old;}}
const budget=()=>({day:Math.floor(Date.now()/86400000),minute:Math.floor(Date.now()/60000),requests:0,games:0,grants:0,gamesThisMinute:0});
const cached=()=>({until:Date.now()+10000,value:{games:[{id:1,parentGame:null}],complete:true,fetchedAtUtc:new Date().toISOString()}});

test("Reviewed production topology keeps one SQLite namespace, edge limits and disabled first deployment",async()=>{
  assert.equal((await validateDeployment()).name,"the-order-igdb-game-videos");
});
test("Damaged durable budget cannot silently reset to an unlimited fresh quota",async()=>{
  const h=host();h.values.set("public-budget-v1",{...budget(),requests:"broken"});let hits=0;
  await upstream(async()=>{hits++;throw Error();},async()=>assert.equal((await h.coordinator.fetch(internal({title:"x"}))).status,503));
  assert.equal(hits,0);assert.equal(h.values.get("public-budget-v1").requests,"broken");
});

test("Public kill switch stops at the edge before creating or invoking any object",async()=>{
  const h=host();h.env.IGDB_ENABLED="false";
  assert.equal((await worker.fetch(req(),h.env)).status,503);assert.equal(h.names.length,0);assert.equal(h.values.size,0);
});
test("Public deployment requires edge limiter and Cloudflare caller identity; no embedded client token",async()=>{
  const h=host();h.env.IGDB_PUBLIC="true";
  assert.equal((await worker.fetch(req(),h.env)).status,503);
  assert.equal((await worker.fetch(req({}, {"CF-Connecting-IP":"192.0.2.1"}),h.env)).status,503);
  h.env.IP_LIMITER={limit:async()=>({success:false})};
  assert.equal((await worker.fetch(req({title:"x"},{"CF-Connecting-IP":"192.0.2.1",Authorization:"Bearer invented-client-token"}),h.env)).status,429);
  h.env.IP_LIMITER={limit:async()=>{throw Error("synthetic_secret");}};
  assert.deepEqual(await(await worker.fetch(req({title:"x"},{"CF-Connecting-IP":"192.0.2.1"}),h.env)).json(),{error:"upstream"});
  assert.equal(h.names.length,0);
});
test("Edge forwards only validated public metadata to the unique quota owner",async()=>{
  const h=host();h.env.IGDB_PUBLIC="true";let received;
  h.env.IP_LIMITER={limit:async({key})=>{assert.equal(key,"igdb:192.0.2.1");return {success:true};}};
  h.env.IGDB_COORDINATOR.get=()=>({fetch:async(u,i)=>{received={u,i};return Response.json({games:[]});}});
  assert.equal((await worker.fetch(req({title:"Game",id:1},{"CF-Connecting-IP":"192.0.2.1",Authorization:"private-client-header","X-User-Path":"C:/private"}),h.env)).status,200);
  assert.equal(received.u,"https://igdb.internal/query");assert.equal(received.i.headers,undefined);
  assert.deepEqual(JSON.parse(received.i.body),{title:"Game",id:1});assert.deepEqual(h.names,[COORDINATOR]);
});
test("Content-type lookalikes, private fields and blank queries never reach the coordinator",async()=>{
  const h=host();for(const request of [req({title:"  "}),req({title:"x",localPath:"C:/games"}),req({title:"x"},{"Content-Type":"application/json-evil"})])assert.equal((await worker.fetch(request,h.env)).status,400);
  assert.equal(h.names.length,0);
});
test("Request-body stall is bounded without contacting an upstream",{timeout:5000},async()=>{
  const h=host();const stream=new ReadableStream({start(c){c.enqueue(new TextEncoder().encode('{"title":'));}});
  const request=new Request("https://fixture.test/v1/game-videos/query",{method:"POST",headers:{"Content-Type":"application/json"},body:stream,duplex:"half"});
  assert.equal((await worker.fetch(request,h.env)).status,400);assert.equal(h.names.length,0);
});
test("Exact identities share cache keys across titles while editions and Steam IDs remain distinct",()=>{
  assert.equal(cacheKey({title:"JP",id:1}),cacheKey({title:"EN",id:1,steamId:"7"}));
  assert.equal(cacheKey({title:"JP",steamId:"7"}),cacheKey({title:"EN",steamId:"7"}));
  assert.notEqual(cacheKey({title:"x",id:1}),cacheKey({title:"x",id:2}));
  assert.notEqual(cacheKey({title:"x",steamId:"7"}),cacheKey({title:"x",steamId:"8"}));
  assert.equal(cacheKey({title:"  Cafe\u0301  GAME "}),cacheKey({title:"Café game"}));
});
test("Daily admission limit survives restarts, including cache hits, with atomic concurrent accounting",async()=>{
  const h=host();h.values.set("public-budget-v1",{...budget(),requests:POLICY.requestsPerDay-1});h.values.set(cacheKey({title:"x"}),cached());
  const results=await Promise.all([1,2,3].map(()=>h.coordinator.fetch(internal({title:"x"}))));
  assert.deepEqual(results.map(r=>r.status).sort(),[200,429,429]);
  assert.equal(h.values.get("public-budget-v1").requests,POLICY.requestsPerDay);
  assert.equal((await h.restart().fetch(internal({title:"x"}))).status,429);
  const old=h.values.get("public-budget-v1");h.values.set("public-budget-v1",{...old,day:old.day-1});
  assert.equal((await h.restart().fetch(internal({title:"x"}))).status,200);
});
test("Daily games budget and per-minute budget are checked before dispatch and survive restarts",async()=>{
  for(const field of ["games","gamesThisMinute"]){
    const h=host();h.values.set("public-budget-v1",{...budget(),[field]:field==="games"?POLICY.gamesPerDay:POLICY.gamesPerMinute});
    h.coordinator.token="cached_synthetic";h.coordinator.expires=Date.now()+10000;
    let hits=0;await upstream(async()=>{hits++;throw Error("must not forward");},async()=>assert.equal((await h.coordinator.fetch(internal({title:"x"}))).status,429));assert.equal(hits,0);
    const restarted=h.restart();restarted.token="cached_synthetic";restarted.expires=Date.now()+10000;
    await upstream(async()=>{hits++;throw Error("must not forward");},async()=>assert.equal((await restarted.fetch(internal({title:"x"}))).status,429));assert.equal(hits,0);
  }
});
test("Grant budget is independent of the games budget and cannot be reset by object recreation",async()=>{
  const h=host();h.values.set("public-budget-v1",{...budget(),grants:POLICY.grantsPerDay});let hits=0;
  await upstream(async()=>{hits++;throw Error("must not forward");},async()=>assert.equal((await h.restart().fetch(internal({title:"x"}))).status,429));assert.equal(hits,0);
});
test("Memory-only app tokens permit ordinary cold starts beyond 24 daily renewals",async()=>{
  const h=host();h.values.set("public-budget-v1",{...budget(),grants:24,games:24});
  await upstream(async u=>u.includes("oauth2/token")?token():Response.json([game]),async()=>{
    assert.equal((await h.restart().fetch(internal({title:"After cold start"}))).status,200);
  });assert.equal(h.values.get("public-budget-v1").grants,25);
});
test("Identical bursts coalesce, bounded waiter overflow does not corrupt waiter accounting",async()=>{
  const h=host();let release;const held=new Promise(r=>release=r);let calls=0;
  await upstream(async u=>u.includes("oauth2/token")?token():(calls++,await held,Response.json([game])),async()=>{
    const waiters=Array.from({length:POLICY.duplicateWaiters},()=>h.coordinator.fetch(internal({title:"x",id:1})));
    await new Promise(r=>setTimeout(r,30));
    try{for(let i=0;i<3;i++)assert.equal((await h.coordinator.fetch(internal({title:"renamed",id:1}))).status,429);}
    finally{release();}assert.ok((await Promise.all(waiters)).every(r=>r.status===200));
  });assert.equal(calls,1);assert.equal(h.coordinator.inflight.size,0);
});
test("Cached metadata bypasses a slow unrelated query, shared cooldown and open circuit",async()=>{
  const h=host();h.values.set(cacheKey({title:"cached",id:1}),cached());h.values.set("quota-cooldown",Date.now()+60000);h.values.set("circuit-v1",{failures:3,until:Date.now()+60000,code:"authentication"});
  let calls=0;await upstream(async()=>{calls++;throw Error();},async()=>{
    assert.equal((await h.coordinator.fetch(internal({title:"renamed",id:1}))).status,200);
    assert.equal((await h.coordinator.fetch(internal({title:"new"}))).status,429);
  });assert.equal(calls,0);
  h.values.delete("quota-cooldown");h.values.delete("circuit-v1");h.coordinator.cooldown=0;
  let release;const held=new Promise(r=>release=r);
  await upstream(async u=>u.includes("oauth2/token")?token():(await held,Response.json([game])),async()=>{
    const slow=h.coordinator.fetch(internal({title:"slow"}));await new Promise(r=>setTimeout(r,10));
    try{assert.equal((await h.coordinator.fetch(internal({title:"cached",id:1}))).status,200);}finally{release();}await slow;
  });
});
test("Invalid application credentials open a persisted circuit; no end-user login or credential echo",async()=>{
  const h=host();let calls=0;const old=console.warn,logs=[];console.warn=s=>logs.push(s);
  try{await upstream(async()=>{calls++;return new Response("synthetic_secret synthetic_app_token",{status:401});},async()=>{
    for(const c of [h.coordinator,h.restart()]){const response=await c.fetch(internal({title:"x"}));assert.equal(response.status,503);assert.deepEqual(await response.json(),{error:"authentication"});}
  });}finally{console.warn=old;}assert.equal(calls,1);assert.ok(!JSON.stringify(logs).includes("synthetic_secret"));assert.equal(h.values.get("public-budget-v1").grants,1);
});
test("Three transient failures open circuit; expiry permits one serial recovery and resets failures",async()=>{
  const h=host();let calls=0;const old=console.warn;console.warn=()=>{};
  try{await upstream(async u=>u.includes("oauth2/token")?token():(calls++,new Response("failure",{status:502})),async()=>{
    for(let i=0;i<4;i++)assert.equal((await h.coordinator.fetch(internal({title:"x"+i}))).status,503);
  });assert.equal(calls,3);const breaker=h.values.get("circuit-v1");h.values.set("circuit-v1",{...breaker,until:Date.now()-1});
  await upstream(async u=>u.includes("oauth2/token")?token():Response.json([game]),async()=>assert.equal((await h.restart().fetch(internal({title:"recovery"}))).status,200));
  assert.equal(h.values.has("circuit-v1"),false);}finally{console.warn=old;}
});
test("Twitch 429 honors Retry-After and prevents repeated token requests after restart",async()=>{
  const h=host();let grants=0;await upstream(async()=>{grants++;return new Response("",{status:429,headers:{"Retry-After":"120"}});},async()=>{
    assert.equal((await h.coordinator.fetch(internal({title:"x"}))).status,429);
    assert.equal((await h.restart().fetch(internal({title:"other"}))).status,429);
  });assert.equal(grants,1);assert.ok(h.values.get("quota-cooldown")>Date.now()+110000);
});
test("Queued work that misses the client time budget is discarded before acquiring a token",async()=>{
  const h=host();const oldNow=Date.now,now=oldNow();let release;h.coordinator.tail=new Promise(r=>release=r);let hits=0;
  await upstream(async()=>{hits++;throw Error();},async()=>{
    const response=h.coordinator.fetch(internal({title:"x"}));await new Promise(r=>setTimeout(r,10));
    try{Date.now=()=>now+POLICY.deadlineMs+1000;release();assert.deepEqual(await(await response).json(),{error:"timeout"});}
    finally{Date.now=oldNow;release();}
  });assert.equal(hits,0);
});
