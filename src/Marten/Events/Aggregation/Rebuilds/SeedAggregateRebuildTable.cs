using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Schema;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage.Metadata;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Aggregation.Rebuilds;

internal class SeedAggregateRebuildTable: IStorageOperation
{
    private readonly string _streamAlias;
    private readonly string _schemaName;

    public SeedAggregateRebuildTable(StoreOptions options, Type aggregateType)
    {
        _streamAlias = options.EventGraph.AggregateAliasFor(aggregateType);
        _schemaName = options.Events.DatabaseSchemaName;
    }

    public void ConfigureCommand(ICommandBuilder builder, IOperationSession session)
    {
        builder.Append($"delete from {_schemaName}.{AggregateRebuildTable.Name} where stream_type = ");
        builder.AppendParameter(_streamAlias);
        builder.StartNewCommand();
        builder.Append($"insert into {_schemaName}.{AggregateRebuildTable.Name} (id, stream_type, {TenantIdColumn.Name}, completed) select id, '{_streamAlias}', {TenantIdColumn.Name}, false from {_schemaName}.{StreamsTable.TableName} where type = '{_streamAlias}' and is_archived = false order by timestamp desc");
    }

    public Type DocumentType => typeof(IEvent);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // Nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Other;
    }
}
