// Opt-in real metadata measurement. Never infers a correct identity from a title.
import {readFile,writeFile} from "node:fs/promises";
import {isIP} from "node:net";
const all=process.argv.includes("--all"),path=process.argv.find(x=>x.startsWith("--output="))?.slice(9),planPath=process.argv.find(x=>x.startsWith("--plan="))?.slice(7);
if(!path)throw Error("Explicit --output=<new report.json> required; no profile is read");
const frozen=JSON.parse((await readFile(new URL("samples.json",import.meta.url),"utf8")).replace(/^\uFEFF/,""));
const selected=all?frozen.samples:frozen.samples.filter(s=>s.phase==="pilot");
const plan=planPath?JSON.parse((await readFile(planPath,"utf8")).replace(/^\uFEFF/,"")):null;
if(plan && (plan.samples?.length!==frozen.samples.length || frozen.samples.some(s=>{const p=plan.samples.filter(p=>p.sample===s.sample);return p.length!==1||p[0].title!==s.title||!Array.isArray(p[0].queries)||p[0].queries.length<1||p[0].queries.length>3||p[0].queries.some(q=>typeof q!=="string"||!q.trim()||q.length>200); })))throw Error("Plan must preserve all frozen samples and contain at most three bounded title queries each");
const report={frozenUtc:frozen.frozenUtc,measuredUtc:new Date().toISOString(),realApi:false,identityAccuracy:"NOT_REVIEWED",playback:"NOT_TESTED",samples:[]};
const raw=process.env.IGDB_TEST_SERVICE_URL;
let endpoint;
if(raw){
  const u=new URL(raw),loop=u.protocol==="http:"&&["127.0.0.1","localhost","[::1]"].includes(u.hostname);
  if(u.username||u.password||u.pathname!=="/"||u.search||u.hash||!(loop||u.protocol==="https:"&&!u.port&&!isIP(u.hostname)&&u.hostname!=="localhost"))throw Error("Fixed HTTPS service root or explicit local Test service required");
  endpoint=new URL("v1/game-videos/query",u);report.transport=loop?"LOCAL_TEST":"CONFIGURED_HTTPS";report.realApi="NOT_ATTESTED_BY_CLIENT";
}
for(const sample of selected){
  const row={...sample,status:endpoint?"PENDING":"BLOCKED_NO_SERVICE_CONFIGURATION",identity:"NOT_REVIEWED",versionAccuracy:"NOT_REVIEWED",embeddedPlayback:"NOT_TESTED"};
  row.queries=[];
  if(endpoint){
    for(const title of plan?.samples.find(p=>p.sample===sample.sample)?.queries??[sample.title]){
    row.candidates=[];row.complete=undefined;row.http=null;row.error=null;
    try{
      const response=await fetch(endpoint,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({title}),redirect:"error",signal:AbortSignal.timeout(15000)});
      row.http=response.status;
      const reader=response.body?.getReader();if(!reader)throw Error("invalid_response");let content="",size=0;const decoder=new TextDecoder();
      try{while(true){const {value,done}=await reader.read();if(done)break;size+=value.length;if(size>262144)throw Error("oversized");content+=decoder.decode(value,{stream:true});}content+=decoder.decode();}finally{await reader.cancel().catch(()=>{});}
      const value=JSON.parse(content);
      row.error=["unconfigured","authentication","rate_limited","timeout","invalid_response","upstream"].includes(value?.error)?value.error:null;
      row.status=response.ok?"CANDIDATES_REQUIRE_REVIEW":["unconfigured","authentication","rate_limited","timeout"].includes(value?.error)?value.error:"REQUEST_FAILED";
      if(response.ok){
        if(!Array.isArray(value.games)||value.games.length>20||typeof value.complete!=="boolean")throw Error("invalid_response");
        row.complete=value.complete;row.candidates=value.games.map(g=>({id:g.id,name:g.name,aliases:g.aliases,developers:g.developers,platforms:g.platforms,releaseDate:g.releaseDate,kind:g.kind,parentGame:g.parentGame,versionParent:g.versionParent,remakes:g.remakes,remasters:g.remasters,externalIds:g.externalIds,videos:g.videos}));
        if(value.games.length===0)row.status="NO_SEARCH_CANDIDATES_NOT_PROOF_OF_ABSENCE";
      }
    }catch{row.status="REQUEST_FAILED";}
    row.queries.push({title,status:row.status,http:row.http,error:row.error,complete:row.complete,candidates:row.candidates??[]});
    // This tool never probes YouTube/platform pages, grants tokens or confirms matches.
    await new Promise(r=>setTimeout(r,350));
    }
    row.candidates=[...new Map(row.queries.flatMap(q=>q.candidates).map(g=>[g.id,g])).values()];
    row.complete=row.queries.every(q=>q.complete===true);
    row.status=row.queries.some(q=>["REQUEST_FAILED","authentication","rate_limited","timeout","unconfigured"].includes(q.status))?"PARTIAL_REQUEST_FAILURE":row.candidates.length?"CANDIDATES_REQUIRE_REVIEW":"NO_SEARCH_CANDIDATES_NOT_PROOF_OF_ABSENCE";
  }
  report.samples.push(row);
}
report.counts={frozen:frozen.samples.length,attempted:endpoint?selected.length:0,requests:report.samples.reduce((n,s)=>n+s.queries.length,0),measured:selected.length,verifiedMatches:0,verifiedVideos:0,successfulGamePlayback:0};
await writeFile(path,JSON.stringify(report,null,2)+"\n",{flag:"wx"});
console.log(JSON.stringify({report:path,...report.counts,realApi:report.realApi,accuracy:report.identityAccuracy}));
