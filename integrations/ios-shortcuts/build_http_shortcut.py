"""Build the authenticated HTTP transport test Shortcut for FM-11."""
from __future__ import annotations

import argparse
import importlib.util
import plistlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("fm11_native_mail", HERE / "build_shortcut.py")
assert SPEC and SPEC.loader
native = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(native)

NAME = "Registrar gasto (HTTP)"
SOURCE_NAME = "Registrar-gasto-http.unsigned.plist"
DEFAULT = HERE / SOURCE_NAME
PREVIEW = "Vista previa (no enviar)"
SEND = "Probar HTTP autenticado (DEV)"
HEADER = "Authorization"

OUTPUTS = {
    "requestId": ("requestId", "Text"),
    "name": ("name", "Provided Input"),
    "amountPen": ("amountPen", "Provided Input"),
    "date": ("dateISO", "Formatted Date"),
    "categoryCode": ("categoryCode", "Updated Text"),
    "branchCode": ("branchCode", "Selected Item"),
    "accountName": ("accountName", "Selected Item"),
    "flowClass": ("flowClass", "Selected Item"),
    "description": ("description", "Provided Input"),
}


def items(values: dict[str, object]) -> dict:
    """Shortcuts parameter encoding for JSON and HTTP headers."""
    return {
        "Value": {
            "WFDictionaryFieldValueItems": [
                {
                    "WFItemType": 0,
                    "WFKey": native.token_string([key]),
                    "WFValue": value if isinstance(value, dict)
                    else native.token_string([str(value)]),
                }
                for key, value in values.items()
            ],
        },
        "WFSerializationType": "WFDictionaryFieldValue",
    }


def build() -> dict:
    existing = native.build()
    prior = existing["WFWorkflowActions"]
    assert prior[0]["WFWorkflowActionParameters"]["UUID"] == native.uid("recipient")
    assert [x["WFWorkflowActionIdentifier"] for x in prior[-3:]] == [
        "is.workflow.actions.dictionary",
        "is.workflow.actions.gettext",
        "is.workflow.actions.sendemail",
    ]
    base_actions = prior[1:-3]
    request_values = {
        key: native.token_string([output])
        for key, output in OUTPUTS.items()
    }
    group = native.uid("http-preview-group")
    base_actions.extend([
        native.action("gettext", "httpEndpoint", WFTextActionText=""),
        *native.list_and_choice(
            "httpMode",
            "Prueba de conexión: no se crea ningún gasto",
            [PREVIEW, SEND],
        ),
        native.action(
            "conditional", "httpPreviewIf",
            WFInput={
                "Type": "Variable",
                "Variable": native.attachment("httpMode", "Selected Item"),
            },
            WFControlFlowMode=0, WFCondition=4,
            WFConditionalActionString=PREVIEW,
            GroupingIdentifier=group,
        ),
        native.action(
            "alert", "httpPreviewNotice",
            WFAlertActionTitle="Solicitud preparada (sin enviar)",
            WFAlertActionMessage=(
                "No se ha enviado ni registrado "
                "ningún gasto. Para probar el transporte HTTP debes elegir "
                "la prueba de conexión y disponer de un token temporal."
            ),
        ),
        native.action(
            "dictionary", "httpPreviewDictionary",
            WFItems=items(request_values),
        ),
        native.action(
            "showresult", "httpPreviewResult",
            WFInput=native.attachment("httpPreviewDictionary", "Dictionary"),
        ),
        native.action("exit", "httpPreviewExit"),
        native.action(
            "conditional", "httpPreviewEnd", WFControlFlowMode=2,
            GroupingIdentifier=group,
        ),
        native.action(
            "alert", "httpDangerNotice",
            WFAlertActionTitle="Prueba de red; sin registro financiero",
            WFAlertActionMessage=(
                "Usa solo un token OAuth delegado y temporal autorizado "
                "para el entorno DEV. No uses contraseñas, Client Secrets "
                "ni tokens permanentes. El flujo de prueba debe estar "
                "encendido y responder 503 NOT_CONFIGURED."
            ),
            WFAlertActionCancelButtonShown=True,
        ),
        native.action(
            "ask", "httpToken",
            WFAskActionPrompt="Token OAuth temporal (no contraseña)",
            WFInputType="Text",
            WFAskActionDefaultAnswer="",
        ),
        native.action(
            "conditional", "httpTokenMissingIf",
            WFInput={
                "Type": "Variable",
                "Variable": native.attachment("httpToken", "Provided Input"),
            },
            WFCondition=101,  # Does not have any value
            WFControlFlowMode=0,
            GroupingIdentifier=native.uid("http-token-missing-group"),
        ),
        native.action(
            "alert", "httpTokenMissingNotice",
            WFAlertActionTitle="No se envió la solicitud",
            WFAlertActionMessage="Falta un token delegado temporal.",
        ),
        native.action("exit", "httpTokenMissingExit"),
        native.action(
            "conditional", "httpTokenMissingEnd",
            WFControlFlowMode=2,
            GroupingIdentifier=native.uid("http-token-missing-group"),
        ),
        native.action(
            "downloadurl", "httpHttpPost",
            WFURL=native.attachment("httpEndpoint", "Text"),
            WFHTTPMethod="POST",
            WFHTTPBodyType="JSON",
            ShowHeaders=True,
            WFHTTPHeaders=items({
                HEADER: native.token_string([
                    "Bearer ", ("httpToken", "Provided Input"),
                ]),
                "Accept": "application/json",
            }),
            WFJSONValues=items(request_values),
        ),
        native.action(
            "alert", "httpNoSuccess",
            WFAlertActionTitle="Comprobación de conexión",
            WFAlertActionMessage=(
                "No hay confirmación de gasto. El flujo HTTP de pruebas "
                "no registra gastos: comprueba el historial de Power Automate."
            ),
        ),
    ])
    existing["WFWorkflowActions"] = base_actions
    existing["WFWorkflowName"] = NAME
    existing["WFWorkflowImportQuestions"] = [{
        "ActionIndex": len(prior[1:-3]),
        "Category": "Parameter",
        "ParameterKey": "WFTextActionText",
        "Text": (
            "URL HTTP del flujo de prueba FM11 (DEV)"
        ),
    }]
    assert existing["WFWorkflowActions"][
        existing["WFWorkflowImportQuestions"][0]["ActionIndex"]
    ]["WFWorkflowActionParameters"]["UUID"] == native.uid("httpEndpoint")
    return existing


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT)
    args = parser.parse_args()
    result = build()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(plistlib.dumps(result, fmt=plistlib.FMT_XML))
    binary = args.output.with_suffix(".shortcut")
    binary.write_bytes(plistlib.dumps(result, fmt=plistlib.FMT_BINARY))
    assert plistlib.loads(binary.read_bytes()) == result
    print("FM11_HTTP_UNSIGNED_PLIST=" + str(args.output))
    print("FM11_HTTP_UNSIGNED_SHORTCUT=" + str(binary))
    print("FM11_HTTP_ACTIONS=" + str(len(result["WFWorkflowActions"])))


if __name__ == "__main__":
    main()
