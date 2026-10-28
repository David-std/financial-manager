"""Create the Outlook-to-Dataverse staging workflow definition.

The stage stores unverified requests in fmg_mailintake only. It cannot
create or approve financial transactions. Invalid JSON fails before storage.
"""
from __future__ import annotations
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
SCHEMA = HERE.parent / "ios-shortcuts/expense-draft.schema.json"
CATEGORY_CATALOG = HERE.parent.parent / "docs/category-catalog.md"
OUTPUT = HERE / "fm11_mail_staging.workflow.json"

def build() -> dict:
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    if schema["additionalProperties"] is not False:
        raise ValueError("Unrestricted request schema")
    schema = {key: schema[key] for key in (
        "type", "additionalProperties", "required", "properties"
    )}
    # Power Automate Parse JSON rejects JSON Schema 'pattern'. Use an
    # explicit leaf-code enumeration for the reviewed management catalog.
    # Format validation of the other text fields remains a review gate.
    codes = []
    for line in CATEGORY_CATALOG.read_text(encoding="utf-8").splitlines():
        cols = [part.strip() for part in line.strip().strip("|").split("|")]
        if len(cols) == 5 and re.fullmatch(r"[1-9][0-9]{2}", cols[2]):
            codes.append(cols[2])
    if len(codes) < 25 or len(set(codes)) != len(codes):
        raise ValueError("Expense category leaf catalog is invalid")
    for field in schema["properties"].values():
        field.pop("pattern", None)
    schema["properties"]["categoryCode"]["enum"] = codes
    schema["properties"]["categoryCode"]["minLength"] = 3
    schema["properties"]["categoryCode"]["maxLength"] = 3
    schema["properties"]["amountPen"]["minLength"] = 1
    schema["properties"]["date"]["minLength"] = 10
    schema["properties"]["date"]["maxLength"] = 10
    return {
        "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
        "contentVersion": "1.0.0.0",
        "parameters": {
            "$connections": {"defaultValue": {}, "type": "Object"},
            "$authentication": {"defaultValue": {}, "type": "SecureObject"}
        },
        "triggers": {
            "When_a_new_email_arrives_V3": {
                "type": "OpenApiConnectionNotification",
                "inputs": {
                    "parameters": {
                        "folderPath": "Inbox",
                        "subjectFilter": "FM11-DRAFT-V1",
                        "fetchOnlyWithAttachment": False,
                        "includeAttachments": False
                    },
                    "host": {
                        "apiId": "/providers/Microsoft.PowerApps/apis/shared_office365",
                        "connectionName": "shared_office365",
                        "operationId": "OnNewEmailV3"
                    },
                    "authentication": "@parameters('$authentication')"
                },
                "splitOn": "@triggerOutputs()?['body/value']"
            }
        },
        "actions": {
            "Read_message_as_text": {
                "type": "OpenApiConnection",
                "inputs": {
                    "host": {
                        "apiId": "/providers/Microsoft.PowerApps/apis/shared_office365",
                        "connectionName": "shared_office365",
                        "operationId": "HttpRequest"
                    },
                    "parameters": {
                        "Uri": "@concat('https://graph.microsoft.com/v1.0/me/messages/', encodeUriComponent(triggerOutputs()?['body/id']), '?$select=id,subject,body')",
                        "Method": "GET",
                        "CustomHeader1": 'Prefer: outlook.body-content-type="text"'
                    },
                    "authentication": "@parameters('$authentication')"
                },
                "runAfter": {}
            },
            "Parse_request": {
                "type": "ParseJson",
                "inputs": {
                    "content": "@body('Read_message_as_text')?['body']?['content']",
                    "schema": schema
                },
                "runAfter": {"Read_message_as_text": ["Succeeded"]}
            },
            "Stage_unverified_request": {
                "type": "OpenApiConnection",
                "inputs": {
                    "host": {
                        "apiId": "/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps",
                        "connectionName": "shared_commondataserviceforapps",
                        "operationId": "CreateRecord"
                    },
                    "parameters": {
                        "entityName": "fmg_mailintakes",
                        "item/fmg_name": "@body('Parse_request')?['requestId']",
                        "item/fmg_requestid": "@body('Parse_request')?['requestId']",
                        "item/fmg_sender": "@triggerOutputs()?['body/from']",
                        "item/fmg_mailmessageid": "@triggerOutputs()?['body/id']",
                        "item/fmg_reviewstate": "Unverified",
                        "item/fmg_validationstate": "FormatChecked",
                        "item/fmg_concept": "@body('Parse_request')?['name']",
                        "item/fmg_amountpen": "@body('Parse_request')?['amountPen']",
                        "item/fmg_requesteddate": "@body('Parse_request')?['date']",
                        "item/fmg_categorycode": "@body('Parse_request')?['categoryCode']",
                        "item/fmg_branchcode": "@body('Parse_request')?['branchCode']",
                        "item/fmg_accountname": "@body('Parse_request')?['accountName']",
                        "item/fmg_flowclass": "@body('Parse_request')?['flowClass']",
                        "item/fmg_rawbody": "@body('Read_message_as_text')?['body']?['content']"
                    },
                    "authentication": "@parameters('$authentication')",
                    "retryPolicy": {"type": "none"}
                },
                "runAfter": {"Parse_request": ["Succeeded"]}
            },
            "Classify_stage_failure": {
                "type": "Compose",
                "inputs": "@if(or(equals(outputs('Stage_unverified_request')?['statusCode'],409),equals(outputs('Stage_unverified_request')?['statusCode'],412)),'DUPLICATE_REQUEST','STAGING_WRITE_FAILED')",
                "runAfter": {"Stage_unverified_request": ["Failed", "TimedOut"]}
            },
            "Stop_stage_failure": {
                "type": "Terminate",
                "inputs": {
                    "runStatus": "Failed",
                    "runError": {
                        "code": "FM11_STAGE_REJECTED",
                        "message": "@outputs('Classify_stage_failure')"
                    }
                },
                "runAfter": {"Classify_stage_failure": ["Succeeded"]}
            }
        },
        "outputs": {}
    }

if __name__ == "__main__":
    OUTPUT.write_text(json.dumps(build(), ensure_ascii=False, indent=2) + "\n",
                      encoding="utf-8", newline="\n")
    print("FM11_MAIL_STAGING_SOURCE_VALID=true")
