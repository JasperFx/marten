using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JasperFx.Events;

namespace Marten.Events;

/// <summary>
/// A testing standin fake for IEventStream that might be helpful in
/// unit testing
/// </summary>
/// <typeparam name="T"></typeparam>
public class StubEventStream<T> : IEventStream<T> where T : notnull
{
    /// <summary>
    /// Start from an existing aggregate -- or null
    /// </summary>
    /// <param name="aggregate"></param>
    public StubEventStream(T? aggregate)
    {
        Aggregate = aggregate;

        EventGraph = new EventGraph(new StoreOptions());
    }

    /// <summary>
    /// Start from an existing aggregate and a configuration for Marten.
    /// You only care about this overload if you are customizing event
    /// type aliases
    /// </summary>
    /// <param name="aggregate"></param>
    /// <param name="options"></param>
    public StubEventStream(T? aggregate, StoreOptions options)
    {
        Aggregate = aggregate;

        EventGraph = new EventGraph(options);
    }

    internal EventGraph EventGraph { get; }

    public void AppendOne(object @event)
    {
        EventsAppended.Add(@event);
    }

    public void AppendMany(params object[] events)
    {
        EventsAppended.AddRange(events);
    }

    public void AppendMany(IEnumerable<object> events)
    {
        EventsAppended.AddRange(events);
    }

    /// <summary>
    /// A record of any events appended to this stream
    /// </summary>
    public List<object> EventsAppended { get; } = new();

    public T? Aggregate { get; }
    public long? StartingVersion { get; set; }
    public long? CurrentVersion { get; set; }
    public CancellationToken Cancellation { get; } = default;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = Guid.NewGuid().ToString();

    public IReadOnlyList<IEvent> Events => EventsAppended.Select(x => EventGraph.BuildEvent(x)).ToList();
}
