#nullable enable
using JasperFx.Events;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Metadata;

/// <summary>
/// <see cref="IEventMetadataBinder"/> for the optional <c>causation_id</c>
/// varchar column. Binds <see cref="IEvent.CausationId"/> as a string
/// parameter. Included in the descriptor's binder array iff
/// <c>StoreOptions.Events.MetadataConfig.CausationIdEnabled</c> is on.
/// </summary>
/// <remarks>
/// Read-back goes through <c>IEventTableColumn.ReadValueSync/Async</c> on
/// the existing <c>CausationIdColumn</c> (#4411 — varchar columns work
/// without serializer threading), so this binder is write-only. The
/// metadata-binder seam only intercepts the write path.
/// </remarks>
internal sealed class CausationIdColumnBinder: IEventMetadataBinder
{
    public string ColumnName => "causation_id";
    public string ValueSql => "?";

    public void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IMartenSession session)
    {
        pb.AppendParameter(@event.CausationId, NpgsqlDbType.Varchar);
    }
}
