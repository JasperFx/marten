using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class Bug_2895_no_stack_overflow_please : BugIntegrationContext
{
    [Fact]
    public async Task do_not_blow_up_in_linq_parsing()
    {
        string? category = null;
        await theSession.Query<Entity4>().Where(p =>
                p.SomeBool == false && (category == null || p.StringArray.Contains(category)))
            .ToListAsync();
    }
}

public sealed record Entity4(Guid Id, bool SomeBool, string[] StringArray);
