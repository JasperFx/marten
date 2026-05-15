#nullable enable
using JasperFx.Events;
using Marten.Events;

namespace Marten.EventStorage;

/// <summary>
/// Dialect seam for the closed-shape event-storage hierarchy. Marten ships
/// the Postgres implementation; Polecat ships the SQL-Server implementation.
/// The two share the closed-shape operation bases + descriptor surface, only
/// differing in what SQL text the descriptor builder bakes in.
/// </summary>
/// <remarks>
/// <para>
/// Spike scope (#4404 W4 / supersedes #4400). The current shape exposes
/// fully-composed SQL templates because that's the simplest seam to land —
/// every dialect implementation produces complete SQL strings the
/// descriptor stores verbatim. A more granular seam (per-fragment builders)
/// would let dialects mix-and-match better at the cost of a more complex
/// abstraction. Open question (6) in SPIKE.md.
/// </para>
/// <para>
/// All template methods receive an <see cref="EventGraph"/> so the dialect
/// can read schema configuration (schema name, optional column flags,
/// stream identity). They return SQL strings the descriptor stores as
/// <c>readonly string</c> fields and that the source-gen-emitted operation
/// subclasses pass straight into <c>ICommandBuilder.Append</c> calls — no
/// per-call string concatenation, no per-call parameter substitution.
/// </para>
/// </remarks>
internal interface IEventStoreSqlDialect
{
    /// <summary>
    /// SQL prefix for a full-mode <c>INSERT INTO mt_events</c> row append. Ends
    /// at the <c>VALUES (</c> token; the closed-shape operation appends its
    /// parameter list + a trailing <c>)</c>. Includes the column list with
    /// the schema's optional columns (headers, causation, correlation, etc.)
    /// baked in based on <paramref name="graph"/>.
    /// </summary>
    string AppendEventFullPrefix(EventGraph graph);

    /// <summary>
    /// Trailing SQL fragment for the full-mode append. Currently always
    /// <c>")"</c> for Postgres but parameterized so SQL Server can emit an
    /// <c>OUTPUT</c> clause if needed.
    /// </summary>
    string AppendEventFullSuffix(EventGraph graph);

    /// <summary>
    /// Full SQL for the quick-with-version variant. Different shape from
    /// full-mode because the event version is bound as a parameter rather
    /// than computed from a stream-state lookup.
    /// </summary>
    string AppendEventQuickWithVersion(EventGraph graph);

    /// <summary>
    /// Full SQL for the batch-quick-append variant — calls the
    /// <c>mt_quick_append_events</c> server function with array parameters.
    /// </summary>
    string QuickAppendEvents(EventGraph graph);

    /// <summary>
    /// Variant of <see cref="QuickAppendEvents"/> for the server-timestamps
    /// configuration. Today's codegen embeds this as a branch inside the
    /// quick-append base; W4 splits it out so the closed-shape hot path has
    /// no per-call branch.
    /// </summary>
    string QuickAppendEventsWithServerTimestamps(EventGraph graph);

    /// <summary>
    /// SQL for the <c>mt_streams</c> row insert when a new stream is opened.
    /// </summary>
    string InsertStream(EventGraph graph);

    /// <summary>
    /// SQL for the <c>UPDATE mt_streams SET version = ?</c> with the
    /// expected-version guard that lets us detect concurrent appends.
    /// </summary>
    string UpdateStreamVersion(EventGraph graph);

    /// <summary>
    /// SQL for the stream-state lookup query handler.
    /// </summary>
    string StreamStateSelect(EventGraph graph);
}
