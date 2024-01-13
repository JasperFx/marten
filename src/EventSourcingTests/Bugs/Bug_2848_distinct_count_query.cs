using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Archiving;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2848_distinct_count_query : BugIntegrationContext
{
    [Fact]
    public async Task can_make_the_query()
    {
        await TheSession.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived())
            .Select(x => x.StreamKey)
            .Distinct()
            .CountAsync();
    }
}
