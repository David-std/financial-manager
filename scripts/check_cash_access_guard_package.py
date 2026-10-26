"""Verify isolated immutable F5 cash access deny-all plugin package."""
from __future__ import annotations

import hashlib
from pathlib import Path
import sys
import zipfile


def verify(path: Path) -> str:
    if not path.is_file() or path.name != "fmg_CashAccessIntegrity.1.0.0.nupkg":
        raise ValueError("Expected isolated fresh cash access guard NuGet")
    with zipfile.ZipFile(path) as archive:
        dlls = [n for n in archive.namelist() if n.endswith(".dll")]
        path_dll = "lib/net48/FinancialManager.CashAccessIntegrity.dll"
        if dlls != [path_dll]:
            raise ValueError("The guard package must contain exactly one DLL")
        raw = archive.read(path_dll)
        for token in (
            b"F5_BlockCashAccessWrites",
            b"CashAccessWritePolicy",
            "F5A009".encode("utf-16-le"),
            "fmg_cashaccessgrant".encode("utf-16-le"),
        ):
            if token not in raw:
                raise ValueError("Missing guard policy marker")
        if len(raw) < 2000:
            raise ValueError("Unexpectedly small grant guard binary")
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: check_cash_access_guard_package.py <fresh-nupkg>")
    print("F5_CASH_ACCESS_GUARD_SHA256=" + verify(Path(sys.argv[1])))
