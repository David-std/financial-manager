# ADR 0012 — Authenticated draft creation from mail intake

## Decision

Keep **Registrar gasto** as a native Apple Shortcut and use Outlook and Power
Automate only for accepting its JSON into the audited `fmg_mailintake` queue.
A received message, its From header and its claimed user fields are
**untrusted**; none authorizes creation of a financial record.

Install a private Dataverse Custom API, `fmg_CreateDraftFromMailIntake`, for
a signed-in and authorized operator. It creates one
`fmg_financialtransaction` in `Draft` status, marks the original intake
`DraftCreated`, and returns the new record ID. The write and state transition
execute within the same Dataverse transaction. Only active, appropriately
related category, branch and cash-account entries and PEN currency are
accepted. The intake row version and an Active unique financial request key
protect against races and repeated requests. Posting and voiding remain
separate privileged operations.

The API requires a current Dataverse caller (reviewer or administrator), plus
the existing Create Financial Transaction privilege. The Outlook receiver
does not invoke it. A manually selected request may be converted using the
same user's delegated Dataverse authentication, without another hosted server
or subscription.

## Identity constraint

Apple Mail sending a message does not bind its From address to a Dataverse
user. The proven Mac → Outlook → Power Automate → Mail Intake route is not an
authentication protocol. Automatic Draft creation will remain disabled until
the iPhone Shortcut can obtain an Entra delegated token or another
independently verifiable identity proof tied to the registered user. The
previous Windows PKCE probe does not establish iPhone sign-in.

There is no requirement to authorize or post a payment merely to create a
Draft. Conversion is a human-authenticated write, not budget approval.

## DEV verification

The financial external request alternate key reached `Active`. The
`fmg_MailDraftExecution` package and private Custom API were installed, and
an invalid-version call was rejected. A synthetic authenticated invocation
created an unposted Draft, updated its intake record, rejected a replay, and
deleted both synthetic records. No live customer or financial operation was
used. Existing F5/F6 controls and production gates remain unchanged.

## Rejected shortcut

Do not connect `Stage_unverified_request` directly to a financial Create
Record action, nor let a Power Automate service account promote a request
solely from email headers, sender-address equality or user-supplied JSON.
Doing so would conceal a financial write privilege behind an unauthenticated
mailbox.
