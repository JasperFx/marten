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
    private readonly ITagTypeRegistration _registration;
    private readonly long _seqId;
    private readonly object _value;
    private readonly bool _isConjoined;
    private readonly bool _useArchivedPartitioning;

    public InsertEventTagOperation(string schemaName, ITagTypeRegistration registration, long seqId, object tagValue,
        bool isConjoined = false, bool useArchivedPartitioning = false)
    {
        _schemaName = schemaName;
        _registration = registration;
        _seqId = seqId;
        _value = registration.ExtractValue(tagValue);
        _isConjoined = isConjoined;
        _useArchivedPartitioning = useArchivedPartitioning;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("insert into ");
        builder.Append(_schemaName);
        builder.Append(".mt_event_tag_");
        builder.Append(_registration.TableSuffix);

        if (_isConjoined && _useArchivedPartitioning)
        {
            builder.Append(" (value, tenant_id, seq_id, is_archived) values (");
            builder.AppendParameter(_value);
            builder.Append(", ");
            builder.AppendParameter(session.TenantId);
            builder.Append(", ");
            builder.AppendParameter(_seqId);
            builder.Append(", false");
        }
        else if (_isConjoined)
        {
            builder.Append(" (value, tenant_id, seq_id) values (");
            builder.AppendParameter(_value);
            builder.Append(", ");
            builder.AppendParameter(session.TenantId);
            builder.Append(", ");
            builder.AppendParameter(_seqId);
        }
        else if (_useArchivedPartitioning)
        {
            builder.Append(" (value, seq_id, is_archived) values (");
            builder.AppendParameter(_value);
            builder.Append(", ");
            builder.AppendParameter(_seqId);
            builder.Append(", false");
        }
        else
        {
            builder.Append(" (value, seq_id) values (");
            builder.AppendParameter(_value);
            builder.Append(", ");
            builder.AppendParameter(_seqId);
        }

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
