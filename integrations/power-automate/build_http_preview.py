"""Generate a deliberately NON-ACTIVATABLE FM-11 HTTP flow preview.

The preview only returns 503 and never reads/writes Dataverse.
Importing it is not deployment approval. Authentication must be set
in the Power Automate designer before any next-phase implementation.
"""
from __future__ import annotations

import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCHEMA = HERE.parent / "ios-shortcuts" / "expense-draft.schema.json"
DEST = HERE / "fm11_http_preview.workflow.json"


def build() -> dict:
    contract = json.loads(SCHEMA.read_text(encoding="utf-8"))
    request_body = {
        "type": "object",
        "additionalProperties": False,
        "properties": contract["properties"],
        "required": contract["required"],
    }
    return {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {},
        "triggers": {
            "manual": {
                "type": "Request",
                "kind": "Http",
                "inputs": {"schema": request_body},
            },
        },
        "actions": {
            "DISABLED_NO_FINANCIAL_WRITES": {
                "type": "Response",
                "kind": "Http",
                "runAfter": {},
                "inputs": {
                    "statusCode": 503,
                    "headers": {"Content-Type": "application/json"},
                    "body": {
                        "status": "NOT_CONFIGURED",
                        "message": "FM-11 direct intake is disabled pending authenticated deployment.",
                    },
                },
            },
        },
        "outputs": {},
    }


if __name__ == "__main__":
    expected = build()
    DEST.write_text(json.dumps(expected, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8", newline="\n")
    print("FM11_HTTP_PREVIEW_READ_ONLY=" + str(DEST))
