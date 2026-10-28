"""Reviewer preflight negative matrix: never grants financial authority."""
import sys
import unittest
import uuid
from dataclasses import replace
from pathlib import Path

BASE = Path(__file__).resolve().parents[1] / "integrations/power-automate"
sys.path.insert(0, str(BASE))
import fm11_review_policy as policy

class ReviewPolicyTests(unittest.TestCase):
    def setUp(self):
        self.request = {
            "requestId": "FM11-123456789", "name": "Test equipment",
            "amountPen": "12.50", "date": "2026-10-29",
            "categoryCode": "103", "branchCode": "LM",
            "accountName": "Caja La Merced", "flowClass": "capital",
            "description": "synthetic"
        }
        self.record = {"fmg_reviewstate": "Unverified",
                       "fmg_validationstate": "FormatChecked",
                       "fmg_requestid": "FM11-123456789",
                       "request": self.request}
        self.context = policy.TrustedReviewContext(
            reviewer_id=uuid.uuid4(), submitter_id=uuid.uuid4(),
            reviewer_roles=frozenset({policy.REVIEWER_ROLE}),
            permitted_branches=frozenset({"LM"}),
            permitted_accounts=frozenset({"Caja La Merced"}),
            active_categories=frozenset({"103"}),
            active_accounts=frozenset({"Caja La Merced"}),
            shared_account_grant=False, intake_rowversion="12345"
        )

    def test_eligible_is_not_an_approval_or_write(self):
        result = policy.preflight(self.record, self.context)
        self.assertEqual(result["review"], "EligibleForIndependentApproval")
        self.assertEqual(result["nextAction"], "ProtectedDataverseCustomAPI")
        self.assertNotIn("Approved", result.values())

    def test_rejects_self_approval_and_missing_role(self):
        with self.assertRaises(policy.ReviewDenied):
            policy.preflight(self.record, replace(self.context,
                submitter_id=self.context.reviewer_id))
        with self.assertRaises(policy.ReviewDenied):
            policy.preflight(self.record, replace(self.context,
                reviewer_roles=frozenset({"System Administrator"})))

    def test_rejects_missing_identity_and_rowversion(self):
        for ctx in (
            replace(self.context, reviewer_id=uuid.UUID(int=0)),
            replace(self.context, submitter_id=uuid.UUID(int=0)),
            replace(self.context, intake_rowversion=""),
        ):
            with self.assertRaises(policy.ReviewDenied):
                policy.preflight(self.record, ctx)

    def test_rejects_unverified_format_or_request_id_mismatch(self):
        for change in (
            {"fmg_reviewstate": "Approved"},
            {"fmg_validationstate": "Unchecked"},
            {"fmg_requestid": "FM11-OTHER"},
            {"request": None},
        ):
            with self.assertRaises(policy.ReviewDenied):
                policy.preflight({**self.record, **change}, self.context)

    def test_rejects_bad_amount_date_category_and_account_rights(self):
        for change in (
            {"amountPen": "-9"},
            {"date": "2026-02-30"},
            {"categoryCode": "999"},
            {"branchCode": "SR"},
            {"accountName": "Cuenta bancaria principal"},
        ):
            with self.subTest(change=change):
                req = {**self.request, **change}
                with self.assertRaises(policy.ReviewDenied):
                    policy.preflight({**self.record, "request": req}, self.context)

    def test_no_direct_financial_storage(self):
        source=(BASE/"fm11_review_policy.py").read_text("utf-8")
        self.assertNotIn("service.Create",source)
        self.assertNotIn("fmg_financialtransaction",source)
        self.assertNotIn("http",source.lower())

if __name__=="__main__":
    unittest.main()
