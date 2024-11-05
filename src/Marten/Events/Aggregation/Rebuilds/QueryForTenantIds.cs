using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Weasel.Postgresql;

namespace Marten.Events.Aggregation.Rebuilds;

internal class QueryForTenantIds: IQueryHandler<IReadOnlyList<string>>
{
    private readonly string _streamAlias;
    private readonly string _schemaName;

    public QueryForTenantIds(StoreOptions options, Type aggregateType)
    {
        _streamAlias = options.Storage.MappingFor(aggregateType).Alias;
        _schemaName = options.Events.DatabaseSchemaName;
    }

    public void ConfigureCommand(IPostgresqlCommandBuilder builder, IMartenSession session)
    {
        // TODO -- what about an extreme number of tenants?
        builder.Append($"select distinct(tenant_id) from {_schemaName}.{AggregateRebuildTable.Name} where stream_type = ");
        builder.AppendParameter(_streamAlias);
    }

    public IReadOnlyList<string> Handle(DbDataReader reader, IMartenSession session)
    {
        throw new NotSupportedException();
    }

    public async Task<IReadOnlyList<string>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        var list = new List<string>();
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            list.Add(await reader.GetFieldValueAsync<string>(0, token).ConfigureAwait(false));
        }

        return list;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
