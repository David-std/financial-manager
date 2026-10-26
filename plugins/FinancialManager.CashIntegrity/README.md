# Cash-domain write lock

This independent Dataverse plugin rejects every direct Create, Update, and Delete against the six F5 tables, including administrator writes, with code `F5G001`. Register 18 synchronous PreValidation steps after the package has been verified.

There is no mutable bypass flag and no financial posting API. This is an intentionally fail-closed interim control. Do not remove it or weaken the validation to make UI controls work. A later reviewed service-side Custom API must authenticate and authorize the initiating user, enforce idempotency and post/reverse cash entries atomically before the lock can be replaced.

Plugin package: `fmg_CashIntegrity`. Type: `FinancialManager.CashIntegrity.F5_BlockDirectCashWrites`. This package is separate from the existing budget and expense plug-ins. Building the source does not imply it is registered in Dataverse.
