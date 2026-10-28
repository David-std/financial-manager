"""FM-11 transactional draft security contract and deployer checks."""
import re
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
PLUGIN=ROOT/"plugins/FinancialManager.MailDraftExecution/MailDraftEndpoint.cs"
DEPLOY=ROOT/"tools/DataverseDeployment/Fm11DraftApiDeployment.cs"
PREFLIGHT=ROOT/"tools/DataverseDeployment/Fm11DraftPreflight.cs"
PROGRAM=ROOT/"tools/DataverseDeployment/Program.cs"

class DraftConversionSecurityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.plugin=PLUGIN.read_text("utf-8")
        cls.deploy=DEPLOY.read_text("utf-8")
        cls.preflight=PREFLIGHT.read_text("utf-8")
        cls.program=PROGRAM.read_text("utf-8")

    def test_authenticated_transactional_entry(self):
        for s in ('ctx.MessageName != Api','ctx.Stage != 30',
                  'ctx.Mode != 0','!ctx.IsInTransaction',
                  'ctx.InitiatingUserId != ctx.UserId',
                  'CreateOrganizationService(ctx.InitiatingUserId)',
                  'HasReviewAuthority(svc, ctx.InitiatingUserId)',
                  'Financial Manager Intake Reviewer',
                  '627090FF-40A3-4053-8790-584EDC5BE201'):
            self.assertIn(s,self.plugin)
        self.assertNotIn('CreateOrganizationService(null)',self.plugin)
        self.assertNotIn('CreateOrganizationService(Guid.Empty)',self.plugin)

    def test_source_staging_exact_and_no_automatic_ingest(self):
        for s in ('"fmg_reviewstate") != "Unverified"',
                  '"fmg_validationstate") != "FormatChecked"',
                  'ExpectedRowVersion','source.RowVersion != expected',
                  'DataContractJsonSerializer','fmg_rawbody',
                  'fmg_requestid','fmg_requesteddate',
                  'Regex.IsMatch(parsed.Amount',
                  'DateTime.TryParseExact',
                  'fmg_financialtransaction'):
            self.assertIn(s,self.plugin)
        self.assertIn('FM11_DRAFT_API_NO_AUTOMATIC_MAIL_CALL=true',self.deploy)

    def test_authorized_live_references_and_draft_only(self):
        for s in ('fmg_code','fmg_isactive','fmg_isgroup',
                  'fmg_parentcategory','fmg_cashaccount',
                  'fmg_branch','isocurrencycode','"PEN"',
                  'fmg_externalrequestid',
                  '["fmg_status"] = new OptionSetValue(Draft)',
                  'svc.Create(draft)'):
            self.assertIn(s,self.plugin)
        self.assertNotIn('fmg_PostFinancialTransaction',self.plugin)
        self.assertNotIn('fmg_VoidFinancialTransaction',self.plugin)
        self.assertNotIn('797280001)',self.plugin.split('var draft = new Entity(Financial)',1)[1])

    def test_unique_key_and_atomic_state_change(self):
        self.assertIn('EntityKeyIndexStatus.Active',self.plugin)
        self.assertIn('"fmg_externalrequestid"',self.plugin)
        self.assertIn('ConcurrencyBehavior.IfRowVersionMatches',self.plugin)
        self.assertIn('"DraftCreated"',self.plugin)
        self.assertIn('ctx.OutputParameters["DraftId"]',self.plugin)
        self.assertIn('ctx.OutputParameters["Created"]',self.plugin)
        self.assertIn('new CreateEntityKeyRequest',self.preflight)
        self.assertIn('EntityKeyIndexStatus.Active',self.deploy)

    def test_authenticated_manual_conversion_entrypoint(self):
        converter=(ROOT/"tools/DataverseDeployment/Fm11DraftConvert.cs").read_text("utf-8")
        self.assertIn('Environment.GetEnvironmentVariable("FM11_REQUEST_ID")',converter)
        self.assertIn('fmg_CreateDraftFromMailIntake',converter)
        self.assertIn('["ExpectedRowVersion"] = intake.RowVersion',converter)
        self.assertIn('FM11_DRAFT_PREVIEW_ONLY=true',converter)
        self.assertIn('FM11_DRAFT_CREATED=true',converter)
        self.assertNotIn('client.Create(',converter)
        self.assertNotIn('client.Update(',converter)
        self.assertNotIn('fmg_PostFinancialTransaction',converter)
        self.assertIn('mode == "fm11-draft-convert"',self.program)
        self.assertIn('mode == "fm11-draft-preview"',self.program)

    def test_private_api_and_permissions(self):
        for s in ('"fmg_CreateDraftFromMailIntake"',
                  '"prvCreatefmg_financialtransaction"',
                  '["isprivate"] = true',
                  '["allowedcustomprocessingsteptype"] = new OptionSetValue(0)',
                  '["workflowsdkstepenabled"] = false',
                  'new [] { ("IntakeId",12), ("ExpectedRowVersion",10) }',
                  'new [] { ("DraftId",12), ("Created",0) }'):
            self.assertIn(s,self.deploy)
        for cmd in ('fm11-draft-package','fm11-draft-api','fm11-draft-key'):
            self.assertIn(cmd,self.program)
        self.assertIn('Mutation requires explicit --apply.',self.program)

if __name__=="__main__":
    unittest.main()
