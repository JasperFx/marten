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

namespace Marten.Events.Operations;

/// <summary>
/// Inserts a tag row by looking up seq_id from the event's id.
/// Used in Quick append mode where sequences aren't pre-assigned.
/// </summary>
internal class InsertEventTagByEventIdOperation: IStorageOperation
{
    private readonly string _schemaName;
    private readonly ITagTypeRegistration _registration;
    private readonly Guid _eventId;
    private readonly object _value;

    public InsertEventTagByEventIdOperation(string schemaName, ITagTypeRegistration registration, Guid eventId, object tagValue)
    {
        _schemaName = schemaName;
        _registration = registration;
        _eventId = eventId;
        _value = registration.ExtractValue(tagValue);
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("insert into ");
        builder.Append(_schemaName);
        builder.Append(".mt_event_tag_");
        builder.Append(_registration.TableSuffix);
        builder.Append(" (value, seq_id) select ");
        builder.AppendParameter(_value);
        builder.Append(", seq_id from ");
        builder.Append(_schemaName);
        builder.Append(".mt_events where id = ");
        builder.AppendParameter(_eventId);
        builder.Append(" on conflict do nothing");
    }

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // No-op
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
