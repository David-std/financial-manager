import { useState } from "react";
import { verifyDelegatedDataverseIdentity } from "./infrastructure/data/identity-check";

/** UI is entirely separate from the synthetic financial workspace. */
export default function DataverseIdentityCheck() {
  const [status, setStatus] = useState(
    "Sin sesión: esta comprobación no concede permisos ni habilita operaciones financieras.",
  );
  const [busy, setBusy] = useState(false);
  const [verified, setVerified] = useState(false);

  async function verify() {
    setBusy(true);
    setVerified(false);
    setStatus("Autenticando mediante Microsoft Entra...");
    try {
      await verifyDelegatedDataverseIdentity();
      setStatus(
        "WhoAmI correcto. Se verificó identidad de Dataverse y organización. " +
          "Esta prueba NO certifica roles, permisos a filas ni operaciones de caja.",
      );
      setVerified(true);
    } catch (e) {
      setStatus(e instanceof Error ? e.message : "No se pudo verificar la identidad.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <main style={{ maxWidth: 640, margin: "5rem auto", padding: "1rem", fontFamily: "system-ui" }}>
      <h1>Comprobación F4 · identidad de Dataverse</h1>
      <p>
        Esta pantalla es independiente del Workspace DEMO y únicamente prueba Microsoft Entra y la
        lectura de WhoAmI. No habilita Post, Void ni aprobaciones.
      </p>
      <button type="button" onClick={verify} disabled={busy}>
        {busy ? "Verificando..." : "Iniciar sesión y verificar WhoAmI"}
      </button>
      <p role="status" aria-live="polite" style={{ marginTop: "1rem" }}>
        {verified ? "✓ " : ""}
        {status}
      </p>
      <a href="/">Volver al modo demostración</a>
    </main>
  );
}
