# Management expense category catalog

Source: the category structure provided for the project. It is an **internal expense taxonomy**, not a standardized accounting chart and not a direct mapping to the Peruvian PCGE. The proposed labels are preserved for requirements review. Display names remain in Spanish.

Codes ending in `00` identify **groups only** and cannot be assigned to posted financial movements. The leaf codes below are provisional and require business approval.

| Group | Group name | Code | Acronym | Category |
| --- | --- | --- | --- | --- |
| 100 | Operación y mantenimiento | 101 | MTO | Mantenimiento |
| 100 | Operación y mantenimiento | 102 | REP | Reparaciones |
| 100 | Operación y mantenimiento | 103 | RED | Infraestructura / Red |
| 100 | Operación y mantenimiento | 104 | HER | Herramientas y Repuestos |
| 200 | Administración | 201 | ADM | Gastos Administrativos |
| 200 | Administración | 202 | PAP | Papelería y Útiles |
| 200 | Administración | 203 | CON | Contabilidad |
| 200 | Administración | 204 | LEG | Gastos Legales / Notariales |
| 300 | Comercial y marketing | 301 | MKT | Marketing / Publicidad |
| 300 | Comercial y marketing | 302 | VEN | Ventas / Comisiones |
| 300 | Comercial y marketing | 303 | PUB | Material Publicitario |
| 300 | Comercial y marketing | 304 | CLI | Atención al Cliente / Fidelización |
| 400 | Transporte y unidad vehicular | 401 | COM | Combustible |
| 400 | Transporte y unidad vehicular | 402 | VEH | Unidad Vehicular |
| 400 | Transporte y unidad vehicular | 403 | FLE | Fletes / Transporte |
| 400 | Transporte y unidad vehicular | 404 | VIA | Viáticos / Movilidad |
| 500 | Representación | 501 | GRE | Gastos Representativos |
| 500 | Representación | 502 | EVE | Eventos / Reuniones |
| 500 | Representación | 503 | ATE | Atención a Clientes |
| 600 | Servicios y operación fija | 601 | ENE | Energía Eléctrica |
| 600 | Servicios y operación fija | 602 | AGU | Agua |
| 600 | Servicios y operación fija | 603 | TEL | Telefonía / Internet |
| 600 | Servicios y operación fija | 604 | ALQ | Alquileres |
| 600 | Servicios y operación fija | 605 | SEG | Seguridad / Vigilancia |
| 700 | Tributos y finanzas | 701 | IMP | Impuestos / Tasas |
| 700 | Tributos y finanzas | 702 | BAN | Comisiones Bancarias |
| 700 | Tributos y finanzas | 703 | MUL | Multas / Recargos |
| 800 | Equipos y tecnología | 801 | EQP | Equipos / Activos |
| 800 | Equipos y tecnología | 802 | LIC | Licencias / Software |
| 800 | Equipos y tecnología | 803 | SOP | Soporte Técnico |
| 900 | Otros | 901 | OTR | Otros |
| 900 | Otros | 999 | NDE | No Definido |

## Classification controls

- Codes are stored as **text**, not numerical account identifiers.
- Group code and leaf code are distinct records linked by parent lookup.
- Validate code uniqueness, active state, leaf status, receipt/payment direction, and the permitted cash-flow classification before posting.
- Restrict `999` to drafts pending review; an administrator must reclassify before posting.
- Prevent deletion of referenced categories; deactivate them instead.
- New income categories require a separate approved catalog, not reuse of expense groups.

## Items requiring review

1. **Payroll and employer obligations:** no explicit salary category appears in the supplied 100–900 tree. Add an approved category or a separate group rather than hiding wages under "administration" without agreement.
2. **Asset purchases:** code 801 may represent capital expenditure rather than operating expense. Buying a router for resale, buying equipment for internal use, and purchasing consumable spares are financially different.
3. **Taxes:** payments collected/remitted for indirect taxes are not necessarily operating expenses. Confirm a payment classification before reporting.
4. **Overlapping descriptions:** 301 vs. 303 and 304 vs. 503 need mutually understood coding guidance.
5. **Bank charges, finance costs, and penalties:** distinguish them from routine operating expenditure if operating metrics exclude them.
6. **ISP-specific infrastructure:** network maintenance, contracted technicians, hosting, link capacity, and equipment may need more detailed categories after reviewing actual expenditures.

Do not assign PCGE accounts automatically on the basis of similar names. Any future PCGE mapping must be reviewed by a qualified accountant.

## Proposed input behavior

Users select the group to narrow the list of leaves, then select one leaf. The chosen code and name are displayed together. Changing an existing code must not retroactively rewrite the meaning of recorded history without a controlled migration.

See [Data model](data-model.md) and [Reporting](reporting.md).
