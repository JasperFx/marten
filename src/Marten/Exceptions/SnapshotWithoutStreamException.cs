#nullable enable
using System;

namespace Marten.Exceptions;

/// <summary>
///     Thrown by <c>FetchForWriting</c>, <c>FetchForExclusiveWriting</c>, <c>FetchLatest</c> and
///     <c>StreamLatestJson</c> for an aggregate projected with <c>ProjectionLifecycle.Async</c>, when the
///     aggregate's snapshot document exists but its stream does not, as after
///     <c>DeleteAllEventDataAsync</c>, which leaves documents in place.
/// </summary>
/// <remarks>
///     There is no stream to append to and nothing to fold the snapshot from, so neither the snapshot nor a
///     new, empty aggregate is a right answer. Delete the snapshot document, or restore the stream.
/// </remarks>
public class SnapshotWithoutStreamException: MartenException
{
    internal SnapshotWithoutStreamException(Type aggregateType, object id): base(
        $"The snapshot of {aggregateType.FullName} '{id}' exists, but its stream does not. Delete the snapshot "
        + "document, or restore the stream.")
    {
        AggregateType = aggregateType;
        Id = id;
    }

    /// <summary>The aggregate type being fetched.</summary>
    public Type AggregateType { get; }

    /// <summary>The identity of the stream being fetched.</summary>
    public object Id { get; }
}
