#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services.BatchQuerying;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    private int _tableNumber;

    public string NextTempTableName()
    {
        return LinqConstants.IdListTableName + ++_tableNumber;
    }

    public IMartenQueryable<T> Query<T>() where T : notnull
    {
        return new MartenLinqQueryable<T>(this);
    }

    public IMartenQueryable<T> QueryForNonStaleData<T>(TimeSpan timeout) where T : notnull
    {
        var queryable = new MartenLinqQueryable<T>(this);
        queryable.MartenProvider.Waiter = new WaitForAggregate(timeout);

        return queryable;
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
    {
        return QueryAsync<T>(DefaultParameterPlaceholder, sql, token, parameters);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(char placeholder, string sql, CancellationToken token, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new UserSuppliedQueryHandler<T>(this, placeholder, sql, parameters);

        if (!handler.SqlContainsCustomSelect)
        {
            await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        }

        var provider = new MartenLinqQueryProvider(this, typeof(T));
        return await provider.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, params object[] parameters)
    {
        return QueryAsync<T>(sql, CancellationToken.None, parameters);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(char placeholder, string sql, params object[] parameters)
    {
        return QueryAsync<T>(placeholder, sql, CancellationToken.None, parameters);
    }

    public IBatchedQuery CreateBatchQuery()
    {
        return new BatchedQuery(this);
    }

    public async Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default) where TDoc : notnull
    {
        var source = _store.GetCompiledQuerySourceFor(query, this);
        await Database.EnsureStorageExistsAsync(typeof(TDoc), token).ConfigureAwait(false);
        var handler = (IQueryHandler<TOut>)source.Build(query, this);

        return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }
}
