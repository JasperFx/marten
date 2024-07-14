using Marten;
using Marten.Events;
using Marten.Events.Projections;

namespace Daemon.StressTest;

public class StressedEventProjection : EventProjection
{
    public StressedEventProjection()
    {
        Options.EnableDocumentTrackingByIdentity = true;

        Options.DeleteViewTypeOnTeardown(typeof(EventProjectionDocument));
    }

    public EventProjectionDocument Create(IEvent<CreateEventProjectionEvent> @event, IDocumentOperations ops)
    {
         return new EventProjectionDocument(@event.Data.Id, "RandomStuff");
    }

    public async Task Project(IEvent<UpdateEventProjectionEvent> @event, IDocumentOperations ops)
    {
        var doc = await ops.LoadAsync<EventProjectionDocument>(@event.Data.Id);

        var updated = doc with { Stuff = "RandomStuff2" };

        ops.Update(updated);
    }
}



public record CreateEventProjectionEvent(Guid Id);

public record UpdateEventProjectionEvent(Guid Id);

public record EventProjectionDocument(Guid Id, string Stuff);
