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
/// Generates: INSERT INTO schema.mt_event_tag_{suffix} (value[, tenant_id], seq_id[, is_archived])
///            SELECT @value[, @tenant], d.seq_id[, d.is_archived] FROM schema.mt_events as d
///            WHERE ({where})[ AND d.tenant_id = @tenant]
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
        var archivedPartitioning = _events.UseArchivedStreamPartitioning;

        builder.Append("insert into ");
        builder.Append(_schemaName);
        builder.Append(".mt_event_tag_");
        builder.Append(_registration.TableSuffix);

        builder.Append(_isConjoined ? " (value, tenant_id, seq_id" : " (value, seq_id");
        builder.Append(archivedPartitioning ? ", is_archived) select " : ") select ");
        builder.AppendParameter(_value);

        if (_isConjoined)
        {
            builder.Append(", ");
            builder.AppendParameter(session.TenantId);
        }

        builder.Append(", d.seq_id");

        // The tag row sits in its event's partition, and the foreign key is on (seq_id, is_archived), so an
        // archived event's tag row has to say it is archived
        if (archivedPartitioning)
        {
            builder.Append(", d.is_archived");
        }

        builder.Append(" from ");
        builder.Append(_schemaName);
        builder.Append(".mt_events as d where (");
        _whereFragment.Apply(builder);
        builder.Append(")");

        // #6080: only the session tenant's events, which are the only ones the tag rows can belong to
        if (_isConjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }

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
