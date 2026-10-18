// This script is an operator convenience, not permission. Run a mutating action
// only after the human approves this exact first deployment and Secret setup.
import {readFile} from "node:fs/promises";
import {spawn} from "node:child_process";
import {createHash} from "node:crypto";
import {join,resolve,sep,dirname} from "node:path";
import assert from "node:assert/strict";
import {validateDeployment,serviceRoot,repo} from "./check-deployment.mjs";

const action=process.argv[2],allowed=["bootstrap","secrets","enable","disable"];
assert(allowed.includes(action),"Choose bootstrap, secrets, enable or disable");await validateDeployment();
assert(process.argv.includes("--user-approved"),"Direct human approval is required; a flag alone never grants authorization");
const reviewPath=process.argv.find(x=>x.startsWith("--review="))?.slice(9);
assert(reviewPath,"A frozen predeployment review receipt is required");
const review=JSON.parse(await readFile(resolve(reviewPath),"utf8"));assert.equal(review.localPredeploymentGate,"PASS");
for(const file of review.serviceFiles){const data=await readFile(join(serviceRoot,file.path));assert.equal(createHash("sha256").update(data).digest("hex"),file.sha256,"Service changed after qualification");}
assert.deepEqual(review.serviceFiles.map(x=>x.path).sort(),["src/index.mjs","wrangler.production.jsonc"]);
const stage=resolve(dirname(resolve(reviewPath)),review.workerStage);
assert(stage.startsWith(join(repo,".artifacts")+sep),"Reviewed credential-free stage required");
const staged=JSON.parse(await readFile(join(stage,"wrangler.production.jsonc"),"utf8"));
const {$schema,...expected}=await validateDeployment();assert.deepEqual(staged,expected);
assert.equal(createHash("sha256").update(await readFile(join(stage,"src/index.mjs"))).digest("hex"),review.serviceFiles.find(x=>x.path==="src/index.mjs").sha256);
if(action!=="disable")assert(process.argv.includes("--free-plan-confirmed"),"Confirm the account's current Workers Free plan; do not upgrade it");
if(["secrets","enable"].includes(action))assert(process.argv.includes("--local-use-stopped"),"Stop every uncoordinated local use of this Twitch application first");
async function run(args,input){
  const env={...process.env,WRANGLER_SEND_METRICS:"false"};
  for(const key of Object.keys(env))if(/^(IGDB_|TWITCH_|THE_ORDER_|KGM_)/i.test(key))delete env[key];
  const child=spawn(process.execPath,[join(repo,"services/bangumi-auth-worker/node_modules/wrangler/bin/wrangler.js"),...args],
    {cwd:stage,windowsHide:true,env,stdio:["pipe","pipe","pipe"]});
  // Do not display or persist raw CLI output. Even an unexpected credential
  // diagnostic from a future Wrangler version stays private.
  let output="";child.stdout.on("data",chunk=>{if(output.length<1024*1024)output+=chunk.toString();});child.stderr.resume();child.stdin.end(input);
  const code=await new Promise((ok,no)=>{child.once("error",no);child.once("close",ok);});
  assert.equal(code,0,"Cloudflare operation failed. Inspect its status privately; do not enable or publish a client.");
  return output;
}
const config=join(stage,"wrangler.production.jsonc");
const expectedService="https://the-order-igdb-game-videos.zyx2251721565.workers.dev/";
if(["secrets","enable"].includes(action)){
  const receipt=JSON.parse(await readFile(join(dirname(resolve(reviewPath)),"bootstrap-receipt.json"),"utf8"));
  assert.equal(receipt.service,expectedService,"Deployment hostname differs from the reviewed client; stop and qualify a corrected candidate");
}
if(action==="secrets"){
  const source=await readFile(join(serviceRoot,".dev.vars"),"utf8"),credentials={};
  for(const key of ["IGDB_CLIENT_ID","IGDB_CLIENT_SECRET"]){
    const line=source.split(/\r?\n/).find(x=>x.trim().startsWith(key+"="));assert(line,"Missing developer credential; value suppressed");
    const value=line.slice(line.indexOf("=")+1).trim().replace(/^['"]|['"]$/g,"");assert(value.length>=6,"Invalid developer credential; value suppressed");credentials[key]=value;
  }
  await run(["secret","bulk","--config",config],JSON.stringify(credentials));
}else {
  const output=await run(["deploy","--config",config,...(action==="enable"?["--var","IGDB_ENABLED:true"]:[])]);
  if(action==="bootstrap"){
    const addresses=[...new Set(output.match(/https:\/\/the-order-igdb-game-videos\.[a-z0-9-]+\.workers\.dev\/?/g)??[])];
    assert.equal(addresses.length,1,"Worker is deployed disabled, but its public hostname could not be verified; do not set Secrets or enable");
    const service=addresses[0].replace(/\/?$/,"/");
    const {writeFile}=await import("node:fs/promises");
    await writeFile(join(dirname(resolve(reviewPath)),"bootstrap-receipt.json"),JSON.stringify({worker:"the-order-igdb-game-videos",service,enabled:false},null,2),{flag:"wx"});
    assert.equal(service,expectedService,"Worker is deployed disabled at a different hostname; requalify the client before enabling");
  }
}
console.log(JSON.stringify({action,completed:true,credentialValuesPrinted:false,worker:"the-order-igdb-game-videos"}));
