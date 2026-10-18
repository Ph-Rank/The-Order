// Opt-in real metadata measurement. Never infers a correct identity from a title.
import {readFile,writeFile} from "node:fs/promises";
import {isIP} from "node:net";
const all=process.argv.includes("--all"),path=process.argv.find(x=>x.startsWith("--output="))?.slice(9);
if(!path)throw Error("Explicit --output=<new report.json> required; no profile is read");
const frozen=JSON.parse((await readFile(new URL("samples.json",import.meta.url),"utf8")).replace(/^\uFEFF/,""));
const selected=all?frozen.samples:frozen.samples.filter(s=>s.phase==="pilot");
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
  if(endpoint){
    try{
      const response=await fetch(endpoint,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({title:sample.title}),redirect:"error",signal:AbortSignal.timeout(15000)});
      const reader=response.body?.getReader();if(!reader)throw Error("invalid_response");let content="",size=0;const decoder=new TextDecoder();
      try{while(true){const {value,done}=await reader.read();if(done)break;size+=value.length;if(size>262144)throw Error("oversized");content+=decoder.decode(value,{stream:true});}content+=decoder.decode();}finally{await reader.cancel().catch(()=>{});}
      const value=JSON.parse(content);
      row.status=response.ok?"CANDIDATES_REQUIRE_REVIEW":["unconfigured","authentication","rate_limited","timeout"].includes(value?.error)?value.error:"REQUEST_FAILED";
      if(response.ok){
        if(!Array.isArray(value.games)||value.games.length>20||typeof value.complete!=="boolean")throw Error("invalid_response");
        row.complete=value.complete;row.candidates=value.games.map(g=>({id:g.id,name:g.name,developers:g.developers,platforms:g.platforms,releaseDate:g.releaseDate,kind:g.kind,versionParent:g.versionParent,videos:g.videos}));
        if(value.games.length===0)row.status="NO_SEARCH_CANDIDATES_NOT_PROOF_OF_ABSENCE";
      }
    }catch{row.status="REQUEST_FAILED";}
    // This tool never probes YouTube/platform pages, grants tokens or confirms matches.
    await new Promise(r=>setTimeout(r,350));
  }
  report.samples.push(row);
}
report.counts={frozen:frozen.samples.length,attempted:endpoint?selected.length:0,measured:selected.length,verifiedMatches:0,verifiedVideos:0,successfulGamePlayback:0};
await writeFile(path,JSON.stringify(report,null,2)+"\n",{flag:"wx"});
console.log(JSON.stringify({report:path,...report.counts,realApi:report.realApi,accuracy:report.identityAccuracy}));
