## Event Projections

sub-classing the `Marten.Events.EventProjection` class will let you efficiently write a projection where you can explicitly define document operations
on individual events. In essence, the `EventProjection` recipe effectively does pattern matching for you. 

To show off what `EventProjection` does, here's a sample that uses pretty well everything that `EventProjection` supports:

<!-- snippet: sample_SampleEventProjection -->
<a id='snippet-sample_sampleeventprojection'></a>
```cs
public class SampleEventProjection : EventProjection
{
    public SampleEventProjection()
    {
        // Inline document operations
        Project<Event1>((e, ops) =>
        {
            // I'm creating a single new document, but
            // I can do as many operations as I want
            ops.Store(new Document1
            {
                Id = e.Id
            });
        });

        Project<StopEvent1>((e, ops) =>
        {
            ops.Delete<Document1>(e.Id);
        });

        ProjectAsync<Event3>(async (e, ops) =>
        {
            var lookup = await ops.LoadAsync<Lookup>(e.LookupId);
            // now use the lookup document and the event to carry
            // out other document operations against the ops parameter
        });
    }

    // This is the conventional method equivalents to the inline calls above
    public Document1 Create(Event1 e) => new Document1 {Id = e.Id};

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Examples/SampleEventProjection.cs#L59-L111' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_sampleeventprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Events/Examples/SampleEventProjection.cs#L12-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_register_event_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `EventProjection` supplies the `ProjectEvent()` and `ProjectEventAsync()` methods if you prefer to use inline Lambda methods to define the operations
that way. Your other option is to use either the `Create()` or `Project()` method conventions.

## Create() Method Convention

The `Create()` method can accept these arguments:

* The actual event type or `Event<T>` where `T` is the event type. One of these is required
* `IEvent` to get access to the event metadata
* Optionally take in `IDocumentOperations` if you need to access other data. This interface supports all the functionality of `IQuerySession`

The `Create()` method needs to return either:

* The document to be created
* Or `Task<T>` where the `T` is the document that is going to be created in this projection

## Project() Method Convention

The `Project()` methods can accept these arguments:

* The actual event type or `Event<T>` where `T` is the event type. One of these is required.
* `IEvent` to get access to the event metadata
* `IDocumentOperations` is mandatory, and this is what you'd use to register any document operations

The return value must be either `void` or `Task` depending on whether or not the method needs to be asynchronous
