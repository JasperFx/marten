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
public sealed class StoredEventRewrite
{
    internal List<Type> Types { get; } = new();
    internal List<(Type Type, Func<object, object> Transform)> Transforms { get; } = new();
    internal bool AllTypes { get; private set; }
    internal Dictionary<string, StoredEventRewriteCursor> ResumeCursors { get; } = new();

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
    ///     Rewrite every event in the store.
    /// </summary>
    public StoredEventRewrite IncludeAllTypes()
    {
        AllTypes = true;
        return this;
    }

    /// <summary>
    ///     Change the events read as <typeparamref name="T" /> before they are written back. Includes the type.
    ///     The readers of these events see old and new rows side by side until the rewrite completes.
    /// </summary>
    public StoredEventRewrite Transform<T>(Func<T, T> transform) where T : class
    {
        Types.Add(typeof(T));
        Transforms.Add((typeof(T), x => transform((T)x)));
        return this;
    }

    /// <summary>
    ///     Continue a database from a cursor an earlier run reported.
    /// </summary>
    public StoredEventRewrite ResumeFrom(StoredEventRewriteCursor cursor)
    {
        ResumeCursors[cursor.Database] = cursor;
        return this;
    }
}

/// <summary>
///     The last event a rewrite visited in one database. Events are ordered by sequence, then tenant, because
///     a sequence alone is not unique with per-tenant event partitions.
/// </summary>
public sealed record StoredEventRewriteCursor(string Database, long Sequence, string TenantId);

/// <param name="Visited">Events read so far.</param>
/// <param name="Rewritten">Events written back, or that would be in a dry run.</param>
/// <param name="Skipped">Events another transaction changed between the read and the write. Run again to include them.</param>
/// <param name="BytesBefore">Stored payload size of the rewritten events before.</param>
/// <param name="BytesAfter">Payload size of the rewritten events after, uncompressed.</param>
public sealed record StoredEventRewriteProgress(
    StoredEventRewriteCursor Cursor,
    long Visited,
    long Rewritten,
    long Skipped,
    long BytesBefore,
    long BytesAfter,
    bool Completed);

public sealed record StoredEventRewriteResult(IReadOnlyList<StoredEventRewriteProgress> Databases);
