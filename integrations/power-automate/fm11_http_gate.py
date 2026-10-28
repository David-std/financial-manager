"""FM-11 authenticated-HTTP readiness gate.

This module does not validate OAuth tokens, create cloud flows, or create
Dataverse rows. OAuth verification must be enforced by Microsoft's trigger.
Fail closed until every real-environment dependency is verified.
"""
from __future__ import annotations

from dataclasses import dataclass
from urllib.parse import urlparse

FLOW_AUDIENCE = "https://service.flow.microsoft.com/"
AUTH_MODE = "Specific users in my tenant"
REQUIRED_GATES = (
    "premium_licensed",
    "oauth_trigger_restricted",
    "allowed_users_nonempty",
    "shortcuts_delegated_login_tested",
    "audience_and_tenant_verified",
    "server_side_actor_authorization",
    "live_category_and_account_resolution",
    "atomic_request_id_deduplication",
    "authenticated_review",
    "draft_only_write_path",
    "synthetic_end_to_end_passed",
)


@dataclass(frozen=True)
class GateState:
    # Every flag is False until explicitly established by a real test,
    # not inferred from local Python tests or unrelated Dataverse login.
    premium_licensed: bool = False
    oauth_trigger_restricted: bool = False
    allowed_users_nonempty: bool = False
    shortcuts_delegated_login_tested: bool = False
    audience_and_tenant_verified: bool = False
    server_side_actor_authorization: bool = False
    live_category_and_account_resolution: bool = False
    atomic_request_id_deduplication: bool = False
    authenticated_review: bool = False
    draft_only_write_path: bool = False
    synthetic_end_to_end_passed: bool = False

    def missing(self) -> tuple[str, ...]:
        return tuple(name for name in REQUIRED_GATES if not getattr(self, name))

    def can_enable(self) -> bool:
        return not self.missing()


def validate_endpoint(endpoint: str) -> None:
    """Avoid sending credentials to http, loopback, raw IPs, or query-only URLs.

    The actual Power Automate domain must additionally be checked against a
    tenant-approved exact allowlist when configuring a real connection.
    """
    if not endpoint or endpoint != endpoint.strip():
        raise ValueError("Missing or malformed flow endpoint")
    parsed = urlparse(endpoint)
    if (
        parsed.scheme != "https"
        or parsed.username is not None
        or parsed.password is not None
        or not parsed.hostname
        or parsed.hostname in {"localhost", "127.0.0.1"}
        or parsed.fragment
    ):
        raise ValueError("Expected an HTTPS Power Automate endpoint")


def validate_direct_payload(payload: dict, *, account_scope: str) -> dict:
    """Validate the transport envelope without trusting client-derived identity.

    Full security/financial validation happens *again* within the Microsoft
    flow using authenticated principal and current Dataverse relationships.
    """
    if not isinstance(payload, dict):
        raise ValueError("Payload must be an object")
    forbidden = {"actorId", "userId", "ownerId", "status", "approved",
                 "postingId", "bearer", "access_token", "authorization"}
    if forbidden.intersection(payload):
        raise ValueError("Client cannot provide identity or approvals")
    if account_scope not in {"LM", "SR", "SHARED"}:
        raise ValueError("Unknown account scope")
    return payload


def success_response(request_id: str, *, gate: GateState,
                     persisted_intake: bool) -> dict:
    """No success claim before acceptance was durably recorded.

    202 means a request is pending; never implies a Dataverse Draft.
    """
    if not gate.can_enable() or not persisted_intake:
        raise RuntimeError("Cannot acknowledge before authenticated durable intake")
    return {"requestId": request_id, "status": "PENDING_REVIEW"}
