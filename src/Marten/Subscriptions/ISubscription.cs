using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using Marten.Events.Daemon;

namespace Marten.Subscriptions;

#region sample_ISubscription

/// <summary>
/// Basic abstraction for custom subscriptions to Marten events through the async daemon. Use this in
/// order to do custom processing against an ordered stream of the events
/// </summary>
public interface ISubscription
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

#endregion
