from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
WORKSPACE = ROOT / "apps" / "financial-workspace"


class DataverseIdentityProbeContractTests(unittest.TestCase):
    def test_disabled_by_default_and_separate_from_demo(self):
        entry = (WORKSPACE / "src/main.tsx").read_text(encoding="utf-8")
        example = (WORKSPACE / ".env.example").read_text(encoding="utf-8")
        self.assertIn('VITE_FM_ENABLE_IDENTITY_CHECK === "true"', entry)
        self.assertIn('get("verify") === "identity"', entry)
        self.assertIn("<App />", entry)
        self.assertIn("VITE_FM_ENABLE_IDENTITY_CHECK=false", example)
        self.assertIn("YOUR_ENTRA_APPLICATION_CLIENT_GUID", example)

    def test_readonly_delegated_identity_and_no_bypass(self):
        code = (WORKSPACE / "src/infrastructure/data/identity-check.ts").read_text(
            encoding="utf-8"
        )
        for marker in (
            "PublicClientApplication",
            "loginPopup",
            "acquireTokenSilent",
            "BrowserCacheLocation.MemoryStorage",
            "/user_impersonation",
            "api/data/v9.2/WhoAmI",
            'method: "GET"',
            "config.organizationId.toLowerCase()",
            '"https:"',
            'window.location.hostname !== "localhost"',
        ):
            self.assertIn(marker, code)
        for forbidden in ('method: "POST"', 'method: "PATCH"', "localStorage", "clientSecret", "fmg_ApproveBudget"):
            self.assertNotIn(forbidden, code)


if __name__ == "__main__":
    unittest.main()
