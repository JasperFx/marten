using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    [ControlledQueryStoryteller]
    public class query_with_IsSubsetOf_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        public query_with_IsSubsetOf_Tests()
        {
            _allTargets = new[]
            {
                CreateTarget("c#"),
                CreateTarget("c#", "json", "webapi"),
                CreateTarget("c#", "logging"),
                CreateTarget("c#", "mssql"),
                CreateTarget("c#", "mssql", "aspnet"),
                CreateTarget("sql", "mssql"),
                CreateTarget(".net", "json", "mssql", "c#")
            };
            theStore.BulkInsert(_allTargets);
        }

        public void is_subset_of_example()
        {
            // SAMPLE: is_subset_of
            // Finds all Posts whose Tags is subset of
            // c#, json, or postgres
            var posts = theSession.Query<Post>()
                .Where(x => x.Tags.IsSubsetOf("c#", "json", "postgres"));

            // ENDSAMPLE
        }

        private readonly Target[] _allTargets;

        private static Target CreateTarget(params string[] tags)
        {
            return new Target {TagsArray = tags, TagsHashSet = new HashSet<string>(tags)};
        }

        [Fact]
        public void Can_query_by_array()
        {
            // given
            var tags = new[] {"c#", "mssql"};

            // than
            var found = theSession
                .Query<Target>()
                .Where(x => x.TagsArray.IsSubsetOf(tags))
                .ToArray();

            var expected = _allTargets
                .Where(x => x.TagsArray.IsSubsetOf(tags))
                .ToArray()
                .OrderBy(x => x.Id)
                .Select(x => x.Id);

            // than
            found.Count().ShouldBe(2);
            found.OrderBy(x => x.Id).Select(x => x.Id).ShouldHaveTheSameElementsAs(expected);
        }

        [Fact]
        public void Can_query_by_hashset()
        {
            // given
            var tags = new[] {"c#", "mssql"};

            // than
            var found = theSession
                .Query<Target>()
                .Where(x => x.TagsHashSet.IsSubsetOf(tags))
                .ToArray();

            var expected = _allTargets
                .Where(x => x.TagsHashSet.IsSubsetOf(tags))
                .ToArray()
                .OrderBy(x => x.Id)
                .Select(x => x.Id);

            // than
            found.Count().ShouldBe(2);
            found.OrderBy(x => x.Id).Select(x => x.Id).ShouldHaveTheSameElementsAs(expected);
        }
    }
}
