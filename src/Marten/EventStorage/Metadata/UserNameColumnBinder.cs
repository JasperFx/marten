#nullable enable
using JasperFx.Events;
using Marten.Internal;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.EventStorage.Metadata;

/// <summary>
/// <see cref="IEventMetadataBinder"/> for the optional <c>user_name</c>
/// varchar column. Binds <see cref="IEvent.UserName"/> — mirrors
/// <c>UserNameColumn.GenerateAppendCode</c> on the codegen path.
/// </summary>
/// <remarks>
/// The session-level "user name" lives on <see cref="IMartenSession.LastModifiedBy"/>
/// but the codegen path binds the per-event <see cref="IEvent.UserName"/>
/// (which the event appender plumbs in from the session before queuing
/// the operation). We match that — bind off the event, not the session.
/// </remarks>
internal sealed class UserNameColumnBinder: IEventMetadataBinder
{
    public string ColumnName => "user_name";
    public string ValueSql => "?";

    public void Bind(IGroupedParameterBuilder pb, StreamAction stream, IEvent @event, IMartenSession session)
    {
        pb.AppendParameter(@event.UserName, NpgsqlDbType.Varchar);
    }
}
