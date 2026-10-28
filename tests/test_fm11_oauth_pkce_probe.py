"""Offline PKCE loopback proof tests: no Microsoft auth or cloud mutation."""
import base64
import hashlib
import importlib.util
import json
import threading
import time
import unittest
from pathlib import Path
from urllib.parse import urlsplit, parse_qs, urlencode
from urllib.request import urlopen
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / "integrations/power-automate/fm11_oauth_pkce_probe.py"
spec = importlib.util.spec_from_file_location("fm11_oauth_pkce_probe", PATH)
pkce = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pkce)


class Fm11PkceTests(unittest.TestCase):
    def test_s256_verifier_and_dynamic_loopback_redirect(self):
        verifier, challenge, state = pkce.proof_values()
        self.assertEqual(
            challenge,
            base64.urlsafe_b64encode(
                hashlib.sha256(verifier.encode("ascii")).digest()
            ).decode("ascii").rstrip("="),
        )
        self.assertGreaterEqual(len(verifier), 43)
        self.assertEqual(pkce.REGISTERED_REDIRECT, "http://localhost/fm11-pkce")
        self.assertEqual(pkce.LOOPBACK_HOST, "127.0.0.1")
        self.assertEqual(pkce.CALLBACK_PATH, "/fm11-pkce")

    def test_auth_url_is_pkce_and_same_tenant_and_no_secret(self):
        _, challenge, state = pkce.proof_values()
        redirect = "http://localhost:35001/fm11-pkce"
        result = pkce.authorization_url(challenge, state, redirect)
        parsed = urlsplit(result)
        fields = parse_qs(parsed.query)
        self.assertEqual(parsed.hostname, "login.microsoftonline.com")
        self.assertEqual(fields["redirect_uri"], [redirect])
        self.assertEqual(fields["client_id"], [pkce.CLIENT])
        self.assertEqual(fields["code_challenge"], [challenge])
        self.assertEqual(fields["code_challenge_method"], ["S256"])
        self.assertEqual(fields["response_type"], ["code"])
        self.assertEqual(fields["state"], [state])
        self.assertEqual(fields["scope"], [pkce.SCOPE])
        self.assertNotIn("client_secret", result)

    def test_callback_rejects_wrong_state_path_and_errors(self):
        state = "expected_State"
        self.assertEqual(
            pkce.parse_callback("/fm11-pkce?code=temp123&state=expected_State", state),
            "temp123",
        )
        for value in (
            "/fm11-pkce?state=" + state,
            "/fm11-pkce?code=two&state=bad",
            "/other?code=two&state=" + state,
            "/fm11-pkce?code=a&code=b&state=" + state,
            "/fm11-pkce?error=access_denied&state=" + state,
            "https://evil.example/fm11-pkce?code=a&state=" + state,
        ):
            with self.subTest(value=value), self.assertRaises(pkce.ProofFailure):
                pkce.parse_callback(value, state)

    def test_loopback_receiver_uses_ephemeral_port_and_private_result(self):
        state = "our_state"
        challenge = "a" * 43
        threads = []
        observed = {}

        def mock_open(url):
            params = parse_qs(urlsplit(url).query)
            redirect = params["redirect_uri"][0]
            observed["redirect"] = redirect
            observed["login"] = url

            def send():
                callback = redirect + "?" + urlencode({
                    "code": "PRIVATE_ONE_TIME_CODE", "state": state,
                })
                with urlopen(callback, timeout=4) as response:
                    observed["status"] = response.status
                    observed["text"] = response.read().decode()

            thread = threading.Thread(target=send, daemon=True)
            threads.append(thread)
            thread.start()
            return True

        code, redirect = pkce.receive_code(
            state, challenge, open_browser=mock_open, max_seconds=5)
        for thread in threads:
            thread.join(3)
        self.assertEqual(code, "PRIVATE_ONE_TIME_CODE")
        self.assertEqual(redirect, observed["redirect"])
        self.assertEqual(observed["status"], 200)
        self.assertNotIn("PRIVATE_ONE_TIME_CODE", observed["text"])
        self.assertEqual(urlsplit(redirect).hostname, "localhost")
        self.assertGreater(urlsplit(redirect).port, 0)
        self.assertEqual(urlsplit(redirect).path, "/fm11-pkce")
        self.assertNotIn("PRIVATE_ONE_TIME_CODE", observed["login"])

    def test_claims_are_rejected_if_wrong_audience_or_client(self):
        claims = {
            "aud": pkce.AUDIENCE, "tid": pkce.TENANT,
            "iss": f"https://login.microsoftonline.com/{pkce.TENANT}/v2.0",
            "oid": "user-oid", "scp": "Flows.Read.All",
            "azp": pkce.CLIENT,
        }
        def token(c):
            payload = base64.urlsafe_b64encode(json.dumps(c).encode())
            return "e30." + payload.decode().rstrip("=") + ".sig"
        self.assertTrue(all(pkce.inspect_token(token(claims)).values()))
        for k, v in (
            ("aud", "https://graph.microsoft.com"),
            ("tid", "wrong"), ("iss", "https://evil.test"),
            ("oid", ""), ("scp", ""), ("azp", "wrong-client"),
        ):
            with self.subTest(key=k):
                self.assertFalse(all(pkce.inspect_token(
                    token({**claims, k: v})
                ).values()))

    def test_only_exact_dev_flow_url_is_accepted(self):
        guid_compact = pkce.DEV_HTTP_RUNTIME_ID
        base = "https://" + pkce.DEV_FLOW_HOST + ":443/powerautomate/automations/direct"
        suffix = "/triggers/manual/paths/invoke"
        good = (base + "/cu/20/workflows/" + guid_compact +
                suffix + "/?api-version=1&tenantId=dummy")
        self.assertEqual(
            pkce.DEV_FLOW_HOST,
            "d5a49e040363e762ba82c4ac8c91c6.f0.environment.api.powerplatform.com"
        )
        for candidate in (
            good, good.replace(suffix + "/", suffix),
            base + "/workflows/" + pkce.DEV_HTTP_RUNTIME_ID + suffix + "?api-version=1",
            base + "/cu/2/workflows/" + guid_compact + suffix + "?api-version=1",
        ):
            with self.subTest(allowed=candidate):
                pkce.validate_test_url(candidate)

        bad = [
            good.replace("https://", "http://"),
            good.replace(pkce.DEV_FLOW_HOST,
                         "d5a49e040363e762ba82c4ac8c91c6f0.environment.api.powerplatform.com"),
            good.replace(pkce.DEV_FLOW_HOST, "other.environment.api.powerplatform.com"),
            good.replace(pkce.DEV_FLOW_HOST, pkce.DEV_FLOW_HOST + ".evil.invalid"),
            good.replace(guid_compact, "00000000000000000000000000000000"),
            good.replace("/cu/20/", "/cu/x/"),
            good.replace("/cu/20/", "/cu/20/extra/"),
            good.replace(suffix + "/", "/triggers/manual/paths/invoke/extra/"),
            good.split("?")[0],
            good + "#fragment",
            good.replace(":443", ":444"),
        ]
        for value in bad:
            with self.subTest(rejected=value), self.assertRaises(pkce.ProofFailure):
                pkce.validate_test_url(value)

    def test_synthetic_http_request_envelope(self):
        body = pkce.synthetic_request()
        import re
        self.assertRegex(body["requestId"], r"^FM11-TEST-[0-9a-f]{16}$")
        self.assertEqual(body["description"], "SINTETICO - NO REGISTRAR")
        schema = json.loads((ROOT / "integrations/ios-shortcuts/expense-draft.schema.json").read_text("utf-8"))
        self.assertEqual(set(body), set(schema["properties"]))

    def test_http_probe_checks_anonymous_denial_then_exact_inert_response(self):
        url = ("https://" + pkce.DEV_FLOW_HOST +
               "/powerautomate/automations/direct/cu/20/workflows/" +
               pkce.DEV_HTTP_RUNTIME_ID +
               "/triggers/manual/paths/invoke/?api-version=1")
        safe_response = {
            "status": "NOT_CONFIGURED",
            "message": "FM-11 direct intake is disabled pending authenticated deployment.",
        }
        calls = []
        def fake_post(_url, _body, *, token):
            calls.append(token)
            return (401, None) if token is None else (503, safe_response)
        with patch.object(pkce.getpass, "getpass", return_value=url), \
             patch("builtins.input", return_value="PRUEBA503"), \
             patch.object(pkce, "post_without_redirect", side_effect=fake_post):
            self.assertTrue(pkce.test_inert_http_flow("private-token"))
        self.assertEqual(calls, [None, "private-token"])
        with patch.object(pkce.getpass, "getpass", return_value=url), \
             patch("builtins.input", return_value="PRUEBA503"), \
             patch.object(pkce, "post_without_redirect", return_value=(503, safe_response)):
            with self.assertRaises(pkce.ProofFailure):
                pkce.test_inert_http_flow("private-token")

    def test_remote_smoke_env_inputs_never_require_console_getpass(self):
        import os
        url = ("https://" + pkce.DEV_FLOW_HOST +
               "/powerautomate/automations/direct/cu/10/workflows/" +
               pkce.DEV_HTTP_RUNTIME_ID +
               "/triggers/manual/paths/invoke?api-version=1")
        accepted = {"status": "NOT_CONFIGURED",
                    "message": "FM-11 direct intake is disabled pending authenticated deployment."}
        with patch.dict(os.environ, {
            "FM11_HTTP_TEST_URL": url,
            "FM11_HTTP_TEST_APPROVED": "PRUEBA503",
        }), patch.object(pkce.getpass, "getpass",
                        side_effect=AssertionError("getpass must not run")), \
             patch("builtins.input", side_effect=AssertionError("input must not run")), \
             patch.object(pkce, "post_without_redirect",
                          side_effect=[(401, None), (503, accepted)]) as post:
            self.assertTrue(pkce.test_inert_http_flow("test-token"))
            self.assertEqual(post.call_count, 2)

    def test_preview_never_opens_browser_or_makes_network_calls(self):
        with patch.object(pkce.webbrowser, "open", side_effect=AssertionError), \
                patch.object(pkce, "exchange", side_effect=AssertionError):
            self.assertEqual(pkce.main(["--preview"]), 0)

    def test_documentation_identifies_wrongplace_failure_and_new_redirect(self):
        docs = (ROOT / "integrations/power-automate/README.md").read_text("utf-8")
        self.assertIn("PKCE desktop", docs)
        self.assertIn("401 without", docs)
        self.assertIn("does not constitute", docs)


if __name__ == "__main__":
    unittest.main()
