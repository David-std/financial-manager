"""F6 Power Platform solution source/release preflight (no external effects).

This validates an exported solution against protected financial components.
Release readiness is *not* equivalent to target-environment import acceptance.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
from pathlib import Path
import xml.etree.ElementTree as ET
from zipfile import is_zipfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "power-platform/solutions/FinancialManager/src"

REQUIRED_APIS = frozenset({
    "fmg_ApproveBudget", "fmg_PostFinancialTransaction",
    "fmg_VoidFinancialTransaction", "fmg_SubmitCashGrantRequest",
    "fmg_DecideCashGrantRequest", "fmg_SettleCashReceipt", "fmg_PostCashTransfer",
})
FINANCIAL_TABLES = ("fmg_cashreceipt", "fmg_cashtransfer", "fmg_cashposting",
                    "fmg_accountopening", "fmg_bankstatement", "fmg_bankreconciliation")
OPERATIONS = ("Create", "Update", "Delete")
REQUIRED_LEGACY = frozenset(
    {f"F5_Block_{table}_{verb}" for table in FINANCIAL_TABLES for verb in OPERATIONS}
    | {f"F5_DenyGrant_{verb}" for verb in OPERATIONS}
    | {f"F5_DenyGrantRequest_{verb}" for verb in OPERATIONS}
)
REQUIRED_SHADOW = frozenset(
    {f"F5_CashStandby_{table}_{verb}"
     for table in FINANCIAL_TABLES[:3] for verb in OPERATIONS}
    | {f"F5_ReviewStandby_{table}_{verb}"
       for table in ("fmg_cashaccessgrant", "fmg_cashgrantrequest") for verb in OPERATIONS}
)
REQUIRED_PACKAGES = frozenset({
    "fmg_BudgetIntegrity", "fmg_CashAccessIntegrity", "fmg_CashExecution",
    "fmg_CashGrantRequestIntegrity", "fmg_CashIntegrity", "fmg_CashOperations",
    "fmg_CashReviewExecution", "fmg_CashTrustProbe",
    "fmg_FinancialManagerValidation", "fmg_PaymentExecution",
})


@dataclass(frozen=True)
class AlMReadiness:
    valid_source: bool
    source_synced: bool
    release_ready: bool
    version: str
    missing_apis: tuple[str, ...]
    missing_guards: tuple[str, ...]
    missing_shadows: tuple[str, ...]
    missing_packages: tuple[str, ...]
    missing_dependencies: tuple[str, ...]
    errors: tuple[str, ...]


def inspect(source: Path = SOURCE) -> AlMReadiness:
    errors: list[str] = []
    version = ""
    missing_dependencies: tuple[str, ...] = ()
    try:
        root = ET.parse(source / "Other/Solution.xml").getroot()
        solution = root.find("SolutionManifest")
        if solution is None:
            errors.append("Missing SolutionManifest.")
        else:
            version = solution.findtext("Version", "")
            if solution.findtext("UniqueName", "") != "FinancialManager":
                errors.append("Unexpected solution unique name.")
            if solution.findtext("Managed", "") != "0":
                errors.append("Source must be an unmanaged export.")
            publisher = solution.find("Publisher")
            if publisher is None or publisher.findtext("CustomizationPrefix", "") != "fmg":
                errors.append("Unexpected publisher prefix.")
            if len(version.split(".")) != 4 or not all(
                part.isdigit() for part in version.split(".")
            ):
                errors.append("Unusable four-part solution version.")
            dependencies = solution.findall("./MissingDependencies/MissingDependency")
            missing_dependencies = tuple(sorted({
                (x.find("Required").get("type", "unknown")
                 if x.find("Required") is not None else "unknown")
                for x in dependencies
            }))
    except (OSError, ET.ParseError) as exc:
        errors.append("Cannot read valid Solution.xml: " + type(exc).__name__)

    if not (source / "Entities").is_dir():
        errors.append("Missing source-controlled entity schemas.")

    api_dir = source / "customapis"
    api_names = {p.name for p in api_dir.iterdir() if p.is_dir()} if api_dir.is_dir() else set()
    missing_apis = tuple(sorted(REQUIRED_APIS - api_names))

    steps_dir = source / "SdkMessageProcessingSteps"
    step_names: dict[str, tuple[str, str]] = {}
    if not steps_dir.is_dir():
        errors.append("Missing source-controlled SDK registration steps.")
    else:
        for path in steps_dir.glob("*.xml"):
            try:
                step = ET.parse(path).getroot()
                name = step.get("Name", "")
                if not name or name in step_names:
                    errors.append("Empty or duplicate SDK step name.")
                    continue
                step_names[name] = (step.findtext("Rank", ""), step.findtext("Stage", ""))
            except ET.ParseError:
                errors.append("Malformed SDK step file.")

    legacy = {
        name for name in REQUIRED_LEGACY
        if name not in step_names or step_names[name] != ("1", "10")
    }
    shadow = {
        name for name in REQUIRED_SHADOW
        if name not in step_names or step_names[name] != ("2", "10")
    }
    packages_dir = source / "pluginpackages"
    missing_packages = set()
    for name in REQUIRED_PACKAGES:
        directory = packages_dir / name
        archives = list(directory.rglob("*.nupkg"))
        if (not (directory / "pluginpackage.xml").is_file() or
                len(archives) != 1 or not is_zipfile(archives[0])):
            missing_packages.add(name)

    source_synced = not (errors or missing_apis or legacy or shadow or missing_packages)
    # MissingDependency entries are real cross-solution dependencies. A local
    # ZIP is NOT independently importable until these are resolved/verified
    # in an authorized target environment.
    release_ready = source_synced and not missing_dependencies
    return AlMReadiness(
        valid_source=not errors,
        source_synced=source_synced,
        release_ready=release_ready,
        version=version,
        missing_apis=missing_apis,
        missing_guards=tuple(sorted(legacy)),
        missing_shadows=tuple(sorted(shadow)),
        missing_packages=tuple(sorted(missing_packages)),
        missing_dependencies=missing_dependencies,
        errors=tuple(errors),
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--release", action="store_true",
                        help="Block a solution with source/security/dependency gaps.")
    args = parser.parse_args()
    result = inspect()
    for error in result.errors:
        print("F6_SOURCE_ERROR=" + error)
    print("F6_SOURCE_MANIFEST_VALID=" + str(result.valid_source).lower())
    print("F6_SOURCE_VERSION=" + (result.version or "unknown"))
    print("F6_REQUIRED_APIS=" + str(len(REQUIRED_APIS)))
    print("F6_MISSING_APIS=" + (",".join(result.missing_apis) or "(none)"))
    print("F6_LEGACY_GUARDS=" + str(len(REQUIRED_LEGACY) - len(result.missing_guards)))
    print("F6_SHADOW_VALIDATORS=" + str(len(REQUIRED_SHADOW) - len(result.missing_shadows)))
    print("F6_PLUGIN_PACKAGES=" + str(len(REQUIRED_PACKAGES) - len(result.missing_packages)))
    print("F6_MISSING_GUARDS=" + (",".join(result.missing_guards) or "(none)"))
    print("F6_MISSING_SHADOWS=" + (",".join(result.missing_shadows) or "(none)"))
    print("F6_MISSING_PACKAGES=" + (",".join(result.missing_packages) or "(none)"))
    print("F6_SOURCE_SYNCED=" + str(result.source_synced).lower())
    print("F6_UNRESOLVED_DEPENDENCY_KINDS=" + (",".join(result.missing_dependencies) or "(none)"))
    print("F6_MANAGED_RELEASE_READY=" + str(result.release_ready).lower())
    print("F6_PREFLIGHT_NO_DATAVERSE_CALLS_OR_IMPORTS")
    if args.release and not result.release_ready:
        print("F6_RELEASE_BLOCKED_PENDING_DEPENDENCIES_AND_TARGET_ACCEPTANCE")
        return 2
    return 0 if result.valid_source else 1


if __name__ == "__main__":
    raise SystemExit(main())
