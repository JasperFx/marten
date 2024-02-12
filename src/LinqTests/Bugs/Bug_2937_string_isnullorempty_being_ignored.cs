using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_2937_string_isnullorempty_being_ignored : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public record ObjectWithString(Guid Id, string String);

    public Bug_2937_string_isnullorempty_being_ignored(ITestOutputHelper output)
    {
        _output = output;
        theSession.Logger = new TestOutputMartenLogger(_output);
    }

    [Fact]
    public async Task broken_linq_condition_4()
    {
        theSession.Store(new ObjectWithString(Guid.NewGuid(), "Item A"));
        theSession.Store(new ObjectWithString(Guid.NewGuid(), "Item B"));
        await theSession.SaveChangesAsync();


        var queryValue = "Item A";
        var items = await theSession.Query<ObjectWithString>()
            .Where(x => string.IsNullOrEmpty(queryValue) || x.String == queryValue).ToListAsync();

        Assert.Single(items);
    }
}
