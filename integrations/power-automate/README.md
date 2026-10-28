# FM-11 — Outlook intake

**Registrar gasto** sends native Apple Shortcuts expense requests to an Outlook
mailbox. Power Automate retrieves the message in text format, parses a closed
JSON contract and stages it in the audited Dataverse table `fmg_mailintake`.

The original end-to-end route (Mac → Outlook → Power Automate → Dataverse)
was confirmed in DEV on 2026-10-29: all four steps succeeded and the
`Unverified` record was visible in the Dataverse table. This is **not**
financial expense creation or approval.

## DEV components

| Component | Responsibility |
| --- | --- |
| `FM11 - Mail Intake (disabled)` | Enhanced five-action receiver deployed in DEV; Off after validation |
| `fmg_mailintake` | Audited intake with an Active unique `requestId` key |
| `FM11 - Pending review` | Dataverse view deployed and independently verified |
| `Financial Manager Intake Reviewer` | Role created in DEV; no user or team assigned |
| `FM11 - Mail Intake Quarantine (disabled)` | Previous inert Outlook test |
| `FM11 - HTTP Intake Test (disabled)` | Inert authenticated HTTP transport |

No additional Azure subscription, mobile application or server is required.

## Request contract and error handling

The message uses the subject `FM11-DRAFT-V1`. Its body must match
[expense-draft.schema.json](../ios-shortcuts/expense-draft.schema.json).
The deployed workflow reads the same Outlook message as text via Microsoft
Graph and stages `requestId`, concept, amount, date, branch, category,
account, expense class, sender, message ID and original text. New display
columns apply to future accepted requests; the earlier test row retains its
original content in the raw JSON.

All staged records remain `Unverified`. `FormatChecked` means only that
the intake JSON passed parsing; it **does not authenticate the sender**.
Account and branch rights, category validity and business authorization
must be checked again by an authenticated reviewer before financial use.

The Dataverse alternate key rejects duplicate `requestId` values atomically.
The flow disables automatic retries of the insert. When the connector fails,
it classifies HTTP 409/412 as `DUPLICATE_REQUEST` and other failures as
`STAGING_WRITE_FAILED`, then terminates with `FM11_STAGE_REJECTED`. A
failure is never reported as a successful expense.

## Reviewer boundary

The view is a queue for review, **not** an authorization mechanism.
The reviewer role provisioning never assigns users or teams. The
[review preflight](fm11_review_policy.py) tests an authenticated, independent
reviewer, live branch/account/category entitlements and a row version, but
returns only an eligibility assessment. No automatic approval, direct financial
write, posting or cash operation is installed.
A later transactional Custom API must verify the authenticated reviewer,
independence from the requester, current catalog/branch/account rights and
idempotency before it can create a financial `Draft`. Editing a status
cell or spoofing an email must never substitute for that API.

## Maintenance (verified DEV only)

```powershell
python integrations/power-automate/build_mail_staging.py
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -- fm11-intake-workspace-inspect
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -- fm11-review-inspect
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -- fm11-mail-stage-inspect
python -m unittest tests.test_fm11_mail_staging tests.test_fm11_intake_workspace tests.test_fm11_review_readiness -v
```

Mutation commands require `--apply` and validated DEV organization credentials:
`fm11-mail-stage-off`, `fm11-mail-stage-upgrade`,
`fm11-intake-workspace`, and `fm11-review-role`.
The receiver was turned Off before the enhanced definition was deployed.
The upgrade requires an Active unique key and verifies both saved connector
references after updating. A read-back confirmed the enhanced definition
and Off state. The view validator accepts the Dataverse-generated
`savedqueryid` only when it matches the retrieved view; its fields and
`Unverified` filter remain unchanged.

`main`, `release/0.1.0` and financial F5/F6 protections are not modified.

## Transport safeguards

The Outlook V3 intake does not create a financial expense. The restricted HTTP
test remains disabled. Its PKCE desktop probe returned 401 without
authentication and 503 with an accepted delegated token; this does not constitute
iPhone authentication or authority to approve a financial transaction.


## Authenticated Draft conversion

The optional private `fmg_CreateDraftFromMailIntake` Custom API converts a
previously staged `Unverified` request to an unposted
`fmg_financialtransaction` with `Draft` status. The API checks the live
request and row version, the caller's Dataverse reviewer or administrator
role, the active category/branch/cash-account relationships, PEN currency and
a unique `fmg_externalrequestid` index. Draft creation and updating the intake
to `DraftCreated` are one Dataverse transaction.

**Sending an email is not an authenticated Dataverse session.** The incoming
Outlook flow has no action that invokes this API. Automatic conversion from an
unverified email would give any spoofed sender a financial write path. No
extra subscription is needed, but a delegated user-authentication mechanism
must be connected before enabling automatic creation.

A reviewer already signed in to the authorized DEV environment can currently
invoke the server command manually using the exact request ID from Mail Intake:

```powershell
$env:FM11_REQUEST_ID = "FM11-EXAMPLE-REQUEST"
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -- fm11-draft-preview --apply
dotnet run --project tools/DataverseDeployment/DataverseDeployment.csproj -- fm11-draft-convert --apply
```

These commands require the existing verified `FM_ENV_URL` and
`FM_ORGANIZATION_ID` settings. Replace the example with a real request ID.
`--apply` is mandatory for the final conversion. The server enforces the
actor's actual rights, never a caller-supplied role or email address.

Negative API testing rejected an invalid row version. A synthetic DEV test
created one `Draft`, marked the intake `DraftCreated`, rejected replay,
and cleaned both test records. The posting and voiding guards were unchanged.
This acceptance proves server-side conversion; it does not prove iPhone OAuth
or automatic email-to-Draft conversion.

Implementation: `plugins/FinancialManager.MailDraftExecution/`,
`tools/DataverseDeployment/Fm11DraftApiDeployment.cs`, and
`tests/test_fm11_draft_conversion.py`.


## Delegated Safari route

An optional second shortcut uses the installed Dataverse HTML web resource
`fmg_shortcut_draft.html` instead of Mail. It sends the user's expense fields
only through a URL fragment to Safari. A browser session then authenticates
with Entra PKCE S256 and invokes the private Draft API as the signed-in
Dataverse user. The Outlook flow remains Off and is not involved in this
route. No additional connector, hosted server, or subscription is required.

The resource and browser logic passed offline tests and DEV content
verification. The Entra SPA callback is now registered, as confirmed in the
Entra portal on October 29, 2026. **A real Safari/iPhone sign-in and
Draft-creation acceptance test is still required.** Do not present this
variant as live until that test succeeds. The original Mail transport remains the accepted fallback.
