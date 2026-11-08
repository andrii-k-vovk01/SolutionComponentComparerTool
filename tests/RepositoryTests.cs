using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SolutionComponentComparer.Core;
using SolutionComponentComparer.Data;

// Dev-only: checks paging + parsing of DataverseRepository against a fake IOrganizationService.
namespace SolutionComponentComparer.Tests
{
internal static class RepositoryTests
{
    private class FakeService : IOrganizationService
    {
        public readonly List<QueryExpression> Queries = new List<QueryExpression>();
        public Guid Sol1 = Guid.NewGuid();

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var q = (QueryExpression)query;
            Queries.Add(q);
            var page = q.PageInfo.PageNumber;
            var col = new EntityCollection();
            if (q.EntityName == "solutioncomponent")
            {
                for (int i = 0; i < 3; i++)
                {
                    var e = new Entity("solutioncomponent", Guid.NewGuid());
                    e["solutionid"] = new EntityReference("solution", Sol1);
                    e["objectid"] = Guid.NewGuid();
                    e["componenttype"] = new OptionSetValue(page == 1 ? 61 : 10045);
                    if (i == 0) e["rootcomponentbehavior"] = new OptionSetValue(0);
                    e.FormattedValues["componenttype"] = page == 1 ? "Web Resource" : "Connection Reference";
                    col.Entities.Add(e);
                }
                col.MoreRecords = page < 2;
                col.PagingCookie = "<cookie page=\"" + page + "\" />";
            }
            else if (q.EntityName == "solution")
            {
                var e = new Entity("solution", Sol1);
                e["uniquename"] = "Core";
                e["friendlyname"] = "Core";
                e["ismanaged"] = true;
                e["isvisible"] = true;
                e["pub.friendlyname"] = new AliasedValue("publisher", "friendlyname", "UDS");
                col.Entities.Add(e);
            }
            return col;
        }

        public Guid Create(Entity entity) { throw new NotSupportedException("read-only"); }
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) { throw new NotSupportedException(); }
        public void Update(Entity entity) { throw new NotSupportedException("read-only"); }
        public void Delete(string entityName, Guid id) { throw new NotSupportedException("read-only"); }
        public OrganizationResponse Execute(OrganizationRequest request) { throw new NotSupportedException(); }
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotSupportedException("read-only"); }
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) { throw new NotSupportedException("read-only"); }
    }

    public static int Run()
    {
        int failures = 0;
        Action<bool, string> check = (ok, msg) => { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + msg); if (!ok) failures++; };

        var svc = new FakeService();
        var repo = new DataverseRepository(svc);

        var sols = repo.LoadSolutions();
        check(sols.Count == 1 && sols[0].UniqueName == "Core" && sols[0].IsManaged && sols[0].Publisher == "UDS", "solutions parsed incl. aliased publisher");

        var msgs = new List<string>();
        var m = repo.LoadMemberships(new List<Guid> { svc.Sol1 }, msgs.Add);
        check(m.Count == 6, "two pages of components read: " + m.Count);
        check(m.Count(x => x.ComponentType == 10045 && x.TypeLabel == "Connection Reference") == 3, "type + formatted label parsed");
        check(m.Count(x => x.RootBehavior == 0) == 2, "root behavior parsed");
        var compQueries = svc.Queries.Where(q => q.EntityName == "solutioncomponent").ToList();
        check(compQueries.Count == 2 && compQueries[1].PageInfo.PageNumber == 2 && compQueries[1].PageInfo.PagingCookie == "<cookie page=\"1\" />", "paging cookie forwarded");
        check(msgs.Count > 0 && msgs.Last().Contains("6"), "progress reported: " + (msgs.Count > 0 ? msgs.Last() : ""));

        Console.WriteLine(failures == 0 ? "ALL REPOSITORY TESTS PASSED" : failures + " FAILED");
        return failures;
    }
}
}
