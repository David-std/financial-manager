"""Register the existing FM11 Entra app's browser callback without secrets."""
from __future__ import annotations

import argparse
import importlib.util
import json
import re
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

HERE=Path(__file__).resolve().parent
SOURCE=HERE.parent/"power-automate"/"fm11_oauth_pkce_probe.py"
spec=importlib.util.spec_from_file_location("fm11_pkce_desktop",SOURCE)
assert spec and spec.loader
oauth=importlib.util.module_from_spec(spec)
spec.loader.exec_module(oauth)
SCOPE="https://graph.microsoft.com/Application.ReadWrite.OwnedBy"
CALLBACK="https://org49f8e9f0.crm3.dynamics.com/WebResources/fmg_shortcut_draft.html"
GRAPH="https://graph.microsoft.com/v1.0"
APP_ID=oauth.CLIENT

class RegistrationError(Exception):
    pass

def merge_redirects(existing:list[str])->list[str]:
    if not isinstance(existing,list) or any(
            not isinstance(x,str) or not x.startswith("https://") for x in existing):
        raise RegistrationError("Unexpected existing SPA redirects")
    values=existing if CALLBACK in existing else existing+[CALLBACK]
    if len(values)>256 or len(set(values))!=len(values):
        raise RegistrationError("Duplicate or excessive SPA redirects")
    return values

def request(url:str, token:str, method:str="GET", body:dict|None=None)->dict:
    if not url.startswith(GRAPH+"/"):
        raise RegistrationError("Unexpected Graph endpoint")
    data=None if body is None else json.dumps(body,separators=(",",":")).encode("utf-8")
    req=urllib.request.Request(url,data=data,method=method,headers={
        "Authorization":"Bearer "+token,"Accept":"application/json",
        "Content-Type":"application/json"
    })
    try:
        with urllib.request.urlopen(req,timeout=25) as response:
            return json.loads(response.read(200000)) if response.status!=204 else {}
    except urllib.error.HTTPError as exc:
        raise RegistrationError("Microsoft Graph rejected the request (HTTP "+
                                str(exc.code)+").") from None

def run(apply:bool)->None:
    oauth.SCOPE=SCOPE
    verifier, challenge, state=oauth.proof_values()
    code, redirect=oauth.receive_code(state,challenge,max_seconds=180)
    exchanged=oauth.exchange(code,verifier,redirect)
    access=exchanged.get("access_token")
    if not isinstance(access,str) or len(access)<80:
        raise RegistrationError("No delegated Microsoft Graph access")
    record=request(GRAPH+"/applications(appId='"+APP_ID+
                   "')?$select=id,appId,spa",access)
    if record.get("appId")!=APP_ID or not re.fullmatch(
            r"[a-fA-F0-9-]{36}",record.get("id","")):
        raise RegistrationError("The returned Entra app is not the expected FM11 app")
    previous=(record.get("spa") or {}).get("redirectUris") or []
    updated=merge_redirects(previous)
    if updated==previous:
        print("FM11_BROWSER_REDIRECT_ALREADY_REGISTERED=true")
        return
    print("FM11_BROWSER_REDIRECT_REGISTERED=false")
    if not apply:
        print("FM11_BROWSER_REDIRECT_PREVIEW_ONLY=true")
        return
    request(GRAPH+"/applications/"+record["id"],access,method="PATCH",
            body={"spa":{"redirectUris":updated}})
    saved=request(GRAPH+"/applications(appId='"+APP_ID+
                  "')?$select=id,appId,spa",access)
    if CALLBACK not in (saved.get("spa") or {}).get("redirectUris",[]):
        raise RegistrationError("SPA callback was not present after update")
    print("FM11_BROWSER_REDIRECT_REGISTERED=true")

def main()->None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply",action="store_true")
    args=parser.parse_args()
    try:
        run(args.apply)
    except (RegistrationError,oauth.ProofFailure) as exc:
        parser.exit(1,str(exc)+"\n")

if __name__=="__main__":
    main()
