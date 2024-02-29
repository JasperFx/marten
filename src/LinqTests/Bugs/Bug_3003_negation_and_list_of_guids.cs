using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;

namespace LinqTests.Bugs;

public class Bug_3003_negation_and_list_of_guids : BugIntegrationContext
{
    public record SimpleRecord(Guid Id);

    [Fact]
    public async Task broken_linq_condition_2()
    {
        var filterA = new List<Guid>() { Guid.NewGuid()};

        await theSession.Query<SimpleRecord>().Where(x =>
                !filterA.Any() || filterA.Contains(x.Id))
            .ToListAsync();
    }
}
