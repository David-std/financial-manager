"""Build an Apple Shortcut that opens the Dataverse sign-in page."""
from __future__ import annotations
import argparse
import importlib.util
import plistlib
from pathlib import Path

HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location("fm11_mail_v2",HERE/"build_shortcut_v2.py")
assert spec and spec.loader
v2=importlib.util.module_from_spec(spec)
spec.loader.exec_module(v2)
native=v2.native

CALLBACK="https://org49f8e9f0.crm3.dynamics.com/WebResources/fmg_shortcut_draft.html"
NAME="Registrar gasto (Microsoft)"

def build()->dict:
    doc=v2.build()
    doc["WFWorkflowName"]=NAME
    actions=doc["WFWorkflowActions"]
    if actions[0]["WFWorkflowActionParameters"].get("UUID")!=native.uid("recipient"):
        raise ValueError("Unexpected recipient action")
    actions.pop(0)
    mail=[i for i,a in enumerate(actions)
          if a["WFWorkflowActionParameters"]["UUID"]==native.uid("mail")]
    if len(mail)!=1:
        raise ValueError("Missing original Mail action")
    actions.pop(mail[0])
    doc["WFWorkflowImportQuestions"]=[]
    actions.extend([
        native.action("base64encode","safariPayload",
            WFInput=native.attachment("json","Text"),
            WFEncodeMode="Encode",WFBase64LineBreakMode="None"),
        native.action("url","safariEntry",
            WFURLActionURL=native.token_string([
                CALLBACK+"#draft=",("safariPayload","Base64 Encoded")
            ])),
        native.action("openurl","safariLogin",
            WFInput=native.attachment("safariEntry","URL")),
    ])
    return doc

def main()->None:
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output",type=Path,
        default=HERE/"Registrar-gasto-entra.unsigned.plist")
    args=parser.parse_args()
    doc=build()
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_bytes(plistlib.dumps(doc,fmt=plistlib.FMT_XML))
    binary=args.output.with_suffix(".shortcut")
    binary.write_bytes(plistlib.dumps(doc,fmt=plistlib.FMT_BINARY))
    if plistlib.loads(binary.read_bytes()) != doc:
        raise RuntimeError("Shortcuts binary roundtrip failed")
    print("FM11_DELEGATED_SHORTCUT="+str(binary))
    print("FM11_ACTIONS="+str(len(doc["WFWorkflowActions"])))

if __name__=="__main__":
    main()
