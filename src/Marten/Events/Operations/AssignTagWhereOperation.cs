using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events.Dcb;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

using Marten.Services;

namespace Marten.Events.Operations;

/// <summary>
/// Retroactively assigns a tag to all events matching a WHERE clause.
/// Generates: INSERT INTO schema.mt_event_tag_{suffix} (value, seq_id)
///            SELECT @value, d.seq_id FROM schema.mt_events as d WHERE {where}
///            ON CONFLICT DO NOTHING
/// then moves the tag's <c>mt_dcb_tag_version</c> rows (#6078, see <see cref="AssignedTagVersionMove"/>).
/// </summary>
internal class AssignTagWhereOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly EventGraph _events;
    private readonly string _schemaName;
    private readonly ITagTypeRegistration _registration;
    private readonly object _value;
    private readonly ISqlFragment _whereFragment;
    private readonly bool _isConjoined;

    public AssignTagWhereOperation(EventGraph events, ITagTypeRegistration registration, object value,
        ISqlFragment whereFragment, bool isConjoined = false)
    {
        _events = events;
        _schemaName = events.DatabaseSchemaName;
        _registration = registration;
        _value = value;
        _whereFragment = whereFragment;
        _isConjoined = isConjoined;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
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

        // #6078: a statement of its own, after the tag rows are in
        builder.StartNewCommand();
        AssignedTagVersionMove.Append(builder, _events, _registration.TableSuffix,
            TagValueStringifier.Stringify(_value), session.TenantId, _whereFragment);
    }

    public Type DocumentType => typeof(IEvent);

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
