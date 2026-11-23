#nullable enable
using System;

namespace Marten.Exceptions;

/// <summary>
///     Thrown by a <b>batched</b> <c>FetchForWriting</c>, <c>FetchForExclusiveWriting</c> or <c>FetchLatest</c> by
///     natural key, when the key moved to another stream, or was retired, while that batch was reading it. Retry
///     the batch.
/// </summary>
/// <remarks>
///     <para>
///         A fetch by natural key resolves the key in each of its statements, and each statement sees the database
///         as it is when it starts. When the key changes hands between the first statement, which reads the
///         stream's id and version, and the reads of the aggregate after it, those reads describe another stream.
///         A last statement resolves the key again, and a different answer is detected rather than handed back as
///         an aggregate that belongs to one stream with the id and version of another.
///     </para>
///     <para>
///         Outside a batch Marten handles this itself: the fetch is redone, and callers see this exception only
///         when the key keeps moving through every redo. Inside a batch there is nothing to redo, as for
///         <see cref="SnapshotRaceException" />.
///     </para>
/// </remarks>
public class NaturalKeyMovedException: MartenException
{
    internal NaturalKeyMovedException(Type aggregateType, object naturalKey): base(
        $"The natural key '{naturalKey}' of {aggregateType.FullName} moved to another stream while it was being "
        + "fetched, so the stream and the aggregate read for it disagree. Retry the fetch.")
    {
        AggregateType = aggregateType;
        NaturalKey = naturalKey;
    }

    /// <summary>The aggregate type being fetched.</summary>
    public Type AggregateType { get; }

    /// <summary>The natural key value being fetched.</summary>
    public object NaturalKey { get; }
}
