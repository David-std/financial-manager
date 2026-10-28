"""Build the native FM-11 Apple Shortcut source. Sign on macOS with Shortcuts."""
from __future__ import annotations

import argparse
import plistlib
import uuid
from pathlib import Path

DIR = Path(__file__).resolve().parent
CATALOG = DIR.parents[1] / "docs" / "category-catalog.md"
NAME = "Registrar gasto"
MARKER = "\uFFFC"

# A nine-digit random number fits in Shortcuts' signed 32-bit range.
# Prefixing it produces a textual request ID accepted by the FM-11 contract.
RANDOM_MIN = 100000000
RANDOM_MAX = 999999999

# Read-only snapshot of active account display names in FinancialManager DEV,
# verified 2026-10-28. The intake must recheck real permissions and scope.
ACCOUNT_OPTIONS = ["Caja La Merced", "Caja San Ramón", "Cuenta bancaria principal"]


def uid(label: str) -> str:
    return str(uuid.uuid5(uuid.NAMESPACE_URL, "financial-manager/fm11/" + label)).upper()


def reference(label: str, output: str) -> dict:
    return {"Type": "ActionOutput", "OutputUUID": uid(label), "OutputName": output}


def attachment(label: str, output: str) -> dict:
    return {"Value": reference(label, output), "WFSerializationType": "WFTextTokenAttachment"}


def token_string(parts: list[str | tuple[str, str]]) -> dict:
    text, links = "", {}
    for part in parts:
        if isinstance(part, str):
            text += part
        else:
            label, output = part
            offset = len(text.encode("utf-16-le")) // 2
            links[f"{{{offset}, 1}}"] = reference(label, output)
            text += MARKER
    return {
        "Value": {"string": text, "attachmentsByRange": links},
        "WFSerializationType": "WFTextTokenString",
    }


def action(name: str, label: str, **params: object) -> dict:
    return {
        "WFWorkflowActionIdentifier": "is.workflow.actions." + name,
        "WFWorkflowActionParameters": {"UUID": uid(label), **params},
    }


def category_options() -> list[str]:
    """Use the documented leaf catalog; group codes are not valid expenses."""
    options = []
    for line in CATALOG.read_text(encoding="utf-8").splitlines():
        if not line.startswith("| "):
            continue
        cells = [cell.strip() for cell in line.split("|")]
        if len(cells) == 7 and cells[3].isdigit() and len(cells[3]) == 3:
            if cells[3].endswith("00"):
                raise ValueError("Group code cannot be a selectable category")
            options.append(f"{cells[3]} · {cells[5]}")
    if len(options) < 25 or len({v[:3] for v in options}) != len(options):
        raise ValueError("Missing or duplicated expense category options")
    return options


def list_and_choice(label: str, prompt: str, options: list[str]) -> list[dict]:
    """Build a real List output, then select one item from that output."""
    if not options or len(set(options)) != len(options):
        raise ValueError(f"Invalid options for {label}")
    list_label = label + "List"
    return [
        action("list", list_label, WFItems=options),
        action(
            "choosefromlist", label,
            WFChooseFromListActionPrompt=prompt,
            WFChooseFromListActionSelectMultiple=False,
            WFInput=attachment(list_label, "List"),
        ),
    ]


def build() -> dict:
    actions = [
        action("gettext", "recipient", WFTextActionText=""),
        action(
            "number.random", "randomPart",
            WFRandomNumberMinimum=RANDOM_MIN,
            WFRandomNumberMaximum=RANDOM_MAX,
        ),
        action(
            "gettext", "requestId",
            WFTextActionText=token_string(["FM11-", ("randomPart", "Random Number")]),
        ),
        action("ask", "name", WFAskActionPrompt="¿Qué gasto deseas registrar?", WFInputType="Text"),
        action("ask", "amountPen", WFAskActionPrompt="Importe en soles (PEN)", WFInputType="Number"),
        action(
            "conditional", "invalidAmountStart",
            WFInput={"Type": "Variable", "Variable": attachment("amountPen", "Provided Input")},
            WFCondition=1, WFNumberValue="0", WFControlFlowMode=0,
            GroupingIdentifier=uid("invalidAmountGroup"),
        ),
        action("alert", "invalidAmountAlert", WFAlertActionTitle="Importe inválido",
               WFAlertActionMessage="El importe debe ser mayor que cero."),
        action("exit", "invalidAmountExit"),
        action("conditional", "invalidAmountEnd", WFControlFlowMode=2,
               GroupingIdentifier=uid("invalidAmountGroup")),
        action(
            "ask", "date",
            WFAskActionPrompt="Selecciona la fecha del gasto",
            WFInputType="Date",
            WFAskActionDateGranularity="Date",
        ),
        action(
            "format.date", "dateISO",
            WFDate=token_string([("date", "Provided Input")]),
            WFDateFormatStyle="Custom",
            WFDateFormat="yyyy-MM-dd",
            WFTimeFormatStyle="None",
        ),
        *list_and_choice("categoryChoice", "Categoría del gasto (código y nombre)", category_options()),
        action(
            "text.replace", "categoryCode",
            WFInput=attachment("categoryChoice", "Selected Item"),
            WFReplaceTextFind="^([0-9]{3}).*$",
            WFReplaceTextReplace="$1",
            WFReplaceTextRegularExpression=True,
            WFReplaceTextCaseSensitive=True,
        ),
        *list_and_choice("branchCode", "Selecciona la sede", ["LM", "SR"]),
        *list_and_choice("accountName", "Selecciona la cuenta de pago", ACCOUNT_OPTIONS),
        *list_and_choice("flowClass", "Clasifica el gasto", ["operativo", "capital", "impuesto", "otro"]),
        action(
            "ask", "description",
            WFAskActionPrompt="Descripción (opcional)",
            WFInputType="Text",
            WFAskActionDefaultAnswer="",
        ),
        action(
            "alert", "confirm",
            WFAlertActionTitle="Confirmar solicitud de gasto",
            WFAlertActionMessage=token_string([
                "Concepto: ", ("name", "Provided Input"),
                "\nPEN: ", ("amountPen", "Provided Input"),
                "\nFecha: ", ("dateISO", "Formatted Date"),
                "\nSede: ", ("branchCode", "Selected Item"),
                "\nCategoría: ", ("categoryChoice", "Selected Item"),
                "\nCuenta: ", ("accountName", "Selected Item"),
                "\nClase: ", ("flowClass", "Selected Item"),
                "\nSolicitud: ", ("requestId", "Text"),
            ]),
            WFAlertActionCancelButtonShown=True,
        ),
    ]
    outputs = {
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
    items = [
        {
            "WFItemType": 0,
            "WFKey": token_string([key]),
            "WFValue": token_string([outputs[key]]),
        }
        for key in outputs
    ]
    actions.extend([
        action(
            "dictionary", "payload",
            WFItems={
                "Value": {"WFDictionaryFieldValueItems": items},
                "WFSerializationType": "WFDictionaryFieldValue",
            },
        ),
        action("gettext", "json", WFTextActionText=token_string([("payload", "Dictionary")])),
        action(
            "sendemail", "mail",
            WFSendEmailActionSubject="FM11-DRAFT-V1",
            WFSendEmailActionShowComposeSheet=True,
            WFSendEmailActionToRecipients=attachment("recipient", "Text"),
            WFSendEmailActionInputAttachments=attachment("json", "Text"),
        ),
    ])
    return {
        "WFWorkflowName": NAME,
        "WFWorkflowActions": actions,
        "WFWorkflowIcon": {"WFWorkflowIconGlyphNumber": 59511, "WFWorkflowIconStartColor": 463140863},
        "WFWorkflowClientVersion": "2700.0.4",
        "WFWorkflowMinimumClientVersion": 900,
        "WFWorkflowMinimumClientVersionString": "900",
        "WFWorkflowHasOutputFallback": False,
        "WFWorkflowHasShortcutInputVariables": False,
        "WFWorkflowImportQuestions": [{
            "ActionIndex": 0,
            "Category": "Parameter",
            "ParameterKey": "WFTextActionText",
            "Text": "Correo de recepción del flujo de Power Automate",
        }],
        "WFWorkflowTypes": [],
        "WFWorkflowInputContentItemClasses": ["WFStringContentItem"],
        "WFWorkflowOutputContentItemClasses": [],
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DIR / "Registrar-gasto.unsigned.plist")
    args = parser.parse_args()
    shortcut = build()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(plistlib.dumps(shortcut, fmt=plistlib.FMT_XML))
    binary_path = args.output.with_suffix(".shortcut")
    binary_path.write_bytes(plistlib.dumps(shortcut, fmt=plistlib.FMT_BINARY))
    assert plistlib.loads(args.output.read_bytes()) == shortcut
    assert plistlib.loads(binary_path.read_bytes()) == shortcut
    print("FM11_SHORTCUT_SOURCE_VALID=" + str(args.output))
    print("FM11_SHORTCUT_BINARY_VALID=" + str(binary_path))


if __name__ == "__main__":
    main()
