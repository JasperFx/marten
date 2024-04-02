using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3103_linq_query_against_event_id : BugIntegrationContext
{
    [Fact]
    public async Task generate_proper_sql()
    {
        var eventsIds = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var command = theSession.Events.QueryAllRawEvents()
            .Where(e => e.Id.In(eventsIds))
            .Where(e => e.AnyTenant())
            .ToCommand();

        command.CommandText.ShouldContain("d.id = ANY(:p0)");
        command.CommandText.ShouldNotContain("CAST(d.data ->> 'Id' as uuid)");
    }
}
