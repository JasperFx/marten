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
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Operations;

/// <summary>
/// Retroactively assigns a tag to all events matching a WHERE clause.
/// Generates: INSERT INTO schema.mt_event_tag_{suffix} (value, seq_id)
///            SELECT @value, d.seq_id FROM schema.mt_events as d WHERE {where}
///            ON CONFLICT DO NOTHING
/// </summary>
internal class AssignTagWhereOperation: IStorageOperation
{
    private readonly string _schemaName;
    private readonly ITagTypeRegistration _registration;
    private readonly object _value;
    private readonly ISqlFragment _whereFragment;
    private readonly bool _isConjoined;

    public AssignTagWhereOperation(string schemaName, ITagTypeRegistration registration, object value,
        ISqlFragment whereFragment, bool isConjoined = false)
    {
        _schemaName = schemaName;
        _registration = registration;
        _value = value;
        _whereFragment = whereFragment;
        _isConjoined = isConjoined;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("insert into ");
        builder.Append(_schemaName);
        builder.Append(".mt_event_tag_");
        builder.Append(_registration.TableSuffix);

        if (_isConjoined)
        {
            builder.Append(" (value, tenant_id, seq_id) select ");
            builder.AppendParameter(_value);
            builder.Append(", ");
            builder.AppendParameter(session.TenantId);
            builder.Append(", d.seq_id from ");
        }
        else
        {
            builder.Append(" (value, seq_id) select ");
            builder.AppendParameter(_value);
            builder.Append(", d.seq_id from ");
        }

        builder.Append(_schemaName);
        builder.Append(".mt_events as d where ");
        _whereFragment.Apply(builder);
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
