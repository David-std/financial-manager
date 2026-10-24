using System;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

internal static class AppNavigation
{
    private const string SitemapName = "financial_manager_bee9f134";
    private const string SolutionName = "FinancialManager";
    private const string AreaId = "area_financial_manager_bee9f134";
    private const string GroupId = "group_financial_manager_bee9f134";
    private const string SpanishLocale = "3082";

    private static readonly (string Id, string Page, string Title)[] ExpectedPages =
    {
        ("subarea_e020eb10", "92083d36-806a-43dd-b289-2fafd9e3afb1", "Gastos"),
        ("subarea_59077c11", "1a6e3c3e-fb2e-43b0-b5dd-9071483d1a1f", "Presupuestos"),
        ("subarea_71c5be31", "35a771c0-5cdf-4d5e-a295-6f893ffcf3d4", "Datos maestros"),
        ("subarea_afd6dd88", "4a317af5-b6a8-4a82-a22a-6b56447a082f", "Detalle del gasto")
    };

    private static XmlDocument Parse(string xml)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        return document;
    }

    private static Entity RequireSitemap(ServiceClient service, bool unpublished)
    {
        var query = new QueryExpression("sitemap")
        {
            ColumnSet = new ColumnSet("sitemapid", "sitemapnameunique", "sitemapxml"),
            TopCount = 2
        };
        query.Criteria.AddCondition("sitemapnameunique", ConditionOperator.Equal, SitemapName);
        var rows = unpublished
            ? ((RetrieveUnpublishedMultipleResponse)service.Execute(
                new RetrieveUnpublishedMultipleRequest { Query = query })).EntityCollection.Entities
            : service.RetrieveMultiple(query).Entities;
        if (rows.Count != 1)
            throw new InvalidOperationException($"Expected one {(unpublished ? "unpublished" : "published")} app sitemap, found {rows.Count}.");
        return rows[0];
    }

    private static XmlElement RequireElement(XmlDocument doc, string xpath)
    {
        var matching = doc.SelectNodes(xpath);
        if (matching?.Count != 1 || matching[0] is not XmlElement element)
            throw new InvalidOperationException("Unexpected site map structure at " + xpath);
        return element;
    }

    private static void RequireExpectedPages(XmlDocument doc)
    {
        if (doc.DocumentElement?.Name != "SiteMap" || doc.SelectNodes("//Area")?.Count != 1 ||
            doc.SelectNodes("//Group")?.Count != 1 || doc.SelectNodes("//SubArea")?.Count != 4)
            throw new InvalidOperationException("Unexpected navigation topology; aborting.");
        var area = RequireElement(doc, "/SiteMap/Area");
        var group = RequireElement(doc, "/SiteMap/Area/Group");
        if (area.GetAttribute("Id") != AreaId || group.GetAttribute("Id") != GroupId)
            throw new InvalidOperationException("Unexpected Area or Group identifier.");
        foreach (var (id, page, _) in ExpectedPages)
        {
            var node = RequireElement(doc, "/SiteMap/Area/Group/SubArea[@Id='" + id + "']");
            if (!Guid.TryParse(node.GetAttribute("GenPageId"), out var actual) ||
                actual != Guid.Parse(page))
                throw new InvalidOperationException("Page target has changed for " + id);
        }
    }

    private static string Label(XmlElement item, string locale)
    {
        var titles = item.SelectSingleNode("Titles") as XmlElement;
        if (titles == null) return "";
        return titles.SelectSingleNode("Title[@LCID='" + locale + "']") is XmlElement title
            ? title.GetAttribute("Title") : "";
    }

    private static void ApplyLabel(XmlDocument doc, XmlElement item, string name)
    {
        var titles = item.SelectSingleNode("Titles") as XmlElement;
        if (titles == null)
        {
            titles = doc.CreateElement("Titles");
            item.PrependChild(titles);
        }
        var title = titles.SelectSingleNode("Title[@LCID='" + SpanishLocale + "']") as XmlElement;
        if (title == null)
        {
            title = doc.CreateElement("Title");
            title.SetAttribute("LCID", SpanishLocale);
            titles.AppendChild(title);
        }
        title.SetAttribute("Title", name);
        if (item.HasAttribute("ResourceId"))
        {
            var resource = item.GetAttribute("ResourceId");
            if (!resource.StartsWith("SitemapDesigner.New", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected localized resource id: " + resource);
            item.RemoveAttribute("ResourceId");
        }
    }

    private static bool IsCorrect(XmlDocument doc)
    {
        RequireExpectedPages(doc);
        var area = RequireElement(doc, "/SiteMap/Area");
        var group = RequireElement(doc, "/SiteMap/Area/Group");
        if (Label(area, SpanishLocale) != "Financial Manager" ||
            Label(group, SpanishLocale) != "Gestión financiera" ||
            area.HasAttribute("ResourceId") || group.HasAttribute("ResourceId"))
            return false;
        foreach (var (id, _, title) in ExpectedPages)
        {
            var node = RequireElement(doc, "/SiteMap/Area/Group/SubArea[@Id='" + id + "']");
            if (Label(node, SpanishLocale) != title || node.HasAttribute("ResourceId"))
                return false;
        }
        return true;
    }

    public static int Run(ServiceClient service, bool apply)
    {
        var published = RequireSitemap(service, false);
        var unpublished = RequireSitemap(service, true);
        var publishedXml = published.GetAttributeValue<string>("sitemapxml") ?? "";
        var unpublishedXml = unpublished.GetAttributeValue<string>("sitemapxml") ?? "";
        var publishedDoc = Parse(publishedXml);
        var unpublishedDoc = Parse(unpublishedXml);
        RequireExpectedPages(publishedDoc);
        RequireExpectedPages(unpublishedDoc);
        Console.WriteLine("SITEMAP_ID_MATCH=" + (published.Id == unpublished.Id));
        Console.WriteLine("PUBLISHED_LOCALIZED=" + IsCorrect(publishedDoc));
        Console.WriteLine("UNPUBLISHED_LOCALIZED=" + IsCorrect(unpublishedDoc));
        Console.WriteLine("PUBLISHED_DEFAULT_RESOURCES=" +
            (publishedDoc.SelectNodes("//*[@ResourceId]")?.Count ?? 0));
        Console.WriteLine("UNPUBLISHED_DEFAULT_RESOURCES=" +
            (unpublishedDoc.SelectNodes("//*[@ResourceId]")?.Count ?? 0));
        if (!apply) return 0;
        if (IsCorrect(publishedDoc) && IsCorrect(unpublishedDoc))
        {
            Console.WriteLine("NAVIGATION_ALREADY_LOCALIZED");
            return 0;
        }
        if (published.Id != unpublished.Id)
            throw new InvalidOperationException("Published/unpublished sitemap IDs differ; no update.");
        if (!IsCorrect(unpublishedDoc))
        {
            var source = unpublishedDoc;
            ApplyLabel(source, RequireElement(source, "/SiteMap/Area"), "Financial Manager");
            ApplyLabel(source, RequireElement(source, "/SiteMap/Area/Group"), "Gestión financiera");
            foreach (var (id, _, title) in ExpectedPages)
                ApplyLabel(source, RequireElement(source,
                    "/SiteMap/Area/Group/SubArea[@Id='" + id + "']"), title);
            if (!IsCorrect(source))
                throw new InvalidOperationException("Prepared sitemap failed structure/locale verification.");
            var backup = Path.Combine(Path.GetTempPath(),
                "FinancialManager-sitemap-before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".xml");
            File.WriteAllText(backup, unpublishedXml);
            Console.WriteLine("SITEMAP_BACKUP_CREATED=" + backup);
            var patch = new Entity("sitemap") { Id = unpublished.Id };
            patch["sitemapxml"] = source.OuterXml;
            var update = new UpdateRequest { Target = patch };
            update.Parameters["SolutionUniqueName"] = SolutionName;
            service.Execute(update);
            var afterWrite = RequireSitemap(service, true);
            var afterWriteDoc = Parse(afterWrite.GetAttributeValue<string>("sitemapxml") ?? "");
            if (!IsCorrect(afterWriteDoc))
                throw new InvalidOperationException("Update was not retained in unpublished sitemap; NOT publishing.");
        }
        Console.WriteLine("UNPUBLISHED_NAVIGATION_VERIFIED");
        // Publishing the sitemap alone does not publish the parent model-driven app.
        // PublishXml appmodules targets only the existing Financial Manager app.
        service.Execute(new PublishXmlRequest
        {
            ParameterXml = "<importexportxml><appmodules><appmodule>82dcca24-efce-f111-b618-70a8a5aca0ae</appmodule></appmodules></importexportxml>"
        });
        var afterPublish = Parse(RequireSitemap(service, false)
            .GetAttributeValue<string>("sitemapxml") ?? "");
        if (!IsCorrect(afterPublish))
            throw new InvalidOperationException("Published sitemap does not yet reflect updated navigation.");
        Console.WriteLine("PUBLISHED_NAVIGATION_VERIFIED");
        return 0;
    }
}
