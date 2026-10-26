# ADR 0006 — Borradores concurrentes y frontera de autorización

Fecha: 2026-10-25
Estado: Contrato implementado en la app local; despliegue de escritura autenticada pendiente

## Contexto

Dataverse es el único libro de registro. La página real `Detalle del gasto` ya comprueba categoría, cuenta, estado y relaciones, y relee los datos antes y después de guardar. Sin embargo, una segunda sesión puede modificar el mismo registro **entre** esa lectura y `dataApi.updateRow`.

El método documentado `dataApi.updateRow(tableName, rowId, row)` de páginas generativas no expone un parámetro de encabezado `If-Match`. Por ello su relectura preventiva **no representa una operación atómica**.

## Decisión

1. Mantener el plug-in síncrono como autoridad para invariantes, permisos Post/Void y estados. No activar la aprobación presupuestaria ni elevar roles para obtener una prueba aparente.
2. Para la futura aplicación autenticada, exigir identidad delegada y actualización condicional con ETag/If-Match, bajo permisos Dataverse. El contrato fuente es `apps/financial-workspace/src/infrastructure/data/conditional-draft.ts`; **no está conectado** a la UI offline ni puede ejecutarse sin un transporte autenticado explícito.
3. La operación condicional debe devolver conflicto si Dataverse responde 412/409, rechazar 401/403, bloquear cualquier campo fuera de los campos de Draft permitidos y hacer una lectura posterior. El backend sigue siendo responsable de vigilar acceso por sede, integridad y auditoría.
4. La transición Draft→Posted/Voided exige operaciones servidor seguras y las pruebas de autorización. No se ofrece desde el Workspace local. Nunca ofrecer aprobación presupuestaria desde React sin dos identidades, rol separado y concurrencia validada.
5. Las comprobaciones de coincidencia del payload tras guardar siguen siendo necesarias incluso cuando el servidor devuelva éxito.
6. La transición de demo a producción requiere Entra delegado, rol real, aislamiento de sedes, expiración de tokens, pruebas de concurrencia, aceptación no-admin, y un proceso reversible de despliegue. Si falla autenticación **no hay fallback a datos sintéticos** en una sesión supuestamente real.

## Límites y pruebas pendientes

- En la aplicación Power Apps generativa se conserva el aviso de edición desactualizada, **no** una garantía de bloqueo atómico. Debe adoptarse una API condicional compatible (Web API autorizado o comando servidor registrado) antes de certificar ediciones concurrentes.
- El contrato `conditionalDraftSave` incluye pruebas con transporte simulado para If-Match, 412, 403, identidad ausente y relectura. La verificación en Dataverse real con dos usuarios licenciados queda pendiente.
- No se han cambiado los roles, datos existentes ni metadatos Dataverse con este ADR.

## Referencias

- Microsoft Learn — [Operaciones condicionales en Dataverse](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/perform-conditional-operations-using-web-api)
- Microsoft Learn — [API dataApi para páginas generativas](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/generative-page/data-api/)
- [ADR 0004](0004-safe-budget-approval.md) y [Seguridad](../security.md)
