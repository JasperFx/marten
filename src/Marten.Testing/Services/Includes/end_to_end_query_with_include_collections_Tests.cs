using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services.Includes
{
    public class end_to_end_query_with_include_collections_Tests : DocumentSessionFixture<IdentityMap>
    {
        public class IssueList
        {
            public IssueList()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }
            public List<Guid> Issues { get; set; }
        }        

        [Fact]
        public void simple_include_for_a_collection()
        {
            var issues = Enumerable.Range(0, 3).Select(x => new Issue {Title = $"Issue {x}"}).ToList();
            var issuelisting = new IssueList { Issues = new List<Guid>(issues.Select(x => x.Id)) };
         
            theSession.StoreObjects(issues);
            theSession.Store<object>(issuelisting);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var issuesFromDb = new List<Issue>();

                var listingFromDb = query.Query<IssueList>()
                    .Include<Issue>(x => x.Issues, i => issuesFromDb.Add(i))
                    .ToList();

                issuesFromDb.Count.ShouldBe(3);
                listingFromDb.Count.ShouldBe(3);
            }
        }

        // TODO
        //[Fact]
        //public void multiple_include_for_a_collection()
        //{
        //    var issues = Enumerable.Range(0, 3).Select(x => new Issue { Title = $"Issue {x}" });
        //    var issues2 = Enumerable.Range(3, 3).Select(x => new Issue { Title = $"Issue {x}" });

        //    var issuelisting = new IssueList { Issues = new List<Guid>(issues.Select(x => x.Id)) };
        //    var issuelisting2 = new IssueList { Issues = new List<Guid>(issues2.Select(x => x.Id)) };

        //    theSession.Store<object>(issues, issues2, issuelisting, issuelisting2);
        //    theSession.SaveChanges();            

        //    using (var query = theStore.QuerySession())
        //    {                
        //        var issuesFromDb = new Dictionary<Guid, List<Issue>>();
        //        var listingFromDb = query.Query<IssueList>()
        //            .Include<List<Issue>, Guid>(x => x.Issues, issuesFromDb)
        //            .ToList();

        //        issuesFromDb.Count.ShouldBe(3);
        //    }
        //}
    }
}