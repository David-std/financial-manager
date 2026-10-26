using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

internal static class CashUniqueKeys
{
    private record Plan(string Table, string Schema, string[] Fields);
    private static readonly Plan[] Plans =
    {
        new("fmg_cashreceipt", "fmg_CR_Request", new[]{"fmg_externalrequestid"}),
        new("fmg_cashtransfer", "fmg_CT_Request", new[]{"fmg_externalrequestid"}),
        new("fmg_cashposting", "fmg_CP_Leg", new[]{"fmg_externalrequestid"}),
        new("fmg_accountopening", "fmg_AO_AccountDate", new[]{"fmg_cashaccount", "fmg_effectiveon"}),
        new("fmg_bankstatement", "fmg_BS_Request", new[]{"fmg_externalrequestid"}),
        new("fmg_bankreconciliation", "fmg_BR_Request", new[]{"fmg_externalrequestid"})
    };
    public static int Run(ServiceClient svc, bool apply)
    {
        foreach (var plan in Plans)
        {
            var metadata = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = plan.Table, EntityFilters = EntityFilters.All, RetrieveAsIfPublished = true
            })).EntityMetadata;
            var key = metadata.Keys?.SingleOrDefault(k =>
                String.Equals(k.SchemaName, plan.Schema, StringComparison.OrdinalIgnoreCase));
            if (key != null)
            {
                if (!plan.Fields.SequenceEqual(key.KeyAttributes, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Existing key has different columns: " + plan.Schema);
                if (key.EntityKeyIndexStatus == EntityKeyIndexStatus.Failed)
                    throw new InvalidOperationException("Key index failed: " + plan.Schema);
                Console.WriteLine("F5_KEY " + plan.Schema + " " + key.EntityKeyIndexStatus);
                continue;
            }
            Console.WriteLine("F5_MISSING_KEY " + plan.Schema);
            if (!apply) continue;
            svc.Execute(new CreateEntityKeyRequest
            {
                EntityName = plan.Table,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = plan.Schema,
                    DisplayName = new Label(plan.Schema.Replace("_", " "), 3082),
                    KeyAttributes = plan.Fields
                }
            });
            Console.WriteLine("F5_CREATED_KEY " + plan.Schema);
        }
        return 0;
    }
}
