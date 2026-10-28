"""Offline FM-11 intake validator; cloud flow must enforce equivalent controls."""
from __future__ import annotations
import datetime as dt
import decimal
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# Read-only snapshot of active DEV accounts, not an authorization grant.
ACCOUNT_BRANCH = {
    "Caja La Merced": "LM", "Caja San Ramón": "SR",
    "Cuenta bancaria principal": "SHARED",
}
FIELDS = {"requestId", "name", "amountPen", "date", "categoryCode",
          "branchCode", "accountName", "flowClass", "description"}
REQUIRED = FIELDS - {"description"}


class InvalidRequest(ValueError):
    pass


def leaf_codes() -> set[str]:
    codes = set()
    for line in (ROOT / "docs/category-catalog.md").read_text(encoding="utf-8").splitlines():
        parts = [v.strip() for v in line.split("|")]
        if len(parts) == 7 and re.fullmatch(r"\d{3}", parts[3] or ""):
            if not parts[3].endswith("00"):
                codes.add(parts[3])
    if len(codes) < 25:
        raise InvalidRequest("Category catalog missing")
    return codes


def validate(raw: object, *, permitted_branches: set[str],
             permitted_accounts: set[str], active_categories: set[str],
             active_accounts: set[str], shared_grant: bool = False) -> dict:
    """Requires external verified authorizations; never creates a Dataverse row."""
    if not isinstance(raw, dict) or set(raw) - FIELDS or REQUIRED - set(raw):
        raise InvalidRequest("Invalid field set")
    if any(not isinstance(v, str) for v in raw.values()):
        raise InvalidRequest("All fields must be text")
    rid = raw["requestId"]
    if not (12 <= len(rid) <= 80 and re.fullmatch(r"[A-Za-z0-9_-]+", rid)):
        raise InvalidRequest("Invalid request ID")
    if not (4 <= len(raw["name"].strip()) <= 100):
        raise InvalidRequest("Invalid expense name")
    amount = raw["amountPen"]
    if not re.fullmatch(r"(?:0|[1-9][0-9]*)(?:\.[0-9]{1,2})?", amount) or len(amount) > 16:
        raise InvalidRequest("Invalid monetary input")
    if decimal.Decimal(amount) <= 0:
        raise InvalidRequest("Amount must be positive")
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", raw["date"]):
        raise InvalidRequest("Invalid date pattern")
    try:
        dt.date.fromisoformat(raw["date"])
    except ValueError as exc:
        raise InvalidRequest("Invalid calendar date") from exc
    branch = raw["branchCode"]
    account = raw["accountName"]
    category = raw["categoryCode"]
    if branch not in {"LM", "SR"} or branch not in permitted_branches:
        raise InvalidRequest("Branch not authorized")
    if category not in leaf_codes() or category not in active_categories:
        raise InvalidRequest("Inactive or invalid category")
    if account not in ACCOUNT_BRANCH or account not in permitted_accounts or account not in active_accounts:
        raise InvalidRequest("Inactive or unauthorized cash account")
    if ACCOUNT_BRANCH[account] == "SHARED" and not shared_grant:
        raise InvalidRequest("Shared cash account requires authorized grant")
    if ACCOUNT_BRANCH[account] not in {branch, "SHARED"}:
        raise InvalidRequest("Account does not belong to branch")
    if raw["flowClass"] not in {"operativo", "capital", "impuesto", "otro"}:
        raise InvalidRequest("Invalid flow class")
    if len(raw.get("description", "")) > 1000:
        raise InvalidRequest("Description too long")
    return {**raw, "name": raw["name"].strip(), "description": raw.get("description", "")}


def validate_json(body: str, **kwargs) -> dict:
    try:
        return validate(json.loads(body), **kwargs)
    except (ValueError, TypeError) as exc:
        if isinstance(exc, InvalidRequest):
            raise
        raise InvalidRequest("Invalid JSON") from exc
