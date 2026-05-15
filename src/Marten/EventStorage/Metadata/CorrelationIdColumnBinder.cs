#nullable enable
using JasperFx.Events;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Metadata;

/// <summary>
/// <see cref="IEventMetadataBinder"/> for the optional <c>correlation_id</c>
/// varchar column. Symmetric with <see cref="CausationIdColumnBinder"/>.
/// </summary>
internal sealed class CorrelationIdColumnBinder: IEventMetadataBinder
{
    public string ColumnName => "correlation_id";
    public string ValueSql => "?";

    public void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IMartenSession session)
    {
        pb.AppendParameter(@event.CorrelationId, NpgsqlDbType.Varchar);
    }
}
