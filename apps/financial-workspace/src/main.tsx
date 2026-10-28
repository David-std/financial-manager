import React, { Suspense, lazy } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./styles.css";

const DataverseIdentityCheck = lazy(() => import("./DataverseIdentityCheck"));
const LiveWorkspace = lazy(() => import("./LiveWorkspace"));
const liveMode =
  import.meta.env.VITE_FM_ENABLE_LIVE_READ === "true" &&
  new URLSearchParams(window.location.search).get("mode") === "live";
const identityCheck =
  import.meta.env.VITE_FM_ENABLE_IDENTITY_CHECK === "true" &&
  new URLSearchParams(window.location.search).get("verify") === "identity";
const root = document.getElementById("root");
if (!root) throw new Error("No root element");
createRoot(root).render(
  <React.StrictMode>
    {liveMode ? (
      <Suspense fallback={<main>Preparando datos conectados…</main>}>
        <LiveWorkspace />
      </Suspense>
    ) : identityCheck ? (
      <Suspense fallback={<main>Preparando comprobación de identidad…</main>}>
        <DataverseIdentityCheck />
      </Suspense>
    ) : (
      <App />
    )}
  </React.StrictMode>,
);
