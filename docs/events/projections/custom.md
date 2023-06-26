# Custom Projections

To build your own Marten projection, you just need a class that implements the `Marten.Events.Projections.IProjection` interface shown below:

<!-- snippet: sample_IProjection -->
<a id='snippet-sample_iprojection'></a>
```cs
/// <summary>
///     Interface for all event projections
/// </summary>
public interface IProjection
{
    /// <summary>
    ///     Apply inline projections during synchronous operations
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="streams"></param>
    void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams);

    /// <summary>
    ///     Apply inline projections during asynchronous operations
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="streams"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Events/Projections/IProjection.cs#L8-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_iprojection' title='Start of snippet'>anchor</a></sup>
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

    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        var questEvents = streams.SelectMany(x => x.Events).OrderBy(s => s.Sequence).Select(s => s.Data);

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
    }

    public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        Apply(operations, streams);
        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L883-L916' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_questpatchtestprojection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

And the custom projection can be registered in your Marten `DocumentStore` like this:

<!-- snippet: sample_registering_custom_projection -->
<a id='snippet-sample_registering_custom_projection'></a>
```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("some connection string");

    // Marten.PLv8 is necessary for patching
    opts.UseJavascriptTransformsAndPatching();

    // Use inline lifecycle
    opts.Projections.Add(new QuestPatchTestProjection(), ProjectionLifecycle.Inline);

    // Or use this as an asychronous projection
    opts.Projections.Add(new QuestPatchTestProjection(), ProjectionLifecycle.Async);
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.PLv8.Testing/Patching/patching_api.cs#L830-L846' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_registering_custom_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
