#nullable enable
using JasperFx.Events;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Metadata;

/// <summary>
/// <see cref="IEventMetadataBinder"/> for the optional <c>user_name</c>
/// varchar column. Binds <see cref="IEvent.UserName"/> as a string parameter.
/// </summary>
/// <remarks>
/// The session-level "user name" lives on <see cref="IStorageSession.LastModifiedBy"/>,
/// but the event appender plumbs that into the per-event
/// <see cref="IEvent.UserName"/> before queuing the operation, and we bind
/// off the event so the SQL stays self-contained.
/// </remarks>
internal sealed class UserNameColumnBinder: IEventMetadataBinder
{
    public string ColumnName => "user_name";
    public string ValueSql => "?";

    public void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IStorageSession session)
    {
        pb.AppendParameter(@event.UserName, NpgsqlDbType.Varchar);
    }
}
