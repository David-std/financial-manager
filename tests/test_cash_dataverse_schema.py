from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "tools" / "DataverseDeployment"


class CashDataverseProvisioningContractTests(unittest.TestCase):
    def setUp(self):
        self.program = (CLI / "Program.cs").read_text(encoding="utf-8")
        self.schema = (CLI / "CashSchemaDeployment.cs").read_text(encoding="utf-8")
        self.keys = (CLI / "CashUniqueKeys.cs").read_text(encoding="utf-8")
        self.probe = (CLI / "CashTransactionProbe.cs").read_text(encoding="utf-8")
        self.verify = (CLI / "CashSchemaReadiness.cs").read_text(encoding="utf-8")

    def test_six_separate_tables_in_financial_manager_solution(self):
        for entity in (
            "fmg_cashreceipt",
            "fmg_cashtransfer",
            "fmg_cashposting",
            "fmg_accountopening",
            "fmg_bankstatement",
            "fmg_bankreconciliation",
        ):
            self.assertIn(entity, self.schema)
            self.assertIn(entity, self.verify)
        self.assertIn('private const string Solution = "FinancialManager"', self.schema)
        self.assertIn("CreateEntityRequest", self.schema)

    def test_user_ownership_native_audit_and_delete_restrictions(self):
        self.assertIn("OwnershipTypes.UserOwned", self.schema)
        self.assertIn("IsAuditEnabled = new BooleanManagedProperty(true)", self.schema)
        self.assertIn("Delete = CascadeType.Restrict", self.schema)
        self.assertIn("IsAuditEnabled?.Value != true", self.verify)
        self.assertIn("IsOptimisticConcurrencyEnabled != true", self.verify)

    def test_cash_amounts_and_dates_are_explicit(self):
        self.assertIn("MoneyAttributeMetadata Money(", self.schema)
        self.assertIn("Precision = 2", self.schema)
        self.assertIn("DateTimeFormat.DateOnly", self.schema)
        self.assertIn("fmg_SignedAmount", self.schema)
        self.assertIn("fmg_SourceAccount", self.schema)
        self.assertIn("fmg_DestinationAccount", self.schema)

    def test_idempotency_keys_and_single_opening(self):
        for token in ("fmg_CR_Request", "fmg_CT_Request", "fmg_CP_Leg",
                      "fmg_AO_AccountDate", "fmg_BS_Request", "fmg_BR_Request"):
            self.assertIn(token, self.keys)
        self.assertIn('new[]{"fmg_cashaccount", "fmg_effectiveon"}', self.keys)
        self.assertIn("CreateEntityKeyRequest", self.keys)
        self.assertIn("DisplayName = new Label(", self.keys)
        self.assertIn("EntityKeyIndexStatus.Failed", self.keys)

    def test_explicit_apply_and_organization_identity_gates(self):
        self.assertIn("FM_ENV_URL", self.program)
        self.assertIn("FM_ORGANIZATION_ID", self.program)
        self.assertIn("WhoAmIRequest", self.program)
        self.assertIn('mode != "cash-schema"', self.program)
        self.assertIn('mode != "cash-keys"', self.program)
        self.assertIn('mode != "cash-schema-publish"', self.program)
        self.assertIn('mode != "cash-transaction-probe"', self.program)
        self.assertIn('mode != "cash-schema-inspect"', self.program)
        self.assertIn('mode != "cash-schema-verify"', self.program)
        self.assertIn('mode != "cash-keys-inspect"', self.program)
        self.assertIn("Mutation requires explicit --apply", self.program)

    def test_rollback_probe_never_commits_a_test_payment(self):
        self.assertIn("ExecuteTransactionRequest", self.probe)
        self.assertIn("MANDATORY_ROLLBACK_SENTINEL", self.probe)
        self.assertIn("new UpdateRequest", self.probe)
        self.assertIn("FaultedRequestIndex", self.probe)
        self.assertIn("Requests.Count - 1", self.probe)
        self.assertIn("fmg_signedamount", self.probe)
        self.assertIn("-17.25m", self.probe)
        self.assertIn("+17.25m", self.probe)
        self.assertIn("F5_ROLLBACK_VERIFIED", self.probe)
        self.assertIn('TopCount = 1', self.probe)

    def test_metadata_only_no_premature_user_exposure(self):
        self.assertNotIn("cashreceipt", (ROOT / "power-platform/model-apps/financial-manager/expenses.tsx").read_text(encoding="utf-8"))
        adr = (ROOT / "docs/decisions/0007-cash-ledger-foundations.md").read_text(encoding="utf-8")
        self.assertIn("synchronous server plug-in", adr)
        self.assertIn("NOT enabled", adr)


if __name__ == "__main__":
    unittest.main()
