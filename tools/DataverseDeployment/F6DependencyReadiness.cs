using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

// DEV solution-metadata inspection and guarded additive component registration.
// Nothing in this class manipulates financial data, plug-in steps, roles or grants.
internal static class F6DependencyReadiness
{
    private const string SolutionName = "FinancialManager";
    private const int UxProjectComponentType = 10384; // read from DEV solutioncomponent records
    private const string SourceFolder = "power-platform/solutions/FinancialManager/src";

    private static Guid[] SourceProjectIds()
    {
        var folder = Path.Combine(SourceFolder, "uxagentprojects");
        if (!Directory.Exists(folder))
            return Array.Empty<Guid>();
        return Directory.EnumerateDirectories(folder)
            .Where(x => File.Exists(Path.Combine(x, "uxagentproject.xml")))
            .Select(x => Guid.TryParse(Path.GetFileName(x), out var id) ? id : Guid.Empty)
            .ToArray();
    }

    private static Guid GetSolutionId(IOrganizationService service)
    {
        var q = new QueryExpression("solution")
        {
            ColumnSet = new ColumnSet("solutionid", "uniquename"), TopCount = 2
        };
        q.Criteria.AddCondition("uniquename", ConditionOperator.Equal, SolutionName);
        var hits = service.RetrieveMultiple(q).Entities;
        if (hits.Count != 1) throw new InvalidOperationException("Missing unique FinancialManager solution.");
        return hits[0].Id;
    }

    private static Entity[] FindProjectComponents(IOrganizationService service, Guid id)
    {
        var q = new QueryExpression("solutioncomponent")
        {
            ColumnSet = new ColumnSet("objectid", "solutionid", "componenttype"), TopCount = 20
        };
        q.Criteria.AddCondition("objectid", ConditionOperator.Equal, id);
        return service.RetrieveMultiple(q).Entities.ToArray();
    }

    private static bool InSolution(Entity row, Guid solutionId) =>
        row.GetAttributeValue<EntityReference>("solutionid")?.Id == solutionId;

    public static int AddExistingUxProject(IOrganizationService service, string raw)
    {
        var pinned = SourceProjectIds();
        if (pinned.Length != 4 || pinned.Contains(Guid.Empty) ||
            pinned.Distinct().Count() != 4 ||
            !Guid.TryParse(raw, out var id) || !pinned.Contains(id))
        {
            Console.Error.WriteLine("F6_UX_PROJECT_NOT_IN_PINNED_SOURCE");
            return 2;
        }
        var solutionId = GetSolutionId(service);
        var rows = FindProjectComponents(service, id);
        if (rows.Length == 0 || rows.Any(row =>
            row.GetAttributeValue<OptionSetValue>("componenttype")?.Value != UxProjectComponentType))
        {
            Console.Error.WriteLine("F6_UX_PROJECT_METADATA_MISMATCH");
            return 2;
        }
        if (rows.Any(row => InSolution(row, solutionId)))
        {
            Console.WriteLine("F6_UX_PROJECT_ALREADY_INCLUDED=" + id);
            return 0;
        }
        if (rows.Length != 1)
        {
            Console.Error.WriteLine("F6_UX_PROJECT_HAS_UNEXPECTED_SOLUTIONS");
            return 2;
        }
        service.Execute(new Microsoft.Crm.Sdk.Messages.AddSolutionComponentRequest
        {
            ComponentId = id,
            ComponentType = UxProjectComponentType,
            SolutionUniqueName = SolutionName,
            AddRequiredComponents = false,
            DoNotIncludeSubcomponents = false
        });
        if (!FindProjectComponents(service, id).Any(row => InSolution(row, solutionId)))
        {
            Console.Error.WriteLine("F6_UX_PROJECT_ADD_NOT_CONFIRMED");
            return 1;
        }
        Console.WriteLine("F6_UX_PROJECT_ADDED_VERIFIED=" + id);
        return 0;
    }

    public static int Inspect(IOrganizationService service)
    {
        var path = Path.Combine(SourceFolder, "Other", "Solution.xml");
        if (!File.Exists(path)) return 2;
        var deps = XDocument.Load(path).Descendants("MissingDependency").ToArray();
        if (deps.Length != 2 || deps.Any(d =>
            (string?)d.Element("Required")?.Attribute("type") == "uxagentproject"))
        {
            Console.Error.WriteLine("F6_UNRESOLVED_SOURCE_DEPENDENCIES=" + deps.Length);
            return 2;
        }
        var ids = SourceProjectIds();
        if (ids.Length != 4 || ids.Contains(Guid.Empty) || ids.Distinct().Count() != 4)
        {
            Console.Error.WriteLine("F6_FOUR_UX_SOURCE_COMPONENTS_REQUIRED");
            return 2;
        }
        var solutionId = GetSolutionId(service);
        foreach (var id in ids)
        {
            var rows = FindProjectComponents(service, id);
            if (!rows.Any(row => InSolution(row, solutionId) &&
                row.GetAttributeValue<OptionSetValue>("componenttype")?.Value == UxProjectComponentType))
            {
                Console.Error.WriteLine("F6_UX_PROJECT_MISSING_FROM_SOLUTION=" + id);
                return 2;
            }
            Console.WriteLine("F6_UX_PROJECT_INCLUDED_VERIFIED=" + id);
        }
        var versions = new[] {
            ("AppModuleWebResources", "2.5"),
            ("msdyn_AppFrameworkInfraExtensions", "1.0.0.27"),
        };
        foreach (var (name, requiredVersion) in versions)
        {
            var q = new QueryExpression("solution")
            {
                ColumnSet = new ColumnSet("uniquename", "version", "ismanaged"),
                TopCount = 5
            };
            q.Criteria.AddCondition("uniquename", ConditionOperator.Equal, name);
            var hits = service.RetrieveMultiple(q).Entities;
            if (!hits.Any(x => x.GetAttributeValue<string>("version") == requiredVersion &&
                x.GetAttributeValue<bool>("ismanaged")))
            {
                Console.Error.WriteLine("F6_SOURCE_MICROSOFT_PREREQUISITE_NOT_VERIFIED=" + name);
                return 2;
            }
            Console.WriteLine("F6_SOURCE_MICROSOFT_PREREQUISITE_VERIFIED=" +
                name + " " + requiredVersion);
        }
        Console.WriteLine("F6_UX_SOURCE_AND_DEV_PREREQUISITES_PASS");
        Console.WriteLine("F6_SEPARATE_TARGET_ACCEPTANCE_STILL_REQUIRED");
        return 0;
    }
}
