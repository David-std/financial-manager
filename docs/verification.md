# Verification plan

These scenarios are acceptance criteria for the eventual implementation. They have not yet been executed against a Power Platform environment.

| ID | Scenario | Expected outcome |
| --- | --- | --- |
| FIN-001 | Create a valid income or expense | Saved with correct amount, period, category, account, and attribution |
| FIN-002 | Submit zero or negative amount through app and other ingestion paths | Rejected without a persisted transaction |
| FIN-003 | Post an expense under an income category | Rejected at the data boundary |
| FIN-004 | Use an inactive category or account for a new posting | Rejected; historical records remain readable |
| FIN-005 | Edit amount, date, or category of a posted record | Rejected; a correction workflow is required |
| FIN-006 | Void a posted record without a reason or permission | Rejected and audited |
| FIN-007 | Re-submit an identical external request ID | Exactly one recorded transaction |
| SEC-001 | Read or modify restricted data as an unauthorized user | Denied in both UI and direct data access |
| SEC-002 | Access a report or semantic model without authorization | Denied, including export and underlying data |
| BI-001 | Compare report totals to a controlled transaction set | Exact agreement; drafts and voided entries excluded |
| BI-002 | Select dates across month and year boundaries | Totals follow the documented business-date semantics |
| BI-003 | Introduce mismatched transaction currencies | Reporting rejects or separates incompatible amounts |
| OPS-001 | Restore sample data from a documented backup | Recovery succeeds within the agreed target |

## Delivery checks

- Review role assignments, connection owners, and security policies.
- Verify supported solution export/import in an isolated environment.
- Confirm Power BI refresh behavior, source credentials, and licensing.
- Review the current financial definitions with the business owner.
- Record test execution, failures, and remediation before production release.

CI currently validates repository hygiene and documentation links only. It is not a substitute for Dataverse solution checks, application testing, security evaluation, or Power BI model validation.
