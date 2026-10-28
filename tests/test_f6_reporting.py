"""F6 Power BI imported-model guardrails; no real identities or financial data."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "power-bi/FinancialManager/FinancialManager.SemanticModel/definition"
ROLE = MODEL / "roles/F6_DenyUntilAuthorizedBranchMembership.tmdl"
SENSITIVE_TABLES = (
    "FactPostedExpense", "DimCashAccount", "DimCategory", "DimBranch", "DimDate"
)


class F6ReportingTests(unittest.TestCase):
    def test_tmdl_deny_all_role_declared_and_referenced(self):
        role = ROLE.read_text(encoding="utf-8")
        model = (MODEL / "model.tmdl").read_text(encoding="utf-8")
        self.assertIn("role F6_DenyUntilAuthorizedBranchMembership", role)
        self.assertIn("modelPermission: read", role)
        self.assertIn("ref role F6_DenyUntilAuthorizedBranchMembership", model)
        for table in SENSITIVE_TABLES:
            self.assertRegex(role, rf"tablePermission {table}\s*=\s*FALSE\(\)")
        # No named users, assumed tenant groups or permissive default roles.
        self.assertNotIn("member ", role)
        self.assertNotIn("TRUE()", role)

    def test_source_uses_environment_placeholder_not_user_tokens(self):
        model = (MODEL / "model.tmdl").read_text(encoding="utf-8")
        self.assertIn("SET_DATAVERSE_HOST", model)
        self.assertNotRegex(model, r"(?i)\.dynamics\.com|onmicrosoft\.com|client_secret")

    def test_no_unauthorized_branch_membership_assumptions(self):
        # Future nonempty RLS needs a server-provisioned User-to-Branch mapping
        # and real Viewer-role acceptance. Static identity claims are prohibited.
        role = ROLE.read_text(encoding="utf-8")
        self.assertNotIn("USERPRINCIPALNAME()", role)
        self.assertNotIn("CUSTOMDATA()", role)
        self.assertFalse(re.search(r"(?i)@(?:outlook|gmail|hotmail)\.", role))


if __name__ == "__main__":
    unittest.main()
