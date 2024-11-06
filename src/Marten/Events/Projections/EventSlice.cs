#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Storage;
using Weasel.Core.Operations;

namespace Marten.Events.Projections;

public interface IEventSlice
{
    Tenant Tenant { get; }
    IReadOnlyList<IEvent> Events();
}

public interface IEventSlice<T>: IEventSlice
{
    void AppendEvent<TEvent>(Guid streamId, TEvent @event);
    void AppendEvent<TEvent>(string streamKey, TEvent @event);
    void AppendEvent<TEvent>(TEvent @event);
    void PublishMessage(object message);

    T? Aggregate { get; }

    string TenantId { get; }

    IEnumerable<IEvent> RaisedEvents();
    IEnumerable<object> PublishedMessages();
}

/// <summary>
///     A grouping of events that will be applied to an aggregate of type TDoc
///     with the identity TId
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public class EventSlice<TDoc, TId>: IEventSlice, IComparer<IEvent>, IEventSlice<TDoc>
{
    private readonly List<IEvent> _events = new();

    public EventSlice(TId id, Tenant tenant, IEnumerable<IEvent>? events = null)
    {
        Id = id;
        Tenant = tenant;
        if (events != null)
        {
            _events.AddRange(events);
        }
    }

    public EventSlice(TId id, IQuerySession querySession, IEnumerable<IEvent>? events = null): this(id,
        new Tenant(querySession.TenantId, querySession.Database), events)
    {
    }

    private readonly StreamActionType? _actionType;

    /// <summary>
    ///     Is this action the start of a new stream or appending
    ///     to an existing stream?
    /// </summary>
    /// <remarks>
    ///     Default's to determining from the version of the first event on
    ///     stream, but can be overridden so that the value works with
    ///     QuickAppend
    /// </remarks>
    public StreamActionType ActionType
    {
        get => _actionType ?? (_events[0].Version == 1 ? StreamActionType.Start : StreamActionType.Append);
        init => _actionType = value;
    }

    /// <summary>
    ///     The aggregate identity
    /// </summary>
    public TId Id { get; }

    /// <summary>
    ///     The current tenant
    /// </summary>
    public Tenant Tenant { get; }

    internal List<IEvent>? RaisedEvents { get; private set; }
    internal List<object>? PublishedMessages { get; private set; }

    void IEventSlice<TDoc>.AppendEvent<TEvent>(Guid streamId, TEvent @event)
    {
        RaisedEvents ??= new();
        RaisedEvents.Add(new Event<TEvent>(@event)
        {
            StreamId = streamId
        });
    }

    void IEventSlice<TDoc>.AppendEvent<TEvent>(string streamKey, TEvent @event)
    {
        RaisedEvents ??= new();
        RaisedEvents.Add(new Event<TEvent>(@event)
        {
            StreamKey = streamKey
        });
    }

    void IEventSlice<TDoc>.AppendEvent<TEvent>(TEvent @event)
    {
        RaisedEvents ??= new();
        var e = new Event<TEvent>(@event);
        if (typeof(TId) == typeof(string))
        {
            e.StreamKey = Id.As<string>();
        }
        else if (typeof(TId) == typeof(Guid))
        {
            e.StreamId = Id.As<Guid>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot determine the stream id for published events for the identity type {typeof(TId).FullNameInCode()}. You will need to explicitly supply the stream id/key");
        }

        RaisedEvents.Add(e);
    }

    void IEventSlice<TDoc>.PublishMessage(object message)
    {
        PublishedMessages ??= new();
        PublishedMessages.Add(message);
    }

    /// <summary>
    ///     The related aggregate document
    /// </summary>
    public TDoc? Aggregate { get; set; }

    string IEventSlice<TDoc>.TenantId => Tenant.TenantId;
    IEnumerable<IEvent> IEventSlice<TDoc>.RaisedEvents()
    {
        if (RaisedEvents == null) yield break;

        foreach (var @event in RaisedEvents)
        {
            yield return @event;
        }
    }

    IEnumerable<object> IEventSlice<TDoc>.PublishedMessages()
    {
        throw new NotImplementedException();
    }

    public int Count => _events.Count;

    int IComparer<IEvent>.Compare(IEvent x, IEvent y)
    {
        return x.Sequence.CompareTo(y.Sequence);
    }

    /// <summary>
    ///     All the events in this slice
    /// </summary>
    public IReadOnlyList<IEvent> Events()
    {
        return _events;
    }

    /// <summary>
    ///     Add a single event to this slice
    /// </summary>
    /// <param name="e"></param>
    public void AddEvent(IEvent e)
    {
        _events.Add(e);
    }

    /// <summary>
    ///     Add a grouping of events to this slice
    /// </summary>
    /// <param name="events"></param>
    public void AddEvents(IEnumerable<IEvent> events)
    {
        _events.AddRange(events);
    }

    /// <summary>
    ///     Iterate through just the event data
    /// </summary>
    /// <returns></returns>
    public IEnumerable<object> AllData()
    {
        foreach (var @event in _events) yield return @event.Data;
    }

    internal void FanOut<TSource, TChild>(Func<TSource, IEnumerable<TChild>> fanOutFunc)
    {
        reorderEvents();
        _events.FanOut(fanOutFunc);
    }

    internal void ApplyFanOutRules(IEnumerable<IFanOutRule> rules)
    {
        // Need to do this first before applying the fanout rules
        reorderEvents();

        foreach (var rule in rules) rule.Apply(_events);
    }

    private void reorderEvents()
    {
        var events = _events.Distinct().OrderBy(x => x.Sequence).ToArray();
        _events.Clear();
        _events.AddRange(events);
    }

    public IEnumerable<IStorageOperation> BuildOperations(EventGraph eventGraph, DocumentSessionBase session,
        IEventStorage storage, bool isSingleStream)
    {
        if (RaisedEvents == null) yield break;

        var now = DateTimeOffset.UtcNow;

        foreach (var e in RaisedEvents)
        {
            var mapping = eventGraph.EventMappingFor(e.EventType);
            e.DotNetTypeName = mapping.DotNetTypeName;
            e.EventTypeName = mapping.EventTypeName;
            e.TenantId = Tenant.TenantId;
            e.Timestamp = now;
            // Dont assign e.Id so StreamAction.Append can auto assign a CombGuid
        }

        if (eventGraph.StreamIdentity == StreamIdentity.AsGuid)
        {
            var groups = RaisedEvents
                .GroupBy(x => x.StreamId);

            foreach (var group in groups)
            {
                var action = StreamAction.Append(group.Key, RaisedEvents.ToArray());
                action.TenantId = Tenant.TenantId;

                if (isSingleStream && ActionType == StreamActionType.Start)
                {
                    var version = _events.Count;
                    action.ExpectedVersionOnServer = version;

                    foreach (var @event in RaisedEvents)
                    {
                        @event.Version = ++version;
                        yield return storage.QuickAppendEventWithVersion(eventGraph, session, action, @event);
                    }

                    action.Version = version;

                    yield return storage.UpdateStreamVersion(action);
                }
                else
                {
                    action.TenantId = Tenant.TenantId;
                    yield return storage.QuickAppendEvents(action);
                }
            }
        }
        else
        {
            var groups = RaisedEvents
                .GroupBy(x => x.StreamKey);

            foreach (var group in groups)
            {
                var action = StreamAction.Append(group.Key, RaisedEvents.ToArray());
                action.TenantId = Tenant.TenantId;

                if (isSingleStream && ActionType == StreamActionType.Start)
                {
                    var version = _events.Count;
                    action.ExpectedVersionOnServer = version;

                    foreach (var @event in RaisedEvents)
                    {
                        @event.Version = ++version;
                        yield return storage.QuickAppendEventWithVersion(eventGraph, session, action, @event);
                    }

                    action.Version = version;

                    yield return storage.UpdateStreamVersion(action);
                }
                else
                {
                    action.TenantId = Tenant.TenantId;
                    yield return storage.QuickAppendEvents(action);
                }
            }
        }
    }
}
