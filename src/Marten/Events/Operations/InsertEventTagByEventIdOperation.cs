using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

using Marten.Services;

namespace Marten.Events.Operations;

/// <summary>
/// Inserts a tag row by looking up seq_id from the event's id.
/// Used in Quick append mode where sequences aren't pre-assigned.
/// </summary>
/// <remarks>
/// #5896: <c>mt_events</c> has no index on <c>id</c> (unless <c>EnableUniqueIndexOnEventId</c>), so a bare
/// <c>where id = $1</c> scanned the whole table for every tag row written this way. The lookup goes through
/// the stream instead: <c>stream_id</c> (after <c>tenant_id</c> under conjoined tenancy) leads
/// <c>pk_mt_events_stream_and_version</c>, which bounds it by the length of one stream.
/// </remarks>
internal class InsertEventTagByEventIdOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly string _schemaName;
    private readonly ITagTypeRegistration _registration;
    private readonly Guid _eventId;
    private readonly object _streamIdentity;
    private readonly object _value;
    private readonly bool _isConjoined;
    private readonly bool _useArchivedPartitioning;

    public InsertEventTagByEventIdOperation(string schemaName, ITagTypeRegistration registration, Guid eventId,
        object streamIdentity, object tagValue, bool isConjoined = false, bool useArchivedPartitioning = false)
    {
        _schemaName = schemaName;
        _registration = registration;
        _eventId = eventId;
        _streamIdentity = streamIdentity;
        _value = registration.ExtractValue(tagValue);
        _isConjoined = isConjoined;
        _useArchivedPartitioning = useArchivedPartitioning;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("insert into ");
        builder.Append(_schemaName);
        builder.Append(".mt_event_tag_");
        builder.Append(_registration.TableSuffix);

        if (_isConjoined && _useArchivedPartitioning)
        {
            builder.Append(" (value, tenant_id, seq_id, is_archived) select ");
            builder.AppendParameter(_value);
            builder.Append(", ");
            builder.AppendParameter(session.TenantId);
            builder.Append(", seq_id, is_archived from ");
        }
        else if (_isConjoined)
        {
            builder.Append(" (value, tenant_id, seq_id) select ");
            builder.AppendParameter(_value);
            builder.Append(", ");
            builder.AppendParameter(session.TenantId);
            builder.Append(", seq_id from ");
        }
        else if (_useArchivedPartitioning)
        {
            builder.Append(" (value, seq_id, is_archived) select ");
            builder.AppendParameter(_value);
            builder.Append(", seq_id, is_archived from ");
        }
        else
        {
            builder.Append(" (value, seq_id) select ");
            builder.AppendParameter(_value);
            builder.Append(", seq_id from ");
        }

        builder.Append(_schemaName);
        builder.Append(".mt_events where stream_id = ");
        builder.AppendParameter(_streamIdentity);
        if (_isConjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }

        builder.Append(" and id = ");
        builder.AppendParameter(_eventId);
        builder.Append(" on conflict do nothing");
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
