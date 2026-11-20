using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events.Rewriting;

/// <summary>
///     Which stored events to rewrite, and how hard to push the database while doing it. Every selected event
///     is read the way Marten reads it (upcasters, binary or JSON, the configured serializer) and written back
///     the way an append would store it today, optionally through a transform. Rows that would come out the
///     same are left alone.
/// </summary>
/// <remarks>
///     A rewrite keeps no state of its own: progress goes out through <see cref="OnProgress" /> and comes back in
///     through <see cref="ResumeFrom" />. Use <see cref="ForDatabase" /> or <see cref="ForTenant" /> to run one
///     database or one tenant at a time.
/// </remarks>
public sealed class StoredEventRewrite
{
    internal List<Type> Types { get; } = new();
    internal List<(Type Type, Func<object, object> Transform)> Transforms { get; } = new();
    internal bool AllTypes { get; private set; }
    internal bool JsonOnly { get; private set; }
    internal string? TenantId { get; private set; }
    internal string? DatabaseIdentifier { get; private set; }
    internal Dictionary<string, StoredEventRewriteCursor> ResumeCursors { get; } = new();
    internal Dictionary<string, long> StopAfterSequences { get; } = new();
    internal long? StopAfterSequence { get; private set; }

    // jasperfx#1215: stored type names a definition lists that no registered event type answers to
    internal List<string> TypeNames { get; } = new();

    /// <summary>
    ///     Events read per page, and written back in one statement. Default 500.
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    ///     How many databases are rewritten at the same time. Default 1.
    /// </summary>
    public int MaxParallelDatabases { get; set; } = 1;

    /// <summary>
    ///     Wait between pages, to leave the database room for the application. Default none.
    /// </summary>
    public TimeSpan PauseBetweenBatches { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     Read and compare everything, but write nothing: the result says how many events would change and
    ///     how many bytes they take before and after.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    ///     Called after every page of every database, with the cursor to resume from.
    /// </summary>
    public Func<StoredEventRewriteProgress, CancellationToken, Task>? OnProgress { get; set; }

    /// <summary>
    ///     Rewrite the events read as <typeparamref name="T" />, including those stored under a name an upcaster
    ///     reads into it.
    /// </summary>
    public StoredEventRewrite IncludeType<T>() where T : class
    {
        Types.Add(typeof(T));
        return this;
    }

    /// <summary>
    ///     Rewrite every event in the store. Events of a type this application no longer knows are left as they
    ///     are, and counted as <see cref="StoredEventRewriteProgress.UnknownTypes" />.
    /// </summary>
    public StoredEventRewrite IncludeAllTypes()
    {
        AllTypes = true;
        return this;
    }

    /// <summary>
    ///     Change the events read as <typeparamref name="T" /> before they are written back. Includes the type.
    ///     This rewrites history: readers see old and new rows side by side until the rewrite completes, and
    ///     nothing that was derived from the old events (projections, natural keys, tags) is updated.
    /// </summary>
    public StoredEventRewrite Transform<T>(Func<T, T> transform) where T : class
    {
        ArgumentNullException.ThrowIfNull(transform);
        Types.Add(typeof(T));
        Transforms.Add((typeof(T), x => transform((T)x)));
        return this;
    }

    /// <summary>
    ///     Only read the selected events still stored as JSON, and leave the ones already in <c>bdata</c> alone.
    ///     Use this when moving events to binary serialization with a serializer that does not write the same
    ///     bytes twice, such as one that encrypts with a random IV: without it, every run rewrites every row
    ///     again.
    /// </summary>
    public StoredEventRewrite OnlyEventsStoredAsJson()
    {
        JsonOnly = true;
        return this;
    }

    /// <summary>
    ///     Only rewrite the events of this tenant: its database, and with conjoined event tenancy only its rows.
    /// </summary>
    public StoredEventRewrite ForTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        TenantId = tenantId;
        return this;
    }

    /// <summary>
    ///     Only rewrite this database, by its <c>Identifier</c>, for a store with more than one.
    /// </summary>
    public StoredEventRewrite ForDatabase(string databaseIdentifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(databaseIdentifier);
        DatabaseIdentifier = databaseIdentifier;
        return this;
    }

    /// <summary>
    ///     Leave events after this sequence alone, in every database the rewrite covers. Take the sequence when the
    ///     rewrite is first requested, so a <see cref="Transform{T}" /> never changes events appended after that.
    /// </summary>
    public StoredEventRewrite StopAfter(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        StopAfterSequence = sequence;
        return this;
    }

    /// <summary>
    ///     Leave events after this sequence alone in one database, by its <c>Identifier</c>. Overrides
    ///     <see cref="StopAfter(long)" /> for that database.
    /// </summary>
    public StoredEventRewrite StopAfter(string databaseIdentifier, long sequence)
    {
        ArgumentException.ThrowIfNullOrEmpty(databaseIdentifier);
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        StopAfterSequences[databaseIdentifier] = sequence;
        return this;
    }

    internal long? StopAfterFor(string databaseIdentifier)
        => StopAfterSequences.TryGetValue(databaseIdentifier, out var sequence) ? sequence : StopAfterSequence;

    /// <summary>
    ///     Continue a database from a cursor an earlier run reported.
    /// </summary>
    public StoredEventRewrite ResumeFrom(StoredEventRewriteCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ResumeCursors[cursor.Database] = cursor;
        return this;
    }
}

/// <summary>
///     The last event a rewrite visited in one database. Events are ordered by sequence, then tenant, because
///     a sequence alone is not unique with per-tenant event partitions.
/// </summary>
public sealed record StoredEventRewriteCursor(string Database, long Sequence, string TenantId);

/// <param name="Cursor">Where to resume this database from.</param>
/// <param name="Visited">Events read so far.</param>
/// <param name="Rewritten">Events written back, or that would be in a dry run.</param>
/// <param name="Skipped">Events another transaction changed between the read and the write. Run again to include them.</param>
/// <param name="BytesBefore">Stored payload size of the rewritten events before.</param>
/// <param name="BytesAfter">Payload size of the rewritten events after, uncompressed.</param>
/// <param name="Completed">Whether every selected event of this database has been visited.</param>
/// <param name="UnknownTypes">Events of a type this application no longer knows, left as they are.</param>
public sealed record StoredEventRewriteProgress(
    StoredEventRewriteCursor Cursor,
    long Visited,
    long Rewritten,
    long Skipped,
    long BytesBefore,
    long BytesAfter,
    bool Completed,
    long UnknownTypes = 0);

public sealed record StoredEventRewriteResult(IReadOnlyList<StoredEventRewriteProgress> Databases);
