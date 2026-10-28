import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";
import { webcrypto } from "node:crypto";
import assert from "node:assert/strict";

const html = readFileSync(new URL("../integrations/power-automate/fm11_shortcut_draft.html", import.meta.url), "utf8");
const source = html.match(/<script>\s*([\s\S]*?)\s*<\/script>/)?.[1];
assert.ok(source, "Missing web resource JavaScript");
const origin = "https://org49f8e9f0.crm3.dynamics.com";
const path = "/WebResources/fmg_shortcut_draft.html";
const request = {
  requestId: "FM11-123456789",
  name: "Synthetic browser expense",
  amountPen: "1.00",
  date: "2026-10-29",
  categoryCode: "103",
  branchCode: "LM",
  accountName: "Caja La Merced",
  flowClass: "capital",
  description: "synthetic-only"
};
const stageUrl = origin+"/api/data/v9.2/fmg_mailintakes";
const apiUrl = origin+"/api/data/v9.2/fmg_CreateDraftFromMailIntake";

function harness(hash, search, options={}) {
  const store = new Map();
  const status = { textContent: "" };
  const calls = [];
  const location = {
    origin,pathname:path,hash,search,
    replace(url){this.navigated=url;}
  };
  const history = {
    replaceState(_state,_title,url){
      const parsed = new URL(url,origin);
      location.hash=parsed.hash;
      location.search=parsed.search;
      location.pathname=parsed.pathname;
    }
  };
  const fetch = async (url,opts) => {
    calls.push({url,opts});
    if(url.includes("/oauth2/v2.0/token")){
      if(options.rejectToken)return {ok:false,status:400,json:async()=>({error:"invalid_grant"})};
      return {ok:true,status:200,json:async()=>({
        access_token:"synthetic-access",
        token_type:"Bearer"
      })};
    }
    if(url===stageUrl){
      if(options.rejectStaging)return {ok:false,status:403,json:async()=>({})};
      return {ok:true,status:204};
    }
    if(url.includes("fmg_mailintakes(fmg_requestid="))
      return {ok:true,status:200,json:async()=>({
        fmg_mailintakeid:"00000000-0000-4000-8000-000000000001",
        versionnumber:100
      })};
    if(url===apiUrl)return {ok:true,status:200,json:async()=>({
      Created:true,DraftId:"00000000-0000-4000-8000-000000000002"
    })};
    throw new Error("Unrecognized request "+url);
  };
  const ctx = {
    crypto:webcrypto,TextEncoder,TextDecoder,Uint8Array,URL,URLSearchParams,
    atob,btoa,JSON,Date,Error,String,Object,Array,Set,RegExp,Promise,
    sessionStorage:{
      getItem:k=>store.get(k)||null,
      setItem:(k,v)=>store.set(k,v),
      removeItem:k=>store.delete(k)
    },
    document:{querySelector:()=>status},history,location,fetch
  };
  return {ctx,store,calls,status,location};
}
async function flush(){
  for(let i=0;i<60;i++)await new Promise(resolve=>setImmediate(resolve));
}
async function waitFor(predicate,timeoutMs=3000){
  const deadline=Date.now()+timeoutMs;
  while(!predicate()&&Date.now()<deadline)
    await new Promise(resolve=>setTimeout(resolve,10));
}

const payload=Buffer.from(JSON.stringify(request)).toString("base64");
const t=harness("#draft="+payload,"");
runInNewContext(source,t.ctx);
await waitFor(()=>Boolean(t.location.navigated));
assert.ok(t.location.navigated,"Browser navigation missing: "+t.status.textContent);
assert.match(t.location.navigated,/login\.microsoftonline\.com/);
const auth = new URL(t.location.navigated);
assert.equal(auth.searchParams.get("code_challenge_method"),"S256");
assert.equal(auth.searchParams.get("redirect_uri"),origin+path);
assert.ok(auth.searchParams.get("state"));
assert.equal(t.calls.length,0,"No financial write before sign-in");
const state=auth.searchParams.get("state");
t.location.search="?code=synthetic-code&state="+encodeURIComponent(state);
runInNewContext(source,t.ctx);
await flush();
assert.equal(t.calls.length,4,"One token exchange and three Dataverse API calls");
assert.equal(t.calls[0].opts.method,"POST");
assert.equal(t.calls[1].url,stageUrl);
const staged=JSON.parse(t.calls[1].opts.body);
assert.equal(staged.fmg_reviewstate,"Unverified");
assert.equal(staged.fmg_requestid,request.requestId);
assert.equal(staged.fmg_rawbody,JSON.stringify(request));
assert.equal(t.calls[3].url,apiUrl);
assert.deepEqual(JSON.parse(t.calls[3].opts.body),{
  IntakeId:"00000000-0000-4000-8000-000000000001",
  ExpectedRowVersion:"100"
});
assert.match(t.status.textContent,/Borrador creado correctamente/);
assert.equal(t.store.size,0,"Authorization state must be cleared after success");

const bad=harness("#draft="+payload,"");
runInNewContext(source,bad.ctx);
await flush();
bad.location.search="?code=synthetic-code&state=invalid-state";
runInNewContext(source,bad.ctx);
await flush();
assert.equal(bad.calls.length,0,"Incorrect state must not contact Dataverse");
assert.equal(bad.store.size,0);

async function denied(options,expectedCalls,expectedError){
  const run=harness("#draft="+payload,"",options);
  runInNewContext(source,run.ctx);
  await waitFor(()=>Boolean(run.location.navigated));
  assert.ok(run.location.navigated,"Authorization step failed: "+run.status.textContent);
  const state=new URL(run.location.navigated).searchParams.get("state");
  run.location.search="?code=synthetic-code&state="+encodeURIComponent(state);
  runInNewContext(source,run.ctx);
  await flush();
  assert.equal(run.calls.length,expectedCalls);
  assert.match(run.status.textContent,expectedError);
  assert.equal(run.store.size,0,"Authorization state must be cleared on failure");
  assert.equal(run.calls.some(x=>x.url===apiUrl),false,
    "A failed token or intake write must not call the financial Draft API");
}
await denied({rejectToken:true},1,/Microsoft no concedió acceso delegado/);
await denied({rejectStaging:true},2,/Dataverse rechazó la solicitud \(403\)/);
console.log("FM11_BROWSER_S256_SUCCESS_AND_REJECTION_PASSED");
