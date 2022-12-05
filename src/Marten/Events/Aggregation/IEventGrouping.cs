using System;
using System.Collections.Generic;

namespace Marten.Events.Aggregation;

/// <summary>
///     Represents a grouping of a range of events by aggregate id. Used in aggregation projections
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IEventGrouping<TId>
{
    /// <summary>
    ///     Add a single event to a single event slice by id
    /// </summary>
    /// <param name="id">The aggregate id</param>
    /// <param name="event"></param>
    void AddEvent(TId id, IEvent @event);

    /// <summary>
    ///     Add many events to a single event slice by aggregate id
    /// </summary>
    /// <param name="id">The aggregate id</param>
    /// <param name="events"></param>
    void AddEvents(TId id, IEnumerable<IEvent> events);

    /// <summary>
    ///     Add events to streams where each event of type TEvent applies to only
    ///     one stream
    /// </summary>
    /// <param name="singleIdSource"></param>
    /// <param name="events"></param>
    /// <typeparam name="TEvent"></typeparam>
    void AddEvents<TEvent>(Func<TEvent, TId> singleIdSource, IEnumerable<IEvent> events);

    /// <summary>
    ///     Add events to streams where each event of type TEvent may be related to many
    ///     different aggregates
    /// </summary>
    /// <param name="multipleIdSource"></param>
    /// <param name="events"></param>
    /// <typeparam name="TEvent"></typeparam>
    void AddEvents<TEvent>(Func<TEvent, IEnumerable<TId>> multipleIdSource, IEnumerable<IEvent> events);

    /// <summary>
    ///     Add events to the grouping based on the outer IEvent<TEvent> envelope type
    /// </summary>
    /// <param name="singleIdSource"></param>
    /// <param name="events"></param>
    /// <typeparam name="TEvent"></typeparam>
    void AddEventsWithMetadata<TEvent>(Func<IEvent<TEvent>, TId> singleIdSource, IEnumerable<IEvent> events);


    /// <summary>
    ///     Apply "fan out" operations to the given TSource type that inserts an enumerable of TChild events right behind the
    ///     parent
    ///     event in the event stream just after any instance of the parent
    /// </summary>
    /// <param name="fanOutFunc"></param>
    /// <typeparam name="TSource"></typeparam>
    /// <typeparam name="TChild"></typeparam>
    void FanOutOnEach<TSource, TChild>(Func<TSource, IEnumerable<TChild>> fanOutFunc);
}
