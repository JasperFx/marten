using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using NotSupportedException = System.NotSupportedException;

namespace Marten.Events.Aggregation.Rebuilds;

internal class QueryForNextAggregateIds: IQueryHandler<IReadOnlyList<AggregateIdentity>>
{
    private readonly string _streamAlias;
    private readonly string _schemaName;
    private readonly TenancyStyle _tenancy;
    private readonly StreamIdentity _streamIdentity;

    public QueryForNextAggregateIds(StoreOptions options, Type aggregateType)
    {
        _streamAlias = options.EventGraph.AggregateAliasFor(aggregateType);
        _schemaName = options.Events.DatabaseSchemaName;
        _tenancy = options.Events.TenancyStyle;
        _streamIdentity = options.EventGraph.StreamIdentity;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append($"select number, id from {_schemaName}.{AggregateRebuildTable.Name} where stream_type = ");
        builder.AppendParameter(_streamAlias);
        if (_tenancy == TenancyStyle.Conjoined)
        {
            builder.Append($" and {TenantIdColumn.Name} = ");
            builder.AppendParameter(session.TenantId);
        }

        builder.Append(" order by number limit ");
        builder.AppendParameter(DaemonSettings.RebuildBatchSize);
    }

    public IReadOnlyList<AggregateIdentity> Handle(DbDataReader reader, IMartenSession session)
    {
        throw new NotSupportedException();
    }

    public async Task<IReadOnlyList<AggregateIdentity>> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
    {
        var list = new List<AggregateIdentity>();

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var number = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            if (_streamIdentity == StreamIdentity.AsGuid)
            {
                var id = await reader.GetFieldValueAsync<Guid>(1, token).ConfigureAwait(false);
                list.Add(new AggregateIdentity(number, id, null));
            }
            else
            {
                var key = await reader.GetFieldValueAsync<string>(1, token).ConfigureAwait(false);
                list.Add(new AggregateIdentity(number, Guid.Empty, key));
            }
        }

        return list;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotSupportedException();
    }
}
