# F6 · ALM y preparación de lanzamiento (incremento inicial)

**Entorno de origen:** `financial-manager-dev`.
**Estado:** comprobación de origen y exportación pendiente; **no preparado para release administrada**.
**Condición F5:** laboratorio aceptado, operaciones financieras bloqueadas por los 24 guardias; la prueba con otra identidad continúa pendiente.

## 1. Fuente versionada frente al entorno DEV

`power-platform/solutions/FinancialManager/src` sigue siendo la exportación
`FinancialManager` versión `0.1.0.0`. El verificador local compara las Custom
APIs financieras que sabemos desplegadas con los componentes **presentes en Git**.
No lee secretos, no llama al entorno ni interpreta permisos de los usuarios.

Ejecutar desde la raíz:

```powershell
python scripts/f6_alm_preflight.py
python scripts/f6_alm_preflight.py --release
```

La primera ejecución permite recopilar evidencia de deriva. La segunda es una
puerta **fail-closed**: hasta actualizar y revisar la fuente, debe devolver
`F6_RELEASE_BLOCKED_UNTIL_REVIEWED_FRESH_DEV_EXPORT` y código distinto de cero.
No cambiar esta regla para ocultar componentes ausentes.

## 2. Refrescar la exportación de forma controlada

Con `pac env who` confirmar que se sigue en DEV. Clonar a un directorio **nuevo
fuera de Git** y comparar con el proyecto existente antes de reemplazarlo:

```powershell
pac env who
$target = Join-Path $env:TEMP "FinancialManager-F6-unmanaged-review"
if (Test-Path $target) { throw "No sobreescribir una exportación anterior." }
pac solution clone --name FinancialManager --packagetype Unmanaged --outputDirectory $target
```

**No** importar en DEV ni en otros entornos. Antes de copiar al repositorio:
verificar nuevas Custom APIs, pasos del SDK, soluciones y componentes de la
app basada en modelos, diferencias de formularios/vistas, referencias a
conexiones, credenciales o URLs sensibles, y versión y dependencias. Confirmar
que el nuevo export incluye los validadores standby y conserva activos todos
los guardias originales. No confundir componentes del paquete con datos ni
historiales; las filas y auditorías requieren un plan de migración distinto.

## 3. Paquete local de comprobación, sin publicación

Se permite `pac solution pack --packagetype Unmanaged` en `$env:TEMP` como
inspección local de la fuente. No se considera un artefacto de producción:
la solución controlada actualmente no contiene todas las APIs desplegadas.
**No crear un paquete Managed** hasta tener exportación revisada y validación
`--release` satisfactoria. Un `pack` exitoso no demuestra importación,
seguridad, compatibilidad de dependencias ni funcionamiento de DataVerse.

## 4. Próximas puertas F6

- Aceptar exportación actualizada, verificador estricto y reproducibilidad del
  ZIP, con revisión de soluciones y dependencias.
- Crear un entorno objetivo aislado, plan de copia, instalación de solución
  administrada y rollback **solo cuando se autorice explícitamente**.
- Preparar pipelines y GitHub environments protegidos, sin poner secretos
  en variables planas de código, y controles de permisos mínimos.
- Revisar auditoría de cuentas de caja: el usuario confirmó `audited=True`
  por interfaz y con `cash-live-entitysets-inspect`. Mantener retención
  actual (30 días) hasta definir una política aprobada.
- Preparar reporting Power BI, RLS y pruebas con un segundo usuario, sin
  enseñar datos de demostración como datos de producción.
- Mantener **F5Q009**, **F5A009**, **F5G001** y los 24 guardias activos.
  No activar cobros/grants/transferencias como parte de ALM.

**Alcance de aceptación:** inicio de F6 completado cuando la comprobación
de deriva corre en CI y bloquea releases desde fuente obsoleta. El release
de Power Platform continúa pendiente de exportación y revisión.
