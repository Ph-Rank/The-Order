const MAX_BODY = 1024, MAX_RESPONSE = 256 * 1024;
const text = (v, n = 200) => typeof v === "string" && v.length > 0 && v.length <= n && !/[\u0000-\u001f\u007f]/u.test(v);
const id = v => Number.isSafeInteger(v) && v > 0 && v <= 2147483647;
const safe = v => String(v).replaceAll("\\", "\\\\").replaceAll('"', '\\"');
const json = (data, status = 200) => new Response(JSON.stringify(data), {status, headers:{"Content-Type":"application/json","Cache-Control":"no-store","X-Content-Type-Options":"nosniff",...(status===429?{"Retry-After":"60"}:{})}});
// One application, one namespace, one named object. Edge limits are approximate;
// only this object's persisted counters account for the global upstream budget.
const COORDINATOR = "single-app-quota-v1";
const POLICY = Object.freeze({requestsPerDay:10000, gamesPerDay:1000, gamesPerMinute:60,
  // A cold object has no in-memory app token. Permit the full admitted miss
  // budget across cold starts instead of assuming exactly 24 renewals/day.
  grantsPerDay:1000, uniquePending:8, duplicateWaiters:32, cacheEntries:256, deadlineMs:12000});
// Workers module exports must be handlers/classes/functions, not data constants.
export function publicPolicy() { return POLICY; }
export function coordinatorName() { return COORDINATOR; }
export function cacheKey(q) {
  return q.id ? "v2:id:"+q.id : q.steamId ? "v2:steam:"+q.steamId : "v2:title:"+q.title.normalize("NFC").trim().replace(/\s+/gu," ").toLowerCase();
}
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
  if (!text(value.title) || !value.title.trim() || value.steamId != null && (typeof value.steamId !== "string" || !/^[1-9][0-9]{0,9}$/.test(value.steamId)) || value.id != null && !id(value.id)) throw Error("bad_request");
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
  constructor(state, env) {
    this.state=state;this.env=env;this.tail=Promise.resolve();this.admission=Promise.resolve();
    this.token=null;this.expires=0;this.next=0;this.cooldown=0;this.inflight=new Map();
  }
  // Separate short admission transactions from the slow upstream queue. Storage
  // I/O can yield in a DO, so read/increment/write must have its own serial tail.
  async budget(kind) {
    const run=this.admission.then(async()=>{
      const now=Date.now(),day=Math.floor(now/86400000),minute=Math.floor(now/60000);
      let value=await this.state.storage.get("public-budget-v1");
      if(value && (!["day","minute","requests","games","grants","gamesThisMinute"].every(k=>Number.isSafeInteger(value[k])&&value[k]>=0)))
        throw new IgdbFailure("upstream","budget_storage");
      if(!value || value.day!==day)value={day,requests:0,games:0,grants:0,minute,gamesThisMinute:0};
      if(value.minute!==minute){value.minute=minute;value.gamesThisMinute=0;}
      if(value[kind]>=({requests:POLICY.requestsPerDay,games:POLICY.gamesPerDay,grants:POLICY.grantsPerDay})[kind]
        || kind==="games" && value.gamesThisMinute>=POLICY.gamesPerMinute)throw new IgdbFailure("rate_limited","budget");
      // Copy: even synthetic/in-memory storage must not mutate prior durable data.
      value={...value,[kind]:value[kind]+1,...(kind==="games"?{gamesThisMinute:value.gamesThisMinute+1}:{})};
      await this.state.storage.put("public-budget-v1",value);
      const ceiling=({requests:POLICY.requestsPerDay,games:POLICY.gamesPerDay,grants:POLICY.grantsPerDay})[kind];
      if(value[kind]===Math.floor(ceiling*0.8)||value[kind]===ceiling)
        console.warn(JSON.stringify({igdb:"budget_threshold",kind,used:value[kind],ceiling}));
    });
    this.admission=run.catch(()=>{});return run;
  }
  async upstream(url, init, stage, deadline) {
    try {
      const remaining=deadline-Date.now();if(remaining<=0)throw new IgdbFailure("timeout","queue");
      const response=await fetch(url,{...init,redirect:"manual",signal:AbortSignal.timeout(Math.min(8000,remaining))});
      if(response.status>=300 && response.status<400) throw new IgdbFailure("invalid_response",stage+"_redirect",response.status);
      return response;
    }catch(e){if(e instanceof IgdbFailure)throw e;throw new IgdbFailure(e?.name==="TimeoutError"||e?.name==="AbortError"?"timeout":"upstream",stage+"_network",null,e);}
  }
  async read(response, stage) {
    try{return await body(response);}catch(e){throw new IgdbFailure(e?.name==="TimeoutError"||e?.name==="AbortError"?"timeout":"invalid_response",stage+"_body",response.status,e);}
  }
  async appToken(deadline) {
    if(this.token && Date.now()<this.expires)return this.token;
    await this.budget("grants");
    const response=await this.upstream("https://id.twitch.tv/oauth2/token",{method:"POST",headers:{"Content-Type":"application/x-www-form-urlencoded"},body:new URLSearchParams({client_id:this.env.IGDB_CLIENT_ID,client_secret:this.env.IGDB_CLIENT_SECRET,grant_type:"client_credentials"})},"token",deadline);
    if(response.status===429)await this.backoff(response);
    if(!response.ok)throw new IgdbFailure(response.status===429?"rate_limited":response.status>=500?"upstream":"authentication","token_http",response.status);
    const value=await this.read(response,"token");
    if(!text(value?.access_token,2048)||!Number.isFinite(value.expires_in)||value.expires_in<=60||value.token_type!=="bearer")throw new IgdbFailure("authentication","token_shape",response.status);
    this.token=value.access_token;this.expires=Date.now()+(value.expires_in-60)*1000;return this.token;
  }
  async backoff(response) {
    const retry=response.headers.get("Retry-After"),seconds=Number(retry),date=Date.parse(retry??"");
    this.cooldown=Date.now()+Math.min(3600000,Math.max(1000,Number.isFinite(seconds)&&seconds>0?seconds*1000:Number.isFinite(date)?date-Date.now():60000));
    await this.state.storage.put("quota-cooldown",this.cooldown);
  }
  async cached(q) {
    const prior=await this.state.storage.get(cacheKey(q));
    return prior && prior.until>Date.now() && Array.isArray(prior.value?.games)
      && prior.value.games.every(g=>Object.hasOwn(g,"parentGame")) ? prior.value : null;
  }
  async query(q,deadline) {
    const prior=await this.cached(q);if(prior)return prior;
    if(Date.now()>=deadline)throw new IgdbFailure("timeout","queue");
    this.cooldown=Math.max(this.cooldown,(await this.state.storage.get("quota-cooldown"))??0);
    if(Date.now()<this.cooldown)throw Error("rate_limited");
    const breaker=await this.state.storage.get("circuit-v1");
    if(breaker?.until>Date.now())throw new IgdbFailure(breaker.code??"upstream","circuit");
    let response;
    for(let attempt=0;attempt<2;attempt++){
      const token=await this.appToken(deadline);
      this.next=Math.max(this.next,(await this.state.storage.get("dispatch-not-before"))??0);
      const delay=Math.max(0,this.next-Date.now());if(Date.now()+delay>=deadline)throw new IgdbFailure("timeout","queue");
      if(delay)await new Promise(resolve=>setTimeout(resolve,delay));
      await this.budget("games");
      this.next=Date.now()+275; await this.state.storage.put("dispatch-not-before",this.next);
      response=await this.upstream("https://api.igdb.com/v4/games",{method:"POST",headers:{"Client-ID":this.env.IGDB_CLIENT_ID,Authorization:"Bearer "+token,"Content-Type":"text/plain",Accept:"application/json"},body:gameQuery(q)},"games",deadline);
      if(response.status===401 && attempt===0){this.token=null;this.expires=0;continue;}break;
    }
    if(response.status===401||response.status===403)throw new IgdbFailure("authentication","games_http",response.status);
    if(response.status===429){await this.backoff(response);throw new IgdbFailure("rate_limited","games_http",429);}
    if(!response.ok)throw new IgdbFailure("upstream","games_http",response.status);
    const rows=await this.read(response,"games");if(!Array.isArray(rows)||rows.length>21)throw new IgdbFailure("invalid_response","games_shape",response.status);
    let games;try{games=rows.slice(0,20).map(mapGame);}catch{throw new IgdbFailure("invalid_response","mapping",response.status);}
    if(new Set(games.map(g=>g.id)).size!==games.length || q.id && (games.length>1||games.some(g=>g.id!==q.id)))throw new IgdbFailure("invalid_response","games_identity",response.status);
    const value={games,complete:rows.length<=20,fetchedAtUtc:new Date().toISOString()};
    const key=cacheKey(q);
    await this.state.storage.put(key,{until:Date.now()+(games.length?43200000:1800000),value});
    const keys=await this.state.storage.list({prefix:"v2:",limit:POLICY.cacheEntries+1});if(keys.size>POLICY.cacheEntries)await this.state.storage.delete([...keys.keys()].find(k=>k!==key));
    if(breaker?.failures)await this.state.storage.delete("circuit-v1");
    return value;
  }
  async protectedQuery(q,deadline) {
    try{return await this.query(q,deadline);}catch(e){
      // Budget/queue/cooldown rejections are not upstream health failures.
      if(e instanceof IgdbFailure && !["rate_limited"].includes(e.code) && !["queue","circuit"].includes(e.diagnostic.stage)) {
        const prior=await this.state.storage.get("circuit-v1"),failures=(prior?.failures??0)+1;
        const until=e.code==="authentication"?Date.now()+300000:failures>=3?Date.now()+30000:0;
        await this.state.storage.put("circuit-v1",{failures,until,code:e.code});
        if(until)console.warn(JSON.stringify({igdb:"circuit_open",code:e.code,stage:e.diagnostic.stage}));
      }
      throw e;
    }
  }
  async fetch(request) {
    if(this.env.IGDB_ENABLED!=="true" || !this.env.IGDB_CLIENT_ID || !this.env.IGDB_CLIENT_SECRET)return json({error:"unconfigured"},503);
    if(request.method!=="POST" || new URL(request.url).pathname!=="/query")return json({error:"not_found"},404);
    let query;try{query=parseQuery(await request.json());}catch{return json({error:"bad_request"},400);}
    const key=cacheKey(query);let entry;
    try{
      await this.budget("requests");
      // Cache remains available through an auth outage or upstream cooldown, and
      // never waits behind an unrelated slow network request.
      const cached=await this.cached(query);if(cached)return json(cached);
      const existing=this.inflight.get(key);
      if(existing){if(existing.waiters>=POLICY.duplicateWaiters)throw Error("rate_limited");entry=existing;entry.waiters++;}
      else {
        if(this.inflight.size>=POLICY.uniquePending)throw Error("rate_limited");
        const deadline=Date.now()+POLICY.deadlineMs;
        entry={waiters:1,run:null};
        entry.run=this.tail.then(()=>this.protectedQuery(query,deadline));
        this.tail=entry.run.catch(()=>{});this.inflight.set(key,entry);
        // Clean once at completion, not when the first waiting client returns.
        entry.run.finally(()=>this.inflight.delete(key)).catch(()=>{});
      }
      return json(await entry.run);
    }catch(e){
      const code=["authentication","rate_limited","invalid_response","timeout"].includes(e?.message)?e.message:"upstream";
      const diagnostic=e instanceof IgdbFailure?e.diagnostic:{stage:"coordinator",http:null,kind:null,networkCode:null};
      // Never log an exception, stack, response body, request headers or URL.
      if(this.env.IGDB_DIAGNOSTICS==="true")console.warn(JSON.stringify({igdb:code,...diagnostic}));
      return json({error:code,...(this.env.IGDB_DIAGNOSTICS==="true"?{diagnostic}:{})},code==="rate_limited"?429:503);
    }finally{if(entry)entry.waiters--;}
  }
}
export default {
  async fetch(request,env) {
    const url=new URL(request.url);
    if(url.pathname!=="/v1/game-videos/query"||url.search||request.method!=="POST")return json({error:"not_found"},404);
    if(env.IGDB_ENABLED!=="true")return json({error:"unconfigured"},503);
    if(request.headers.get("Origin"))return json({error:"bad_request"},400);
    if(!/^application\/json(?:\s*;|$)/i.test(request.headers.get("Content-Type")??"")||Number(request.headers.get("Content-Length"))>MAX_BODY)return json({error:"bad_request"},400);
    // No application-wide client token: it is an anonymous, bounded endpoint.
    // CF supplies this header at its edge. Never trust a client-provided actor ID.
    if(env.IGDB_PUBLIC==="true"){
      const ip=request.headers.get("CF-Connecting-IP");
      if(!ip || !env.IP_LIMITER)return json({error:"unconfigured"},503);
      try{if(!(await env.IP_LIMITER.limit({key:"igdb:"+ip})).success)return json({error:"rate_limited"},429);}
      catch{return json({error:"upstream"},503);}
    }
    // Bounded streaming input, including absent Content-Length.
    const reader=request.body?.getReader();if(!reader)return json({error:"bad_request"},400);
    let chunks=[],size=0,timer;
    const expired=new Promise((_,reject)=>{timer=setTimeout(()=>{reader.cancel().catch(()=>{});reject(Error("body_timeout"));},2000);});
    try{await Promise.race([(async()=>{while(true){const {value,done}=await reader.read();if(done)break;size+=value.length;if(size>MAX_BODY)throw Error();chunks.push(value);}})(),expired]);}
    catch{return json({error:"bad_request"},400);}finally{clearTimeout(timer);await reader.cancel().catch(()=>{});}
    const bytes=new Uint8Array(size);let offset=0;for(const part of chunks){bytes.set(part,offset);offset+=part.length;}
    let q;try{q=parseQuery(JSON.parse(new TextDecoder().decode(bytes)));}catch{return json({error:"bad_request"},400);}
    try{
      const stub=env.IGDB_COORDINATOR.get(env.IGDB_COORDINATOR.idFromName(COORDINATOR));
      return await stub.fetch("https://igdb.internal/query",{method:"POST",body:JSON.stringify(q)});
    }catch{return json({error:"upstream"},503);}
  }
};
