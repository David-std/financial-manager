"""FM-11 inert email-receiving Power Automate flow preview.

Only the standard Office 365 Outlook connector trigger and a Cancelled
Terminate action. Not an expense intake, no Dataverse writes, no approvals,
no private mailbox/connection IDs in this source file.
"""
from __future__ import annotations
import json
from pathlib import Path

OUT = Path(__file__).with_name("fm11_mail_quarantine.workflow.json")
SUBJECT = "FM11-DRAFT-V1"

def build() -> dict:
    return {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {
            "$connections": {"defaultValue": {}, "type": "Object"},
            "$authentication": {"defaultValue": {}, "type": "SecureObject"},
        },
        "triggers": {
            "When_a_new_email_arrives_V3": {
                "type": "OpenApiConnectionNotification",
                "inputs": {
                    "parameters": {
                        "folderPath": "Inbox",
                        "subjectFilter": SUBJECT,
                        "fetchOnlyWithAttachment": False,
                        "includeAttachments": False,
                    },
                    "host": {
                        "apiId": "/providers/Microsoft.PowerApps/apis/shared_office365",
                        "connectionName": "shared_office365",
                        "operationId": "OnNewEmailV3",
                    },
                    "authentication": "@parameters('$authentication')",
                },
                "splitOn": "@triggerOutputs()?['body/value']",
            },
        },
        "actions": {
            "Quarantine_no_financial_writes": {
                "type": "Terminate",
                "runAfter": {},
                "inputs": {"runStatus": "Cancelled"},
            }
        },
        "outputs": {},
    }

if __name__ == "__main__":
    OUT.write_text(json.dumps(build(), indent=2, ensure_ascii=False) + "\n",
                   encoding="utf-8", newline="\n")
    print("FM11_MAIL_QUARANTINE_OFF_SOURCE=" + str(OUT))
