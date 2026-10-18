import { createRequire } from "node:module";
import { createServer } from "node:http";
import {readFile,writeFile,access} from "node:fs/promises";
import {resolve,join} from "node:path";
const require=createRequire(new URL("../../bangumi-auth-worker/package.json",import.meta.url));
const {Miniflare,Response:LocalResponse}=require("miniflare");
const root=resolve(process.argv[2]??"");
if(!root.endsWith(join(".artifacts","igdb-game-pv-20261018")))throw Error("Owned synthetic evidence root required");
const records=id=>({id,name:id===1?"IGDB 合成验收游戏":"IGDB 合成验收游戏 REMASTERED",game_type:{name:id===1?"Main Game":"Remaster"},
  version_parent:id===2?1:undefined,alternative_names:[{name:"Synthetic game"}],first_release_date:1072915200,platforms:[{name:"PC"}],involved_companies:[{developer:true,company:{name:"Synthetic test studio"}}],
  videos:[{id:100+id,game:id,name:"Synthetic Launch Trailer (not a real IGDB record)",video_id:"abcdefghijk"},{id:200+id,game:id,name:"Synthetic OP (not a real IGDB record)",video_id:"12345678901"},{id:300+id,game:id,name:"Duplicate original name",video_id:"abcdefghijk"}]});
let grants=0,queries=0;
const mf=new Miniflare({modules:true,script:await readFile(new URL("../src/index.mjs",import.meta.url),"utf8"),compatibilityDate:"2026-08-01",cf:false,
  durableObjects:{IGDB_COORDINATOR:{className:"IgdbCoordinator",useSQLite:true}},
  bindings:{IGDB_ENABLED:"true",IGDB_CLIENT_ID:"synthetic_id",IGDB_CLIENT_SECRET:"synthetic_secret"},
  outboundService:async request=>{
    if(request.url==="https://id.twitch.tv/oauth2/token"){grants++;return new LocalResponse(JSON.stringify({access_token:"synthetic_test_token",expires_in:3600,token_type:"bearer"}),{headers:{"Content-Type":"application/json"}});}
    if(request.url==="https://api.igdb.com/v4/games"){queries++;const body=await request.text();const exact=body.match(/where id = (\d+);/);return new LocalResponse(JSON.stringify(exact?[records(Number(exact[1]))]:[records(1),records(2)]),{headers:{"Content-Type":"application/json"}});}
    throw Error("Synthetic contract refuses every other upstream destination");
  }});
await mf.ready;
const server=createServer(async(req,res)=>{
  const chunks=[];let size=0;for await(const value of req){size+=value.length;if(size>1024){res.writeHead(400);res.end();return;}chunks.push(value);}
  try{const r=await mf.dispatchFetch("https://fixture.invalid"+req.url,{method:req.method,headers:{"Content-Type":"application/json"},body:Buffer.concat(chunks)});res.writeHead(r.status,{"Content-Type":"application/json"});res.end(Buffer.from(await r.arrayBuffer()));}
  catch{res.writeHead(503,{"Content-Type":"application/json"});res.end('{"error":"upstream"}');}
});
await new Promise(r=>server.listen(0,"127.0.0.1",r));
const url="http://127.0.0.1:"+server.address().port+"/";
await writeFile(join(root,"local-service.json"),JSON.stringify({url,pid:process.pid,synthetic:true,realIgdbRequests:false,productionDeployed:false}));
console.log(JSON.stringify({url,synthetic:true}));
const timer=setInterval(async()=>{try{await access(join(root,"stop-local-service"));clearInterval(timer);server.close();await mf.dispose();await writeFile(join(root,"local-service-exit.json"),JSON.stringify({grants,queries,synthetic:true,normalExit:true}));}catch{}},300);