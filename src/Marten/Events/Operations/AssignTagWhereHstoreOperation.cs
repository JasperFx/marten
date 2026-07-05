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
using Weasel.Postgresql.SqlGeneration;

using Marten.Services;

namespace Marten.Events.Operations;

/// <summary>
/// HStore-mode counterpart of <see cref="AssignTagWhereOperation"/>: retroactively
/// merges a single tag key-value into <c>mt_events.tags</c> for every row matching
/// a user-supplied WHERE clause. Uses the <c>hstore || hstore</c> concatenation
/// operator so existing tags on the row are preserved.
/// </summary>
internal class AssignTagWhereHstoreOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly string _schemaName;
    private readonly Dictionary<string, string> _tags;
    private readonly ISqlFragment _whereFragment;
    private readonly bool _isConjoined;

    public AssignTagWhereHstoreOperation(string schemaName, string tagKey, string tagValue,
        ISqlFragment whereFragment, bool isConjoined)
    {
        _schemaName = schemaName;
        _tags = new Dictionary<string, string>(1) { [tagKey] = tagValue };
        _whereFragment = whereFragment;
        _isConjoined = isConjoined;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        builder.Append("update ");
        builder.Append(_schemaName);
        builder.Append(".mt_events as d set tags = coalesce(d.tags, ''::hstore) || ");
        var p = builder.AppendParameter(_tags);
        p.NpgsqlDbType = NpgsqlDbType.Hstore;
        builder.Append(" where ");
        _whereFragment.Apply(builder);
        if (_isConjoined)
        {
            builder.Append(" and d.tenant_id = ");
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
