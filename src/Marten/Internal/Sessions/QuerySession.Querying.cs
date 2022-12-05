#nullable enable
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

    public IMartenQueryable<T> Query<T>()
    {
        return new MartenLinqQueryable<T>(this);
    }

    public IReadOnlyList<T> Query<T>(string sql, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new UserSuppliedQueryHandler<T>(this, sql, parameters);

        if (!handler.SqlContainsCustomSelect)
        {
            Database.EnsureStorageExists(typeof(T));
        }

        var provider = new MartenLinqQueryProvider(this);
        return provider.ExecuteHandler(handler);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new UserSuppliedQueryHandler<T>(this, sql, parameters);

        if (!handler.SqlContainsCustomSelect)
        {
            await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        }

        var provider = new MartenLinqQueryProvider(this);
        return await provider.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, params object[] parameters)
    {
        return QueryAsync<T>(sql, CancellationToken.None, parameters);
    }

    public IBatchedQuery CreateBatchQuery()
    {
        return new BatchedQuery(this);
    }

    public TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
    {
        var source = _store.GetCompiledQuerySourceFor(query, this);
        Database.EnsureStorageExists(typeof(TDoc));
        var handler = (IQueryHandler<TOut>)source.Build(query, this);

        return ExecuteHandler(handler);
    }

    public async Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default)
    {
        var source = _store.GetCompiledQuerySourceFor(query, this);
        await Database.EnsureStorageExistsAsync(typeof(TDoc), token).ConfigureAwait(false);
        var handler = (IQueryHandler<TOut>)source.Build(query, this);

        return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }
}
