#nullable enable
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Selectors;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.QueryHandlers;

internal class ListWithStatsQueryHandler<T>: IQueryHandler<IReadOnlyList<T>>, IQueryHandler<IEnumerable<T>>,
    IMaybeStatefulHandler where T : notnull
{
    private readonly int _countIndex;
    private readonly ISelector<T> _selector;
    private readonly ISqlFragment? _statement;
    private readonly QueryStatistics _statistics;

    public ListWithStatsQueryHandler(int countIndex, ISqlFragment? statement, ISelector<T> selector,
        QueryStatistics statistics)
    {
        _countIndex = countIndex;
        _statement = statement;
        _selector = selector;
        _statistics = statistics;
    }

    public bool DependsOnDocumentSelector()
    {
        // There will be from dynamic codegen
        // ReSharper disable once SuspiciousTypeConversion.Global
        return _selector is IDocumentSelector;
    }

    public IQueryHandler CloneForSession(IStorageSession session, QueryStatistics statistics)
    {
        var selector = (ISelector<T>)session.StorageFor<T>().BuildSelector(session);

        return new ListWithStatsQueryHandler<T>(_countIndex, null, selector, statistics);
    }

    async Task<IEnumerable<T>> IQueryHandler<IEnumerable<T>>.HandleAsync(DbDataReader reader, IStorageSession session,
        CancellationToken token)
    {
        var list = await HandleAsync(reader, session, token).ConfigureAwait(false);
        return list;
    }

    public void ConfigureCommand(ICommandBuilder builder, IStorageSession session)
    {
        _statement?.Apply(builder);
    }

    public async Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        var count = 0;
        var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

        await stream.WriteBytes(JsonStreamingExtensions.LeftBracket, token).ConfigureAwait(false);

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            _statistics.TotalResults = await reader.GetFieldValueAsync<int>(_countIndex, token).ConfigureAwait(false);

            count++;
            var source = await reader.As<NpgsqlDataReader>().GetStreamAsync(ordinal, token).ConfigureAwait(false);
            await source.CopyStreamSkippingSOHAsync(stream, token).ConfigureAwait(false);
        }

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            count++;
            await stream.WriteBytes(JsonStreamingExtensions.Comma, token).ConfigureAwait(false);

            var source = await reader.As<NpgsqlDataReader>().GetStreamAsync(ordinal, token).ConfigureAwait(false);
            await source.CopyStreamSkippingSOHAsync(stream, token).ConfigureAwait(false);
        }

        await stream.WriteBytes(JsonStreamingExtensions.RightBracket, token).ConfigureAwait(false);

        return count;
    }

    public async Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IStorageSession session,
        CancellationToken token)
    {
        var list = new List<T>();

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            _statistics.TotalResults = await reader.GetFieldValueAsync<int>(_countIndex, token).ConfigureAwait(false);
            var item = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);
            list.Add(item);
        }
        else
        {
            // no data
            _statistics.TotalResults = 0;
            return list;
        }

        // Get the rest of the data
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var item = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);
            list.Add(item);
        }

        return list;
    }
}
