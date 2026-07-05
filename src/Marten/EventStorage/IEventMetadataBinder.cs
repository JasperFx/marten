#nullable enable
using System.Data.Common;
using JasperFx.Events;
using Marten.Internal;
using Weasel.Postgresql;

namespace Marten.EventStorage;

/// <summary>
/// Per-metadata-column seam in the closed-shape event-storage hierarchy.
/// Sits alongside (not instead of) the inlined source-gen-emitted writes
/// for the core columns (id, version, data, type, dotnet_type, stream id) —
/// see SPIKE.md for the hybrid rationale.
/// </summary>
/// <remarks>
/// <para>
/// Metadata columns are where the variability lives: each is on-or-off per
/// <see cref="EventGraph"/> configuration, some carry server-side values
/// that need writing back onto the event after Postprocess, and the dialect
/// can swap their representation entirely (Postgres HSTORE tags ↔ SQL Server
/// JSON / varbinary). Inlining those into the source-gen output would
/// either blow up the closed-shape matrix combinatorially or push runtime
/// if/then branches into the hot path.
/// </para>
/// <para>
/// The binder array is built once per <see cref="EventGraph"/> by the
/// descriptor builder, ordered to match the SQL column list. Per-call cost
/// is one virtual <see cref="Bind"/> per active metadata binder — typically
/// 1–4 (tenant + zero or more of headers / causation / correlation /
/// username / tags / sequence / timestamp). Cheap relative to the Npgsql
/// parameter allocation each Bind triggers.
/// </para>
/// </remarks>
public interface IEventMetadataBinder
{
    /// <summary>
    /// Stable column name. The descriptor builder threads this through to
    /// the dialect when composing the INSERT column list so the parameter
    /// order matches the column order.
    /// </summary>
    string ColumnName { get; }

    /// <summary>
    /// SQL fragment placed in the VALUES list at this binder's position.
    /// <c>"?"</c> for a real parameter, <c>"now()"</c> for a server-side
    /// constant (timestamp), <c>"nextval('mt_events_sequence_seq')"</c> for
    /// a server-generated sequence (sequence column). Server-side values
    /// bind nothing in <see cref="Bind"/> but may need to read back in
    /// <see cref="OnRead"/> if the operation has a RETURNING clause.
    /// </summary>
    string ValueSql { get; }

    /// <summary>
    /// Per-event write hook. Called by the closed-shape operation's
    /// <c>ConfigureCommand</c> after the inlined core-column writes, in the
    /// same order as the descriptor's binder array. No-op for
    /// server-side-constant binders (timestamp / sequence under the
    /// quick-append paths).
    /// </summary>
    void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IStorageSession session);

    /// <summary>
    /// Per-event read-back hook called from the operation's
    /// <c>Postprocess</c> / <c>PostprocessAsync</c> after the database
    /// returns rows. <paramref name="columnOrdinal"/> is the binder's
    /// position in the result row (which is the same as its position in
    /// the binder array for RETURNING-shaped operations).
    /// </summary>
    /// <remarks>
    /// Default implementation is no-op. Binders that own server-set values
    /// (sequence, timestamp) override this to write the returned value back
    /// onto the <see cref="IEvent"/> instance — that's the "call back and
    /// set values on the document itself after we get results" path that
    /// motivates this seam in the first place. Operations whose SQL has no
    /// RETURNING clause (today's full-mode append) never call this; the
    /// binder array's <see cref="OnRead"/> hooks are only exercised by the
    /// quick-append variants.
    /// </remarks>
    void OnRead(DbDataReader reader, int columnOrdinal, StreamAction stream, IEvent @event)
    {
        // Default no-op — only binders with server-set values override this.
    }
}
