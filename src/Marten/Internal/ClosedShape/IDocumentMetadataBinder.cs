#nullable enable
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Npgsql;

namespace Marten.Internal.ClosedShape;

/// <summary>
/// W3 spike (M1): per-metadata-column seam for the closed-shape document
/// storage hierarchy. Mirrors W4's <c>IEventMetadataBinder</c> — every
/// metadata column on the document table (<c>mt_version</c>,
/// <c>mt_dotnet_type</c>, <c>mt_last_modified</c>, tenant id, soft-delete
/// flag, custom user-set metadata, etc.) gets one binder. The descriptor
/// holds them in an array; the storage operation loops over the
/// client-side subset in <c>ConfigureCommand</c> + the selector loops
/// over the full array on read.
/// </summary>
/// <remarks>
/// <para>
/// Per-call cost is one virtual <see cref="BindParameter"/> per active
/// client-side metadata column on the write path. Server-side values
/// (e.g. <c>transaction_timestamp()</c>) declare a non-<c>?</c>
/// <see cref="ValueSql"/> fragment that goes directly into the VALUES list
/// — they have no parameter slot to fill and <see cref="IsServerSide"/>
/// is <c>true</c>.
/// </para>
/// </remarks>
public interface IDocumentMetadataBinder<TDoc>
    where TDoc : notnull
{
    /// <summary>
    /// Stable column name used to compose the INSERT column list, the
    /// SELECT projection, and the ON CONFLICT UPDATE clause. The descriptor
    /// builder threads these through to the SQL strings in the same order
    /// as the binder array — parameter order matches column order.
    /// </summary>
    string ColumnName { get; }

    /// <summary>
    /// SQL fragment placed in the VALUES list at this binder's position.
    /// <c>"?"</c> for a bound parameter (the common case);
    /// <c>"transaction_timestamp()"</c> / <c>"now()"</c> / similar for a
    /// server-side computed value.
    /// </summary>
    string ValueSql { get; }

    /// <summary>
    /// True when <see cref="ValueSql"/> is something other than <c>"?"</c>
    /// — i.e. the value is computed server-side and this binder has no
    /// parameter slot to fill on write. Server-side binders skip
    /// <see cref="BindParameter"/> but may still implement
    /// <see cref="Apply"/> to project the column's value back onto the
    /// document on read.
    /// </summary>
    bool IsServerSide => ValueSql != "?";

    /// <summary>
    /// Per-row write hook on the INSERT / UPSERT path. The storage
    /// operation pre-allocates the <see cref="NpgsqlParameter"/> via
    /// <c>ICommandBuilder.AppendWithParameters("…, ?, …", '?')</c> and
    /// hands the parameter to each client-side binder in order. Server-side
    /// binders don't get called here — their <see cref="ValueSql"/>
    /// fragment is in the SQL directly.
    /// </summary>
    void BindParameter(NpgsqlParameter parameter, TDoc document, IStorageSession session);

    /// <summary>
    /// Optional pre-serialization hook. Called once per write before the
    /// document body is serialized into the <c>data</c> parameter — gives
    /// session-derived binders (Correlation/Causation/LastModifiedBy/
    /// Headers) a chance to project the session value onto the document's
    /// mapped member so the value flows into the JSON data column too.
    /// Default no-op; binders whose value isn't session-derived (Version,
    /// LastModified, soft-delete, etc.) don't override.
    /// </summary>
    void ApplyToDocument(TDoc document, IStorageSession session) { }

    /// <summary>
    /// Per-row read hook on the SELECT path. Called from the selector
    /// after the document body has been deserialized, with the metadata
    /// column's position in the result row. Binders for columns whose
    /// values get projected onto the document (Version → <c>[Version]</c>
    /// member, LastModified → <c>[LastModified]</c> member) write through
    /// to the doc's annotated member; binders whose stored values are
    /// purely informational (DotNetType in non-hierarchical case) no-op.
    /// The <paramref name="session"/> is threaded through for binders that
    /// need to deserialize JSON values (e.g. Headers) via the configured
    /// <see cref="ISerializer"/>.
    /// </summary>
    void Apply(DbDataReader reader, int columnOrdinal, TDoc document, IStorageSession session);

    /// <summary>
    /// W3 spike (M16): per-row write hook on the COPY (bulk) path. Each
    /// binder produces a value + NpgsqlDbType and writes both to the
    /// <see cref="NpgsqlBinaryImporter"/> in COPY column order. Unlike
    /// <see cref="BindParameter"/>, server-side binders (e.g.
    /// <c>transaction_timestamp()</c>) must compute a client-side value
    /// here since COPY doesn't honor inline SQL literals — the override
    /// produces <see cref="System.DateTimeOffset.UtcNow"/> for the
    /// LastModified case.
    /// </summary>
    Task WriteToBulkAsync(NpgsqlBinaryImporter writer, TDoc document,
        ISerializer serializer, CancellationToken cancellation);
}
