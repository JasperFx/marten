using System;
using System.Linq;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_997_or_queries_with_hierarchical_docs: BugIntegrationContext
    {
        public class Bug997User
        {
            public Guid Id { get; set; }

            public string RealName { get; set; }

            public string DisplayName { get; set; }
        }

        public class MegaUser: Bug997User
        {
        }

        [Fact]
        public void query_with_or_on_child_document()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Bug997User>().AddSubClass<MegaUser>();
            });

            var megaUser1 = new MegaUser() { DisplayName = "Yann", RealName = "Yann Yann" };
            var megaUser2 = new MegaUser() { DisplayName = "Robin", RealName = "Robin Robin" };
            var megaUser3 = new MegaUser() { DisplayName = "Marten", RealName = "Marten Marten" };

            theStore.BulkInsert(new Bug997User[] { megaUser1, megaUser2, megaUser3 });

            using (var session = theStore.QuerySession())
            {
                session.Query<MegaUser>()
                    .Where(_ => _.DisplayName == "Yann" || _.DisplayName == "Robin").OrderBy(x => x.DisplayName).Select(x => x.DisplayName)
                    .ToList()
                    .ShouldHaveTheSameElementsAs("Robin", "Yann");
            }
        }

    }
}
