"""Pure preflight for an authenticated, independent FM-11 reviewer.

This module does not approve, persist changes, or create a financial Draft.
Identity and entitlements MUST come from a trusted Dataverse/Entra server
context, never from the email body or caller-supplied strings.
"""
from __future__ import annotations
from dataclasses import dataclass
import uuid

try:
    from .fm11_validation import validate, InvalidRequest
except ImportError:
    from fm11_validation import validate, InvalidRequest

REVIEWER_ROLE = "Financial Manager Intake Reviewer"


class ReviewDenied(ValueError):
    pass


@dataclass(frozen=True)
class TrustedReviewContext:
    reviewer_id: uuid.UUID
    submitter_id: uuid.UUID
    reviewer_roles: frozenset[str]
    permitted_branches: frozenset[str]
    permitted_accounts: frozenset[str]
    active_categories: frozenset[str]
    active_accounts: frozenset[str]
    shared_account_grant: bool
    intake_rowversion: str


def preflight(intake: dict, context: TrustedReviewContext) -> dict:
    """Return an eligibility assessment, never a persisted approval."""
    if not isinstance(context, TrustedReviewContext):
        raise ReviewDenied("Trusted reviewer context is required")
    if (not isinstance(context.reviewer_id, uuid.UUID)
            or context.reviewer_id.int == 0
            or not isinstance(context.submitter_id, uuid.UUID)
            or context.submitter_id.int == 0):
        raise ReviewDenied("Both independent identities must be verified")
    if context.reviewer_id == context.submitter_id:
        raise ReviewDenied("Self-review is prohibited")
    if REVIEWER_ROLE not in context.reviewer_roles:
        raise ReviewDenied("Dedicated reviewer role is required")
    if not context.intake_rowversion or len(context.intake_rowversion) > 100:
        raise ReviewDenied("Dataverse row version is required")
    if not isinstance(intake, dict) or intake.get("fmg_reviewstate") != "Unverified":
        raise ReviewDenied("Only unverified intake may be considered")
    if intake.get("fmg_validationstate") != "FormatChecked":
        raise ReviewDenied("Intake format has not been checked")
    raw = intake.get("request")
    if not isinstance(raw, dict):
        raise ReviewDenied("Missing parsed request")
    if raw.get("requestId") != intake.get("fmg_requestid"):
        raise ReviewDenied("Request identity mismatch")
    try:
        parsed = validate(
            raw,
            permitted_branches=set(context.permitted_branches),
            permitted_accounts=set(context.permitted_accounts),
            active_categories=set(context.active_categories),
            active_accounts=set(context.active_accounts),
            shared_grant=context.shared_account_grant,
        )
    except InvalidRequest as exc:
        raise ReviewDenied(str(exc)) from exc
    return {
        "requestId": parsed["requestId"],
        "review": "EligibleForIndependentApproval",
        "nextAction": "ProtectedDataverseCustomAPI",
        "rowVersion": context.intake_rowversion,
    }
