using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Npgsql;
using Remotion.Linq.Clauses;

namespace Marten.Services.BatchQuerying
{
    public class BatchedQuery: IBatchedQuery, IBatchEvents
    {
        private readonly IList<IBatchQueryItem> _items = new List<IBatchQueryItem>();
        private readonly QuerySession _parent;
        private readonly IManagedConnection _runner;

        public BatchedQuery(IManagedConnection runner, QuerySession parent)
        {
            _runner = runner;
            _parent = parent;
        }

        public IBatchEvents Events => this;

        public Task<T> Load<T>(string id) where T : class
        {
            return load<T, string>(id);
        }

        public Task<T> Load<T>(int id) where T : class
        {
            return load<T, int>(id);
        }

        public Task<T> Load<T>(long id) where T : class
        {
            return load<T, long>(id);
        }

        public Task<T> Load<T>(Guid id) where T : class
        {
            return load<T, Guid>(id);
        }

        public IBatchLoadByKeys<TDoc> LoadMany<TDoc>() where TDoc : class
        {
            return new BatchLoadByKeys<TDoc>(this);
        }

        public Task<IReadOnlyList<T>> Query<T>(string sql, params object[] parameters) where T : class
        {
            return AddItem(new UserSuppliedQueryHandler<T>(_parent, sql, parameters));
        }

        public IBatchedQueryable<T> Query<T>() where T : class
        {
            return new BatchedQueryable<T>(this, _parent.Query<T>());
        }

        public async Task Execute(CancellationToken token = default(CancellationToken))
        {
            if (!_items.Any())
                return;

            var command = _parent.BuildCommand(_items.Select(x => x.Handler));

            using (var reader = await _runner.ExecuteReaderAsync(command, token))
            {
                await _items[0].ReadAsync(reader, _parent, token).ConfigureAwait(false);

                var others = _items.Skip(1).ToArray();

                foreach (var item in others)
                {
                    var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

                    if (!hasNext)
                        throw new InvalidOperationException("There is no next result to read over.");

                    await item.ReadAsync(reader, _parent, token).ConfigureAwait(false);
                }
            }
        }

        public void ExecuteSynchronously()
        {
            if (!_items.Any())
                return;

            var command = _parent.BuildCommand(_items.Select(x => x.Handler));


            using (var reader = _runner.ExecuteReader(command))
            {
                _items[0].Read(reader, _parent);

                foreach (var item in _items.Skip(1))
                {
                    var hasNext = reader.NextResult();

                    if (!hasNext)
                        throw new InvalidOperationException("There is no next result to read over.");

                    item.Read(reader, _parent);
                }
            }
        }

        public Task<TResult> Query<TDoc, TResult>(ICompiledQuery<TDoc, TResult> query)
        {
            var source = _parent.Options.GetCompiledQuerySourceFor(query, _parent);
            var handler = (IQueryHandler<TResult>)source.Build(query, _parent);

            return AddItem(handler);
        }

        public Task<T> AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null)
            where T : class
        {
            var events = _parent.DocumentStore.Events;
            var inner = new EventQueryHandler<Guid>(_parent.Tenant.EventStorage(), streamId, version,
                timestamp, events.TenancyStyle, _parent.Tenant.TenantId);
            var aggregator = events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner);

            return AddItem(handler);
        }

        public Task<IEvent> Load(Guid id)
        {
            var handler = new SingleEventQueryHandler(id, _parent.Tenant.EventStorage());
            return AddItem(handler);
        }

        public Task<StreamState> FetchStreamState(Guid streamId)
        {
            var handler = new StreamStateByGuidHandler(_parent.Options.Events, streamId, _parent.Tenant.TenantId);
            return AddItem(handler);
        }

        public Task<IReadOnlyList<IEvent>> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            var handler = new EventQueryHandler<Guid>(_parent.Tenant.EventStorage(), streamId, version, timestamp, _parent.Options.Events.TenancyStyle, _parent.Tenant.TenantId);

            return AddItem(handler);
        }

        public Task<T> AddItem<T>(IQueryHandler<T> handler)
        {
            var item = new BatchQueryItem<T>(handler);
            _items.Add(item);

            return item.Result;
        }

        private Task<T> load<T, TId>(TId id) where T : class
        {
            var storage = _parent.StorageFor<T>();
            if (storage is IDocumentStorage<T, TId> s)
            {
                var handler = new LoadByIdHandler<T, TId>(s, id);
                return AddItem(handler);
            }

            var idType = storage.IdType;

            throw new DocumentIdTypeMismatchException(storage, typeof(TId));
        }

        private Task<TResult> addItem<TDoc, TResult>(IQueryable<TDoc> queryable, ResultOperatorBase op)
        {
            var handler = queryable.As<MartenLinqQueryable<TDoc>>().BuildHandler<TResult>(op);
            return AddItem(handler);
        }

        public Task<bool> Any<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return addItem<TDoc, bool>(queryable, LinqConstants.AnyOperator);
        }

        public Task<long> Count<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return addItem<TDoc, long>(queryable, LinqConstants.LongCountOperator);
        }

        internal Task<IReadOnlyList<T>> Query<T>(IMartenQueryable<T> queryable)
        {
            var handler = queryable.As<MartenLinqQueryable<T>>().BuildHandler<IReadOnlyList<T>>();
            return AddItem(handler);
        }

        public Task<T> First<T>(IMartenQueryable<T> queryable)
        {
            return addItem<T, T>(queryable, LinqConstants.FirstOperator);
        }

        public Task<T> FirstOrDefault<T>(IMartenQueryable<T> queryable)
        {
            return addItem<T, T>(queryable, LinqConstants.FirstOrDefaultOperator);
        }

        public Task<T> Single<T>(IMartenQueryable<T> queryable)
        {
            return addItem<T, T>(queryable, LinqConstants.SingleOperator);
        }

        public Task<T> SingleOrDefault<T>(IMartenQueryable<T> queryable)
        {
            return addItem<T, T>(queryable, LinqConstants.SingleOrDefaultOperator);
        }

        public Task<TResult> Min<TResult>(IQueryable<TResult> queryable)
        {
            return addItem<TResult, TResult>(queryable, LinqConstants.MinOperator);
        }

        public Task<TResult> Max<TResult>(IQueryable<TResult> queryable)
        {
            return addItem<TResult, TResult>(queryable, LinqConstants.MaxOperator);
        }

        public Task<TResult> Sum<TResult>(IQueryable<TResult> queryable)
        {
            return addItem<TResult, TResult>(queryable, LinqConstants.SumOperator);
        }

        public Task<double> Average<T>(IQueryable<T> queryable)
        {
            return addItem<T, double>(queryable, LinqConstants.AverageOperator);
        }

        public class BatchLoadByKeys<TDoc>: IBatchLoadByKeys<TDoc> where TDoc : class
        {
            private readonly BatchedQuery _parent;

            public BatchLoadByKeys(BatchedQuery parent)
            {
                _parent = parent;
            }

            public Task<IReadOnlyList<TDoc>> ById<TKey>(params TKey[] keys)
            {
                return load(keys);
            }

            public Task<IReadOnlyList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys)
            {
                return load(keys.ToArray());
            }

            private Task<IReadOnlyList<TDoc>> load<TKey>(TKey[] keys)
            {
                var storage = _parent._parent.StorageFor<TDoc>();
                return _parent.AddItem(new LoadByIdArrayHandler<TDoc, TKey>(storage, keys));
            }
        }
    }
}
