"""Source control evidence for the unassigned, disabled budget approval infrastructure."""

from pathlib import Path
import unittest
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
SOLUTION = ROOT / "power-platform" / "solutions" / "FinancialManager" / "src"


class ApprovalMetadataTests(unittest.TestCase):
    def test_exported_annual_claim_contains_unique_scope_key(self) -> None:
        path = SOLUTION / "Entities" / "fmg_BudgetApprovalClaim" / "Entity.xml"
        root = ET.parse(path).getroot()
        keys = root.findall(".//EntityKeys/EntityKey")
        matches = [
            key for key in keys
            if key.findtext("Name") == "fmg_BAC_AnnualScopeUnique"
        ]
        self.assertEqual(len(matches), 1)
        self.assertEqual(
            [item.text for item in matches[0].findall("./EntityKeyAttributes/AttributeName")],
            ["fmg_scopekey"],
        )

    def test_finance_approver_role_not_autoassigned(self) -> None:
        path = SOLUTION / "Roles" / "Financial Manager Budget Approver.xml"
        root = ET.parse(path).getroot()
        self.assertEqual(root.attrib.get("name"), "Financial Manager Budget Approver")
        self.assertEqual(root.findtext("IsAutoAssigned"), "0")
        self.assertGreaterEqual(len(root.findall("./RolePrivileges/RolePrivilege")), 1)

    def test_deployed_budget_guard_still_has_explicit_blocking_path(self) -> None:
        code = (
            ROOT / "plugins" / "FinancialManager.BudgetIntegrity" / "BudgetGuard.cs"
        ).read_text(encoding="utf-8")
        self.assertIn('Error("BGV009","Direct budget approval is forbidden.")', code)
        self.assertIn("!trustedApproval", code)

    def test_approval_readiness_cannot_be_inferred_from_a_role_assignment(self) -> None:
        code = (
            ROOT / "tools" / "DataverseDeployment" / "BudgetApprovalReadiness.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("TwoIdentityAcceptanceRecorded = false", code)
        self.assertIn("ConcurrencyAcceptanceRecorded = false", code)
        self.assertIn("APPROVER_ROLE_EXACT_MINIMUM_PRIVILEGES=", code)
        self.assertIn('GetAttributeValue<bool>("isprivate")', code)
        self.assertIn("roleAssignmentReady && TwoIdentityAcceptanceRecorded", code)
        self.assertIn("TwoIdentityAcceptanceRecorded && ConcurrencyAcceptanceRecorded", code)


if __name__ == "__main__":
    unittest.main()
