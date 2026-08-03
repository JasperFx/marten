using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events;

namespace Marten.Events.TestSupport;

public partial class ProjectionScenario
{
    private static string describe(object[] events)
    {
        return events.Length > 3 ? "events" : events.Select(x => x.ToString()).Join(", ");
    }

    /// <summary>
    ///     Queue appending events to an existing event stream in the scenario sequence
    /// </summary>
    /// <param name="stream">The stream id</param>
    /// <param name="events">The events to append</param>
    public void Append(Guid stream, IEnumerable<object> events)
    {
        Append(stream, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue appending events to an existing event stream in the scenario sequence
    /// </summary>
    /// <param name="stream">The stream id</param>
    /// <param name="events">The events to append</param>
    public void Append(Guid stream, params object[] events)
    {
        action(e => e.Append(stream, events)).Description = $"Append({stream}, {describe(events)})";
    }

    /// <summary>
    ///     Queue appending events to an existing event stream in the scenario sequence
    /// </summary>
    /// <param name="stream">The stream key</param>
    /// <param name="events">The events to append</param>
    public void Append(string stream, IEnumerable<object> events)
    {
        Append(stream, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue appending events to an existing event stream in the scenario sequence
    /// </summary>
    /// <param name="stream">The stream key</param>
    /// <param name="events">The events to append</param>
    public void Append(string stream, params object[] events)
    {
        action(e => e.Append(stream, events)).Description = $"Append(\"{stream}\", {describe(events)})";
    }

    /// <summary>
    ///     Queue appending events to an existing event stream with an expected version
    ///     in the scenario sequence
    /// </summary>
    /// <param name="stream">The stream id</param>
    /// <param name="expectedVersion">The expected stream version after appending</param>
    /// <param name="events">The events to append</param>
    public void Append(Guid stream, long expectedVersion, params object[] events)
    {
        action(e => e.Append(stream, expectedVersion, events)).Description =
            $"Append({stream}, {expectedVersion}, {describe(events)})";
    }

    /// <summary>
    ///     Queue appending events to an existing event stream with an expected version
    ///     in the scenario sequence
    /// </summary>
    /// <param name="stream">The stream key</param>
    /// <param name="expectedVersion">The expected stream version after appending</param>
    /// <param name="events">The events to append</param>
    public void Append(string stream, long expectedVersion, IEnumerable<object> events)
    {
        Append(stream, expectedVersion, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue appending events to an existing event stream with an expected version
    ///     in the scenario sequence
    /// </summary>
    /// <param name="stream">The stream key</param>
    /// <param name="expectedVersion">The expected stream version after appending</param>
    /// <param name="events">The events to append</param>
    public void Append(string stream, long expectedVersion, params object[] events)
    {
        action(e => e.Append(stream, expectedVersion, events)).Description =
            $"Append(\"{stream}\", {expectedVersion}, {describe(events)})";
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="id">The stream id</param>
    /// <param name="events">The initial events</param>
    /// <typeparam name="TAggregate">The aggregate type for the new stream</typeparam>
    public void StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class
    {
        action(e => e.StartStream<TAggregate>(id, events)).Description =
            $"StartStream<{typeof(TAggregate).FullNameInCode()}>({id}, {describe(events)})";
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="aggregateType">The aggregate type for the new stream</param>
    /// <param name="id">The stream id</param>
    /// <param name="events">The initial events</param>
    public void StartStream(Type aggregateType, Guid id, IEnumerable<object> events)
    {
        StartStream(aggregateType, id, (events as object[] ?? events.ToArray()));
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="aggregateType">The aggregate type for the new stream</param>
    /// <param name="id">The stream id</param>
    /// <param name="events">The initial events</param>
    public void StartStream(Type aggregateType, Guid id, params object[] events)
    {
        action(e => e.StartStream(aggregateType, id, events)).Description =
            $"StartStream({aggregateType.FullNameInCode()}, {id}, {describe(events)})";
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="streamKey">The stream key</param>
    /// <param name="events">The initial events</param>
    /// <typeparam name="TAggregate">The aggregate type for the new stream</typeparam>
    public void StartStream<TAggregate>(string streamKey, IEnumerable<object> events)
        where TAggregate : class
    {
        StartStream<TAggregate>(streamKey, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="streamKey">The stream key</param>
    /// <param name="events">The initial events</param>
    /// <typeparam name="TAggregate">The aggregate type for the new stream</typeparam>
    public void StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class
    {
        action(e => e.StartStream<TAggregate>(streamKey, events)).Description =
            $"StartStream<{typeof(TAggregate).FullNameInCode()}>(\"{streamKey}\", {describe(events)})";
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="aggregateType">The aggregate type for the new stream</param>
    /// <param name="streamKey">The stream key</param>
    /// <param name="events">The initial events</param>
    public void StartStream(Type aggregateType, string streamKey, IEnumerable<object> events)
    {
        StartStream(aggregateType, streamKey, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="aggregateType">The aggregate type for the new stream</param>
    /// <param name="streamKey">The stream key</param>
    /// <param name="events">The initial events</param>
    public void StartStream(Type aggregateType, string streamKey, params object[] events)
    {
        action(e => e.StartStream(aggregateType, streamKey, events)).Description =
            $"StartStream({aggregateType.FullNameInCode()}, \"{streamKey}\", {describe(events)})";
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="id">The stream id</param>
    /// <param name="events">The initial events</param>
    public void StartStream(Guid id, IEnumerable<object> events)
    {
        StartStream(id, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="id">The stream id</param>
    /// <param name="events">The initial events</param>
    public void StartStream(Guid id, params object[] events)
    {
        action(e => e.StartStream(id, events)).Description = $"StartStream({id}, {describe(events)})";
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="streamKey">The stream key</param>
    /// <param name="events">The initial events</param>
    public void StartStream(string streamKey, IEnumerable<object> events)
    {
        StartStream(streamKey, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue starting a new event stream in the scenario sequence
    /// </summary>
    /// <param name="streamKey">The stream key</param>
    /// <param name="events">The initial events</param>
    public void StartStream(string streamKey, params object[] events)
    {
        action(e => e.StartStream(streamKey, events)).Description =
            $"StartStream(\"{streamKey}\", {describe(events)})";
    }

    /// <summary>
    ///     Queue starting a new event stream with a newly generated stream id
    ///     in the scenario sequence
    /// </summary>
    /// <param name="events">The initial events</param>
    /// <typeparam name="TAggregate">The aggregate type for the new stream</typeparam>
    /// <returns>The generated id of the new stream</returns>
    public Guid StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class
    {
        return StartStream<TAggregate>(events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue starting a new event stream with a newly generated stream id
    ///     in the scenario sequence
    /// </summary>
    /// <param name="events">The initial events</param>
    /// <typeparam name="TAggregate">The aggregate type for the new stream</typeparam>
    /// <returns>The generated id of the new stream</returns>
    public Guid StartStream<TAggregate>(params object[] events) where TAggregate : class
    {
        var streamId = Guid.NewGuid();
        action(e => e.StartStream<TAggregate>(streamId, events)).Description =
            $"StartStream<{typeof(TAggregate).FullNameInCode()}>({streamId}, {describe(events)})";

        return streamId;
    }

    /// <summary>
    ///     Queue starting a new event stream with a newly generated stream id
    ///     in the scenario sequence
    /// </summary>
    /// <param name="aggregateType">The aggregate type for the new stream</param>
    /// <param name="events">The initial events</param>
    /// <returns>The generated id of the new stream</returns>
    public Guid StartStream(Type aggregateType, IEnumerable<object> events)
    {
        return StartStream(aggregateType, events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue starting a new event stream with a newly generated stream id
    ///     in the scenario sequence
    /// </summary>
    /// <param name="aggregateType">The aggregate type for the new stream</param>
    /// <param name="events">The initial events</param>
    /// <returns>The generated id of the new stream</returns>
    public Guid StartStream(Type aggregateType, params object[] events)
    {
        var streamId = Guid.NewGuid();
        action(e => e.StartStream(aggregateType, streamId, events)).Description =
            $"StartStream({aggregateType.FullNameInCode()}, {streamId}, {describe(events)})";

        return streamId;
    }

    /// <summary>
    ///     Queue starting a new event stream with a newly generated stream id
    ///     in the scenario sequence
    /// </summary>
    /// <param name="events">The initial events</param>
    /// <returns>The generated id of the new stream</returns>
    public Guid StartStream(IEnumerable<object> events)
    {
        return StartStream(events as object[] ?? events.ToArray());
    }

    /// <summary>
    ///     Queue starting a new event stream with a newly generated stream id
    ///     in the scenario sequence
    /// </summary>
    /// <param name="events">The initial events</param>
    /// <returns>The generated id of the new stream</returns>
    public Guid StartStream(params object[] events)
    {
        var streamId = Guid.NewGuid();
        action(e => e.StartStream(streamId, events)).Description =
            $"StartStream({streamId}, {describe(events)})";

        return streamId;
    }

    /// <summary>
    ///     Make any number of append event operations in the scenario sequence
    /// </summary>
    /// <param name="description">Descriptive explanation of the action in case of failures</param>
    /// <param name="appendAction"></param>
    public void AppendEvents(string description, Action<IEventOperations> appendAction)
    {
        action(appendAction).Description = description;
    }

    /// <summary>
    ///     Make any number of append event operations in the scenario sequence
    /// </summary>
    /// <param name="appendAction"></param>
    public void AppendEvents(Action<IEventOperations> appendAction)
    {
        AppendEvents("Appending events...", appendAction);
    }
}
