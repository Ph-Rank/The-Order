import test from "node:test";
import assert from "node:assert/strict";
import worker,{IgdbCoordinator,parseQuery,gameQuery,mapGame} from "../src/index.mjs";
const fixtureGame=(id=1)=>({id,name:"Synthetic game",game_type:{type:"Remaster"},parent_game:99,version_parent:2,platforms:[{name:"PC"}],external_games:[{uid:"123",external_game_source:{name:"Steam"}}],videos:[{id:10,game:id,name:"Launch Trailer",video_id:"abcdefghijk"}]});
const request=q=>new Request("https://fixture.test/v1/game-videos/query",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(q)});
function host(handler,enabled=true){
  const map=new Map(),store={get:async k=>map.get(k),put:async(k,v)=>map.set(k,v),delete:async k=>map.delete(k),list:async({prefix,limit})=>new Map([...map].filter(([k])=>k.startsWith(prefix)).sort().slice(0,limit))};
  const env={IGDB_ENABLED:enabled?"true":"false",IGDB_CLIENT_ID:"synthetic_client",IGDB_CLIENT_SECRET:"synthetic_secret"};
  const coordinator=new IgdbCoordinator({storage:store},env);let names=[];
  env.IGDB_COORDINATOR={idFromName:n=>{names.push(n);return n;},get:()=>({fetch:async(u,i)=>coordinator.fetch(new Request(u,i))})};
  return {env,coordinator,map,names,handler};
}
async function run(handler,action){const old=globalThis.fetch;globalThis.fetch=handler;try{return await action();}finally{globalThis.fetch=old;}}
const token=()=>Response.json({access_token:"synthetic_app_token",expires_in:3600,token_type:"bearer"});
test("Fixed query has bounded fields, escaped title, and exact ID predicates",()=>{
  assert.throws(()=>parseQuery({title:"test",query:"fields *;"}));
  assert.throws(()=>parseQuery({title:"test",url:"http://127.0.0.1"}));
  for(const title of ["","x".repeat(201),"\n"] )assert.throws(()=>parseQuery({title}));
  for(const id of [-1,0,1.5,2147483648,"1"])assert.throws(()=>parseQuery({title:"x",id}));
  const q=gameQuery(parseQuery({title:'foo"; fields *; "bar'}));assert.ok(q.includes('search "foo\\"; fields *; \\"bar"; limit 21;'));assert.ok(q.includes("videos.game"));assert.ok(q.includes("game_type.type"));assert.ok(!q.includes("game_type.name"));
  assert.ok(gameQuery({title:"test",id:123}).includes("where id = 123; limit 1;"));
  assert.ok(gameQuery({title:"test",steamId:"123"}).includes('external_games.external_game_source.name = "Steam"'));assert.ok(!gameQuery({title:"test"}).includes("category"));
});
test("Game-video mapping retains original names, relation identity, and edition parent",()=>{
  const game=mapGame(fixtureGame());assert.equal(game.versionParent,2);assert.equal(game.parentGame,99);assert.equal(game.kind,"Remaster");assert.equal(game.videos[0].gameId,1);assert.equal(game.videos[0].name,"Launch Trailer");
  assert.throws(()=>mapGame({...fixtureGame(),videos:[{id:1,game:999,name:"Movie",video_id:"abcdefghijk"}]}));
  assert.deepEqual(mapGame({...fixtureGame(),videos:[{id:1,game:1,name:"Movie",video_id:"bad"}]}).videos,[]);
});
test("Unconfigured is explicit and never means no videos",async()=>{
  const h=host(null,false);const result=await worker.fetch(request({title:"test"}),h.env);assert.equal(result.status,503);assert.deepEqual(await result.json(),{error:"unconfigured"});assert.equal([...h.map.keys()].filter(k=>k.startsWith("v1:")).length,0);
});
test("Rejects arbitrary routes, redirects, browser origins, oversized and unknown inputs",async()=>{
  const h=host();for(const [req,status]of[[new Request("https://fixture.test/other"),404],[request({title:"test",query:"x"}),400],[request({title:"x".repeat(5000)}),400],[new Request("https://fixture.test/v1/game-videos/query",{method:"POST",headers:{"Content-Type":"application/json",Origin:"https://evil.test"},body:'{"title":"test"}'}),400]]){
    assert.equal((await worker.fetch(req,h.env)).status,status);
  }assert.equal(h.names.length,0);
});
test("Concurrent installations share one coordinator, token grant and cached query",async()=>{
  const h=host();let grants=0,calls=0;
  await run(async(u,i)=>{
    if(u==="https://id.twitch.tv/oauth2/token"){grants++;assert.equal(i.method,"POST");assert.equal(i.body.get("grant_type"),"client_credentials");assert.equal(i.body.get("client_secret"),"synthetic_secret");assert.ok(!u.includes("synthetic_secret"));return token();}
    assert.equal(u,"https://api.igdb.com/v4/games");assert.equal(i.redirect,"manual");assert.equal(i.headers.Authorization,"Bearer synthetic_app_token");calls++;return Response.json([fixtureGame()]);
  },async()=>{const results=await Promise.all([1,2,3].map(()=>worker.fetch(request({title:"test"}),h.env)));for(const r of results){const value=await r.json();assert.equal(value.games[0].id,1);assert.equal(value.complete,true);assert.ok(!JSON.stringify(value).includes("synthetic_app_token"));}});
  assert.equal(grants,1);assert.equal(calls,1);assert.equal(new Set(h.names).size,1);
});
test("401 renews the application grant once, 429 has shared cooldown, failures are not cached",async()=>{
  const h=host();let calls=0,grants=0;
  await run(async u=>u.includes("oauth2/token")?(grants++,token()):(++calls===1?new Response("",{status:401}):Response.json([fixtureGame()])),async()=>{
    assert.equal((await worker.fetch(request({title:"test"}),h.env)).status,200);
  });assert.equal(grants,2);assert.equal(calls,2);
  const limited=host();let hits=0;await run(async u=>u.includes("oauth2/token")?token():(hits++,new Response("",{status:429,headers:{"Retry-After":"1"}})),async()=>{
    assert.equal((await worker.fetch(request({title:"test"}),limited.env)).status,429);assert.equal((await worker.fetch(request({title:"other"}),limited.env)).status,429);
  });assert.equal(hits,1);assert.equal([...limited.map.keys()].filter(k=>k.startsWith("v1:")).length,0);
});
test("At most 20 candidates and incomplete result cannot assert completeness",async()=>{
  const h=host();await run(async u=>u.includes("oauth2/token")?token():Response.json(Array.from({length:21},(_,i)=>fixtureGame(i+1))),async()=>{
    const result=await(await worker.fetch(request({title:"test"}),h.env)).json();assert.equal(result.complete,false);assert.equal(result.games.length,20);
  });
});
test("Mismatched exact ID and video ownership fail closed without credential echo",async()=>{
  for(const value of [[fixtureGame(2)],[{...fixtureGame(),videos:[{id:10,game:999,name:"Trailer",video_id:"abcdefghijk"}]}]]){
    const h=host();await run(async u=>u.includes("oauth2/token")?token():Response.json(value),async()=>{
      const response=await worker.fetch(request({title:"test",id:1}),h.env);assert.equal(response.status,503);assert.deepEqual(await response.json(),{error:"invalid_response"});
    });assert.equal([...h.map.keys()].filter(k=>k.startsWith("v1:")).length,0);
  }
});
test("Redirect and oversized provider responses never become empty success",async()=>{
  for(const response of [()=>new Response("",{status:302,headers:{Location:"https://evil.test"}}),()=>new Response("x".repeat(262145))]){
    const h=host();await run(async u=>u.includes("oauth2/token")?token():response(),async()=>{const r=await worker.fetch(request({title:"test"}),h.env);assert.equal(r.status,503);});assert.equal([...h.map.keys()].filter(k=>k.startsWith("v1:")).length,0);
  }
});
test("Global upstream dispatch stays serial and below four per second",async()=>{
  const h=host();let active=0,max=0,starts=[];
  await run(async u=>{if(u.includes("oauth2/token"))return token();active++;max=Math.max(max,active);starts.push(Date.now());await new Promise(r=>setTimeout(r,10));active--;return Response.json([fixtureGame()]);},
  async()=>{const result=await Promise.all([1,2,3].map(n=>worker.fetch(request({title:"test "+n}),h.env)));assert.ok(result.every(r=>r.status===200));});
  assert.equal(max,1);assert.ok(starts[1]-starts[0]>=250);assert.ok(starts[2]-starts[1]>=250);
});
test("Expired application grant renews once for concurrent requests without a user login",async()=>{
  const h=host();h.coordinator.token="expired_synthetic_token";h.coordinator.expires=Date.now()-1;let grants=0,calls=0;
  await run(async u=>u.includes("oauth2/token")?(grants++,token()):(calls++,Response.json([fixtureGame()])),async()=>{
    const values=await Promise.all(["one","two"].map(title=>worker.fetch(request({title}),h.env)));
    assert.ok(values.every(r=>r.status===200));
  });assert.equal(grants,1);assert.equal(calls,2);
});
test("Coordinator restart preserves shared cooldown and dispatch guard",async()=>{
  const h=host();h.map.set("quota-cooldown",Date.now()+10000);
  let calls=0;
  await run(async()=>{calls++;throw Error("must not call upstream");},async()=>{
    assert.equal((await worker.fetch(request({title:"test"}),h.env)).status,429);
  });assert.equal(calls,0);
  h.map.set("quota-cooldown",0);h.coordinator.cooldown=0;
  const barrier=Date.now()+300;h.map.set("dispatch-not-before",barrier);
  await run(async u=>{if(u.includes("oauth2/token"))return token();assert.ok(Date.now()>=barrier);return Response.json([fixtureGame()]);},async()=>{
    assert.equal((await worker.fetch(request({title:"test"}),h.env)).status,200);
  });
});
test("A bounded shared queue rejects excess work instead of forwarding unbounded requests",async()=>{
  const h=host();let release;const held=new Promise(r=>release=r);
  await run(async u=>{if(u.includes("oauth2/token"))return token();await held;return Response.json([fixtureGame()]);},async()=>{
    const pending=Array.from({length:8},()=>h.coordinator.fetch(new Request("https://internal.test/query",{method:"POST",body:JSON.stringify({title:"same cached query"})})));
    await new Promise(r=>setTimeout(r,10));
    assert.equal((await h.coordinator.fetch(new Request("https://internal.test/query",{method:"POST",body:JSON.stringify({title:"excess"})}))).status,429);
    release();assert.ok((await Promise.all(pending)).every(r=>r.status===200));
  });
});
test("Upstream timeout remains a retryable failure and recovery is not poisoned by a cached miss",async()=>{
  const h=host();let failing=true;
  await run(async u=>{if(u.includes("oauth2/token"))return token();if(failing)throw new DOMException("synthetic timeout","TimeoutError");return Response.json([fixtureGame()]);},async()=>{
    const response=await worker.fetch(request({title:"test"}),h.env);assert.equal(response.status,503);assert.deepEqual(await response.json(),{error:"timeout"});
    assert.equal([...h.map.keys()].filter(k=>k.startsWith("v1:")).length,0);failing=false;
    assert.equal((await worker.fetch(request({title:"test"}),h.env)).status,200);
  });
});
test("Developer diagnostics identify token, upstream HTTP, network and mapping stages without secret echo",async()=>{
  const cases=[
    {stage:"token_http",http:401,handler:async()=>new Response("synthetic_secret synthetic_app_token",{status:401})},
    {stage:"token_network",http:null,handler:async()=>{throw new TypeError("synthetic_secret synthetic_app_token");}},
    {stage:"games_http",http:400,handler:async u=>u.includes("oauth2/token")?token():new Response("synthetic_secret synthetic_app_token",{status:400})},
    {stage:"games_network",http:null,handler:async u=>{if(u.includes("oauth2/token"))return token();throw new TypeError("synthetic_secret synthetic_app_token");}},
    {stage:"mapping",http:200,handler:async u=>u.includes("oauth2/token")?token():Response.json([{...fixtureGame(),videos:[{id:1,game:999,name:"bad",video_id:"abcdefghijk"}]}])},
    {stage:"token_body",http:200,handler:async()=>new Response("synthetic_secret synthetic_app_token")}
  ];
  for(const item of cases){
    const h=host();h.env.IGDB_DIAGNOSTICS="true";const old=console.warn,logs=[];console.warn=x=>logs.push(x);
    try{await run(item.handler,async()=>{
      const response=await worker.fetch(request({title:"test"}),h.env),value=await response.json();
      assert.equal(response.status,503);assert.equal(value.diagnostic.stage,item.stage);assert.equal(value.diagnostic.http,item.http);
      const text=JSON.stringify({value,logs});assert.ok(!text.includes("synthetic_secret"));assert.ok(!text.includes("synthetic_app_token"));assert.ok(!text.includes("synthetic_client"));
    });}finally{console.warn=old;}
  }
});
test("Public responses keep diagnostics private by default, including unexpected internal adapter errors",async()=>{
  const h=host();h.map.set("quota-cooldown",{valueOf(){throw Error("synthetic_secret");}});
  const r=await worker.fetch(request({title:"test"}),h.env);assert.equal(r.status,503);assert.deepEqual(await r.json(),{error:"upstream"});
});
test("Legacy cached results lacking game parent context are refetched without confusing edition parents",async()=>{
  const h=host(),q={title:"test"};let calls=0;
  h.map.set('v1:'+JSON.stringify(q),{until:Date.now()+10000,value:{games:[{id:1,name:"Synthetic game",kind:"Port"}],complete:true,fetchedAtUtc:new Date().toISOString()}});
  await run(async u=>u.includes("oauth2/token")?token():(calls++,Response.json([fixtureGame()])),async()=>{
    const value=await(await worker.fetch(request(q),h.env)).json();assert.equal(value.games[0].parentGame,99);assert.equal(value.games[0].versionParent,2);
  });assert.equal(calls,1);
  assert.throws(()=>mapGame({...fixtureGame(),parent_game:1}));
});
