"""F6 ALM source-only preflight. Never connects to Dataverse or deploys.

Dev's current APIs must exist in source control before any managed release.
The default mode reports drift; --release fails closed on it.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "power-platform/solutions/FinancialManager/src"

REQUIRED_APIS = frozenset({
    "fmg_ApproveBudget",
    "fmg_PostFinancialTransaction",
    "fmg_VoidFinancialTransaction",
    "fmg_SubmitCashGrantRequest",
    "fmg_DecideCashGrantRequest",
    "fmg_SettleCashReceipt",
    "fmg_PostCashTransfer",
})


@dataclass(frozen=True)
class AlMReadiness:
    valid_source: bool
    release_ready: bool
    version: str
    missing_apis: tuple[str, ...]
    errors: tuple[str, ...]


def inspect(source: Path = SOURCE) -> AlMReadiness:
    errors: list[str] = []
    version = ""
    manifest = source / "Other/Solution.xml"
    try:
        root = ET.parse(manifest).getroot()
        solution = root.find("SolutionManifest")
        if solution is None:
            errors.append("Missing SolutionManifest.")
        else:
            version = solution.findtext("Version", "")
            if solution.findtext("UniqueName", "") != "FinancialManager":
                errors.append("Unexpected solution unique name.")
            if solution.findtext("Managed", "") != "0":
                errors.append("Source must be the unmanaged DEV export.")
            publisher = solution.find("Publisher")
            if publisher is None or publisher.findtext("CustomizationPrefix", "") != "fmg":
                errors.append("Unexpected publisher prefix.")
            if not version or not all(part.isdigit() for part in version.split(".")):
                errors.append("Unusable source solution version.")
    except (OSError, ET.ParseError) as exc:
        errors.append("Cannot read valid Solution.xml: " + type(exc).__name__)
    if not (source / "Entities").is_dir():
        errors.append("Missing source-controlled entity schemas.")
    if not (source / "SdkMessageProcessingSteps").is_dir():
        errors.append("Missing source-controlled SDK registration steps.")
    api_dir = source / "customapis"
    available = {p.name for p in api_dir.iterdir() if p.is_dir()} if api_dir.is_dir() else set()
    missing = tuple(sorted(REQUIRED_APIS - available))
    return AlMReadiness(
        valid_source=not errors,
        release_ready=not errors and not missing,
        version=version,
        missing_apis=missing,
        errors=tuple(errors),
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", action="store_true",
                        help="Block when the tracked solution is stale or invalid.")
    args = parser.parse_args()
    result = inspect()
    for error in result.errors:
        print("F6_SOURCE_ERROR=" + error)
    print("F6_SOURCE_MANIFEST_VALID=" + str(result.valid_source).lower())
    print("F6_SOURCE_VERSION=" + (result.version or "unknown"))
    print("F6_REQUIRED_APIS=" + str(len(REQUIRED_APIS)))
    print("F6_MISSING_APIS=" + (",".join(result.missing_apis) or "(none)"))
    print("F6_MANAGED_RELEASE_READY=" + str(result.release_ready).lower())
    print("F6_PREFLIGHT_NO_DATAVERSE_CALLS_OR_IMPORTS")
    if args.release and not result.release_ready:
        print("F6_RELEASE_BLOCKED_UNTIL_REVIEWED_FRESH_DEV_EXPORT")
        return 2
    return 0 if result.valid_source else 1


if __name__ == "__main__":
    raise SystemExit(main())
