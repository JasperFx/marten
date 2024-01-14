using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqTests.Acceptance.Support;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class child_collection_queries: LinqTestContext<child_collection_queries>
{
    public child_collection_queries(DefaultQueryFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        TestOutput = output;
    }

    static child_collection_queries()
    {
        // Child document collections
        @where(x => x.Children.Any()).NoCteUsage();

        // CTE required queries
        @where(x => x.Children.Where(c => c.String.StartsWith("o")).Any());
        @where(x => x.Children.Any(c => c.String.StartsWith("o")));

        // CTE + Filter at parent
        @where(x => x.Color == Colors.Blue && x.Children.Any(c => c.String.StartsWith("o")));

        // CTE + filter has to stay at the bottom level
        @where(x => x.Color == Colors.Blue || x.Children.Any(c => c.String.StartsWith("o")));


        // Child value collections
        @where(x => x.NumberArray.Any()).NoCteUsage();
        @where(x => !x.NumberArray.Any()).NoCteUsage();
        @where(x => x.NumberArray.IsEmpty()).NoCteUsage();
        @where(x => !x.NumberArray.IsEmpty()).NoCteUsage();

        // Any or IsEmpty
        @where(x => x.Children.Any()).NoCteUsage();
        @where(x => !x.Children.Any()).NoCteUsage();
        @where(x => x.Children.IsEmpty()).NoCteUsage();
        @where(x => !x.Children.IsEmpty()).NoCteUsage();

        // Deeper Child value collections
        @where(x => x.Inner != null && x.Inner.Children != null && x.Inner.NumberArray.Any()).NoCteUsage();
        @where(x => x.Inner != null && x.Inner.Children != null && !x.Inner.NumberArray.Any()).NoCteUsage();
        @where(x => x.Inner != null && x.Inner.Children != null && x.Inner.NumberArray.IsEmpty()).NoCteUsage();
        @where(x => x.Inner != null && x.Inner.Children != null && !x.Inner.NumberArray.IsEmpty()).NoCteUsage();

        // Deeper Any or IsEmpty
        @where(x => x.Inner != null && x.Inner.Children != null && x.Inner.Children.Any()).NoCteUsage();
        @where(x => x.Inner != null && x.Inner.Children != null && !x.Inner.Children.Any()).NoCteUsage();
        @where(x => x.Inner != null && x.Inner.Children != null && x.Inner.Children.IsEmpty()).NoCteUsage();
        @where(x => x.Inner != null && x.Inner.Children != null && !x.Inner.Children.IsEmpty()).NoCteUsage();

        @where(x => x.StringArray != null && x.StringArray.Any(c => c.StartsWith("o")));

        // These permutations come from GH-2401
        @where(x => x.StringArray != null && x.String.Equals("Orange") && x.StringArray.Contains("Red"));
        @where(x => x.StringArray != null && !x.StringArray.Contains("Red") && x.String.Equals("Orange"));
        @where(x => x.StringArray != null && x.String.Equals("Orange") && !x.StringArray.Contains("Red"));
        @where(x => x.StringArray != null && x.String.Equals("Orange") && x.StringArray.Contains("Red") && x.AnotherString.Equals("one"));
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task run_query(string description)
    {
        return assertTestCase(description, Fixture.Store);
    }


    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task with_duplicated_fields(string description)
    {
        return assertTestCase(description, Fixture.DuplicatedFieldStore);
    }



    public class child_collection_is_empty_or_any: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public child_collection_is_empty_or_any(ITestOutputHelper output)
    {
        _output = output;
    }

    protected async Task withData()
    {
        await TheStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Target));
        var targets = Target.GenerateRandomData(5).ToArray();
        EmptyNumberArray = targets[0];
        EmptyNumberArray.NumberArray = Array.Empty<int>();

        HasNumberArray = targets[1];
        HasNumberArray.NumberArray = new[] { 1, 2, 3 };

        NullNumberArray = targets[2];
        NullNumberArray.NumberArray = null;

        EmptyChildren = targets[3];
        EmptyChildren.Children = Array.Empty<Target>();

        NullChildren = targets[4];
        NullChildren.Children = null;

        await TheStore.BulkInsertAsync(targets);
    }

    public Target HasNumberArray { get; set; }

    public Target NullChildren { get; set; }

    public Target NullNumberArray { get; set; }

    public Target EmptyChildren { get; set; }

    public Target EmptyNumberArray { get; set; }

    [Fact]
    public async Task is_empty_with_value_collection()
    {
        await withData();

        TheSession.Logger = new TestOutputMartenLogger(_output);

        var results = await TheSession
            .Query<Target>()
            .Where(x => x.NumberArray.IsEmpty())
            .ToListAsync();

        results.ShouldContain(NullNumberArray);
        results.ShouldContain(EmptyNumberArray);
        results.ShouldNotContain(HasNumberArray);
    }

    [Fact]
    public async Task is_not_empty_with_value_collection()
    {
        await withData();

        TheSession.Logger = new TestOutputMartenLogger(_output);

        var results = await TheSession
            .Query<Target>()
            .Where(x => !x.NumberArray.IsEmpty())
            .ToListAsync();

        results.ShouldNotContain(NullNumberArray);
        results.ShouldNotContain(EmptyNumberArray);
        results.ShouldContain(HasNumberArray);
    }
}
}



internal static class TargetExtensions
{
    public static void ShouldContain(this IEnumerable<Target> targets, Target target)
    {
        targets.Any(x => x.Id == target.Id).ShouldBeTrue();
    }

    public static void ShouldNotContain(this IEnumerable<Target> targets, Target target)
    {
        targets.Any(x => x.Id == target.Id).ShouldBeFalse();
    }
}


