"""FM-11 Microsoft Entra device-code probe: offline safety regression tests."""
import base64
import importlib.util
import io
import json
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "integrations/power-automate/fm11_oauth_device_probe.py"
spec = importlib.util.spec_from_file_location("fm11_oauth_device_probe", PATH)
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


def fake_jwt(data):
    payload = base64.urlsafe_b64encode(json.dumps(data).encode()).decode().rstrip("=")
    return "e30." + payload + ".signature"


class DeviceCodeProbeTests(unittest.TestCase):
    def claims(self):
        return {
            "aud": probe.FLOW_AUDIENCE,
            "tid": probe.TENANT_ID,
            "iss": f"https://login.microsoftonline.com/{probe.TENANT_ID}/v2.0",
            "oid": "00000000-0000-0000-0000-000000000001",
            "scp": "Flows.Read.All",
            "azp": probe.CLIENT_ID,
        }

    def test_public_client_scope_uses_double_slash(self):
        self.assertEqual(probe.FLOW_SCOPE,
                         "https://service.flow.microsoft.com//.default")
        self.assertIn("offline_access", probe.LOGIN_SCOPE)
        self.assertNotIn("client_secret", PATH.read_text(encoding="utf-8"))

    def test_decode_claims_from_token_payload(self):
        claims = self.claims()
        self.assertEqual(probe.decode_claims(fake_jwt(claims)), claims)
        self.assertTrue(all(probe.validate_claims(claims).values()))
        with self.assertRaises(probe.OAuthProbeError):
            probe.decode_claims("nonsense")

    def test_rejects_wrong_audience_tenant_issuer_identity_or_app(self):
        edits = {
            "aud": "https://graph.microsoft.com/",
            "tid": "00000000-0000-0000-0000-000000000002",
            "iss": "https://evil.test/",
            "oid": "",
            "scp": "",
            "azp": "00000000-0000-0000-0000-000000000003",
        }
        for key, value in edits.items():
            with self.subTest(key=key):
                with self.assertRaises(probe.OAuthProbeError):
                    probe.validate_claims({**self.claims(), key: value})
        with patch.dict("os.environ", {"FM11_EXPECTED_USER_OID": "different"}):
            with self.assertRaises(probe.OAuthProbeError):
                probe.validate_claims(self.claims())

    def test_callback_url_allowlist_prevents_token_exfiltration(self):
        correct = ("https://d5a49e040363e762ba82c4ac8c91c6f0."
                   "environment.api.powerplatform.com:443/powerautomate/automations/direct/"
                   "workflows/123/triggers/manual/paths/invoke?api-version=1")
        probe.check_callback_url(correct)
        for candidate in (
            "https://another.environment.api.powerplatform.com/triggers/manual/paths/invoke",
            "https://other.logic.azure.com/triggers/manual/paths/invoke",
            "http://example.com/triggers/manual/paths/invoke",
            "https://evil.test/triggers/manual/paths/invoke",
            "https://environment.api.powerplatform.com.evil.test/triggers/manual/paths/invoke",
            "https://user:password@x.environment.api.powerplatform.com/triggers/manual/paths/invoke",
            "https://x.environment.api.powerplatform.com:444/triggers/manual/paths/invoke",
            "https://x.environment.api.powerplatform.com/flows/test",
            " https://x.environment.api.powerplatform.com/triggers/manual/paths/invoke",
        ):
            with self.subTest(candidate=candidate), self.assertRaises(probe.OAuthProbeError):
                probe.check_callback_url(candidate)

    def test_synthetic_body_uses_only_contract_fields(self):
        body = probe.synthetic_body()
        expected = json.loads(
            (ROOT / "integrations/ios-shortcuts/expense-draft.schema.json").read_text("utf-8")
        )
        self.assertEqual(set(body), set(expected["properties"]))
        self.assertTrue(body["requestId"].startswith("FM11-TEST-"))
        self.assertEqual(body["description"], "SINTETICO - NO REGISTRAR")
        self.assertEqual(body["amountPen"], "12.50")

    def test_probe_mode_never_requests_tokens(self):
        with patch.object(probe, "device_code", return_value={
            "device_code": "private", "user_code": "temporary",
        }), patch.object(probe, "get_token", side_effect=AssertionError("must not poll")):
            f = io.StringIO()
            with redirect_stdout(f):
                result = probe.main(["--probe"])
        self.assertEqual(result, 0)
        self.assertIn("FM11_ENTRA_DEVICE_CODE_ACCEPTED=true", f.getvalue())
        self.assertNotIn("private", f.getvalue())
        self.assertNotIn("temporary", f.getvalue())

    def test_token_is_not_echoed_or_sent_to_cloud_implicitly(self):
        claims = self.claims()
        with patch.object(probe, "device_code", return_value={
            "device_code": "device-secret", "user_code": "user-temp",
        }), patch.object(probe, "get_token", return_value=fake_jwt(claims)), \
                patch.object(probe, "post_synthetic", side_effect=AssertionError("unexpected network call")):
            output = io.StringIO()
            with redirect_stdout(output):
                result = probe.main([])
        self.assertEqual(result, 0)
        for secret in ("device-secret", "user-temp", fake_jwt(claims)):
            self.assertNotIn(secret, output.getvalue())
        self.assertIn("FM11_DELEGATED_TOKEN_VALID=true", output.getvalue())

    def test_error_diagnostics_do_not_echo_microsoft_details(self):
        sensitive = {
            "error": "invalid_scope",
            "error_description": "AADSTS70011: sensitive raw metadata do not log",
        }
        self.assertEqual(probe.diagnostic_error(sensitive), "invalid_scope (AADSTS70011)")


if __name__ == "__main__":
    unittest.main()
