using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

public class is_super_set_of_operator : IntegrationContext
{
    public void is_superset_of_example()
    {
        #region sample_is_superset_of
        // Finds all Posts whose Tags is superset of
        // c#, json, or postgres
        var posts = theSession.Query<Post>()
            .Where(x => x.Tags.IsSupersetOf("c#", "json", "postgres"));

        #endregion
    }

    private Target[] _allTargets;

    public is_super_set_of_operator(DefaultStoreFixture fixture) : base(fixture)
    {

    }

    protected override Task fixtureSetup()
    {
        _allTargets =
        [
            CreateTarget("c#"),
            CreateTarget("c#", "json", "webapi"),
            CreateTarget("c#", "logging"),
            CreateTarget("c#", "mssql"),
            CreateTarget("c#", "mssql", "aspnet"),
            CreateTarget("sql", "mssql"),
            CreateTarget(".net", "json", "mssql", "c#")
        ];
        return theStore.BulkInsertAsync(_allTargets);
    }

    [Fact]
    public void Can_query_by_array()
    {
        // given
        var tags = new[] {"c#", "mssql"};

        // than
        var found = theSession
            .Query<Target>()
            .Where(x => x.TagsArray.IsSupersetOf(tags))
            .ToArray();

        var expected = _allTargets
            .Where(x => x.TagsArray.IsSupersetOf(tags))
            .ToArray()
            .OrderBy(x => x.Id)
            .Select(x => x.Id);

        // than
        found.Count().ShouldBe(3);
        found.OrderBy(x => x.Id).Select(x => x.Id).ShouldHaveTheSameElementsAs(expected);
    }

    [Fact]
    public void Can_query_by_hashset()
    {
        // given
        var tags = new[] { "c#", "mssql" };

        // than
        var found = theSession
            .Query<Target>()
            .Where(x => x.TagsHashSet.IsSupersetOf(tags))
            .ToArray();

        var expected = _allTargets
            .Where(x => x.TagsHashSet.IsSupersetOf(tags))
            .ToArray()
            .OrderBy(x => x.Id)
            .Select(x => x.Id);

        // than
        found.Count().ShouldBe(3);
        found.OrderBy(x => x.Id).Select(x => x.Id).ShouldHaveTheSameElementsAs(expected);
    }

    private static Target CreateTarget(params string[] tags)
    {
        return new Target {TagsArray = tags, TagsHashSet = new HashSet<string>(tags)};
    }
}
