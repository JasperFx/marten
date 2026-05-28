#nullable enable
using System;
using JasperFx.Events;
using Marten.Events;

namespace Marten.EventStorage.Rich;

/// <summary>
/// Per-<see cref="EventGraph"/> descriptor for the Rich (full-mode) append
/// flow. Holds only the SQL strings + delegates the Rich operations need —
/// nothing about the Quick paths leaks in.
/// </summary>
/// <remarks>
/// Rich-mode SQL is split into a prefix + suffix because the source-gen
/// output appends parameters inline between them, one per core column plus
/// one virtual <see cref="IEventMetadataBinder.Bind"/> call per active
/// metadata binder. See <see cref="MetadataBinders"/> for the metadata
/// hybrid; see SPIKE.md "Metadata columns" for the rationale.
/// </remarks>
public sealed class RichEventStorageDescriptor
{
    public RichEventStorageDescriptor(
        string appendEventSqlPrefix,
        string appendEventSqlSuffix,
        string insertStreamSql,
        string updateStreamVersionSql,
        string streamStateSelectSql,
        Func<IEvent, string> serializeEventData,
        Func<IEvent, byte[]?> serializeEventBdata,
        IEventMetadataBinder[] metadataBinders)
    {
        AppendEventSqlPrefix = appendEventSqlPrefix;
        AppendEventSqlSuffix = appendEventSqlSuffix;
        InsertStreamSql = insertStreamSql;
        UpdateStreamVersionSql = updateStreamVersionSql;
        StreamStateSelectSql = streamStateSelectSql;
        SerializeEventData = serializeEventData;
        SerializeEventBdata = serializeEventBdata;
        MetadataBinders = metadataBinders;
    }

    public string AppendEventSqlPrefix { get; }
    public string AppendEventSqlSuffix { get; }
    public string InsertStreamSql { get; }
    public string UpdateStreamVersionSql { get; }
    public string StreamStateSelectSql { get; }

    /// <summary>
    ///     Serializer for the <c>data</c> jsonb column. Returns the full JSON
    ///     payload for JSON-serialized events and the literal <c>{}</c>
    ///     placeholder for binary-serialized events (the real payload lives in
    ///     <c>bdata</c> in that case — see <see cref="SerializeEventBdata"/>).
    /// </summary>
    public Func<IEvent, string> SerializeEventData { get; }

    /// <summary>
    ///     #4515: serializer for the <c>bdata</c> bytea column. Returns the
    ///     serialized bytes for binary-serialized events; returns <c>null</c>
    ///     (bound as <see cref="System.DBNull.Value"/>) for JSON-serialized
    ///     events.
    /// </summary>
    public Func<IEvent, byte[]?> SerializeEventBdata { get; }

    /// <summary>
    /// Ordered metadata-column binders. Rich-mode only — Quick-mode
    /// descriptors don't expose this because Quick's metadata binding is
    /// hand-written inline in the source-gen output (per-batch array
    /// parameters; no per-event dispatch worth abstracting).
    /// </summary>
    public IEventMetadataBinder[] MetadataBinders { get; }

    /// <summary>
    /// SQL suffix for the per-event <c>QuickWithVersion</c> path on this
    /// Rich descriptor — same per-event INSERT shape as
    /// <see cref="AppendEventSqlSuffix"/>, but with a server-side
    /// <c>nextval('{schema}.mt_events_sequence')</c> literal in place of the
    /// bound <c>seq_id</c> parameter. Used by
    /// <see cref="RichEventStorage{TId}.QuickAppendEventWithVersion"/>, which
    /// is invoked by <c>JasperFx.Events.EventSlice.BuildOperations</c>
    /// during async-projection side-effect replay (raised events). The
    /// caller pre-assigns <c>event.Version</c> but not <c>event.Sequence</c>,
    /// so the server claims the sequence inline. Tracked in #4428.
    /// </summary>
    public string AppendEventQuickWithVersionSqlSuffix { get; init; } = string.Empty;

    /// <summary>
    /// Ordered metadata-column binders for the per-event
    /// <c>QuickWithVersion</c> path on this Rich descriptor. Identical to
    /// <see cref="MetadataBinders"/> except <c>SequenceColumnBinder</c> is
    /// omitted — <c>seq_id</c> is server-set via the <c>nextval(...)</c>
    /// literal in <see cref="AppendEventQuickWithVersionSqlSuffix"/>.
    /// Tracked in #4428.
    /// </summary>
    public IEventMetadataBinder[] MetadataBindersWithoutSequence { get; init; }
        = System.Array.Empty<IEventMetadataBinder>();

    /// <summary>
    /// Whether the events table is conjoined-tenant — every per-stream query
    /// (StreamState lookup, UpdateStreamVersion, etc.) needs a trailing
    /// <c>and tenant_id = $N</c> when this is true.
    /// </summary>
    /// <remarks>
    /// Init-only so the dialect sets it once at descriptor construction.
    /// The codegen path checks <c>graph.TenancyStyle == TenancyStyle.Conjoined</c>
    /// inline; the closed-shape path lifts that to a per-descriptor boolean
    /// so the storage methods don't carry an <see cref="EventGraph"/> reference.
    /// </remarks>
    public bool IsTenancyConjoined { get; init; }

    /// <summary>
    /// Whether streams are identified by <see cref="System.Guid"/> (true) or
    /// <see cref="string"/> (false). The Rich AppendEvent operation reads
    /// <c>Stream.Id</c> vs <c>Stream.Key</c> based on this flag.
    /// </summary>
    public bool IsGuidStreamIdentity { get; init; }

    /// <summary>
    /// Configures the <c>mt_streams</c> insert command. The closure owns
    /// the SQL shape (column list, parameter binds, tenancy/identity
    /// variants) and the actual <see cref="IGroupedParameterBuilder"/>
    /// dispatch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why a closure rather than an inlined operation subclass: InsertStream
    /// runs once per stream — not on the per-event hot path. Source-gen
    /// (#4413 — <see cref="RichAppendEventOperation"/>) gets its value from
    /// the N-events-per-stream path; per-stream operations are
    /// descriptor-driven so the variant matrix stays in the dialect.
    /// </para>
    /// <para>
    /// Init-only so the dialect installs it at descriptor-build time. Throws
    /// if invoked before a closure is installed (means the dialect hasn't
    /// wired this codepath yet — e.g., strict-identity-enforcement variant
    /// isn't supported in v9-alpha).
    /// </para>
    /// </remarks>
    public System.Action<Weasel.Postgresql.ICommandBuilder, StreamAction> ConfigureInsertStreamCommand { get; init; }
        = static (_, _) => throw new System.NotSupportedException(
            "RichEventStorageDescriptor.ConfigureInsertStreamCommand was not installed by the dialect. " +
            "This indicates a Rich-mode configuration variant (e.g., strict stream-identity enforcement) " +
            "that the closed-shape hierarchy doesn't yet cover. Track on #4412.");

    /// <summary>
    /// Configures the <c>mt_streams</c> update-version command. Symmetric
    /// to <see cref="ConfigureInsertStreamCommand"/> — SQL shape and binds
    /// owned by the dialect-installed closure.
    /// </summary>
    public System.Action<Weasel.Postgresql.ICommandBuilder, StreamAction> ConfigureUpdateStreamVersionCommand { get; init; }
        = static (_, _) => throw new System.NotSupportedException(
            "RichEventStorageDescriptor.ConfigureUpdateStreamVersionCommand was not installed by the dialect. " +
            "Track on #4412.");
}
