using System;

namespace Marten.Events;

/// <summary>
/// Metadata for a single event stream, used to seed <c>mt_streams</c> during a streamed bulk import
/// (<see cref="Marten.IDocumentStore.BulkInsertEventStreamAsync"/>). One header per stream is supplied up
/// front — small and bounded even for a tenant with millions of events — and written before the events so
/// the <c>mt_events</c> → <c>mt_streams</c> foreign key is satisfied while the events themselves stream in.
/// </summary>
public sealed class BulkEventStreamHeader
{
    /// <summary>Stream id for a store using <see cref="Marten.Events.StreamIdentity.AsGuid"/>.</summary>
    public Guid Id { get; init; }

    /// <summary>Stream key for a store using <see cref="Marten.Events.StreamIdentity.AsString"/>.</summary>
    public string? Key { get; init; }

    /// <summary>The stream's (final) version — the number of events on the stream.</summary>
    public long Version { get; init; }

    /// <summary>Optional aggregate CLR type; its Marten alias is written to <c>mt_streams.type</c>.</summary>
    public Type? AggregateType { get; init; }

    /// <summary>Optional pre-resolved aggregate alias; takes precedence over <see cref="AggregateType"/>.</summary>
    public string? AggregateTypeName { get; init; }

    /// <summary>
    /// Whether the stream is archived. A migration read should carry the source's
    /// <c>mt_streams.is_archived</c> flag through so archived streams stay archived in the target.
    /// </summary>
    public bool IsArchived { get; init; }
}
