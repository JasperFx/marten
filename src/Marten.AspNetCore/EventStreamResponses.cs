using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Events;

namespace Marten.AspNetCore;

#region sample_event_stream_response_types

/// <summary>
/// The HTTP wire shape written by <see cref="StreamEventState"/> for a single event stream's
/// <see cref="StreamState"/> metadata.
/// <para>
/// <see cref="StreamState"/> is not written directly because <c>StreamState.AggregateType</c> is a
/// <see cref="Type"/>, and System.Text.Json refuses to serialize <see cref="Type"/> instances
/// ("Serialization and deserialization of 'System.Type' instances is not supported"). This record
/// projects the aggregate type down to its simple name and is a stable contract for HTTP clients.
/// </para>
/// </summary>
public sealed record StreamStateResponse
{
    /// <summary>Identity of the stream when using Guid identity; <see cref="Guid.Empty"/> for string-keyed streams.</summary>
    public Guid Id { get; init; }

    /// <summary>Identity of the stream when using string identity; null for Guid-keyed streams.</summary>
    public string? Key { get; init; }

    /// <summary>Current version of the stream, i.e. the count of events.</summary>
    public long Version { get; init; }

    /// <summary>Simple name of the aggregate type the stream was tagged with, when it was tagged at all.</summary>
    public string? AggregateTypeName { get; init; }

    /// <summary>The last time this stream was appended to.</summary>
    public DateTimeOffset LastTimestamp { get; init; }

    /// <summary>The time at which this stream was created.</summary>
    public DateTimeOffset Created { get; init; }

    /// <summary>Whether the stream has been archived.</summary>
    public bool IsArchived { get; init; }

    /// <summary>
    /// Project a Marten <see cref="StreamState"/> onto the wire shape.
    /// </summary>
    public static StreamStateResponse From(StreamState state)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));

        return new StreamStateResponse
        {
            Id = state.Id,
            Key = state.Key,
            Version = state.Version,
            AggregateTypeName = state.AggregateType?.Name,
            LastTimestamp = state.LastTimestamp,
            Created = state.Created,
            IsArchived = state.IsArchived
        };
    }
}

/// <summary>
/// The HTTP wire shape written by <see cref="StreamEvents"/> for one raw event in a stream.
/// <para>
/// <see cref="IEvent"/> is not written directly because <c>IEvent.EventType</c> is a
/// <see cref="Type"/>, which System.Text.Json refuses to serialize. <c>DotNetTypeName</c> — the
/// assembly qualified .NET type name — is deliberately left off the wire as well; use
/// <see cref="EventTypeName"/>, Marten's stable event type alias, to discriminate event types on
/// the client.
/// </para>
/// </summary>
public sealed record EventResponse
{
    /// <summary>Unique identifier of the event.</summary>
    public Guid Id { get; init; }

    /// <summary>The event's position within its stream.</summary>
    public long Version { get; init; }

    /// <summary>The event's sequential position across the entire event store.</summary>
    public long Sequence { get; init; }

    /// <summary>Owning stream's id when using Guid identity; <see cref="Guid.Empty"/> otherwise.</summary>
    public Guid StreamId { get; init; }

    /// <summary>Owning stream's key when using string identity; null otherwise.</summary>
    public string? StreamKey { get; init; }

    /// <summary>Marten's event type alias — the stable discriminator for clients.</summary>
    public string? EventTypeName { get; init; }

    /// <summary>The UTC time at which the event was captured.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The owning tenant id.</summary>
    public string? TenantId { get; init; }

    /// <summary>Whether the event has been archived.</summary>
    public bool IsArchived { get; init; }

    /// <summary>Optional causation id metadata.</summary>
    public string? CausationId { get; init; }

    /// <summary>Optional correlation id metadata.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Optional user defined metadata. Null when no headers were set.</summary>
    public Dictionary<string, object>? Headers { get; init; }

    /// <summary>The event body itself.</summary>
    public object Data { get; init; } = default!;

    /// <summary>
    /// Project a Marten <see cref="IEvent"/> onto the wire shape.
    /// </summary>
    public static EventResponse From(IEvent @event)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        return new EventResponse
        {
            Id = @event.Id,
            Version = @event.Version,
            Sequence = @event.Sequence,
            StreamId = @event.StreamId,
            StreamKey = @event.StreamKey,
            EventTypeName = @event.EventTypeName,
            Timestamp = @event.Timestamp,
            TenantId = @event.TenantId,
            IsArchived = @event.IsArchived,
            CausationId = @event.CausationId,
            CorrelationId = @event.CorrelationId,
            Headers = @event.Headers,
            Data = @event.Data
        };
    }

    /// <summary>
    /// Project a list of Marten <see cref="IEvent"/> onto the wire shape.
    /// </summary>
    public static EventResponse[] From(IReadOnlyList<IEvent> events)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));

        return events.Select(From).ToArray();
    }
}

#endregion
