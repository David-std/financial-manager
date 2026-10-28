"""FM-11 delegated Microsoft Entra sign-in probe (stdlib only; no app or server).

Execute locally on macOS/Windows, after verifying the client registration.
The script never prints, writes or caches access/refresh tokens or a flow URL.
Only --send-http-test sends a synthetic body to an explicitly supplied URL.
"""
from __future__ import annotations

import argparse
import base64
import datetime as dt
import getpass
import json
import os
import re
import secrets
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

TENANT_ID = "0128a3bb-db2d-49e9-b4e2-27dcbd1423cc"
CLIENT_ID = "d6ad936e-45ae-4d21-b04f-d04397f0bc2b"
FLOW_AUDIENCE = "https://service.flow.microsoft.com/"
# The resource URI *ends* in a slash: one more is required before .default.
FLOW_SCOPE = "https://service.flow.microsoft.com//.default"
DEVICE_CODE_URL = f"https://login.microsoftonline.com/{TENANT_ID}/oauth2/v2.0/devicecode"
TOKEN_URL = f"https://login.microsoftonline.com/{TENANT_ID}/oauth2/v2.0/token"
LOGIN_SCOPE = FLOW_SCOPE + " offline_access"
DEV_ENVIRONMENT_HOST = "d5a49e040363e762ba82c4ac8c91c6f0.environment.api.powerplatform.com"


class OAuthProbeError(Exception):
    """Sanitized error; never include a token, device code or callback URL."""


def post_form(url: str, values: dict[str, str]) -> dict:
    body = urllib.parse.urlencode(values).encode("ascii")
    req = urllib.request.Request(
        url, data=body, method="POST",
        headers={"Content-Type": "application/x-www-form-urlencoded"},
    )
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            return json.loads(response.read(128 * 1024).decode("utf-8"))
    except urllib.error.HTTPError as exc:
        try:
            payload = json.loads(exc.read(16 * 1024).decode("utf-8"))
        except (ValueError, UnicodeDecodeError):
            payload = {}
        return {
            "error": payload.get("error", "http_error"),
            "error_description": payload.get("error_description", ""),
            "_http_status": exc.code,
        }
    except (OSError, ValueError) as exc:
        raise OAuthProbeError("No se pudo contactar con Microsoft Entra.") from exc


def device_code() -> dict:
    info = post_form(DEVICE_CODE_URL, {
        "client_id": CLIENT_ID, "scope": LOGIN_SCOPE,
    })
    if "device_code" not in info or "user_code" not in info:
        raise OAuthProbeError(
            "Entra no emitió código de dispositivo. " + diagnostic_error(info)
        )
    return info


def diagnostic_error(result: dict) -> str:
    # Never log raw Microsoft error strings: they may include request metadata.
    description = str(result.get("error_description", ""))
    code = re.search(r"AADSTS\d+", description)
    label = str(result.get("error", "unknown")).strip()
    if label not in {
        "authorization_pending", "slow_down", "authorization_declined",
        "expired_token", "invalid_grant", "invalid_client", "invalid_scope",
        "interaction_required", "consent_required", "unauthorized_client",
        "http_error", "unknown"
    }:
        label = "other"
    return f"{label}" + (f" ({code.group(0)})" if code else "")


def decode_claims(token: str) -> dict:
    """Diagnostic-only JWT payload decoding, NOT signature verification.

    The token must first originate from the official HTTPS Entra token endpoint.
    Signature/issuer validation of *incoming* requests belongs to Microsoft.
    """
    try:
        segment = token.split(".")[1]
        padded = segment + ("=" * (-len(segment) % 4))
        claims = json.loads(base64.urlsafe_b64decode(padded).decode("utf-8"))
    except (IndexError, ValueError, UnicodeDecodeError) as exc:
        raise OAuthProbeError("Entra devolvió un token con formato inesperado.") from exc
    if not isinstance(claims, dict):
        raise OAuthProbeError("El token no contiene declaraciones válidas.")
    return claims


def validate_claims(claims: dict) -> dict[str, bool]:
    aud = claims.get("aud") == FLOW_AUDIENCE
    tenant = str(claims.get("tid", "")).lower() == TENANT_ID
    user = bool(claims.get("oid"))
    delegated = bool(claims.get("scp"))
    client = claims.get("azp", claims.get("appid")) == CLIENT_ID
    valid_issuers = {
        f"https://sts.windows.net/{TENANT_ID}/",
        f"https://login.microsoftonline.com/{TENANT_ID}/v2.0",
    }
    issuer = claims.get("iss") in valid_issuers
    expected_oid = os.getenv("FM11_EXPECTED_USER_OID", "").strip().lower()
    matching_user = not expected_oid or str(claims.get("oid", "")).lower() == expected_oid
    checks = {
        "audience": aud, "tenant": tenant, "issuer": issuer,
        "user_oid": user, "delegated_scope": delegated,
        "client_id": client, "expected_user": matching_user,
    }
    if not all(checks.values()):
        raise OAuthProbeError(
            "Token rechazado: " + ", ".join(k for k, v in checks.items() if not v)
        )
    return checks


def get_token(info: dict) -> str:
    print("Visita la dirección oficial de Microsoft y escribe el código mostrado:")
    print(info.get("verification_uri", "https://microsoft.com/devicelogin"))
    print("Código temporal:", info["user_code"])
    print("Regresa aquí después de iniciar sesión; no compartas tokens ni contraseñas.")
    interval = max(5, int(info.get("interval", 5)))
    deadline = time.monotonic() + min(900, int(info.get("expires_in", 900)))
    while time.monotonic() < deadline:
        time.sleep(interval)
        result = post_form(TOKEN_URL, {
            "client_id": CLIENT_ID,
            "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
            "device_code": info["device_code"],
        })
        if isinstance(result.get("access_token"), str):
            return result["access_token"]
        err = result.get("error")
        if err == "authorization_pending":
            continue
        if err == "slow_down":
            interval += 5
            continue
        raise OAuthProbeError(
            "No se pudo completar la autorización: " + diagnostic_error(result)
        )
    raise OAuthProbeError("El código caducó antes de autorizarse.")


def check_callback_url(url: str) -> None:
    parts = urllib.parse.urlsplit(url.strip())
    host = (parts.hostname or "").lower()
    # Exact known DEV host: a random Power Platform tenant can also have a
    # Microsoft-owned host and should never receive our bearer token.
    allowed = host == DEV_ENVIRONMENT_HOST
    if (
        parts.scheme != "https" or not allowed or
        parts.username or parts.password or parts.fragment or
        parts.port not in (None, 443) or
        "/triggers/manual/paths/invoke" not in parts.path
    ):
        raise OAuthProbeError("La URL no corresponde a un callback HTTP admitido.")
    if url != url.strip():
        raise OAuthProbeError("La URL no debe contener espacios adicionales.")


def synthetic_body() -> dict[str, str]:
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


def post_synthetic(token: str) -> int:
    # Paste locally: callback URLs may contain a SAS signature, so neither
    # command-line args nor the repository should contain the URL.
    endpoint = getpass.getpass("Pega la URL HTTP privada del flujo de prueba: ")
    check_callback_url(endpoint)
    print("Solo es seguro usar un flujo verificado con una única respuesta 503, sin escrituras.")
    choice = input("Escribe PRUEBA503 para enviar el JSON sintético: ").strip()
    if choice != "PRUEBA503":
        print("Prueba cancelada. No se enviaron datos.")
        return 0
    req = urllib.request.Request(
        endpoint, data=json.dumps(synthetic_body()).encode("utf-8"),
        method="POST",
        headers={
            "Authorization": "Bearer " + token,
            "Content-Type": "application/json",
            "Accept": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            status = response.status
            response.read(4096)  # drain but don't print any workflow output
    except urllib.error.HTTPError as exc:
        status = exc.code
        exc.close()
    except OSError as exc:
        raise OAuthProbeError("No se pudo conectar con Power Automate.") from exc
    print("Código HTTP:", status)
    if status == 503:
        print("Petición alcanzó la respuesta segura 503 del flujo de prueba.")
        return 0
    if status in (401, 403):
        print("Solicitud no autorizada; comprueba token, usuario y permisos.")
        return 3
    print("Respuesta inesperada; revisar flujo antes de cualquier nuevo intento.")
    return 4


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--probe", action="store_true",
                        help="Solo verifica el inicio de device-code; sin iniciar sesión")
    parser.add_argument("--send-http-test", action="store_true",
                        help="Tras autenticarse, probar exclusivamente el flujo inerte 503")
    args = parser.parse_args(argv)
    try:
        info = device_code()
        print("FM11_ENTRA_DEVICE_CODE_ACCEPTED=true")
        if args.probe:
            print("No se obtuvo ni guardó ningún token.")
            return 0
        token = get_token(info)
        checks = validate_claims(decode_claims(token))
        print("FM11_DELEGATED_TOKEN_VALID=true")
        for check, passed in checks.items():
            print("  " + check + ": " + ("OK" if passed else "REJECTED"))
        if args.send_http_test:
            return post_synthetic(token)
        print("Autenticación comprobada. No se llamó al flujo HTTP.")
        return 0
    except (OAuthProbeError, KeyboardInterrupt) as exc:
        print("FM11_AUTH_FAILED:", str(exc) if not isinstance(exc, KeyboardInterrupt) else "Cancelado.")
        return 2


if __name__ == "__main__":
    sys.exit(main())
