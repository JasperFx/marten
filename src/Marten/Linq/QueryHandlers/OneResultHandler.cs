#nullable enable
using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Selectors;
using Marten.Util;
using Npgsql;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.QueryHandlers;

internal class OneResultHandler<T>: IQueryHandler<T>, IMaybeStatefulHandler where T : notnull
{
    private const string NoElementsMessage = "Sequence contains no elements";
    private const string MoreThanOneElementMessage = "Sequence contains more than one element";
    private readonly bool _canBeMultiples;
    private readonly bool _canBeNull;
    private readonly ISelector<T> _selector;
    private readonly ISqlFragment? _statement;

    public OneResultHandler(ISqlFragment? statement, ISelector<T> selector,
        bool canBeNull = true, bool canBeMultiples = true)
    {
        _statement = statement;
        _selector = selector;
        _canBeNull = canBeNull;
        _canBeMultiples = canBeMultiples;
    }

    public bool DependsOnDocumentSelector()
    {
        // There will be from dynamic codegen
        // ReSharper disable once SuspiciousTypeConversion.Global
        return _selector is IDocumentSelector;
    }

    public IQueryHandler CloneForSession(IMartenSession session, QueryStatistics statistics)
    {
        var selector = (ISelector<T>)session.StorageFor<T>().BuildSelector(session);
        return new OneResultHandler<T>(null, selector, _canBeNull, _canBeMultiples);
    }

    public void ConfigureCommand(IPostgresqlCommandBuilder builder, IMartenSession session)
    {
        _statement?.Apply(builder);
    }

    public T Handle(DbDataReader reader, IMartenSession session)
    {
        var hasResult = reader.Read();
        if (!hasResult)
        {
            if (_canBeNull)
            {
                return default;
            }

            throw new InvalidOperationException(NoElementsMessage);
        }

        var result = _selector.Resolve(reader);

        if (!_canBeMultiples && reader.Read())
        {
            throw new InvalidOperationException(MoreThanOneElementMessage);
        }

        return result;
    }

    public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session,
        CancellationToken token)
    {
        var hasResult = await reader.ReadAsync(token).ConfigureAwait(false);
        if (!hasResult)
        {
            if (_canBeNull)
            {
                return default;
            }

            throw new InvalidOperationException(NoElementsMessage);
        }

        var result = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);

        if (!_canBeMultiples && await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw new InvalidOperationException(MoreThanOneElementMessage);
        }

        return result;
    }

    public async Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        var npgsqlReader = reader.As<NpgsqlDataReader>();

        var hasResult = await reader.ReadAsync(token).ConfigureAwait(false);
        if (!hasResult)
        {
            if (_canBeNull)
            {
                return 0;
            }

            throw new InvalidOperationException(NoElementsMessage);
        }

        var ordinal = reader.FieldCount == 1 ? 0 : reader.GetOrdinal("data");

        var source = await npgsqlReader.GetStreamAsync(ordinal, token).ConfigureAwait(false);
        await source.CopyStreamSkippingSOHAsync(stream, token).ConfigureAwait(false);

        if (_canBeMultiples)
        {
            return 1;
        }

        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            throw new InvalidOperationException(MoreThanOneElementMessage);
        }

        return 1;
    }
}
