using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3027_using_string_null_check : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3027_using_string_null_check(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task broken_linq_condition_3()
    {
        var guid = Guid.NewGuid();

        theSession.Store(new Bug3027Object(Guid.NewGuid(), guid, "sometext"));
        await theSession.SaveChangesAsync();
        string? str = null;

        theSession.Logger = new TestOutputMartenLogger(_output);

        var query = await theSession.Query<Bug3027Object>()
            .Where(x=> x.FilterGuid == guid && (str == null || x.SomeText == str)).ToListAsync();

        query.Count.ShouldBe(1);

    }
}

public record Bug3027Object(Guid Id, Guid FilterGuid, string SomeText);
