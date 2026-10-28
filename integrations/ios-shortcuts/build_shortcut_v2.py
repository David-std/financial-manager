"""Build the Registrar gasto Shortcut with direct Mail delivery."""
from __future__ import annotations

import argparse
import importlib.util
import plistlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "build_shortcut.py"
spec = importlib.util.spec_from_file_location("fm11_interactive_mail", SOURCE)
if spec is None or spec.loader is None:
    raise RuntimeError("Original native generator is missing")
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)

DEFAULT = HERE / "Registrar-gasto-v2.unsigned.plist"
NAME = "Registrar gasto"


def use_native_category_code(actions: list[dict]) -> None:
    """Extract the code before the middle-dot separator, without regex."""
    code_uid = native.uid("categoryCode")
    indices = [
        i for i, item in enumerate(actions)
        if item["WFWorkflowActionParameters"]["UUID"] == code_uid
    ]
    if len(indices) != 1 or actions[indices[0]]["WFWorkflowActionIdentifier"] != (
        "is.workflow.actions.text.replace"
    ):
        raise ValueError("Unexpected original category extraction")
    position = indices[0]
    group = native.uid("missingCategoryCodeGroup")
    actions[position:position + 1] = [
        native.action(
            "text.split", "categoryParts",
            text=native.attachment("categoryChoice", "Selected Item"),
            WFTextSeparator="Custom",
            WFTextCustomSeparator=" · ",
        ),
        native.action(
            "getitemfromlist", "categoryCode",
            WFInput=native.attachment("categoryParts", "Split Text"),
            WFItemSpecifier="First Item",
        ),
        native.action(
            "conditional", "missingCategoryCodeIf",
            WFInput={
                "Type": "Variable",
                "Variable": native.attachment("categoryCode", "Item from List"),
            },
            WFCondition=101,
            WFControlFlowMode=0,
            GroupingIdentifier=group,
        ),
        native.action(
            "alert", "missingCategoryCodeAlert",
            WFAlertActionTitle="Categoría no válida",
            WFAlertActionMessage=(
                "No se ha podido obtener el código de categoría. "
                "La solicitud no se enviará."
            ),
        ),
        native.action("exit", "missingCategoryCodeExit"),
        native.action(
            "conditional", "missingCategoryCodeEnd",
            WFControlFlowMode=2,
            GroupingIdentifier=group,
        ),
    ]

    payload = next(
        item for item in actions
        if item["WFWorkflowActionParameters"]["UUID"] == native.uid("payload")
    )
    values = payload["WFWorkflowActionParameters"]["WFItems"]["Value"][
        "WFDictionaryFieldValueItems"
    ]
    code_fields = [
        field for field in values
        if field["WFKey"]["Value"]["string"] == "categoryCode"
    ]
    if len(code_fields) != 1:
        raise ValueError("Expected one categoryCode payload field")
    code_fields[0]["WFValue"] = native.token_string([
        ("categoryCode", "Item from List"),
    ])


def build() -> dict:
    original = native.build()
    use_native_category_code(original["WFWorkflowActions"])
    mail = [
        a for a in original["WFWorkflowActions"]
        if a["WFWorkflowActionIdentifier"] == "is.workflow.actions.sendemail"
    ]
    if len(mail) != 1:
        raise ValueError("Expected exactly one native Send Email action")
    params = mail[0]["WFWorkflowActionParameters"]
    if params["UUID"] != native.uid("mail") or params.get(
        "WFSendEmailActionShowComposeSheet"
    ) is not True:
        raise ValueError("Original Mail action has changed unexpectedly")

    params["WFSendEmailActionShowComposeSheet"] = False
    # Mail treats this field as displayable text; a raw action-output
    # attachment can serialize successfully but render an empty body.
    params["WFSendEmailActionInputAttachments"] = native.token_string([
        ("json", "Text"),
    ])
    # Explicitly coerce the structured Dictionary into text, rather than
    # interpolating a Dictionary object inside a Text action.
    json_action = next(
        a for a in original["WFWorkflowActions"]
        if a["WFWorkflowActionParameters"]["UUID"] == native.uid("json")
    )
    json_action["WFWorkflowActionIdentifier"] = "is.workflow.actions.detect.text"
    json_action["WFWorkflowActionParameters"] = {
        "UUID": native.uid("json"),
        "WFInput": native.attachment("payload", "Dictionary"),
    }
    # Stop before sending if iOS/macOS fails to produce any message body.
    group = native.uid("missingMailBodyGroup")
    before_mail = [
        native.action(
            "conditional", "missingMailBodyIf",
            WFInput={
                "Type": "Variable",
                "Variable": native.attachment("json", "Text"),
            },
            WFCondition=101,
            WFControlFlowMode=0,
            GroupingIdentifier=group,
        ),
        native.action(
            "alert", "missingMailBodyAlert",
            WFAlertActionTitle="No se pudo preparar la solicitud",
            WFAlertActionMessage=(
                "El contenido del correo está vacío. No se ha enviado "
                "ninguna solicitud."
            ),
        ),
        native.action("exit", "missingMailBodyExit"),
        native.action(
            "conditional", "missingMailBodyEnd",
            WFControlFlowMode=2,
            GroupingIdentifier=group,
        ),
    ]
    original["WFWorkflowActions"][-1:-1] = before_mail
    original["WFWorkflowName"] = NAME
    return original


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=DEFAULT)
    args = parser.parse_args()
    document = build()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(plistlib.dumps(document, fmt=plistlib.FMT_XML))
    binary = args.output.with_suffix(".shortcut")
    binary.write_bytes(plistlib.dumps(document, fmt=plistlib.FMT_BINARY))
    if plistlib.loads(binary.read_bytes()) != document:
        raise RuntimeError("Shortcut binary roundtrip failed")
    print(f"FM11_SHORTCUT_V2={binary}")
    print(f"FM11_SHORTCUT_V2_ACTIONS={len(document['WFWorkflowActions'])}")


if __name__ == "__main__":
    main()
