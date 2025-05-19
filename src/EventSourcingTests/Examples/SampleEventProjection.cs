using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;

namespace EventSourcingTests.Examples;

public static class EventProjectionRegistration
{
    public static void Register()
    {
        #region sample_register_event_projection

        var store = DocumentStore.For(opts =>
        {
            opts.Connection("some connection string");

            // Run inline...
            opts.Projections.Add(new SampleEventProjection(), ProjectionLifecycle.Inline);

            // Or nope, run it asynchronously
            opts.Projections.Add(new SampleEventProjection(), ProjectionLifecycle.Async);
        });

        #endregion
    }
}

public interface ISpecialEvent{}

public class Event1: ISpecialEvent
{
    public Guid Id { get; set; }
}

public class StopEvent1
{
    public Guid Id { get; set; }
}

public class Event2: ISpecialEvent
{
    public Guid Id { get; set; }
}

public class Event3
{
    public Guid LookupId { get; set; }
}
public class Event4{}
public class Event5{}

public class Document1
{
    public Guid Id { get; set; }
}

public class Lookup
{
    public Guid Id { get; set; }
}

public class Document2
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

#region sample_SampleEventProjection

public class SampleEventProjection : EventProjection
{
    public SampleEventProjection()
    {
        throw new NotImplementedException();
        // // Inline document operations
        // Project<Event1>((e, ops) =>
        // {
        //     // I'm creating a single new document, but
        //     // I can do as many operations as I want
        //     ops.Store(new Document1
        //     {
        //         Id = e.Id
        //     });
        // });
        //
        // Project<StopEvent1>((e, ops) =>
        // {
        //     ops.Delete<Document1>(e.Id);
        // });
        //
        // ProjectAsync<Event3>(async (e, ops) =>
        // {
        //     var lookup = await ops.LoadAsync<Lookup>(e.LookupId);
        //     // now use the lookup document and the event to carry
        //     // out other document operations against the ops parameter
        // });
    }

    // This is the conventional method equivalents to the inline calls above
    public Document1 Create(Event1 e) => new Document1 {Id = e.Id};

    // Or with event metadata
    public Document2 Create(IEvent<Event2> e) => new Document2 { Id = e.Data.Id, Timestamp = e.Timestamp };

    public void Project(StopEvent1 e, IDocumentOperations ops)
        => ops.Delete<Document1>(e.Id);

    public async Task Project(Event3 e, IDocumentOperations ops)
    {
        var lookup = await ops.LoadAsync<Lookup>(e.LookupId);
        // now use the lookup document and the event to carry
        // out other document operations against the ops parameter
    }

    // This will apply to *any* event that implements the ISpecialEvent
    // interface. Likewise, the pattern matching will also work with
    // common base classes
    public void Project(ISpecialEvent e, IDocumentOperations ops)
    {

    }
}

#endregion
