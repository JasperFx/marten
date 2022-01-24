using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services.BatchQuerying;

#nullable enable
namespace Marten.Internal.Sessions
{
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
            var provider = new MartenLinqQueryProvider(this);
            return provider.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
        {
            assertNotDisposed();
            var handler = new UserSuppliedQueryHandler<T>(this, sql, parameters);
            var provider = new MartenLinqQueryProvider(this);
            return provider.ExecuteHandlerAsync(handler, token);
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
            var source = Options.GetCompiledQuerySourceFor(query, this);
            Database.EnsureStorageExists(typeof(TDoc));
            var handler = (IQueryHandler<TOut>)source.Build(query, this);

            return ExecuteHandler(handler);
        }

        public async Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default)
        {
            var source = Options.GetCompiledQuerySourceFor(query, this);
            await Database.EnsureStorageExistsAsync(typeof(TDoc), token).ConfigureAwait(false);
            var handler = (IQueryHandler<TOut>)source.Build(query, this);

            return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
        }
    }
}
