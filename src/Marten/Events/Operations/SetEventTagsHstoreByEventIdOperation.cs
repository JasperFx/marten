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
/// <remarks>
/// #5896: the bulk <c>mt_quick_append_events</c> function now writes HStore tags in its own insert, so this
/// only runs for the per-event insert routes. <c>mt_events</c> has no index on <c>id</c> (unless
/// <c>EnableUniqueIndexOnEventId</c>), so the row is found through the stream: <c>stream_id</c> leads
/// <c>pk_mt_events_stream_and_version</c> (after <c>tenant_id</c> under conjoined tenancy), which bounds the
/// lookup by the length of one stream instead of scanning the whole table.
/// </remarks>
internal class SetEventTagsHstoreByEventIdOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly string _schemaName;
    private readonly Guid _eventId;
    private readonly object _streamIdentity;
    private readonly Dictionary<string, string> _tags;
    private readonly bool _isConjoined;

    public SetEventTagsHstoreByEventIdOperation(string schemaName, Guid eventId, object streamIdentity,
        Dictionary<string, string> tags, bool isConjoined)
    {
        _schemaName = schemaName;
        _eventId = eventId;
        _streamIdentity = streamIdentity;
        _tags = tags;
        _isConjoined = isConjoined;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("update ");
        builder.Append(_schemaName);
        builder.Append(".mt_events set tags = coalesce(tags, ''::hstore) || ");
        var p = builder.AppendParameter(_tags);
        p.NpgsqlDbType = NpgsqlDbType.Hstore;
        builder.Append(" where stream_id = ");
        builder.AppendParameter(_streamIdentity);
        if (_isConjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }

        builder.Append(" and id = ");
        builder.AppendParameter(_eventId);
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
