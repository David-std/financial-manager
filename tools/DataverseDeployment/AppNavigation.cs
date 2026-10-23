using System;
using System.Linq;
using System.Xml;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Messages;

internal static class AppNavigation
{
    private const string SitemapName="financial_manager_bee9f134";
    private const string DetailPage="4a317af5-b6a8-4a82-a22a-6b56447a082f";
    public static int Run(ServiceClient service,bool apply)
    {
        var query=new QueryExpression("sitemap"){
            ColumnSet=new ColumnSet("sitemapid","sitemapnameunique","sitemapxml"),TopCount=3
        };
        query.Criteria.AddCondition("sitemapnameunique",ConditionOperator.Equal,SitemapName);
        var rows=service.RetrieveMultiple(query).Entities;
        Console.WriteLine("APP_SITEMAP_ROWS="+rows.Count);
        if(rows.Count!=1) return 6;
        var row=rows[0];
        var xml=row.GetAttributeValue<string>("sitemapxml")??"";
        var doc=new XmlDocument{XmlResolver=null};
        doc.LoadXml(xml);
        var node=doc.SelectNodes("//SubArea[@GenPageId='"+DetailPage+"']/Titles/Title")?
            .OfType<XmlElement>().FirstOrDefault(e=>e.GetAttribute("LCID")=="1033");
        if(node==null) {Console.Error.WriteLine("Detail page not in sitemap. No mutation.");return 7;}
        var title=node.GetAttribute("Title");
        Console.WriteLine("DETAIL_SITEMAP_LABEL="+title);
        if(!apply)return 0;
        if(title=="Detalle del gasto") {Console.WriteLine("Already labeled");return 0;}
        if(title!="Generated Page") {Console.Error.WriteLine("Unexpected sitemap title; no mutation.");return 8;}
        node.SetAttribute("Title","Detalle del gasto");
        var update=new Entity("sitemap") {Id=row.Id};
        update["sitemapxml"]=doc.OuterXml;
        service.Execute(new UpdateRequest{Target=update});
        Console.WriteLine("SITEMAP_DETAIL_LABEL_UPDATED");
        service.Execute(new PublishXmlRequest {ParameterXml = "<importexportxml><sitemaps><sitemap></sitemap></sitemaps></importexportxml>"});
        Console.WriteLine("SITEMAP_PUBLISH_REQUEST_COMPLETED");
        // Check the published representation after a targeted sitemap publish.
        var current=service.Retrieve("sitemap",row.Id,new ColumnSet("sitemapxml"));
        if(!(current.GetAttributeValue<string>("sitemapxml")??"").Contains("Detalle del gasto"))
            Console.WriteLine("WARNING: sitemap read may reflect cached metadata; verify the label from a fresh unmanaged solution export.");
        else
            Console.WriteLine("SITEMAP_READBACK_CONFIRMED");
        return 0;
    }
}
