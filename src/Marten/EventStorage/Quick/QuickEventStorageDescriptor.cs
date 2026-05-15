#nullable enable
using System;
using JasperFx.Events;

namespace Marten.EventStorage.Quick;

/// <summary>
/// Per-<see cref="EventGraph"/> descriptor for the Quick (batch) append
/// flow. Holds only the SQL strings the Quick operations need — no Rich
/// per-row INSERT prefix, no <see cref="IEventMetadataBinder"/> array.
/// </summary>
/// <remarks>
/// <para>
/// Why no metadata binder array on Quick descriptors: Quick-mode binds
/// metadata as <i>per-batch arrays</i> — one <c>NpgsqlDbType.Array | ...</c>
/// parameter per column, with the array's contents being one value per
/// event in the stream. That shape is uniform enough across columns (build
/// a <c>List&lt;T&gt;</c>, fill it from the events, bind as an array) that
/// inlining the bind code per active column in the source-gen output is
/// just as clean as a binder dispatch — and avoids the per-event-list
/// allocation churn a per-binder approach would force.
/// </para>
/// <para>
/// The Quick operation's <c>ConfigureCommand</c> body, when source-gen
/// emits it, includes inlined <c>writeHeaders</c> / <c>writeCausationIds</c>
/// / etc. calls based on the consumer's <c>StoreOptions.Events</c>
/// configuration at compile time. The Quick mode's configuration axes
/// thus DO show up in the source-gen matrix — different from Rich, where
/// they're descriptor-level — but the matrix is still bounded because the
/// quick operation classes already exist as hand-written bases with
/// protected helpers for each column.
/// </para>
/// </remarks>
public sealed class QuickEventStorageDescriptor
{
    public QuickEventStorageDescriptor(
        string quickAppendEventsSql,
        string insertStreamSql,
        string updateStreamVersionSql,
        string streamStateSelectSql,
        Func<IEvent, string> serializeEventData)
    {
        QuickAppendEventsSql = quickAppendEventsSql;
        InsertStreamSql = insertStreamSql;
        UpdateStreamVersionSql = updateStreamVersionSql;
        StreamStateSelectSql = streamStateSelectSql;
        SerializeEventData = serializeEventData;
    }

    /// <summary>
    /// Complete SQL for the <c>select mt_quick_append_events(...)</c>
    /// function call. Already includes the per-column array-parameter
    /// placeholder list; the source-gen operation appends parameters in
    /// order via <see cref="IGroupedParameterBuilder"/>.
    /// </summary>
    public string QuickAppendEventsSql { get; }

    public string InsertStreamSql { get; }
    public string UpdateStreamVersionSql { get; }
    public string StreamStateSelectSql { get; }
    public Func<IEvent, string> SerializeEventData { get; }
}
