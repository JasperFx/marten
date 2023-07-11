using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Operators;

public class is_subset_of_operator : IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public is_subset_of_operator(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        _output = output;
    }

    protected override Task fixtureSetup()
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
        return theStore.BulkInsertAsync(_allTargets);
    }

    public void is_subset_of_example()
    {
        #region sample_is_subset_of
        // Finds all Posts whose Tags is subset of
        // c#, json, or postgres
        var posts = theSession.Query<Post>()
            .Where(x => x.Tags.IsSubsetOf("c#", "json", "postgres"));

        #endregion
    }

    private Target[] _allTargets;

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

        theSession.Logger = new TestOutputMartenLogger(_output);

        /*
         * CURRENT:
select d.id, d.data from public.mt_doc_target as d where CAST(d.data ->> 'tags' as jsonb) <@ :p0
  p0: ["c#","mssql"]



            BEFORE:
select d.id, d.data from public.mt_doc_target as d where CAST(d.data ->> 'TagsHashSet' as jsonb) <@ :p0
  p0: ["c#","mssql"]
         *
         */

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
