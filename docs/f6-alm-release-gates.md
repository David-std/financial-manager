# F6 · ALM, seguridad y puertas de publicación

**Fuente:** solución real `FinancialManager`, exportada sin escrituras desde
`financial-manager-dev` el 28/10/2026. **No es una aprobación productiva**.

## Estado comprobado

- Código fuente ahora actualizado desde el export real: 371 archivos;
  siete Custom APIs obligatorias, **24** guardias rank-1,
  **15** validadores standby rank-2 y **10** paquetes NuGet.
- `python scripts/f6_alm_preflight.py` comprueba manifiesto, componentes,
  ranks/stages de guardias, existencia e integridad ZIP de los NuGet.
- `pac solution pack --packagetype Unmanaged` empaquetó localmente la
  exportación a un ZIP temporal; **no se importó** en ningún entorno.
- El manifiesto todavía declara **seis dependencias externas**:
  tipo 61 (recurso app), `SettingDefinition` y cuatro
  `uxagentproject`. También hubo advertencias de diez referencias
  PluginAssembly no definidas en customizations. Requieren resolver
  requisitos en el destino, aunque `pac solution pack` finalice.
- `F6_SOURCE_SYNCED=true`, pero
  **`F6_MANAGED_RELEASE_READY=false`**. La puerta `--release` debe
  fallar con código 2 y bloquear etiquetas de release en CI.

## Inspección sin efectos externos

Desde la raíz del repositorio:

```powershell
python scripts/f6_alm_preflight.py
python -m unittest discover -s tests -q
python scripts/check_repository.py
npm run check:workspace
powerbi-report-author validate power-bi/FinancialManager/FinancialManager.pbip
```

Para inspeccionar el ZIP sin publicar, utilizar una ruta **nueva**
bajo `$env:TEMP`:

```powershell
pac solution pack --folder "power-platform/solutions/FinancialManager/src" `
  --zipfile "$env:TEMP/FinancialManager-f6-review.zip" --packagetype Unmanaged
```

No guardar el ZIP en Git, ni convertir el éxito del empaquetado en un
resultado de importación. No ejecutar `pac solution import`,
`publish` o `upgrade` sin autorización de un entorno destino aislado.

## Pendientes para una release administrada

1. Decidir si se instala la app basada en modelos con sus dependencias
   `uxagentproject` o se separa la solución operativa portable;
   verificar con los fabricantes todas las dependencias requeridas.
2. En un **entorno de aceptación separado y autorizado**, instalar
   y comprobar los paquetes y Custom APIs, confirmar los 24 guardias
   activos, auditar el estado de los 15 standby y demostrar que no hay
   cobros/transferencias activadas sin grants.
3. Registrar identidad de aplicación de despliegue, permisos mínimos,
   environment variables, referencias de conexión y configuración
   protegida; usar GitHub Environments con revisores/secretos, nunca
   credenciales en el repositorio ni en artefactos.
4. Ensayar importación administrada, comprobación de diferencias
   y recuperación/rollback sobre el destino aislado, sin crear balances
   productivos ni eliminar tablas como mecanismo de rollback.
5. Aceptar la experiencia de usuario (teclado, zoom, lectura de
   formularios), RLS de Power BI con usuarios Viewer de distintas
   sedes, refresh y exportación autorizada, y la recuperación tras
   errores de conexión o expiración de sesión.
6. Tras estas evidencias, emitir una versión incrementada y artefacto
   firmado con hash; nunca una etiqueta de release mientras
   `python scripts/f6_alm_preflight.py --release` falle.

**F5 continúa cerrado únicamente en laboratorio.** No desactivar
`F5G001`, `F5A009`, `F5Q009` ni conceder grants para pruebas F6.
La UAT con segundo usuario sigue aplazada, no aprobada.

Ver también [RLS y reporting](f6-reporting-rls.md) y
[plan de recuperación](f6-release-recovery.md).
