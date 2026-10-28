from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "tools" / "DataverseDeployment"


class CashGrantReviewStandbyContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.program = (CLI / "Program.cs").read_text(encoding="utf-8")
        cls.standby = (CLI / "CashGrantReviewStandbyDeployment.cs").read_text(
            encoding="utf-8"
        )
        cls.candidate = (
            ROOT
            / "plugins/FinancialManager.CashOperations/F5_ValidatedNestedReviewGuard.cs"
        ).read_text(encoding="utf-8")

    def test_only_explicit_apply_can_register_standby(self):
        self.assertIn('"cash-review-standby-inspect"', self.program)
        self.assertIn('"cash-review-standby-register"', self.program)
        self.assertIn('"cash-review-standby-one"', self.program)
        self.assertIn('mode == "cash-review-standby-register"', self.program)
        self.assertIn("Mutation requires explicit --apply", self.program)
        self.assertNotIn('mode != "cash-review-standby-register" && !apply', self.program)

    def test_existing_guards_are_rechecked_before_and_after(self):
        for call in (
            "CashGuardDeployment.Run(svc, false, true);",
            "CashGrantGuardDeployment.Steps(svc, false, true);",
            "CashGrantRequestGuardDeployment.Steps(svc, false, true);",
        ):
            self.assertGreaterEqual(self.standby.count(call), 2)
        self.assertIn('table.Prefix + verb', self.standby)
        self.assertIn('"rank") != 1', self.standby)
        self.assertIn('"statecode")?.Value != 0', self.standby)
        self.assertIn("F5_REVIEW_24_LEGACY_GUARDS_ACTIVE_TRUE", self.standby)
        self.assertNotIn("svc.Delete(", self.standby)

    def test_six_new_steps_are_stage10_sync_rank2_and_actor_scoped(self):
        for table in ("fmg_cashgrantrequest", "fmg_cashaccessgrant"):
            self.assertIn(table, self.standby)
        for verb in ('"Create"', '"Update"', '"Delete"'):
            self.assertIn(verb, self.standby)
        for expected in (
            'step["stage"] = new OptionSetValue(10);',
            'step["mode"] = new OptionSetValue(0);',
            'step["rank"] = 2;',
            'step["configuration"] = configuration;',
            'steps[0].GetAttributeValue<int>("rank") != 2',
            'GetAttributeValue<EntityReference>("impersonatinguserid") != null',
            'F5_REVIEW_NO_CUTOVER_OR_GRANT_ISSUED',
        ):
            self.assertIn(expected, self.standby)
        self.assertNotIn('step["impersonatinguserid"]', self.standby)

    def test_exact_two_pinned_apis_not_tags_or_privileged_bypass(self):
        for token in (
            "fmg_SubmitCashGrantRequest",
            "fmg_DecideCashGrantRequest",
            "prvCreatefmg_CashGrantRequest",
            "prvWritefmg_CashGrantRequest",
            "matching.Length != 1",
            "ids[0] == ids[1]",
            "F5_ValidatedNestedReviewGuard",
        ):
            self.assertIn(token, self.standby)
        self.assertIn("parent.OwningExtension", self.candidate)
        self.assertIn("CashGrantApprovalPolicy.Approve", self.candidate)
        for forbidden in ('step["statecode"] = new OptionSetValue(1)', "BypassCustomPluginExecution"):
            self.assertNotIn(forbidden, self.standby)


if __name__ == "__main__":
    unittest.main()
