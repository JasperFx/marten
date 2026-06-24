using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Postgresql;

using Marten.Services;

namespace Marten.Events.Operations;

/// <summary>
/// Writes DCB tags inline on <c>mt_events.tags</c> for an event whose row identifier
/// is the event <c>id</c> rather than a pre-assigned <c>seq_id</c> (quick append mode).
/// Mirrors <see cref="InsertEventTagByEventIdOperation"/> but writes the tags as a
/// single hstore concatenation against the existing row.
/// </summary>
internal class SetEventTagsHstoreByEventIdOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly string _schemaName;
    private readonly Guid _eventId;
    private readonly Dictionary<string, string> _tags;
    private readonly bool _isConjoined;

    public SetEventTagsHstoreByEventIdOperation(string schemaName, Guid eventId, Dictionary<string, string> tags,
        bool isConjoined)
    {
        _schemaName = schemaName;
        _eventId = eventId;
        _tags = tags;
        _isConjoined = isConjoined;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("update ");
        builder.Append(_schemaName);
        builder.Append(".mt_events set tags = coalesce(tags, ''::hstore) || ");
        var p = builder.AppendParameter(_tags);
        p.NpgsqlDbType = NpgsqlDbType.Hstore;
        builder.Append(" where id = ");
        builder.AppendParameter(_eventId);
        if (_isConjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
