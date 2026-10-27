using System;
using Microsoft.Xrm.Sdk;

namespace FinancialManager.CashTrustProbe
{
    /// <summary>
    /// Synthetic DEV-only alternative to SharedVariables. The GUID must be
    /// an independently verified SDK MainOperation step bound to the exact
    /// Custom API and registered plugin; admins configure it on each isolated
    /// guard step. Not suitable for financial guards without deeper review.
    /// </summary>
    public static class F5_SyntheticServerOriginPolicy
    {
        public static void Require(IPluginExecutionContext child, Guid pinnedMainStepId)
        {
            if (child == null || pinnedMainStepId == Guid.Empty)
                throw new InvalidPluginExecutionException("F5S005: Unpinned server origin.");
            var parent = child.ParentContext;
            if (child.Stage != 10 || child.Mode != 0 ||
                !child.IsInTransaction ||
                child.MessageName != "Create" ||
                child.PrimaryEntityName != "fmg_cashtrustprobe" ||
                parent == null ||
                parent.MessageName != "fmg_CashNestedTrustProbe" ||
                parent.Stage != 30 || parent.Mode != 0 ||
                !parent.IsInTransaction ||
                parent.OwningExtension?.LogicalName != "sdkmessageprocessingstep" ||
                parent.OwningExtension.Id != pinnedMainStepId ||
                parent.UserId == Guid.Empty ||
                parent.UserId != parent.InitiatingUserId ||
                child.UserId != parent.UserId ||
                child.InitiatingUserId != parent.InitiatingUserId ||
                child.Depth != parent.Depth + 1 ||
                parent.Depth < 1 ||
                parent.CorrelationId == Guid.Empty ||
                child.CorrelationId != parent.CorrelationId ||
                child.OperationId != parent.OperationId)
                throw new InvalidPluginExecutionException(
                    "F5S005: Synthetic Create did not originate from the registered MainOperation.");

            if (child.InputParameters == null ||
                !child.InputParameters.ContainsKey("Target") ||
                !(child.InputParameters["Target"] is Entity row) ||
                row.LogicalName != "fmg_cashtrustprobe" ||
                row.Id == Guid.Empty)
                throw new InvalidPluginExecutionException(
                    "F5S005: Synthetic record identity is missing.");
        }
    }
}
