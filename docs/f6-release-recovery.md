# F6 · Runbook de publicación y recuperación

**Estado actual:** preparado para evaluación de laboratorio,
**no autorizado para importación administrada**.

## Entrada (sin cambios)

Verificar commit aprobado, CI en verde, `F6_SOURCE_SYNCED=true`,
`F6_MANAGED_RELEASE_READY=true` y aprobación externa para un
entorno objetivo **distinto de** `financial-manager-dev`.
Registrar responsable, hora, versión, SHA-256 del paquete,
dependencias del destino, plan de regreso y ventana de observación.
Si cualquiera falta, detenerse antes de importar.

## Preparación en entorno destino autorizado

- Capturar inventario de soluciones, tablas, plug-ins, flujos,
  roles, referencias de conexión y configuración; preservar el
  paquete administrado anterior y los datos con mecanismos soportados.
- Confirmar publisher `fmg`, versión incremental y permisos mínimos
  de la identidad de despliegue; verificar dependencias
  `uxagentproject`/AppModuleWebResources/Settings.
- Configurar GitHub Environment con aprobación y secretos; ninguna
  credencial debe quedar en Git, consola compartida o paquete.
- Ensayar importación mediante herramientas oficiales, registrar
  resultado y comprobar que no hay cambio de estado financiero.

## Aceptación después de importar

Comprobar `WhoAmI`, entorno esperado, permisos y endpoints
sin grants; verificar **24** guardias rank-1 y **15** standby.
Repetir sondas negativas aisladas, la sonda de rollback sintético,
navegación de gastos, contraste y teclado de formularios.
Confirmar auditoría de siete tablas y retención acordada.
Validar Power BI/RLS y acceso Viewer con usuarios reales.

## Si falla

**No** borrar tablas operativas ni desactivar guardias como
recuperación. Suspender la promoción; conservar el registro de import,
errores, versión y hashes. Volver a la versión administrada anterior
según el procedimiento aprobado para ese tipo de actualización,
tras comprobar dependencias y proteger datos. Ejecutar de nuevo
WhoAmI, comprobaciones de plugins y no-escritura; escalar a
responsables de plataforma ante inconsistencias. Una reversión
de código no revierte automáticamente filas o auditorías de Dataverse.

## Salida

La publicación solo queda aceptada con acta de importación en
destino, pruebas de usuarios reales y plan de soporte. Un `pack`
local o CI verde demuestra preparación del código, no aceptación
de seguridad, permisos ni conciliación bancaria.
