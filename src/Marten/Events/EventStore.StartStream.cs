#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Schema.Identity;

namespace Marten.Events;

internal partial class EventStore
{
    public StreamAction StartStream<TAggregate>(Guid id, IEnumerable<object> events) where TAggregate : class
    {
        return StartStream<TAggregate>(id, events?.ToArray()!);
    }

    public StreamAction StartStream<T>(Guid id, params object[] events) where T : class
    {
        return StartStream(typeof(T), id, events);
    }

    public StreamAction StartStream(Type aggregateType, Guid id, IEnumerable<object> events)
    {
        return StartStream(aggregateType, id, events?.ToArray()!);
    }

    public StreamAction StartStream(Type aggregateType, Guid id, params object[] events)
    {
        var stream = _store.Events.StartStream(_session, id, events);
        stream.AggregateType = aggregateType;

        return stream;
    }

    public StreamAction StartStream<TAggregate>(string streamKey, IEnumerable<object> events)
        where TAggregate : class
    {
        return StartStream<TAggregate>(streamKey, events?.ToArray()!);
    }

    public StreamAction StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class
    {
        return StartStream(typeof(TAggregate), streamKey, events);
    }

    public StreamAction StartStream(Type aggregateType, string streamKey, IEnumerable<object> events)
    {
        return StartStream(aggregateType, streamKey, events?.ToArray()!);
    }

    public StreamAction StartStream(Type aggregateType, string streamKey, params object[] events)
    {
        var stream = _store.Events.StartStream(_session, streamKey, events);
        stream.AggregateType = aggregateType;

        return stream;
    }

    public StreamAction StartStream(Guid id, IEnumerable<object> events)
    {
        return StartStream(id, events?.ToArray()!);
    }

    public StreamAction StartStream(Guid id, params object[] events)
    {
        if (_store.Events.UseMandatoryStreamTypeDeclaration) throw new StreamTypeMissingException();
        return _store.Events.StartStream(_session, id, events);
    }

    public StreamAction StartStream(string streamKey, IEnumerable<object> events)
    {
        return StartStream(streamKey, events?.ToArray()!);
    }

    public StreamAction StartStream(string streamKey, params object[] events)
    {
        if (_store.Events.UseMandatoryStreamTypeDeclaration) throw new StreamTypeMissingException();
        return _store.Events.StartStream(_session, streamKey, events);
    }

    public StreamAction StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class
    {
        return StartStream<TAggregate>(events?.ToArray()!);
    }

    public StreamAction StartStream<TAggregate>(params object[] events) where TAggregate : class
    {
        return StartStream(typeof(TAggregate), events);
    }

    public StreamAction StartStream(Type aggregateType, IEnumerable<object> events)
    {
        return StartStream(aggregateType, events?.ToArray()!);
    }

    public StreamAction StartStream(Type aggregateType, params object[] events)
    {
        return StartStream(aggregateType, CombGuidIdGeneration.NewGuid(), events);
    }

    public StreamAction StartStream(IEnumerable<object> events)
    {
        return StartStream(events?.ToArray()!);
    }

    public StreamAction StartStream(params object[] events)
    {
        return StartStream(CombGuidIdGeneration.NewGuid(), events);
    }
}
