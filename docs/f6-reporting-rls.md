# F6 · Power BI, seguridad por sede y aceptación

**Modelo:** `power-bi/FinancialManager/FinancialManager.pbip`.
Su origen es Dataverse autenticado (parámetro local `DataverseHost`),
no un extracto con datos financieros versionados.

## Implementación actual

En TMDL se añadió el rol
`F6_DenyUntilAuthorizedBranchMembership`, sin miembros ni direcciones
reales, con `FALSE()` sobre `FactPostedExpense` y las cuatro
dimensiones. Es una **barrera de denegación**, no RLS funcional de
una sede. El PBIP valida su estructura, pero no se ha publicado ni
verificado con un usuario Viewer.

Hasta contar con correspondencia **autorizada y verificable** entre
usuario/UPN, sede y ámbito de lectura, no crear filtros
`USERPRINCIPALNAME()` que concedan datos globales o compartidos por
omisión. Los pagos de ámbito Shared cuentan una sola vez en el
consolidado; no se asignan a cada sede sin una regla real.

## Mapeo necesario

La tabla de seguridad debe proceder de un proceso controlado (no de
Power Query calculando roles a partir de nombres libres) y contener
identificador humano verificable, sede autorizada, vigencia,
revocación y permiso explícito para datos Shared. Auditar cambios
y refrescar/revocar permisos. El acceso Dataverse no se hereda
automáticamente como RLS en un modelo Import.

## Pruebas imprescindibles para habilitar el rol

1. Usuario Viewer de sede A: solo ve filas de su sede; nada de B.
2. Viewer de B: no ve A; sin permisos retorna BLANK/ninguna fila.
3. Viewer con derecho Shared explícito: Shared aparece **una vez**,
   sin multiplicar la cifra entre sedes.
4. Viewer sin afiliación, revocado o caducado: cero filas.
5. Usuario no editor: no se le asigna rol
   Admin/Member/Contributor de workspace, porque éstos **no están
   sujetos a RLS** del semantic model.
6. Comparar recuentos, IDs y totales Posted/PEN con consulta
   Dataverse independiente y autorizada; Draft y Void no cuentan.
7. Probar en servicio refresh, expiración de credenciales, exportación,
   compartir/Build y acceso de usuario invitado si se utiliza B2B.

La prueba con otra cuenta y conexión real de Power BI Service
permanece **pendiente aceptada para avanzar**, no `PASS`.
No publicar el semantic model ni distribuir datos financieros hasta
completarla.

Documentación oficial:
https://learn.microsoft.com/en-us/power-bi/service-admin-rls
https://learn.microsoft.com/en-us/analysis-services/tmdl/tmdl-overview
