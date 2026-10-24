"""Verify a freshly packed BudgetIntegrity NuGet BEFORE any Dataverse deployment.

Usage:
    python scripts/check_budget_plugin_package.py path/to/fmg_BudgetIntegrity.1.0.0.nupkg

Do not use an old package in bin/Release: the project may compile a new DLL
without regenerating the existing NuGet artifact. Prefer a fresh, empty
PackageOutputPath and inspect the DLL inside the resulting package.
"""
from __future__ import annotations

import hashlib
from pathlib import Path
import sys
import zipfile

REQUIRED_TYPES = (
    "BG_EnforceBudgetIntegrity",
    "BA_EnforceAllocationIntegrity",
    "AE_BlockDirectEventWrites",
    "BG_ApproveBudget",
    "BAC_BlockDirectClaimWrites",
)


def verify(path: Path) -> str:
    if not path.is_file():
        raise ValueError(f"Missing NuGet: {path}")
    with zipfile.ZipFile(path) as archive:
        manifest = [n for n in archive.namelist() if n.lower().endswith(".nuspec")]
        if len(manifest) != 1:
            raise ValueError("Expected exactly one NuGet manifest")
        names = [
            n for n in archive.namelist()
            if n.endswith("/FinancialManager.BudgetIntegrity.dll")
        ]
        if len(names) != 1:
            raise ValueError("Expected exactly one BudgetIntegrity assembly")
        assembly = archive.read(names[0])
        missing = [name for name in REQUIRED_TYPES if name.encode() not in assembly]
        if missing:
            raise ValueError("Package contains a stale assembly; missing: " + ", ".join(missing))
        if len(assembly) < 10_000:
            raise ValueError("Unexpectedly small BudgetIntegrity assembly")
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: check_budget_plugin_package.py <nupkg>", file=sys.stderr)
        return 2
    try:
        digest = verify(Path(sys.argv[1]))
    except (OSError, ValueError, zipfile.BadZipFile) as exc:
        print(f"INVALID_BUDGET_PLUGIN_PACKAGE: {exc}", file=sys.stderr)
        return 1
    print("VALIDATED_NUGET_SHA256=" + digest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
