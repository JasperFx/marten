# Event Projections

Sub-classing the `Marten.Events.EventProjection` class will let you efficiently write a projection where you can explicitly define document operations
on individual events. In essence, the `EventProjection` recipe does pattern matching for you.

To show off what `EventProjection` does, here's a sample that uses most features that `EventProjection` supports:

<!-- snippet: sample_SampleEventProjection -->
<a id='snippet-sample_sampleeventprojection'></a>
```cs
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
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/SampleEventProjection.cs#L72-L128' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sampleeventprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Do note that at any point you can access event metadata by accepting `IEvent<T>` where `T` is the event type instead of just the event type. You can also take in an additional variable for `IEvent` to just
access the current event metadata (it's the same object regardless, but sometimes taking in both the event body and the event metadata results in simpler code);

And that projection can run either inline or asynchronously with the registration as shown below:

<!-- snippet: sample_register_event_projection -->
<a id='snippet-sample_register_event_projection'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Run inline...
    opts.Projections.Add(new SampleEventProjection(), ProjectionLifecycle.Inline);

    // Or nope, run it asynchronously
    opts.Projections.Add(new SampleEventProjection(), ProjectionLifecycle.Async);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/SampleEventProjection.cs#L15-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_register_event_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `EventProjection` supplies the `ProjectEvent()` and `ProjectEventAsync()` methods if you prefer to use inline Lambda methods to define the operations
that way. Your other option is to use either the `Create()` or `Project()` method conventions.

## Create() Method Convention

The `Create()` method can accept these arguments:

* The actual event type or `IEvent<T>` where `T` is the event type. One of these is required
* `IEvent` to get access to the event metadata
* Optionally take in `IDocumentOperations` if you need to access other data. This interface supports all the functionality of `IQuerySession`

The `Create()` method needs to return either:

* The document to be created
* Or `Task<T>` where the `T` is the document that is going to be created in this projection

## Project() Method Convention

The `Project()` methods can accept these arguments:

* The actual event type or `IEvent<T>` where `T` is the event type. One of these is required.
* `IEvent` to get access to the event metadata
* `IDocumentOperations` is mandatory, and this is what you'd use to register any document operations

The return value must be either `void` or `Task` depending on whether or not the method needs to be asynchronous

## Reusing Documents in the Same Batch

::: tip
If you find yourself wanting this feature, maybe look to use one of the aggregation projection recipes instead that are heavily optimized for this use case.
:::

If there is any need within your `EventProjection` to use and/or modify the exact same document within the same batch of events -- and remember that event batches in projection rebuilds are measured in the hundreds -- you may want to force Marten to use its identity map tracking to cache those documents in memory rather than reloading them. And also to make sure you are applying changes to the correct version of the document as well if you are doing some kind of aggregation within an `EventProjection`.

To use identity map tracking for a particular projection, you should enable it in its async option by setting the `EnableDocumentTrackingByIdentity` property.

<!-- snippet: sample_using_enable_document_tracking_in_event_projection_registration -->
<a id='snippet-sample_using_enable_document_tracking_in_event_projection_registration'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    opts.Projections.Add(
        new TrackedEventProjection(),
        // Register projection to run it asynchronously
        ProjectionLifecycle.Async,
        // enable document tracking using identity map
        asyncOptions => asyncOptions.EnableDocumentTrackingByIdentity = true
    );
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/TrackedEventProjection.cs#L13-L28' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_enable_document_tracking_in_event_projection_registration' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Usage of `EnableDocumentTrackingByIdentity` is shown below for an `EventProjection` that potentially makes several changes to the same document:

::: danger
Due to the async daemon processing projection operations in parallel to applying projection updates, directly mutating the content of a tracked object may result in an exception or unexpected behavior.

When this feature is enabled, we recommend using immutable projection & collection types within the EventProjection to avoid any issues.
:::

<!-- snippet: sample_using_enable_document_tracking_in_event_projection -->
<a id='snippet-sample_using_enable_document_tracking_in_event_projection'></a>
```cs
public enum Team
{
    VisitingTeam,
    HomeTeam
}

public record Run(Guid GameId, Team Team, string Player);

public record BaseballGame
{
    public Guid Id { get; init; }
    public int HomeRuns { get; init; }
    public int VisitorRuns { get; init; }

    public int Outs { get; init; }
    public ImmutableHashSet<string> PlayersWithRuns { get; init; }
}

public class TrackedEventProjection: EventProjection
{
    public TrackedEventProjection()
    {
        throw new NotImplementedException("Redo");
        // ProjectAsync<Run>(async (run, ops) =>
        // {
        //     var game = await ops.LoadAsync<BaseballGame>(run.GameId);
        //
        //     var updatedGame = run.Team switch
        //     {
        //         Team.HomeTeam => game with
        //         {
        //             HomeRuns = game.HomeRuns + 1,
        //             PlayersWithRuns = game.PlayersWithRuns.Add(run.Player)
        //         },
        //         Team.VisitingTeam => game with
        //         {
        //             VisitorRuns = game.VisitorRuns + 1,
        //             PlayersWithRuns = game.PlayersWithRuns.Add(run.Player)
        //         },
        //     };
        //
        //     ops.Store(updatedGame);
        // });
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Examples/TrackedEventProjection.cs#L32-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_enable_document_tracking_in_event_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
