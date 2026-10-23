#nullable enable
using System;
using System.Collections.Generic;
using JasperFx.Events;

namespace Marten.Events.Cold;

/// <summary>
///     One event as it was stored outside <c>mt_events</c>: its payload and the metadata Marten kept for it.
///     Turned into an <see cref="IEvent" /> by <see cref="EventGraph.DeserializeAsync" />, with the same type
///     resolution and upcasters as an event read from <c>mt_events</c>.
/// </summary>
public sealed class ColdEvent
{
    public Guid Id { get; init; }

    public required long Version { get; init; }

    /// <summary>
    ///     The event's original <c>seq_id</c>, if the application kept it.
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    ///     The stored event type name (<c>mt_events.type</c>), which may be an alias or an upcaster's source name.
    /// </summary>
    public required string EventTypeName { get; init; }

    /// <summary>
    ///     The stored .NET type name (<c>mt_events.mt_dotnet_type</c>), the fallback when the event type name
    ///     is not known to the store.
    /// </summary>
    public string? DotNetTypeName { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    ///     The JSON payload (<c>mt_events.data</c>). Set this or <see cref="BinaryData" />.
    /// </summary>
    public string? Data { get; init; }

    /// <summary>
    ///     The binary payload (<c>mt_events.bdata</c>) of an event type that uses a binary serializer.
    /// </summary>
    public byte[]? BinaryData { get; init; }

    public IReadOnlyDictionary<string, object>? Headers { get; init; }

    public string? CorrelationId { get; init; }

    public string? CausationId { get; init; }

    public string? UserName { get; init; }

    public IReadOnlyList<EventTag>? Tags { get; init; }

    /// <summary>
    ///     Only read by <see cref="EventGraph.DeserializeAsync" /> when called directly. When Marten reads a
    ///     stream's cold part, the stream key and tenant come from that stream.
    /// </summary>
    public string? StreamKey { get; init; }

    /// <inheritdoc cref="StreamKey" />
    public string? TenantId { get; init; }
}
