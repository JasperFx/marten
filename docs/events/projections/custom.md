# Custom Projections

To build your own Marten projection, you just need a class that implements the `Marten.Events.Projections.IProjection` interface shown below:

<!-- snippet: sample_IProjection -->
<a id='snippet-sample_iprojection'></a>
```cs
/// <summary>
///     Interface for all event projections
///     IProjection implementations define the projection type and handle its projection document lifecycle
///     Optimized for inline usage
/// </summary>
public interface IProjection: IJasperFxProjection<IDocumentOperations>, IMartenRegistrable
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Projections/IProjection.cs#L10-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `StreamAction` aggregates outstanding events by the event stream, which is how Marten tracks events inside of an `IDocumentSession` that has
yet to be committed. The `IDocumentOperations` interface will give you access to a large subset of the `IDocumentSession` API to make document changes
or deletions. Here's a sample custom projection from our tests:

<!-- snippet: sample_QuestPatchTestProjection -->
<a id='snippet-sample_questpatchtestprojection'></a>
```cs
public class QuestPatchTestProjection: IProjection
{
    public Guid Id { get; set; }

    public string Name { get; set; }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events, CancellationToken cancellation)
    {
        var questEvents = events.Select(s => s.Data);

        foreach (var @event in questEvents)
        {
            if (@event is Quest quest)
            {
                operations.Store(new QuestPatchTestProjection { Id = quest.Id });
            }
            else if (@event is QuestStarted started)
            {
                operations.Patch<QuestPatchTestProjection>(started.Id).Set(x => x.Name, "New Name");
            }
        }
        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L1196-L1223' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_questpatchtestprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And the custom projection can be registered in your Marten `DocumentStore` like this:

<!-- snippet: sample_registering_custom_projection -->
<a id='snippet-sample_registering_custom_projection'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Use inline lifecycle
    opts.Projections.Add(new QuestPatchTestProjection(), ProjectionLifecycle.Inline);

    // Or use this as an asychronous projection
    opts.Projections.Add(new QuestPatchTestProjection(), ProjectionLifecycle.Async);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/PatchingTests/Patching/patching_api.cs#L1148-L1161' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_custom_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
