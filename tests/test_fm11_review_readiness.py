"""FM-11 reviewer permission gates remain unassigned until approved."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
class ReviewReadinessTests(unittest.TestCase):
    def test_role_unassigned_and_separate_from_email(self):
        c=(ROOT/"tools/DataverseDeployment/Fm11ReviewReadiness.cs").read_text("utf-8")
        p=(ROOT/"tools/DataverseDeployment/Program.cs").read_text("utf-8")
        self.assertIn("Financial Manager Intake Reviewer",c)
        self.assertIn("systemuserroles",c)
        self.assertIn("teamroles",c)
        self.assertIn("FM11_REVIEW_APPROVAL_ENABLED=false",c)
        self.assertIn("FM11_REVIEW_FINANCIAL_WRITES=0",c)
        self.assertNotIn("fmg_financialtransaction",c)
        self.assertNotIn('client.Create(new Entity("fmg_',c)
        self.assertIn('mode == "fm11-review-role"',p)
        self.assertIn('mode == "fm11-review-inspect"',p)
    def test_flow_deactivation_guard(self):
        c=(ROOT/"tools/DataverseDeployment/Fm11MailStagingFlow.cs").read_text("utf-8")
        p=(ROOT/"tools/DataverseDeployment/Program.cs").read_text("utf-8")
        self.assertIn('public static int TurnOff',c)
        self.assertIn('ValidateDefinition(definition, allowPrevious: true)',c)
        self.assertIn('State = new OptionSetValue(0)',c)
        self.assertIn('FM11_MAIL_STAGING_OFF=true',c)
        self.assertIn('mode == "fm11-mail-stage-off"',p)
        self.assertIn("Mutation requires explicit --apply.",p)
if __name__=="__main__":unittest.main()
