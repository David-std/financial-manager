using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;

internal static class SchemaDeployment
{
    private const string Solution = "FinancialManager";
    private const int Language = 3082;
    private static readonly string[] Tables = { "fmg_budget", "fmg_budgetallocation", "fmg_activityevent" };

    public static int Run(ServiceClient service, bool apply)
    {
        Console.WriteLine("Financial Manager schema deployment, apply=" + apply);
        foreach (var table in Tables)
        {
            var exists = TryEntity(service, table);
            Console.WriteLine("Table " + table + " exists=" + exists);
            if (!exists && apply)
            {
                CreateTable(service, table);
                Console.WriteLine("CREATED TABLE: " + table);
            }
        }
        if (!apply) return 0;

        // Every operation reads live metadata before attempting to create, making reruns safe.
        ConfigureBudget(service);
        ConfigureAllocation(service);
        ConfigureActivity(service);

        // Relations are created after all three tables exist.
        CreateLookup(service, "fmg_budget", "fmg_branch", "fmg_Branch", "Branch", "fmg_BG_Branch", false);
        CreateLookup(service, "fmg_budget", "systemuser", "fmg_ApprovedBy", "Approved By", "fmg_BG_Approver", false);
        CreateLookup(service, "fmg_budgetallocation", "fmg_budget", "fmg_Budget", "Budget", "fmg_BA_Budget", true);
        CreateLookup(service, "fmg_budgetallocation", "fmg_category", "fmg_Category", "Category", "fmg_BA_Category", true);
        CreateLookup(service, "fmg_activityevent", "systemuser", "fmg_Actor", "Actor", "fmg_AE_Actor", false);
        Console.WriteLine("SCHEMA_DEPLOYMENT_COMPLETE");
        return 0;
    }

    private static bool TryEntity(ServiceClient svc, string name)
    {
        try
        {
            var response = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            { LogicalName=name, EntityFilters=EntityFilters.Entity, RetrieveAsIfPublished=true });
            return response.EntityMetadata != null;
        }
        catch (FaultException<OrganizationServiceFault> ex)
        {
            // A missing entity gives a metadata fault. Other failures must not be treated as absence.
            var message = (ex.Detail?.Message ?? ex.Message).ToLowerInvariant();
            if (ex.Detail?.ErrorCode == unchecked((int)0x80040217) ||
                message.Contains("not found") || message.Contains("could not find") || message.Contains("does not exist") ||
                message.Contains("cannot find") || message.Contains("does not contain") ||
                message.Contains("no se encontró"))
                return false;
            throw;
        }
    }

    private static Label L(string text) => new Label(text, Language);
    private static AttributeRequiredLevelManagedProperty Required(bool yes)
        => new AttributeRequiredLevelManagedProperty(yes ? AttributeRequiredLevel.ApplicationRequired : AttributeRequiredLevel.None);

    private static void CreateTable(ServiceClient svc, string logical)
    {
        bool activity = logical == "fmg_activityevent";
        string singular = logical == "fmg_budget" ? "Budget"
            : logical == "fmg_budgetallocation" ? "Budget Allocation" : "Activity Event";
        string plural = logical == "fmg_budget" ? "Budgets"
            : logical == "fmg_budgetallocation" ? "Budget Allocations" : "Activity Events";
        string schema = logical == "fmg_budget" ? "fmg_Budget" :
            logical == "fmg_budgetallocation" ? "fmg_BudgetAllocation" : "fmg_ActivityEvent";
        var entity = new EntityMetadata
        {
            SchemaName = schema,
            DisplayName = L(singular),
            DisplayCollectionName = L(plural),
            Description = L(activity
                ? "Restricted curated business event feed, additional to native Dataverse auditing."
                : "Management spending budget data; amounts represent allocations, not bank balances."),
            OwnershipType = OwnershipTypes.UserOwned,
            IsActivity = false,
            IsAuditEnabled = new BooleanManagedProperty(true)
        };
        var primary = new StringAttributeMetadata
        {
            SchemaName = "fmg_Name",
            DisplayName = L("Name"),
            Description = L("Human-readable reference"),
            RequiredLevel = Required(true),
            MaxLength = 200
        };
        svc.Execute(new CreateEntityRequest
        {
            Entity = entity, PrimaryAttribute = primary, SolutionUniqueName = Solution
        });
    }

    private static bool HasAttribute(ServiceClient svc, string table, string name)
    {
        var response = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
        {
            LogicalName=table, EntityFilters=EntityFilters.Attributes, RetrieveAsIfPublished=true
        });
        return response.EntityMetadata.Attributes.Any(a => String.Equals(a.LogicalName,name,StringComparison.OrdinalIgnoreCase));
    }

    private static void Add(ServiceClient svc, string table, AttributeMetadata attr)
    {
        if (HasAttribute(svc, table, attr.SchemaName.ToLowerInvariant()))
        {
            Console.WriteLine("EXISTS COLUMN "+table+"."+attr.SchemaName.ToLowerInvariant());
            return;
        }
        svc.Execute(new CreateAttributeRequest { EntityName=table, Attribute=attr, SolutionUniqueName=Solution });
        Console.WriteLine("CREATED COLUMN "+table+"."+attr.SchemaName.ToLowerInvariant());
    }

    private static IntegerAttributeMetadata Number(string name, string display, int min, int max)
        => new IntegerAttributeMetadata
        {
            SchemaName=name,DisplayName=L(display),RequiredLevel=Required(false),
            MinValue=min,MaxValue=max
        };

    private static StringAttributeMetadata Text(string name, string display, int length)
        => new StringAttributeMetadata
        {
            SchemaName=name,DisplayName=L(display),RequiredLevel=Required(false),MaxLength=length
        };

    private static OptionSetMetadata Choice(string[] labels, int initial)
    {
        var option = new OptionSetMetadata { IsGlobal=false, OptionSetType=OptionSetType.Picklist };
        for (int i=0;i<labels.Length;i++)
            option.Options.Add(new OptionMetadata(L(labels[i]),initial+i));
        return option;
    }

    private static PicklistAttributeMetadata Picklist(string name,string display,string[] values,int baseValue,int? defaultValue=null)
        => new PicklistAttributeMetadata
        {
            SchemaName=name,DisplayName=L(display),RequiredLevel=Required(false),
            OptionSet=Choice(values,baseValue),DefaultFormValue=defaultValue ?? -1
        };

    private static void ConfigureBudget(ServiceClient svc)
    {
        const string table="fmg_budget";
        Add(svc,table,Number("fmg_Year","Year",1,9999));
        // The global Branch/Shared choice is deliberately NOT reused: it is an independent local option set.
        Add(svc,table,Picklist("fmg_Scope","Scope",new[]{"Branch","Shared"},797280300));
        Add(svc,table,Number("fmg_Version","Version",1,100000));
        Add(svc,table,Number("fmg_EffectiveFromMonth","Effective From Month",1,12));
        Add(svc,table,Picklist("fmg_Status","Status",
            new[]{"Draft","Submitted","Approved","Rejected","Superseded"},797280400,797280400));
        Add(svc,table,new DateTimeAttributeMetadata {
            SchemaName="fmg_ApprovedOn",DisplayName=L("Approved On"),
            RequiredLevel=Required(false),Format=DateTimeFormat.DateAndTime
        });
    }

    private static void ConfigureAllocation(ServiceClient svc)
    {
        const string table="fmg_budgetallocation";
        Add(svc,table,Number("fmg_Month","Month",1,12));
        Add(svc,table,new MoneyAttributeMetadata {
            SchemaName="fmg_Amount",DisplayName=L("Amount"),
            RequiredLevel=Required(false),MinValue=0,Precision=2,PrecisionSource=0
        });
    }

    private static void ConfigureActivity(ServiceClient svc)
    {
        const string table="fmg_activityevent";
        Add(svc,table,Picklist("fmg_EventType","Event Type",new[]{
            "TransactionCreated","TransactionPosted","TransactionVoided",
            "BudgetSubmitted","BudgetApproved","CategoryDeactivated"},797280500));
        Add(svc,table,Text("fmg_RecordType","Record Type",100));
        Add(svc,table,Text("fmg_RecordId","Record ID",36));
        Add(svc,table,Text("fmg_Summary","Summary",500));
        Add(svc,table,new DateTimeAttributeMetadata{
            SchemaName="fmg_EventTime",DisplayName=L("Event Time"),RequiredLevel=Required(false),
            Format=DateTimeFormat.DateAndTime
        });
    }

    private static void CreateLookup(ServiceClient svc, string source, string parent, string lookupSchema,
                                     string display, string relation, bool required)
    {
        if (HasAttribute(svc,source,lookupSchema.ToLowerInvariant()))
        {
            Console.WriteLine("EXISTS LOOKUP "+source+"."+lookupSchema.ToLowerInvariant());
            return;
        }
        var rel=new OneToManyRelationshipMetadata
        {
            SchemaName=relation,ReferencedEntity=parent,ReferencingEntity=source,
            CascadeConfiguration=new CascadeConfiguration
            {
                Assign=CascadeType.NoCascade,
                Delete=CascadeType.Restrict,
                Merge=CascadeType.NoCascade,
                Reparent=CascadeType.NoCascade,
                Share=CascadeType.NoCascade,
                Unshare=CascadeType.NoCascade
            }
        };
        var lookup=new LookupAttributeMetadata {
            SchemaName=lookupSchema,DisplayName=L(display),RequiredLevel=Required(required)
        };
        svc.Execute(new CreateOneToManyRequest {
            OneToManyRelationship=rel, Lookup=lookup, SolutionUniqueName=Solution
        });
        Console.WriteLine("CREATED LOOKUP "+source+"."+lookupSchema.ToLowerInvariant()+
                          " -> "+parent+" ("+relation+")");
    }
}
