using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3024_wildcards_in_string_insensitive_comparisons : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3024_wildcards_in_string_insensitive_comparisons(ITestOutputHelper output)
    {
        _output = output;
    }

    public record ComparisionRecord(Guid Id, string Data);

    [Fact]
    public async Task ignore_case_with_percentage_wildcard_should_not_impact_results()
    {
        theSession.Store(new ComparisionRecord(Guid.NewGuid(), "MyString"));
        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var results = await theSession.Query<ComparisionRecord>()
            .Where(x => x.Data.Equals("%MyString", StringComparison.OrdinalIgnoreCase))
            .ToListAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task ignore_case_with_underscore_wildcard_should_not_impact_results()
    {
        theSession.Store(new ComparisionRecord(Guid.NewGuid(), "MyString"));
        await theSession.SaveChangesAsync();

        var results = await theSession.Query<ComparisionRecord>()
            .Where(x => x.Data.Equals("MyStrin_", StringComparison.OrdinalIgnoreCase))
            .ToListAsync();

        Assert.Empty(results);
    }
}
