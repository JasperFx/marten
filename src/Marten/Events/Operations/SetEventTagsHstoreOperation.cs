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

namespace Marten.Events.Operations;

/// <summary>
/// Writes DCB tags inline on <c>mt_events.tags</c> for an event whose <c>seq_id</c>
/// is already known (rich append mode). Merges with any existing tags via the
/// <c>hstore || hstore</c> concatenation operator so multiple tag writes on the same
/// event compose correctly.
/// </summary>
internal class SetEventTagsHstoreOperation: IStorageOperation
{
    private readonly string _schemaName;
    private readonly long _seqId;
    private readonly Dictionary<string, string> _tags;
    private readonly bool _isConjoined;

    public SetEventTagsHstoreOperation(string schemaName, long seqId, Dictionary<string, string> tags,
        bool isConjoined)
    {
        _schemaName = schemaName;
        _seqId = seqId;
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
        builder.Append(" where seq_id = ");
        builder.AppendParameter(_seqId);
        if (_isConjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(session.TenantId);
        }
    }

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // no-op
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role() => OperationRole.Events;
}
