using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_IsSupersetOf_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        public void is_superset_of_example()
        {
            // SAMPLE: is_superset_of
            // Finds all Posts whose Tags is superset of
            // c#, json, or postgres
            var posts = theSession.Query<Post>()
                .Where(x => x.Tags.IsSupersetOf("c#", "json", "postgres"));

            // ENDSAMPLE
        }

        [Fact]
        public void Can_query_by_tags()
        {
            // given
            Target[] targets =
            {
                new Target {Tags = new[] {"c#"}},
                new Target {Tags = new[] {"c#", "json", "webapi"}},
                new Target {Tags = new[] {"c#", "logging"}},
                new Target {Tags = new[] {"c#", "mssql"}},
                new Target {Tags = new[] {"c#", "mssql", "aspnet"}},
                new Target {Tags = new[] {"sql", "mssql"}}
            };
            theStore.BulkInsert(targets);

            var tags = new[] {"c#", "mssql"};

            // than
            var found = theSession
                .Query<Target>()
                .Where(x => x.Tags.IsSupersetOf(tags))
                .ToArray();

            var expected = targets
                .Where(x => x.Tags.IsSupersetOf(tags))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArray();

            // than
            found.Count().ShouldBe(2);
            found.OrderBy(x => x.Id).Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(expected);
        }
    }
}