"""HTTP-only FM-11 predeployment contract: must fail closed, never mutate."""
from __future__ import annotations

import importlib.util
import json
import unittest
from dataclasses import fields
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "integrations/power-automate"


def load(module: str):
    spec = importlib.util.spec_from_file_location(module, BASE / (module + ".py"))
    result = importlib.util.module_from_spec(spec)
    # dataclasses needs a module registered before evaluating annotations
    import sys
    sys.modules[module] = result
    spec.loader.exec_module(result)
    return result


gate = load("fm11_http_gate")
preview = load("build_http_preview")


class HTTPDirectReadinessTests(unittest.TestCase):
    def test_no_default_permission_or_implicit_enablement(self):
        state = gate.GateState()
        self.assertFalse(state.can_enable())
        self.assertEqual(set(state.missing()), set(gate.REQUIRED_GATES))
        self.assertEqual({field.name for field in fields(gate.GateState)},
                         set(gate.REQUIRED_GATES))

    def test_real_dependencies_required_for_enablement(self):
        values = {name: True for name in gate.REQUIRED_GATES}
        self.assertTrue(gate.GateState(**values).can_enable())
        for name in values:
            with self.subTest(missing=name):
                degraded = {**values, name: False}
                self.assertEqual(gate.GateState(**degraded).missing(), (name,))
                self.assertFalse(gate.GateState(**degraded).can_enable())

    def test_transport_rejects_user_claimed_authority(self):
        for key in ("actorId", "userId", "ownerId", "status", "approved",
                    "postingId", "bearer", "access_token", "authorization"):
            with self.subTest(key=key), self.assertRaises(ValueError):
                gate.validate_direct_payload({key: "fake"}, account_scope="LM")
        with self.assertRaises(ValueError):
            gate.validate_direct_payload(None, account_scope="LM")
        with self.assertRaises(ValueError):
            gate.validate_direct_payload({}, account_scope="unknown")
        self.assertEqual(gate.validate_direct_payload(
            {"requestId": "FM11-123456789"}, account_scope="LM"),
            {"requestId": "FM11-123456789"})

    def test_no_success_without_durable_queue_and_every_gate(self):
        with self.assertRaises(RuntimeError):
            gate.success_response("FM11-123456789", gate=gate.GateState(), persisted_intake=True)
        valid = gate.GateState(**{name: True for name in gate.REQUIRED_GATES})
        with self.assertRaises(RuntimeError):
            gate.success_response("FM11-123456789", gate=valid, persisted_intake=False)
        self.assertEqual(gate.success_response("FM11-123456789",
                         gate=valid, persisted_intake=True),
                         {"requestId": "FM11-123456789", "status": "PENDING_REVIEW"})

    def test_insecure_urls_are_rejected(self):
        for url in ("", "http://flow.microsoft.com/x", "https://127.0.0.1/",
                    "https://localhost/", "https://example.test/#token",
                    "https://user:password@example.test/"):
            with self.subTest(url=url), self.assertRaises(ValueError):
                gate.validate_endpoint(url)
        gate.validate_endpoint("https://example.test/test")  # host allowlist is a separate deployment gate

    def test_preview_flow_has_no_financial_mutation_and_only_returns_503(self):
        contract = preview.build()
        self.assertEqual(len(contract["triggers"]), 1)
        trigger = contract["triggers"]["manual"]
        self.assertEqual((trigger["type"], trigger["kind"]), ("Request", "Http"))
        actions = contract["actions"]
        self.assertEqual(set(actions), {"DISABLED_NO_FINANCIAL_WRITES"})
        self.assertEqual(actions["DISABLED_NO_FINANCIAL_WRITES"]["type"], "Response")
        self.assertEqual(actions["DISABLED_NO_FINANCIAL_WRITES"]["kind"], "Http")
        self.assertEqual(actions["DISABLED_NO_FINANCIAL_WRITES"]["inputs"]["statusCode"], 503)
        self.assertNotIn("OpenApiConnection", json.dumps(contract))
        self.assertNotIn("Add a new row", json.dumps(contract))
        self.assertNotIn("Authorization", json.dumps(contract))
        generated = json.loads(preview.DEST.read_text(encoding="utf-8"))
        self.assertEqual(generated, contract)
        schema = json.loads((ROOT / "integrations/ios-shortcuts/expense-draft.schema.json").read_text(encoding="utf-8"))
        self.assertEqual(trigger["inputs"]["schema"]["required"], schema["required"])

    def test_live_deployment_source_keeps_flow_off_and_inert(self):
        source = (ROOT / "tools/DataverseDeployment/Fm11HttpTestFlow.cs").read_text(encoding="utf-8")
        program = (ROOT / "tools/DataverseDeployment/Program.cs").read_text(encoding="utf-8")
        self.assertIn('FM11 - HTTP Intake Test (disabled)', source)
        self.assertIn('SolutionUniqueName = SolutionName', source)
        self.assertIn('ComponentType = 29', source)
        self.assertIn('statecode', source)
        self.assertIn('!= 0', source)
        self.assertIn('statusCode', source)
        # The test-only state toggle is guarded by inert response + one user.
        self.assertIn("SetInertTestState", source)
        self.assertIn("new SetStateRequest", source)
        self.assertIn("onlyExpected", source)
        self.assertIn('response?["inputs"]?["statusCode"]?.GetValue<int>() == 503', source)
        self.assertIn('http?["inputs"]?["triggerAuthenticationType"]?.GetValue<string>() == "User"', source)
        self.assertIn('mode == "fm11-http-test-on"', program)
        self.assertIn('mode == "fm11-http-test-off"', program)
        self.assertNotIn('"fmg_financialtransaction"', source)
        self.assertIn('mode == "fm11-http-test-create"', program)
        self.assertIn('Mutation requires explicit --apply.', program)

    def test_sdk_inspection_requires_one_allowed_user_and_inert_action(self):
        source = (ROOT / "tools/DataverseDeployment/Fm11HttpTestFlow.cs").read_text(
            encoding="utf-8")
        program = (ROOT / "tools/DataverseDeployment/Program.cs").read_text(
            encoding="utf-8")
        self.assertIn("InspectTriggerSecurity", source)
        self.assertIn('authMode == "User" && onlyIntendedUser', source)
        self.assertIn("FM11_EXPECTED_ALLOWED_USER", source)
        self.assertNotIn("DavidMorales@company717.onmicrosoft.com", source)
        self.assertIn('actions["DISABLED_NO_FINANCIAL_WRITES"]', source)
        self.assertIn('GetValue<int>() == 503', source)
        self.assertIn('FM11_TRIGGER_OAUTH_RESTRICTION_VERIFIED=', source)
        self.assertIn('mode == "fm11-http-auth-inspect"', program)

    def test_current_guidance_does_not_claim_live_flow(self):
        doc = (BASE / "README.md").read_text(encoding="utf-8")
        for required in ("restricted HTTP", "503",
                         "FM11 - HTTP Intake Test (disabled)", "PKCE", "Outlook"):
            self.assertIn(required.casefold(), doc.casefold())


if __name__ == "__main__":
    unittest.main()
