using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Aggregation.Rebuilds;

internal class DequeuePendingAggregateRebuilds: IStorageOperation, NoDataReturnedCall
{
    private readonly long[] _numbers;
    private readonly string _schemaName;

    public DequeuePendingAggregateRebuilds(StoreOptions options, IEnumerable<long> numbers)
    {
        _numbers = numbers.ToArray();
        _schemaName = options.Events.DatabaseSchemaName;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append($"delete from {_schemaName}.{AggregateRebuildTable.Name} where number = ANY(");
        builder.AppendParameter(_numbers, NpgsqlDbType.Array | NpgsqlDbType.Bigint);
        builder.Append(")");
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

    public OperationRole Role() => OperationRole.Other;
}
