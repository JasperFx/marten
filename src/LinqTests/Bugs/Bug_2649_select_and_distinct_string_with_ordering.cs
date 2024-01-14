using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_2649_select_and_distinct_string_with_ordering : BugIntegrationContext
{
    [Fact]
    public async Task can_query_with_this_combination()
    {
        await TheStore.BulkInsertAsync(Target.GenerateRandomData(100).ToArray());

        var ex = await Should.ThrowAsync<BadLinqExpressionException>(async () =>
        {
            var query = await TheSession.Query<Target>()
                .OrderBy(x=> x.String, StringComparer.InvariantCultureIgnoreCase)
                .Select(x => x.String)
                .Distinct()
                .ToListAsync();
        });


    }
}
