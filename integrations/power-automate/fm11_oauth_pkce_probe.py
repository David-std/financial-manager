"""FM-11 OAuth Authorization Code + PKCE desktop diagnostic (loopback-only).

No external server, browser extension, extra application, client secret, token
cache, cloud-flow activation, or financial write. Uses one ephemeral loopback
HTTP listener during login, then shuts it down.
Requires Entra Mobile/Desktop redirect: http://localhost/fm11-pkce
This is *not* the final iOS Shortcut authentication implementation.
"""
from __future__ import annotations

import argparse
import base64
import datetime as dt
import getpass
import hashlib
from http.server import BaseHTTPRequestHandler, HTTPServer
import json
import os
import re
import secrets
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import webbrowser

TENANT = "0128a3bb-db2d-49e9-b4e2-27dcbd1423cc"
CLIENT = "d6ad936e-45ae-4d21-b04f-d04397f0bc2b"
AUDIENCE = "https://service.flow.microsoft.com/"
# Exact callback host shown by Power Automate DEV, including the platform shard.
# The shard is separated by a dot; it is not part of the GUID hostname.
DEV_FLOW_HOST = "d5a49e040363e762ba82c4ac8c91c6.f0.environment.api.powerplatform.com"
DEV_TEST_FLOW = "6b7eca28-51d3-f111-b1ec-70a8a5aca0ae"
# Distinct HTTP runtime workflow ID read from the exact FM11 designer URL.
# This is an endpoint identifier, not a secret or the Dataverse workflow ID.
DEV_HTTP_RUNTIME_ID = "7515a307f6674c42bc0cfcdce6b9de84"
SCOPE = "https://service.flow.microsoft.com//.default"
AUTHORIZE = f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/authorize"
TOKEN = f"https://login.microsoftonline.com/{TENANT}/oauth2/v2.0/token"
LOOPBACK_HOST = "127.0.0.1"
REDIRECT_HOST = "localhost"
CALLBACK_PATH = "/fm11-pkce"
REGISTERED_REDIRECT = "http://localhost/fm11-pkce"


class ProofFailure(Exception):
    """Sanitized errors only; no authorization codes or tokens."""


def proof_values() -> tuple[str, str, str]:
    verifier = secrets.token_urlsafe(64)
    challenge = base64.urlsafe_b64encode(
        hashlib.sha256(verifier.encode("ascii")).digest()
    ).decode("ascii").rstrip("=")
    state = secrets.token_urlsafe(24)
    return verifier, challenge, state


def authorization_url(challenge: str, state: str, redirect_uri: str) -> str:
    params = {
        "client_id": CLIENT,
        "response_type": "code",
        "redirect_uri": redirect_uri,
        "response_mode": "query",
        "scope": SCOPE,
        "code_challenge": challenge,
        "code_challenge_method": "S256",
        "state": state,
        "prompt": "select_account",
    }
    return AUTHORIZE + "?" + urllib.parse.urlencode(params)


def parse_callback(path_and_query: str, expected_state: str) -> str:
    """Only local server uses this; never print path_and_query or code."""
    parsed = urllib.parse.urlsplit(path_and_query)
    if (parsed.path != CALLBACK_PATH or parsed.fragment or parsed.scheme
            or parsed.netloc):
        raise ProofFailure("Ruta de devolución inesperada.")
    fields = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
    if "error" in fields:
        raw = str(fields["error"][0])
        safe = raw if re.fullmatch(r"[a-z_]+", raw) else "error"
        raise ProofFailure("Entra rechazó el inicio de sesión: " + safe)
    if fields.get("state") != [expected_state]:
        raise ProofFailure("La respuesta OAuth no corresponde a esta sesión.")
    codes = fields.get("code")
    if not codes or len(codes) != 1 or not codes[0]:
        raise ProofFailure("No se recibió un código de autorización.")
    return codes[0]


def receive_code(expected_state: str, challenge: str,
                 *, open_browser=webbrowser.open, max_seconds: int = 180) -> tuple[str, str]:
    """Run a one-time server bound exclusively to 127.0.0.1, never LAN/public."""
    result: dict[str, str] = {}
    server_port = 0

    class OAuthCallback(BaseHTTPRequestHandler):
        def do_GET(self) -> None:
            # Verify Host to avoid processing unrelated local/DNS-rebinding requests.
            expected_host = f"{REDIRECT_HOST}:{server_port}"
            if self.headers.get("Host", "").lower() != expected_host:
                self.send_error(400, "Host not permitted")
                return
            try:
                code = parse_callback(self.path, expected_state)
                result["code"] = code
                message = b"Microsoft sign-in received. Return to the terminal.\n"
                status = 200
            except ProofFailure as exc:
                result["error"] = str(exc)
                message = b"Microsoft sign-in was not accepted. Return to the terminal.\n"
                status = 400
            self.send_response(status)
            self.send_header("Content-Type", "text/plain; charset=utf-8")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Referrer-Policy", "no-referrer")
            self.send_header("Content-Security-Policy", "default-src 'none'")
            self.send_header("Content-Length", str(len(message)))
            self.end_headers()
            self.wfile.write(message)

        def log_message(self, format: str, *args: object) -> None:
            # Default BaseHTTPRequestHandler logs the URL including OAuth code.
            return

    with HTTPServer((LOOPBACK_HOST, 0), OAuthCallback) as server:
        server_port = server.server_address[1]
        server.timeout = 1
        redirect = f"http://{REDIRECT_HOST}:{server_port}{CALLBACK_PATH}"
        login_url = authorization_url(challenge, expected_state, redirect)
        print("Abriendo el inicio de sesión oficial de Microsoft en tu navegador...")
        print("Al terminar, el navegador volverá automáticamente a esta computadora.")
        print("No compartas URLs, contraseñas ni códigos. Este proceso no crea gastos.")
        if not open_browser(login_url):
            raise ProofFailure("No se pudo abrir el navegador del sistema.")
        until = time.monotonic() + max_seconds
        while time.monotonic() < until and not result:
            server.handle_request()
    if "error" in result:
        raise ProofFailure(result["error"])
    if "code" not in result:
        raise ProofFailure("No se recibió la redirección antes de caducar.")
    return result["code"], redirect


def exchange(code: str, verifier: str, redirect_uri: str) -> dict:
    values = {
        "client_id": CLIENT,
        "grant_type": "authorization_code",
        "code": code,
        "redirect_uri": redirect_uri,
        "code_verifier": verifier,
        "scope": SCOPE,
    }
    request = urllib.request.Request(
        TOKEN, data=urllib.parse.urlencode(values).encode("ascii"),
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=25) as response:
            return json.loads(response.read(200_000))
    except urllib.error.HTTPError as exc:
        try:
            response = json.loads(exc.read(8_192))
        except (ValueError, UnicodeDecodeError):
            response = {}
        aadsts = re.search(r"AADSTS\d+",
                           str(response.get("error_description", "")))
        label = aadsts.group(0) if aadsts else "OAuthHTTP" + str(exc.code)
        raise ProofFailure("Error de autorización Microsoft: " + label) from None
    except (OSError, ValueError) as exc:
        raise ProofFailure("La respuesta del servicio de identidad no es válida.") from exc


def inspect_token(token: str) -> dict[str, bool]:
    """Inspect trusted HTTPS token-endpoint response; does not verify JWT signature."""
    try:
        payload = token.split(".")[1]
        claims = json.loads(base64.urlsafe_b64decode(
            payload + "=" * (-len(payload) % 4)))
    except (ValueError, IndexError) as exc:
        raise ProofFailure("Formato del token inesperado.") from exc
    return {
        "audience": claims.get("aud") == AUDIENCE,
        "tenant": str(claims.get("tid", "")).lower() == TENANT,
        "issuer": claims.get("iss") in {
            f"https://login.microsoftonline.com/{TENANT}/v2.0",
            f"https://sts.windows.net/{TENANT}/",
        },
        "delegated_user": bool(claims.get("oid") and claims.get("scp")),
        "application": claims.get("azp", claims.get("appid")) == CLIENT,
    }


def validate_test_url(url: str) -> None:
    """Never send a delegated token outside the exact FM11 DEV test callback."""
    if not url or url != url.strip():
        raise ProofFailure("URL del flujo vacía o con espacios.")
    parsed = urllib.parse.urlsplit(url)
    if (parsed.scheme != "https" or parsed.hostname != DEV_FLOW_HOST or
            parsed.username or parsed.password or parsed.fragment or
            parsed.port not in (None, 443)):
        raise ProofFailure("La URL no corresponde al entorno DEV autorizado.")
    # The 2026 Power Automate callback includes the environment prefix and
    # may contain /cu/<scale-unit> and a trailing slash after /invoke.
    # Workflow IDs are emitted as either 32 hexadecimal digits or dashed GUIDs.
    path = parsed.path.lower()
    prefix = r"/powerautomate/automations/direct(?:/cu/[1-9][0-9]*)?"
    guid = r"([0-9a-f]{32}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})"
    match = re.fullmatch(
        prefix + r"/workflows/" + guid + r"/triggers/manual/paths/invoke/?",
        path,
    )
    if not match or match.group(1).replace("-", "") != DEV_HTTP_RUNTIME_ID:
        raise ProofFailure("La URL no corresponde al flujo de prueba FM-11.")
    if not parsed.query:
        raise ProofFailure("La URL debe ser el callback completo del desencadenador.")


def synthetic_request() -> dict[str, str]:
    return {
        "requestId": "FM11-TEST-" + secrets.token_hex(8),
        "name": "Prueba de transporte",
        "amountPen": "12.50",
        "date": dt.date.today().isoformat(),
        "categoryCode": "101",
        "branchCode": "LM",
        "accountName": "Caja La Merced",
        "flowClass": "operativo",
        "description": "SINTETICO - NO REGISTRAR",
    }


def post_without_redirect(url: str, payload: dict[str, str],
                          *, token: str | None) -> tuple[int, dict | None]:
    """No redirects: never forward a private callback or bearer token."""
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, req, fp, code, msg, headers, newurl):
            return None

    headers = {"Content-Type": "application/json", "Accept": "application/json"}
    if token is not None:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(
        url, method="POST", headers=headers,
        data=json.dumps(payload, separators=(",", ":")).encode("utf-8"),
    )
    opener = urllib.request.build_opener(NoRedirect)
    try:
        with opener.open(req, timeout=30) as response:
            status = response.status
            raw = response.read(4096)
    except urllib.error.HTTPError as exc:
        status = exc.code
        raw = exc.read(4096)
        exc.close()
    except OSError as exc:
        raise ProofFailure("No fue posible conectar con el flujo DEV.") from exc
    try:
        body = json.loads(raw.decode("utf-8")) if raw else None
    except (ValueError, UnicodeDecodeError):
        body = None
    return status, body if isinstance(body, dict) else None


def test_inert_http_flow(token: str) -> bool:
    # Remote controlled DEV smoke tests can provide a process-local environment
    # variable to avoid getpass() blocking on a noninteractive Windows console.
    # Never print or persist the private callback URL.
    url = os.getenv("FM11_HTTP_TEST_URL") or getpass.getpass(
        "URL HTTP privada del flujo de prueba (entrada oculta): "
    )
    validate_test_url(url)
    print("Solo se probará el flujo FM11 de DEV con respuesta 503 y sin gastos.")
    approval = os.getenv("FM11_HTTP_TEST_APPROVED")
    if approval is None:
        approval = input("Escribe PRUEBA503 para realizar una prueba sintética: ")
    if approval.strip() != "PRUEBA503":
        print("Prueba cancelada, sin solicitud.")
        return False
    payload = synthetic_request()
    anonymous_status, anonymous_body = post_without_redirect(url, payload, token=None)
    print("HTTP sin autenticación:", anonymous_status)
    if anonymous_status not in (401, 403):
        raise ProofFailure("SEGURIDAD: entrada anónima no rechazada; prueba detenida.")
    if anonymous_body and anonymous_body.get("status") == "NOT_CONFIGURED":
        raise ProofFailure("SEGURIDAD: respuesta interna accesible sin token.")
    status, body = post_without_redirect(url, payload, token=token)
    print("HTTP con OAuth:", status)
    if (status == 503 and body and
            body.get("status") == "NOT_CONFIGURED" and
            body.get("message") == "FM-11 direct intake is disabled pending authenticated deployment."):
        print("FM11_AUTHENTICATED_INERT_503_CONFIRMED=true")
        return True
    if status in (401, 403):
        raise ProofFailure("El token fue rechazado por el desencadenador.")
    raise ProofFailure("El flujo no devolvió exactamente el marcador seguro esperado.")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true",
                        help="No browser or network; show configuration only")
    parser.add_argument("--send-http-test", action="store_true",
                        help="Require a private DEV URL, then test rejection without OAuth and inert 503 with OAuth")
    args = parser.parse_args(argv)
    try:
        verifier, challenge, state = proof_values()
        if args.preview:
            print("FM11_PKCE_LOOPBACK_PREVIEW=true")
            print("Register Mobile/Desktop redirect:", REGISTERED_REDIRECT)
            print("Only local 127.0.0.1 listener, no hosted server.")
            return 0
        code, redirect = receive_code(state, challenge)
        result = exchange(code, verifier, redirect)
        access = result.get("access_token")
        if not isinstance(access, str):
            raise ProofFailure("Microsoft no emitió un access token.")
        checks = inspect_token(access)
        for key, ok in checks.items():
            print(f"{key}: {'OK' if ok else 'RECHAZADO'}")
        if not all(checks.values()):
            raise ProofFailure("El token no coincide con la audiencia o identidad esperada.")
        print("FM11_PKCE_DELEGATED_TOKEN_VALID=true")
        if args.send_http_test:
            return 0 if test_inert_http_flow(access) else 2
        print("Sin llamada HTTP a Power Automate ni almacenamiento de tokens.")
        return 0
    except (ProofFailure, KeyboardInterrupt, OSError) as exc:
        message = "Cancelado" if isinstance(exc, KeyboardInterrupt) else (
            str(exc) if isinstance(exc, ProofFailure)
            else "No fue posible iniciar el receptor local.")
        print("FM11_PKCE_FAILED:", message)
        return 2


if __name__ == "__main__":
    sys.exit(main())
