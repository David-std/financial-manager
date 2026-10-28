"""FM11 review queue contract; no financial mutation on ingestion."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/"tools/DataverseDeployment/Fm11IntakeWorkspace.cs"
PROGRAM=ROOT/"tools/DataverseDeployment/Program.cs"
class Fm11WorkspaceTests(unittest.TestCase):
    def test_table_view_and_fields(self):
        s=SOURCE.read_text("utf-8")
        for name in ("fmg_Concept","fmg_AmountPen","fmg_RequestedDate",
                     "fmg_CategoryCode","fmg_BranchCode","fmg_AccountName",
                     "fmg_FlowClass","fmg_ValidationState"):
            self.assertIn(name,s)
        for field in ("fmg_concept","fmg_amountpen","fmg_requesteddate",
                      "fmg_categorycode","fmg_branchcode","fmg_accountname",
                      "fmg_reviewstate","fmg_validationstate"):
            self.assertIn(field,s)
        self.assertIn("FM11 - Pending review", s)
        self.assertIn("value='Unverified'",s)
        self.assertIn("order attribute='createdon' descending='true'",s)
        self.assertIn('SolutionUniqueName"] = Solution',s)
        self.assertNotIn("fmg_financialtransaction",s)
        self.assertNotIn('service.Delete(',s)
    def test_fetch_xml_allows_only_dataverse_view_id_serialization(self):
        source = SOURCE.read_text("utf-8")
        self.assertIn("MatchesStoredFetch", source)
        self.assertIn('Attribute("savedqueryid")', source)
        self.assertIn("Guid.TryParse", source)
        self.assertIn("savedId != viewId", source)
        self.assertIn("id.Remove()", source)
        self.assertIn("XNode.DeepEquals(actual.Root, expected.Root)", source)
        self.assertNotIn("FM11_VIEW_FETCH_XML=", source)

    def test_read_only_by_default(self):
        p=PROGRAM.read_text("utf-8")
        self.assertIn('mode == "fm11-intake-workspace-inspect"',p)
        self.assertIn('mode == "fm11-intake-workspace"',p)
        self.assertIn('mode != "fm11-intake-workspace-inspect"',p)
        self.assertIn("Mutation requires explicit --apply.",p)
if __name__=="__main__":
    unittest.main()
