// ---------------- Type Definitions which can be imported from ./RuntimeTypes -------------------------
export interface TableRegistrations extends BaseTableRegistrations {
    "fmg_branch": fmg_branch,
    "fmg_budget": fmg_budget,
    "fmg_budgetallocation": fmg_budgetallocation,
    "fmg_cashaccount": fmg_cashaccount,
    "fmg_category": fmg_category,
    "fmg_financialtransaction": fmg_financialtransaction,
}
export interface EnumRegistrations extends BaseEnumRegistrations {
    "fmg_branch-fmg_isactive": fmg_branch_fmg_isactive,
    "fmg_branch-statecode": fmg_branch_statecode,
    "fmg_branch-statuscode": fmg_branch_statuscode,
    "fmg_budget-fmg_scope": fmg_budget_fmg_scope,
    "fmg_budget-fmg_status": fmg_budget_fmg_status,
    "fmg_budget-statecode": fmg_budget_statecode,
    "fmg_budget-statuscode": fmg_budget_statuscode,
    "fmg_budgetallocation-statecode": fmg_budgetallocation_statecode,
    "fmg_budgetallocation-statuscode": fmg_budgetallocation_statuscode,
    "fmg_cashaccount-fmg_isactive": fmg_cashaccount_fmg_isactive,
    "fmg_cashaccount-fmg_type": fmg_cashaccount_fmg_type,
    "fmg_cashaccount-statecode": fmg_cashaccount_statecode,
    "fmg_cashaccount-statuscode": fmg_cashaccount_statuscode,
    "fmg_category-fmg_isactive": fmg_category_fmg_isactive,
    "fmg_category-fmg_isgroup": fmg_category_fmg_isgroup,
    "fmg_category-statecode": fmg_category_statecode,
    "fmg_category-statuscode": fmg_category_statuscode,
    "fmg_financialtransaction-fmg_flowclass": fmg_financialtransaction_fmg_flowclass,
    "fmg_financialtransaction-fmg_scope": fmg_financialtransaction_fmg_scope,
    "fmg_financialtransaction-fmg_status": fmg_financialtransaction_fmg_status,
    "fmg_financialtransaction-statecode": fmg_financialtransaction_statecode,
    "fmg_financialtransaction-statuscode": fmg_financialtransaction_statuscode,
}
export type fmg_branch = TableRow<{
    // Primary Key Column
    readonly fmg_branchid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmg_code: string,
    fmg_isactive: fmg_branch_fmg_isactive,
    fmg_name: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmg_branch_statecode,
    statuscode: fmg_branch_statuscode,
}>

export type fmg_budget = TableRow<{
    // Primary Key Column
    readonly fmg_budgetid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _fmg_approvedby_value: `/systemuser(${string})`,
    readonly fmg_approvedbyname: string,
    readonly fmg_approvedbyyominame: string,
    fmg_approvedon: Date,
    // Foreign Key Column
    _fmg_branch_value: `/fmg_branch(${string})`,
    readonly fmg_branchname: string,
    fmg_effectivefrommonth: number,
    fmg_name: string,
    fmg_scope: fmg_budget_fmg_scope,
    fmg_status: fmg_budget_fmg_status,
    fmg_version: number,
    fmg_year: number,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    readonly owningbusinessunitname: string,
    statecode: fmg_budget_statecode,
    statuscode: fmg_budget_statuscode,
}>

export type fmg_budgetallocation = TableRow<{
    // Primary Key Column
    readonly fmg_budgetallocationid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    readonly exchangerate: number,
    fmg_amount: number,
    readonly fmg_amount_base: number,
    // Foreign Key Column
    _fmg_budget_value: `/fmg_budget(${string})`,
    readonly fmg_budgetname: string,
    // Foreign Key Column
    _fmg_category_value: `/fmg_category(${string})`,
    readonly fmg_categoryname: string,
    fmg_month: number,
    fmg_name: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    readonly owningbusinessunitname: string,
    statecode: fmg_budgetallocation_statecode,
    statuscode: fmg_budgetallocation_statuscode,
    // Foreign Key Column
    readonly _transactioncurrencyid_value: `/transactioncurrency(${string})`,
    readonly transactioncurrencyidname: string,
}>

export type fmg_cashaccount = TableRow<{
    // Primary Key Column
    readonly fmg_cashaccountid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    // Foreign Key Column
    _fmg_branch_value: `/fmg_branch(${string})`,
    readonly fmg_branchname: string,
    fmg_isactive: fmg_cashaccount_fmg_isactive,
    fmg_name: string,
    fmg_type: fmg_cashaccount_fmg_type,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmg_cashaccount_statecode,
    statuscode: fmg_cashaccount_statuscode,
}>

export type fmg_category = TableRow<{
    // Primary Key Column
    readonly fmg_categoryid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmg_acronym: string,
    fmg_code: string,
    fmg_description: string,
    fmg_isactive: fmg_category_fmg_isactive,
    fmg_isgroup: fmg_category_fmg_isgroup,
    fmg_name: string,
    // Foreign Key Column
    _fmg_parentcategory_value: `/fmg_category(${string})`,
    readonly fmg_parentcategoryname: string,
    fmg_parentcode: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmg_category_statecode,
    statuscode: fmg_category_statuscode,
}>

export type fmg_financialtransaction = TableRow<{
    // Primary Key Column
    readonly fmg_financialtransactionid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    readonly exchangerate: number,
    fmg_amount: number,
    readonly fmg_amount_base: number,
    // Foreign Key Column
    _fmg_branch_value: `/fmg_branch(${string})`,
    readonly fmg_branchname: string,
    // Foreign Key Column
    _fmg_cashaccount_value: `/fmg_cashaccount(${string})`,
    readonly fmg_cashaccountname: string,
    // Foreign Key Column
    _fmg_category_value: `/fmg_category(${string})`,
    readonly fmg_categoryname: string,
    fmg_description: string,
    fmg_documentreference: string,
    fmg_externalrequestid: string,
    fmg_flowclass: fmg_financialtransaction_fmg_flowclass,
    fmg_name: string,
    fmg_scope: fmg_financialtransaction_fmg_scope,
    fmg_status: fmg_financialtransaction_fmg_status,
    fmg_transactiondate: Date,
    fmg_voidreason: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    readonly owningbusinessunitname: string,
    statecode: fmg_financialtransaction_statecode,
    statuscode: fmg_financialtransaction_statuscode,
    // Foreign Key Column
    readonly _transactioncurrencyid_value: `/transactioncurrency(${string})`,
    readonly transactioncurrencyidname: string,
}>

const enum fmg_branch_fmg_isactive {
"No" = 0,
"Sí" = 1,
}
const enum fmg_branch_statecode {
"Activo" = 0,
"Inactivo" = 1,
}
const enum fmg_branch_statuscode {
"Activo" = 1,
"Inactivo" = 2,
}
const enum fmg_budget_fmg_scope {
"Branch" = 797280300,
"Shared" = 797280301,
}
const enum fmg_budget_fmg_status {
"Draft" = 797280400,
"Submitted" = 797280401,
"Approved" = 797280402,
"Rejected" = 797280403,
"Superseded" = 797280404,
}
const enum fmg_budget_statecode {
"Activo" = 0,
"Inactivo" = 1,
}
const enum fmg_budget_statuscode {
"Activo" = 1,
"Inactivo" = 2,
}
const enum fmg_budgetallocation_statecode {
"Activo" = 0,
"Inactivo" = 1,
}
const enum fmg_budgetallocation_statuscode {
"Activo" = 1,
"Inactivo" = 2,
}
const enum fmg_cashaccount_fmg_isactive {
"No" = 0,
"Sí" = 1,
}
const enum fmg_cashaccount_fmg_type {
"Cash" = 797280000,
"Bank" = 797280001,
"Wallet" = 797280002,
"Other" = 797280003,
}
const enum fmg_cashaccount_statecode {
"Activo" = 0,
"Inactivo" = 1,
}
const enum fmg_cashaccount_statuscode {
"Activo" = 1,
"Inactivo" = 2,
}
const enum fmg_category_fmg_isactive {
"No" = 0,
"Sí" = 1,
}
const enum fmg_category_fmg_isgroup {
"No" = 0,
"Sí" = 1,
}
const enum fmg_category_statecode {
"Activo" = 0,
"Inactivo" = 1,
}
const enum fmg_category_statuscode {
"Activo" = 1,
"Inactivo" = 2,
}
const enum fmg_financialtransaction_fmg_flowclass {
"OperatingPayment" = 797280000,
"CapitalPayment" = 797280001,
"TaxPayment" = 797280002,
"OtherPayment" = 797280003,
}
const enum fmg_financialtransaction_fmg_scope {
"Branch" = 797280000,
"Shared" = 797280001,
}
const enum fmg_financialtransaction_fmg_status {
"Draft" = 797280000,
"Posted" = 797280001,
"Voided" = 797280002,
}
const enum fmg_financialtransaction_statecode {
"Activo" = 0,
"Inactivo" = 1,
}
const enum fmg_financialtransaction_statuscode {
"Activo" = 1,
"Inactivo" = 2,
}

export interface UxAgentDataApi extends BaseUxAgentDataApi<TableRegistrations, EnumRegistrations> {}

export interface GeneratedComponentProps {
    dataApi: UxAgentDataApi;
}
