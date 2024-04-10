# Event Subscriptions <Badge type="tip" text="7.7" />

Hey folks, there will be much more on this topic soon. Right now, it's in Marten > 7.6, but part of the core Marten team is
working with a client to prove out this feature. When that's done, we'll fill in the docs and sample code.

<!-- snippet: sample_ISubscription -->
<a id='snippet-sample_isubscription'></a>
```cs
/// <summary>
/// Basic abstraction for custom subscriptions to Marten events through the async daemon. Use this in
/// order to do custom processing against an ordered stream of the events
/// </summary>
public interface ISubscription : IAsyncDisposable
{
    /// <summary>
    /// Processes a page of events at a time
    /// </summary>
    /// <param name="page"></param>
    /// <param name="controller">Use to log dead letter events that are skipped or to stop the subscription from processing based on an exception</param>
    /// <param name="operations">Access to Marten queries and writes that will be committed with the progress update for this subscription</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task<IChangeListener> ProcessEventsAsync(EventRange page, ISubscriptionController controller,
        IDocumentOperations operations,
        CancellationToken cancellationToken);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten/Subscriptions/ISubscription.cs#L9-L30' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_isubscription' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Registering Subscriptions

## Event Filtering

## Rewinding or Replaying Subscriptions
