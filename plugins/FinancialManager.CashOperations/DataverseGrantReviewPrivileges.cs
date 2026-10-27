using System;
using FinancialManager.CashDomain;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace FinancialManager.CashOperations
{
    /// <summary>
    /// Server-only check of effective Dataverse privileges, including
    /// team-inherited privileges. No caller-supplied permission or role claim.
    /// These generated privileges belong to fmg_cashgrantrequest and must
    /// be deliberately assigned using distinct least-privilege roles.
    /// </summary>
    public sealed class DataverseGrantReviewPrivileges
    {
        private readonly IOrganizationService service;
        public const string ProposePrivilege = "prvCreatefmg_CashGrantRequest";
        public const string DecidePrivilege = "prvWritefmg_CashGrantRequest";

        public DataverseGrantReviewPrivileges(IOrganizationService service)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public void RequireCanPropose(Guid authenticatedUserId) =>
            Require(authenticatedUserId, ProposePrivilege);

        public void RequireCanDecide(Guid authenticatedUserId) =>
            Require(authenticatedUserId, DecidePrivilege);

        private void Require(Guid userId, string exactPrivilegeName)
        {
            if (userId == Guid.Empty)
                throw new CashPlanningException("F5Q010", "Missing platform user.");
            var user = service.Retrieve("systemuser", userId,
                new ColumnSet("isdisabled", "accessmode", "applicationid"));
            if (user == null || user.Id != userId || !user.Contains("isdisabled") ||
                !user.Contains("accessmode") ||
                user.GetAttributeValue<bool>("isdisabled") ||
                user.GetAttributeValue<OptionSetValue>("accessmode")?.Value != 0 ||
                user.GetAttributeValue<Guid>("applicationid") != Guid.Empty)
                throw new CashPlanningException("F5Q010",
                    "A verified active interactive human user is required.");

            RetrieveUserPrivilegeByPrivilegeNameResponse? response;
            try
            {
                response = service.Execute(
                    new RetrieveUserPrivilegeByPrivilegeNameRequest
                    {
                        UserId = userId, PrivilegeName = exactPrivilegeName
                    }) as RetrieveUserPrivilegeByPrivilegeNameResponse;
            }
            catch (System.ServiceModel.FaultException<OrganizationServiceFault>)
            {
                throw new CashPlanningException("F5Q010", "Cannot prove required effective privilege.");
            }
            if (response?.RolePrivileges == null ||
                !Array.Exists(response.RolePrivileges, role =>
                    role != null &&
                    string.Equals(role.PrivilegeName, exactPrivilegeName,
                        StringComparison.OrdinalIgnoreCase) &&
                    // Approval changes another human's request. Require
                    // organization-wide effective privilege, not Basic,
                    // Local, team-inherited Basic or a UI-only role label.
                    (exactPrivilegeName == ProposePrivilege ||
                     role.Depth == PrivilegeDepth.Global)))
                throw new CashPlanningException("F5Q010", "Required effective privilege missing.");
        }
    }
}
