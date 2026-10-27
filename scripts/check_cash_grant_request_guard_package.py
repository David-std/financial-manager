"""Validate the immutable, isolated F5 request-guard NuGet before registration."""
from __future__ import annotations

import hashlib
from pathlib import Path
import sys
import zipfile


def verify(path: Path) -> str:
    if path.name != "fmg_CashGrantRequestIntegrity.1.0.0.nupkg" or not path.is_file():
        raise ValueError("A freshly built F5 grant request package is required")
    with zipfile.ZipFile(path) as z:
        binaries = [name for name in z.namelist() if name.endswith(".dll")]
        expected = "lib/net48/FinancialManager.CashGrantRequestIntegrity.dll"
        if binaries != [expected]:
            raise ValueError("Grant request package must have a single protected plugin DLL")
        dll = z.read(expected)
        for marker in ("F5_BlockGrantRequestWrites", "GrantRequestWritePolicy",
                       "F5Q009", "fmg_cashgrantrequest"):
            if marker.encode("utf-16-le") not in dll and marker.encode("utf-8") not in dll:
                raise ValueError("Grant request deny-all marker is missing: " + marker)
        if len(dll) < 2000 or len(dll) > 1500000:
            raise ValueError("Unexpected deny-all plugin size")
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: check_cash_grant_request_guard_package.py <nupkg>")
    print("F5_REQUEST_GUARD_PACKAGE_SHA256=" + verify(Path(sys.argv[1])))
