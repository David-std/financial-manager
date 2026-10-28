"""Inspect F6 model-app deployment dependencies without changing Dataverse.

The first-party requirements must be installed in a target environment.
UX Agent Project references belong to the app and require solution-aware
inclusion through the official app/solution designer followed by a fresh export.
Do not edit MissingDependencies to make release checks green.
"""
from __future__ import annotations
from dataclasses import dataclass
from pathlib import Path
import argparse
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "power-platform/solutions/FinancialManager/src"

@dataclass(frozen=True)
class DependencyReport:
    valid: bool
    microsoft: tuple[str, ...]
    active: tuple[str, ...]
    errors: tuple[str, ...]

def inspect(source: Path = SOURCE) -> DependencyReport:
    errors: list[str] = []
    try:
        xml = ET.parse(source / "Other/Solution.xml").getroot()
        deps = xml.findall("./SolutionManifest/MissingDependencies/MissingDependency")
    except (ET.ParseError, OSError):
        return DependencyReport(False, (), (), ("Unreadable source manifest.",))
    microsoft: list[str] = []
    active: list[str] = []
    for entry in deps:
        req, dep = entry.find("Required"), entry.find("Dependent")
        if req is None or dep is None:
            errors.append("A dependency has no Required/Dependent pair.")
            continue
        kind, package = req.get("type"), req.get("solution")
        if (kind == "61" and req.get("schemaName") ==
              "msdyn_/Images/AppModule_Default_Icon.png" and
              package == "AppModuleWebResources (2.5)" and
              dep.get("type") == "80"):
            microsoft.append("AppModuleWebResources 2.5")
        elif (kind == "SettingDefinition" and
              req.get("id.uniquename") == "AppChannel" and
              package == "msdyn_AppFrameworkInfraExtensions (1.0.0.27)" and
              dep.get("type") == "AppSetting"):
            microsoft.append("msdyn_AppFrameworkInfraExtensions 1.0.0.27")
        elif (kind == "uxagentproject" and package == "Active" and
              dep.get("type") == "62" and
              dep.get("schemaName") == "financial_manager_bee9f134"):
            uid = req.get("id", "").strip("{}")
            try:
                import uuid
                uid = str(uuid.UUID(uid))
                active.append(uid)
            except ValueError:
                errors.append("UX Agent Project has an invalid GUID.")
        else:
            errors.append("Unexpected dependency type or owner: " + str(kind))
    if len(microsoft) != 2 or len(set(microsoft)) != 2:
        errors.append("Expected exactly two pinned Microsoft dependencies.")
    if len(active) not in (0, 4) or len(set(active)) != len(active):
        errors.append("Unexpected or duplicate app-bound UX Agent Projects.")
    # The four exact UX projects are versioned with the model-driven app.
    expected_projects = {
        "1a6e3c3e-fb2e-43b0-b5dd-9071483d1a1f",
        "35a771c0-5cdf-4d5e-a295-6f893ffcf3d4",
        "4a317af5-b6a8-4a82-a22a-6b56447a082f",
        "92083d36-806a-43dd-b289-2fafd9e3afb1",
    }
    project_root = source / "uxagentprojects"
    actual_projects = {
        p.name for p in project_root.iterdir()
        if p.is_dir() and (p / "uxagentproject.xml").is_file()
    } if project_root.is_dir() else set()
    if actual_projects != expected_projects:
        errors.append("The four pinned solution-aware UX projects are incomplete.")
    if active:
        errors.append("Unincluded Active UX project dependencies remain in the export.")
    app = source / "AppModules/financial_manager_bee9f134/appmodule.xml"
    if not app.is_file():
        errors.append("Source app module missing; cannot validate app dependencies.")
    else:
        try:
            app_tree = ET.parse(app).getroot()
            if app_tree.findtext("UniqueName") != "financial_manager_bee9f134":
                errors.append("App module name changed.")
            if app_tree.find("./appsettings/appsetting[@settingdefinitionid.uniquename='AppChannel']") is None:
                errors.append("AppChannel app setting missing.")
        except ET.ParseError:
            errors.append("Malformed app module.")
    return DependencyReport(not errors, tuple(sorted(microsoft)),
                            tuple(sorted(active)), tuple(errors))

def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.parse_args()
    r = inspect()
    for message in r.errors:
        print("F6_DEPENDENCY_ERROR=" + message)
    print("F6_DEPENDENCIES_MANIFEST_VALID=" + str(r.valid).lower())
    print("F6_MICROSOFT_PREREQUISITES=" + ";".join(r.microsoft))
    print("F6_ACTIVE_UX_AGENT_PROJECTS=" + str(len(r.active)))
    print("F6_MANAGED_TARGET_DEPENDENCY_VERIFICATION_REQUIRED=true")
    print("F6_UX_PROJECTS_INCLUDED_IN_SOURCE=" + str(len(r.active) == 0 and r.valid).lower())
    print("F6_DEPENDENCIES_UNCHANGED_NO_IMPORT_OR_MANIFEST_MUTATION")
    return 0 if r.valid else 2

if __name__ == "__main__":
    raise SystemExit(main())
