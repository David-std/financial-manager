"""Verify a fresh, read-only F5 runtime probe NuGet before registration."""
from __future__ import annotations
import hashlib
from pathlib import Path
import sys
import zipfile

def verify(path: Path) -> str:
    if not path.is_file() or path.name != "fmg_CashOperations.1.0.0.nupkg":
        raise ValueError("A fresh fmg_CashOperations.1.0.0.nupkg is required")
    with zipfile.ZipFile(path) as bundle:
        expected = "lib/net48/FinancialManager.CashOperations.dll"
        if bundle.namelist().count(expected) != 1:
            raise ValueError("Expected one CashOperations assembly")
        if len([n for n in bundle.namelist() if n.endswith(".nuspec")]) != 1:
            raise ValueError("Unexpected package manifests")
        data = bundle.read(expected)
        required = (
            (b"F5_RuntimeProbe", "probe type"),
            (b"F5_RuntimeProbePolicy", "policy type"),
            ("F5D001:".encode("utf-16-le"), "pipeline safety marker"),
            ("CashWritesEnabled".encode("utf-16-le"), "no-write response marker"),
        )
        for token, label in required:
            if token not in data:
                raise ValueError("Incorrect/stale runtime probe: " + label)
        if len(data) < 3000:
            raise ValueError("Runtime assembly unexpectedly small")
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: check_cash_runtime_package.py <fresh.nupkg>")
    print("VERIFIED_F5_RUNTIME_NUGET_SHA256=" + verify(Path(sys.argv[1])))
