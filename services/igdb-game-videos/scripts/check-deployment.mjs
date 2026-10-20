import {readFile,mkdir,writeFile} from "node:fs/promises";
import {spawnSync} from "node:child_process";
import {fileURLToPath} from "node:url";
import {resolve,sep,join,dirname} from "node:path";
import {createHash} from "node:crypto";
import assert from "node:assert/strict";

export const serviceRoot=fileURLToPath(new URL("../",import.meta.url));
export const repo=resolve(serviceRoot,"../..");
export async function validateDeployment(){
  const c=JSON.parse(await readFile(join(serviceRoot,"wrangler.production.jsonc"),"utf8"));
  assert.equal(c.name,"the-order-igdb-game-videos");assert.equal(c.main,"src/index.mjs");
  assert.equal(c.workers_dev,true);assert.equal(c.preview_urls,false);assert.equal(c.send_metrics,false);
  assert.deepEqual(c.observability,{enabled:true,head_sampling_rate:1,logs:{enabled:true,invocation_logs:false},traces:{enabled:false}});
  assert.deepEqual(c.vars,{IGDB_ENABLED:"false",IGDB_DIAGNOSTICS:"false",IGDB_PUBLIC:"true"});
  assert.deepEqual(c.durable_objects.bindings,[{name:"IGDB_COORDINATOR",class_name:"IgdbCoordinator"}]);
  assert.deepEqual(c.migrations,[{tag:"v1",new_sqlite_classes:["IgdbCoordinator"]}]);
  assert.deepEqual(c.ratelimits,[{name:"IP_LIMITER",namespace_id:"2026101801",simple:{limit:60,period:60}}]);
  assert.equal(c.env,undefined,"Never create an independent staging quota owner");assert.equal(c.routes,undefined);
  assert.equal(c.keep_vars,undefined,"An ordinary redeploy must restore the off switch, not preserve an enabled dashboard variable");
  assert.deepEqual(Object.keys(c.vars).filter(k=>/CLIENT|TOKEN|SECRET/.test(k)),[]);
  const client=await readFile(join(repo,"native/TheOrder.Infrastructure/Online/IgdbServiceEndpoint.cs"),"utf8");
  assert(client.includes('PublicServiceUrl = "https://the-order-igdb-game-videos.zyx2251721565.workers.dev/"'));
  const server=await readFile(join(serviceRoot,"src/index.mjs"),"utf8");
  assert(server.includes('COORDINATOR = "single-app-quota-v1"'));assert(server.includes('idFromName(COORDINATOR)'));
  return c;
}

export async function bundle(output){
  const config=await validateDeployment(),folder=resolve(output),allowed=join(repo,".artifacts")+sep;
  assert(folder.startsWith(allowed),"Owned artifact directory required");
  await mkdir(dirname(folder),{recursive:true});await mkdir(folder);await mkdir(join(folder,"src"));
  const source=await readFile(join(serviceRoot,"src/index.mjs"));
  // Bundle from a credential-free staging directory, not beside .dev.vars.
  await writeFile(join(folder,"src/index.mjs"),source);
  const {$schema,...staged}=config;await writeFile(join(folder,"wrangler.production.jsonc"),JSON.stringify(staged,null,2));
  const env={...process.env,WRANGLER_SEND_METRICS:"false"};
  for(const key of Object.keys(env))if(/^(IGDB_|TWITCH_|THE_ORDER_|KGM_)/i.test(key))delete env[key];
  const result=spawnSync(process.execPath,[join(repo,"services/bangumi-auth-worker/node_modules/wrangler/bin/wrangler.js"),"deploy","--config",join(folder,"wrangler.production.jsonc"),"--dry-run","--outdir",join(folder,"bundle")],
    {cwd:folder,env,windowsHide:true,encoding:"utf8",timeout:60000,maxBuffer:1024*1024});
  await writeFile(join(folder,"dry-run.log"),(result.stdout??"")+(result.stderr??""));
  assert.equal(result.status,0,"Wrangler dry-run failed; inspect the credential-free stage log");
  const value={configuration:"PASS",worker:config.name,sqlite:true,coordinators:1,previewUrls:false,enabled:false,
    serviceSourceSha256:createHash("sha256").update(source).digest("hex"),cloudflareWrites:0,credentialsRead:false,publicDeploymentExecuted:false};
  await writeFile(join(folder,"bundle-receipt.json"),JSON.stringify(value,null,2));return value;
}
if(process.argv[1] && resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const output=process.argv.find(x=>x.startsWith("--bundle="))?.slice(9);
  console.log(JSON.stringify(output?await bundle(output):(await validateDeployment(),{configuration:"PASS",deploymentExecuted:false})));
}
