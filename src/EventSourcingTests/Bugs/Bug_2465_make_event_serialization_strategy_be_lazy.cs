using System.Text.Json.Nodes;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Services;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2465_make_event_serialization_strategy_be_lazy : BugIntegrationContext
{
    [Fact]
    public async Task try_it_out()
    {
        StoreOptions(o =>
        {
            o.Events.StreamIdentity = StreamIdentity.AsString;

            o.Events.AddEventType<TestEvent>();

            // If this is set before adding the events works fine
            // Qualified since Weasel 9.30.0 lifted an STJ serializer of the same simple name into
            // Weasel.Core (weasel#559); this test is about Marten's.
            o.Serializer(
                new Marten.Services.SystemTextJsonSerializer { EnumStorage = EnumStorage.AsString });
        });
        await using var session = theStore.LightweightSession();
        var @event = new TestEvent()
        {
            Data = JsonObject.Parse(@"{""Value"":1}")
        };

        session.Events.Append("test-id", @event);
        await session.SaveChangesAsync();

        // The error is thrown here
        var allEvents = await session.Events.QueryAllRawEvents().ToListAsync();
    }
}

public class TestEvent
{
    public JsonNode Data { get; set; }
}
