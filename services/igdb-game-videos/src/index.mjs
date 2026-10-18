const MAX_BODY = 1024, MAX_RESPONSE = 256 * 1024;
const text = (v, n = 200) => typeof v === "string" && v.length > 0 && v.length <= n && !/[\u0000-\u001f\u007f]/u.test(v);
const id = v => Number.isSafeInteger(v) && v > 0 && v <= 2147483647;
const safe = v => String(v).replaceAll("\\", "\\\\").replaceAll('"', '\\"');
const json = (data, status = 200) => new Response(JSON.stringify(data), {status, headers:{"Content-Type":"application/json","Cache-Control":"no-store","X-Content-Type-Options":"nosniff"}});
const fields = "id,name,alternative_names.name,first_release_date,game_type.type,parent_game,version_parent,remakes,remasters,platforms.name,involved_companies.developer,involved_companies.company.name,external_games.uid,external_games.external_game_source.name,videos.id,videos.game,videos.name,videos.video_id";
const networkCodes = new Set(["ENETUNREACH","ECONNREFUSED","ECONNRESET","ETIMEDOUT","ENOTFOUND","EAI_AGAIN","CERT_HAS_EXPIRED","UNABLE_TO_VERIFY_LEAF_SIGNATURE","DEPTH_ZERO_SELF_SIGNED_CERT","ERR_TLS_CERT_ALTNAME_INVALID","UND_ERR_CONNECT_TIMEOUT","UND_ERR_SOCKET"]);
export class IgdbFailure extends Error {
  constructor(code, stage, http = null, cause = null) {
    super(code);this.code=code;
    this.diagnostic={stage,http,kind:["TimeoutError","AbortError","TypeError","SyntaxError"].includes(cause?.name)?cause.name:null,
      networkCode:networkCodes.has(cause?.cause?.code)?cause.cause.code:null};
  }
}
export function parseQuery(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) || Object.keys(value).some(k => !["title","steamId","id"].includes(k))) throw Error("bad_request");
  if (!text(value.title) || value.steamId != null && (typeof value.steamId !== "string" || !/^[1-9][0-9]{0,9}$/.test(value.steamId)) || value.id != null && !id(value.id)) throw Error("bad_request");
  return {title:value.title, ...(value.steamId == null ? {} : {steamId:value.steamId}), ...(value.id == null ? {} : {id:value.id})};
}
export function gameQuery(q) {
  if (q.id) return "fields "+fields+"; where id = "+q.id+"; limit 1;";
  if (q.steamId) return "fields "+fields+'; where external_games.external_game_source.name = "Steam" & external_games.uid = "'+safe(q.steamId)+'"; limit 21;';
  return "fields "+fields+'; search "'+safe(q.title)+'"; limit 21;';
}
function list(value, max, map) {
  if (value == null) return [];
  if (!Array.isArray(value) || value.length > max) throw Error("invalid_response");
  return value.map(map);
}
export function mapGame(g) {
  if (!g || !id(g.id) || !text(g.name,500) || g.version_parent != null && !id(g.version_parent) || g.parent_game != null && (!id(g.parent_game)||g.parent_game===g.id)) throw Error("invalid_response");
  const strings = (v,max) => list(v,max,x => {if(!text(x.name,500))throw Error("invalid_response");return x.name;});
  const videos = list(g.videos,32,v => {
    if(v.game !== g.id) throw Error("invalid_response");
    if(!id(v.id) || !text(v.name,500) || !/^[a-zA-Z0-9_-]{11}$/.test(v.video_id)) return null;
    return {id:v.id,gameId:g.id,videoId:v.video_id,name:v.name};
  }).filter(Boolean);
  const date = g.first_release_date == null ? null : new Date(g.first_release_date * 1000);
  if(date && (!Number.isFinite(g.first_release_date) || !Number.isFinite(date.getTime()))) throw Error("invalid_response");
  return {id:g.id,name:g.name,aliases:strings(g.alternative_names,32),
    developers:list(g.involved_companies,32,x=>x.developer ? x.company : null).filter(Boolean).map(x=>{if(!text(x.name,500))throw Error("invalid_response");return x.name;}),
    platforms:strings(g.platforms,32),releaseDate:date ? date.toISOString().slice(0,10) : null,
    kind:text(g.game_type?.type,100) ? g.game_type.type : "Unknown",parentGame:g.parent_game ?? null,versionParent:g.version_parent ?? null,
    remakes:list(g.remakes,32,x=>{if(!id(x))throw Error("invalid_response");return x;}),
    remasters:list(g.remasters,32,x=>{if(!id(x))throw Error("invalid_response");return x;}),
    externalIds:list(g.external_games,64,x=>{if(!text(x.uid,100)||!text(x.external_game_source?.name,100))throw Error("invalid_response");return {source:x.external_game_source.name,id:x.uid};}),videos};
}
async function body(response) {
  if(Number(response.headers.get("Content-Length")) > MAX_RESPONSE) throw Error("invalid_response");
  const reader=response.body?.getReader(); if(!reader) throw Error("invalid_response");
  let size=0,chunks=[];try{while(true){const {value,done}=await reader.read();if(done)break;size+=value.length;if(size>MAX_RESPONSE)throw Error("invalid_response");chunks.push(value);}}
  finally{await reader.cancel().catch(()=>{});}
  const data=new Uint8Array(size);let offset=0;for(const part of chunks){data.set(part,offset);offset+=part.length;}return JSON.parse(new TextDecoder().decode(data));
}
export class IgdbCoordinator {
  constructor(state, env) {this.state=state;this.env=env;this.tail=Promise.resolve();this.token=null;this.expires=0;this.next=0;this.cooldown=0;this.pending=0;}
  async upstream(url, init, stage) {
    try {
      const response=await fetch(url,{...init,redirect:"manual",signal:AbortSignal.timeout(8000)});
      if(response.status>=300 && response.status<400) throw new IgdbFailure("invalid_response",stage+"_redirect",response.status);
      return response;
    }catch(e){if(e instanceof IgdbFailure)throw e;throw new IgdbFailure(e?.name==="TimeoutError"||e?.name==="AbortError"?"timeout":"upstream",stage+"_network",null,e);}
  }
  async read(response, stage) {
    try{return await body(response);}catch(e){throw new IgdbFailure(e?.name==="TimeoutError"||e?.name==="AbortError"?"timeout":"invalid_response",stage+"_body",response.status,e);}
  }
  async appToken() {
    if(this.token && Date.now()<this.expires)return this.token;
    const response=await this.upstream("https://id.twitch.tv/oauth2/token",{method:"POST",headers:{"Content-Type":"application/x-www-form-urlencoded"},body:new URLSearchParams({client_id:this.env.IGDB_CLIENT_ID,client_secret:this.env.IGDB_CLIENT_SECRET,grant_type:"client_credentials"})},"token");
    if(!response.ok)throw new IgdbFailure(response.status===429?"rate_limited":response.status>=500?"upstream":"authentication","token_http",response.status);
    const value=await this.read(response,"token");
    if(!text(value?.access_token,2048)||!Number.isFinite(value.expires_in)||value.expires_in<=60||value.token_type!=="bearer")throw new IgdbFailure("authentication","token_shape",response.status);
    this.token=value.access_token;this.expires=Date.now()+(value.expires_in-60)*1000;return this.token;
  }
  async query(q) {
    this.cooldown=Math.max(this.cooldown,(await this.state.storage.get("quota-cooldown"))??0);
    if(Date.now()<this.cooldown)throw Error("rate_limited");
    const key="v1:"+JSON.stringify(q),prior=await this.state.storage.get(key);
    if(prior && prior.until>Date.now() && prior.value?.games?.every(g=>Object.hasOwn(g,"parentGame")))return prior.value;
    let response;
    for(let attempt=0;attempt<2;attempt++){
      const token=await this.appToken();
      this.next=Math.max(this.next,(await this.state.storage.get("dispatch-not-before"))??0);
      const delay=Math.max(0,this.next-Date.now());if(delay)await new Promise(resolve=>setTimeout(resolve,delay));
      this.next=Date.now()+275; await this.state.storage.put("dispatch-not-before",this.next);
      response=await this.upstream("https://api.igdb.com/v4/games",{method:"POST",headers:{"Client-ID":this.env.IGDB_CLIENT_ID,Authorization:"Bearer "+token,"Content-Type":"text/plain",Accept:"application/json"},body:gameQuery(q)},"games");
      if(response.status===401 && attempt===0){this.token=null;this.expires=0;continue;}break;
    }
    if(response.status===401||response.status===403)throw new IgdbFailure("authentication","games_http",response.status);
    if(response.status===429){this.cooldown=Date.now()+Math.min(60000,Math.max(1000,(Number(response.headers.get("Retry-After"))||2)*1000));await this.state.storage.put("quota-cooldown",this.cooldown);throw new IgdbFailure("rate_limited","games_http",429);}
    if(!response.ok)throw new IgdbFailure("upstream","games_http",response.status);
    const rows=await this.read(response,"games");if(!Array.isArray(rows)||rows.length>21)throw new IgdbFailure("invalid_response","games_shape",response.status);
    let games;try{games=rows.slice(0,20).map(mapGame);}catch{throw new IgdbFailure("invalid_response","mapping",response.status);}
    if(new Set(games.map(g=>g.id)).size!==games.length || q.id && (games.length>1||games.some(g=>g.id!==q.id)))throw new IgdbFailure("invalid_response","games_identity",response.status);
    const value={games,complete:rows.length<=20,fetchedAtUtc:new Date().toISOString()};
    await this.state.storage.put(key,{until:Date.now()+(games.length?43200000:1800000),value});
    const keys=await this.state.storage.list({prefix:"v1:",limit:257});if(keys.size>256)await this.state.storage.delete([...keys.keys()].find(k=>k!==key));
    return value;
  }
  async fetch(request) {
    if(this.env.IGDB_ENABLED!=="true" || !this.env.IGDB_CLIENT_ID || !this.env.IGDB_CLIENT_SECRET)return json({error:"unconfigured"},503);
    if(this.pending>=8)return json({error:"rate_limited"},429);
    let query;try{query=parseQuery(await request.json());}catch{return json({error:"bad_request"},400);}
    this.pending++; const run=this.tail.then(()=>this.query(query));this.tail=run.catch(()=>{});
    try{return json(await run);}catch(e){
      const code=["authentication","rate_limited","invalid_response","timeout"].includes(e?.message)?e.message:"upstream";
      const diagnostic=e instanceof IgdbFailure?e.diagnostic:{stage:"coordinator",http:null,kind:null,networkCode:null};
      // Never log an exception, stack, response body, request headers or URL.
      if(this.env.IGDB_DIAGNOSTICS==="true")console.warn(JSON.stringify({igdb:code,...diagnostic}));
      return json({error:code,...(this.env.IGDB_DIAGNOSTICS==="true"?{diagnostic}:{})},code==="rate_limited"?429:503);
    }finally{this.pending--;}
  }
}
export default {
  async fetch(request,env) {
    const url=new URL(request.url);
    if(url.pathname!=="/v1/game-videos/query"||url.search||request.method!=="POST")return json({error:"not_found"},404);
    if(request.headers.get("Origin"))return json({error:"bad_request"},400);
    if(!request.headers.get("Content-Type")?.startsWith("application/json")||Number(request.headers.get("Content-Length"))>MAX_BODY)return json({error:"bad_request"},400);
    // Bounded streaming input, including absent Content-Length.
    const reader=request.body?.getReader();if(!reader)return json({error:"bad_request"},400);
    let chunks=[],size=0;try{while(true){const {value,done}=await reader.read();if(done)break;size+=value.length;if(size>MAX_BODY)throw Error();chunks.push(value);}}catch{return json({error:"bad_request"},400);}finally{await reader.cancel().catch(()=>{});}
    const bytes=new Uint8Array(size);let offset=0;for(const part of chunks){bytes.set(part,offset);offset+=part.length;}
    let q;try{q=parseQuery(JSON.parse(new TextDecoder().decode(bytes)));}catch{return json({error:"bad_request"},400);}
    const stub=env.IGDB_COORDINATOR.get(env.IGDB_COORDINATOR.idFromName("single-app-quota-v1"));
    return stub.fetch("https://igdb.internal/query",{method:"POST",body:JSON.stringify(q)});
  }
};
