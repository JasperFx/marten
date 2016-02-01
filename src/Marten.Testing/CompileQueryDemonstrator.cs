using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Testing.Documents;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class CompileQueryDemonstrator
    {
        [Fact]
        public void look_at_compiled_queries()
        {
            var container = Container.For<DevelopmentModeRegistry>();

            var store = container.GetInstance<IDocumentStore>();

            // Compile a Linq query that "remembers" the underlying NpgsqlCommand object for the query
            var query1 = store.CompileQuery<User, string, string>()
                .For<IList<User>>((q, first, last) => q.Where(x => x.FirstName == first && x.LastName == last).ToList());

            using (var session = store.QuerySession())
            {
                // Execute the compiled query against the current Session
                var users = query1.Query(session, "Jeremy", "Miller");

                users.Each(x => Debug.WriteLine(x.FullName));
            }

        }
    }
}