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

internal class InsertEventTagOperation: IStorageOperation
{
    private readonly string _schemaName;
    private readonly TagTypeRegistration _registration;
    private readonly long _seqId;
    private readonly object _value;

    public InsertEventTagOperation(string schemaName, TagTypeRegistration registration, long seqId, object tagValue)
    {
        _schemaName = schemaName;
        _registration = registration;
        _seqId = seqId;
        _value = registration.ExtractValue(tagValue);
    }

    public InsertEventTagOperation(string schemaName, TagTypeRegistration registration, long seqId, object value,
        bool valueAlreadyExtracted)
    {
        _schemaName = schemaName;
        _registration = registration;
        _seqId = seqId;
        _value = value;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("insert into ");
        builder.Append(_schemaName);
        builder.Append(".mt_event_tag_");
        builder.Append(_registration.TableSuffix);
        builder.Append(" (value, seq_id) values (");
        builder.AppendParameter(_value);
        builder.Append(", ");
        builder.AppendParameter(_seqId);
        builder.Append(") on conflict do nothing");
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
