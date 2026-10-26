"""Fail-closed verification of freshly packed FinancialManager.CashIntegrity NuGet."""
from __future__ import annotations
import hashlib
from pathlib import Path
import sys
import zipfile


def verify(path: Path) -> str:
    if not path.is_file() or path.name != "fmg_CashIntegrity.1.0.0.nupkg":
        raise ValueError("Expected a fresh fmg_CashIntegrity.1.0.0.nupkg")
    with zipfile.ZipFile(path) as bundle:
        names = bundle.namelist()
        assembly_path = "lib/net48/FinancialManager.CashIntegrity.dll"
        if names.count(assembly_path) != 1:
            raise ValueError("CashIntegrity assembly missing or duplicated")
        if len([x for x in names if x.endswith(".nuspec")]) != 1:
            raise ValueError("Unexpected package manifest")
        assembly = bundle.read(assembly_path)
        required = (
            (b"F5_BlockDirectCashWrites", "guard plugin type"),
            (b"CashWritePolicy", "policy type"),
            ("F5G001: Cash operations are locked".encode("utf-16-le"), "deny marker"),
        )
        for token, label in required:
            if token not in assembly:
                raise ValueError("Stale/incomplete package: missing " + label)
        if len(assembly) < 3000:
            raise ValueError("CashIntegrity DLL unexpectedly small")
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: check_cash_plugin_package.py <fresh.nupkg>")
    print("VERIFIED_F5_CASH_NUGET_SHA256=" + verify(Path(sys.argv[1])))
