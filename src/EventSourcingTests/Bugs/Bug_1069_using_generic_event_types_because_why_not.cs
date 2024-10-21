using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_1069_using_generic_event_types_because_why_not: BugIntegrationContext
{
    public class Envelope<T>
    {
        public T Value { get; set; }
        public Guid ExecutingUserId { get; set; }
    }

    public class Created
    {
        public Guid Id { get; set; }
    }

    public class Updated
    {
        public String UpdateValue { get; set; }
    }

    [Fact]
    public async Task try_to_save_then_load_events()
    {
        var streamId = Guid.NewGuid();
        var event1 = new Envelope<Created> { Value = new Created { Id = Guid.NewGuid() } };
        var event2 = new Envelope<Updated> { Value = new Updated { UpdateValue = "something" } };

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, event1, event2);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.LightweightSession())
        {
            var events = await session.Events.FetchStreamAsync(streamId);
            events.Select(x => x.Data.GetType())
                .ShouldHaveTheSameElementsAs(typeof(Envelope<Created>), typeof(Envelope<Updated>));
        }
    }

    [Fact]
    public async Task try_to_save_then_load_events_across_stores()
    {
        var streamId = Guid.NewGuid();
        var event1 = new Envelope<Created> { Value = new Created { Id = Guid.NewGuid() } };
        var event2 = new Envelope<Updated> { Value = new Updated { UpdateValue = "something" } };

        using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId, event1, event2);
            await session.SaveChangesAsync();
        }

        var store2 = SeparateStore(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
        });

        using (var session = store2.LightweightSession())
        {
            var events = session.Events.FetchStream(streamId);
            events.Select(x => x.Data.GetType())
                .ShouldHaveTheSameElementsAs(typeof(Envelope<Created>), typeof(Envelope<Updated>));
        }
    }

}
