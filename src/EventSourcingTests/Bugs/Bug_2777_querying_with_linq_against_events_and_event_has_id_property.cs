using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_2777_querying_with_linq_against_events_and_event_has_id_property : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_2777_querying_with_linq_against_events_and_event_has_id_property(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task do_not_blow_up()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Logger(new TestOutputMartenLogger(_output));
        }, true);

        await using (var session = TheStore.LightweightSession())
        {
            session.Events.Append("a", new DummyEvent("a"));

            await session.SaveChangesAsync();
        }

        await using (var session = TheStore.QuerySession())
        {
            var ids = await session.Events.QueryRawEventDataOnly<DummyEvent>()
                .Select(d => d.Id)          // This causes the operation to fail
                .ToListAsync();
        }
    }
}

public record DummyEvent(string Id);
