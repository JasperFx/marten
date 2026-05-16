#nullable enable
using JasperFx.Events;
using Marten.EventStorage.Quick;
using Marten.EventStorage.QuickWithServerTimestamps;
using Marten.EventStorage.Rich;
using Marten.Events;
using Marten.Services;

namespace Marten.EventStorage;

/// <summary>
/// Dialect seam for the closed-shape event-storage hierarchy. Marten ships
/// the Postgres implementation; Polecat ships the SQL-Server implementation
/// (after W2 cuts <c>JasperFx.Storage</c>). The dialect builds per-mode
/// descriptors end-to-end — SQL strings, metadata-column ordering, and
/// binder selection are all joint concerns the dialect owns.
/// </summary>
/// <remarks>
/// <para>
/// Earlier sketch had the dialect expose SQL templates plus a separate
/// binder-list builder; that split kept the SQL and the binder ordering
/// in two places and made the coordination contract implicit. Production
/// shape: one method per mode returns the whole descriptor. The dialect's
/// implementation knows which columns are in the SQL (config-aware) and
/// which binders bind each metadata column, in lockstep.
/// </para>
/// </remarks>
internal interface IEventStoreSqlDialect
{
    RichEventStorageDescriptor BuildRichDescriptor(EventGraph graph, ISerializer serializer);

    QuickEventStorageDescriptor BuildQuickDescriptor(EventGraph graph, ISerializer serializer);

    QuickWithServerTimestampsEventStorageDescriptor BuildQuickWithServerTimestampsDescriptor(
        EventGraph graph, ISerializer serializer);
}
