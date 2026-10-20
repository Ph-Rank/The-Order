// workerd + real SQLite persistence, entirely synthetic outbound API responses.
// The probe subclass exists only in this test and never in a deployable bundle.
import {createRequire} from "node:module";
import {readFile,writeFile,mkdir,readdir} from "node:fs/promises";
import {resolve,join,sep} from "node:path";
import assert from "node:assert/strict";
import {serviceRoot,repo,validateDeployment} from "../scripts/check-deployment.mjs";
const require=createRequire(new URL("../../bangumi-auth-worker/package.json",import.meta.url));
const {Miniflare,Response:LocalResponse,Log,LogLevel}=require("miniflare");
const output=resolve(process.argv.find(x=>x.startsWith("--output="))?.slice(9)??join(repo,".artifacts/igdb-production-ready-20261018/contract-v1"));
assert(output.startsWith(join(repo,".artifacts")+sep));await mkdir(output);await validateDeployment();
const source=await readFile(join(serviceRoot,"src/index.mjs"),"utf8");
const probe=source+`\nexport class ProbeCoordinator extends IgdbCoordinator {
  async fetch(request) {
    if(new URL(request.url).pathname==='/__probe'){
      const value=await request.json();
      if(value.action==='get')return Response.json((await this.state.storage.get(value.key))??null);
      if(value.action==='put')await this.state.storage.put(value.key,value.value);
      if(value.action==='delete')await this.state.storage.delete(value.key);
      return Response.json({done:true});
    }
    return super.fetch(request);
  }
}`;
let grants=0,games=0,mf,stub;const checks=[];
const settings={modules:true,script:probe,compatibilityDate:"2026-08-01",cf:false,log:new Log(LogLevel.ERROR),
  durableObjects:{IGDB_COORDINATOR:{className:"ProbeCoordinator",useSQLite:true}},durableObjectsPersist:join(output,"sqlite"),
  ratelimits:{IP_LIMITER:{namespace_id:"2026101801",simple:{limit:60,period:60}}},
  bindings:{IGDB_ENABLED:"true",IGDB_PUBLIC:"true",IGDB_CLIENT_ID:"synthetic_client",IGDB_CLIENT_SECRET:"synthetic_secret"},
  outboundService:async request=>{
    if(request.url==="https://id.twitch.tv/oauth2/token"){grants++;assert.equal(request.method,"POST");return LocalResponse.json({access_token:"synthetic_token",expires_in:3600,token_type:"bearer"});}
    assert.equal(request.url,"https://api.igdb.com/v4/games");games++;
    return LocalResponse.json([{id:1,name:"Synthetic SQLite game",game_type:{type:"Main Game"},videos:[{id:10,game:1,name:"Trailer",video_id:"abcdefghijk"}]}]);
  }};
const start=async()=>{mf=new Miniflare(settings);await mf.ready;const ns=await mf.getDurableObjectNamespace("IGDB_COORDINATOR");stub=ns.get(ns.idFromName("single-app-quota-v1"));};
const query=async(value={title:"Game",id:1})=>mf.dispatchFetch("https://fixture.test/v1/game-videos/query",{method:"POST",headers:{"Content-Type":"application/json","CF-Connecting-IP":"192.0.2.1"},body:JSON.stringify(value)});
const state=async(action,key,value)=>(await stub.fetch("https://internal.test/__probe",{method:"POST",body:JSON.stringify({action,key,value})})).json();
function pass(name){checks.push({name,result:"PASS"});}
try{
  await start();const first=await Promise.all([query(),query(),query()]);assert.ok(first.every(x=>x.status===200));assert.equal(grants,1);assert.equal(games,1);pass("Burst coalesces with real workerd request scheduling");
  const initial=await state("get","public-budget-v1");assert.equal(initial.requests,3);assert.equal(initial.games,1);pass("SQLite persists atomic global request and upstream counts");
  await state("put","quota-cooldown",Date.now()+60000);await state("put","circuit-v1",{failures:3,until:Date.now()+60000,code:"authentication"});
  assert.equal((await query({title:"Renamed",id:1})).status,200);assert.equal((await query({title:"Uncached"})).status,429);pass("Cached identity survives a provider cooldown and auth outage");
  await mf.dispose();await start();
  assert.equal((await query({title:"After restart",id:1})).status,200);assert.equal((await query({title:"Still uncached"})).status,429);assert.equal(games,1);assert.equal(grants,1);pass("Runtime restart preserves cache and cooldown without another OAuth grant");
  let budget=await state("get","public-budget-v1");assert.equal(budget.requests,7);await state("put","public-budget-v1",{...budget,requests:10000});
  await mf.dispose();await start();assert.equal((await query()).status,429);pass("Restart cannot reset the global daily admission ceiling");
  assert.equal((await mf.dispatchFetch("https://fixture.test/__probe")).status,404);pass("Public router never exposes the test storage probe");
  await mf.dispose();mf=new Miniflare({...settings,bindings:{...settings.bindings,IGDB_ENABLED:"false"}});await mf.ready;
  assert.equal((await query()).status,503);assert.equal(games,1);pass("Off switch blocks even cached results without contacting IGDB");
  const result={checks,passed:checks.length,failed:0,realIgdb:false,realTlsEndpoint:false,sqlite:true,grants,games,productionWrites:0};
  await writeFile(join(output,"result.json"),JSON.stringify(result,null,2));console.log(JSON.stringify(result));
}finally{await mf?.dispose();}
