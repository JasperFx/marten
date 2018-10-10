using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Linq;
using Marten.Linq.Model;
using Marten.Linq.QueryHandlers;
using Marten.Util;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class BatchedQuery : IBatchedQuery, IBatchEvents
    {
        private static readonly MartenQueryParser QueryParser = new MartenQueryParser();
        private readonly IIdentityMap _identityMap;
        private readonly IList<IBatchQueryItem> _items = new List<IBatchQueryItem>();
        private readonly QuerySession _parent;
        private readonly DocumentStore _store;
        private readonly IManagedConnection _runner;

        public BatchedQuery(DocumentStore store, IManagedConnection runner, IIdentityMap identityMap, QuerySession parent)
        {
            _store = store;
            _runner = runner;
            _identityMap = identityMap;
            _parent = parent;
        }

        public IBatchEvents Events => this;

        public Task<T> Load<T>(string id) where T : class
        {
            return load<T>(id);
        }

        public Task<T> Load<T>(ValueType id) where T : class
        {
            return load<T>(id);
        }

        public IBatchLoadByKeys<TDoc> LoadMany<TDoc>() where TDoc : class
        {
            return new BatchLoadByKeys<TDoc>(this);
        }

        public Task<IReadOnlyList<T>> Query<T>(string sql, params object[] parameters) where T : class
        {
            return AddItem(new UserSuppliedQueryHandler<T>(_store, sql, parameters), null);
        }

        public IBatchedQueryable<T> Query<T>() where T : class
        {
            return new BatchedQueryable<T>(this, _parent.Query<T>());
        }

        private NpgsqlCommand buildCommand()
        {
            return CommandBuilder.ToBatchCommand(_parent.Tenant, _items.Select(x => x.Handler));
        }

        public async Task Execute(CancellationToken token = default(CancellationToken))
        {
            var map = _identityMap.ForQuery();

            if (!_items.Any()) return;

            var command = buildCommand();
            await _runner.ExecuteAsync(command, async (cmd, tk) =>
            {
                using (var reader = await command.ExecuteReaderAsync(tk).ConfigureAwait(false))
                {
                    await _items[0].Read(reader, map, token).ConfigureAwait(false);

                    var others = _items.Skip(1).ToArray();

                    foreach (var item in others)
                    {
                        var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

                        if (!hasNext)
                            throw new InvalidOperationException("There is no next result to read over.");

                        await item.Read(reader, map, token).ConfigureAwait(false);
                    }
                }

                return 0;
            }, token).ConfigureAwait(false);
        }

        public void ExecuteSynchronously()
        {
            var map = _identityMap.ForQuery();

            if (!_items.Any()) return;

            var command = buildCommand();
            _runner.Execute(command, cmd =>
            {
                using (var reader = command.ExecuteReader())
                {
                    _items[0].Read(reader, map);

                    _items.Skip(1).Each(item =>
                    {
                        var hasNext = reader.NextResult();

                        if (!hasNext)
                            throw new InvalidOperationException("There is no next result to read over.");

                        item.Read(reader, map);
                    });
                }
            });
        }

        public Task<TResult> Query<TDoc, TResult>(ICompiledQuery<TDoc, TResult> query)
        {
            QueryStatistics stats;
            var handler = _store.HandlerFactory.HandlerFor(query, out stats);
            return AddItem(handler, stats);
        }

        public Task<T> AggregateStream<T>(Guid streamId, int version = 0, DateTime? timestamp = null)
            where T : class, new()
        {
            var inner = new EventQueryHandler<Guid>(new EventSelector(_store.Events, _store.Serializer), streamId, version,
                timestamp, _store.Events.TenancyStyle, _parent.Tenant.TenantId);
            var aggregator = _store.Events.AggregateFor<T>();
            var handler = new AggregationQueryHandler<T>(aggregator, inner);

            return AddItem(handler, null);
        }

        public Task<IEvent> Load(Guid id)
        {
            var handler = new SingleEventQueryHandler(id, _store.Events, _store.Serializer);
            return AddItem(handler, null);
        }

        public Task<StreamState> FetchStreamState(Guid streamId)
        {
            var handler = new StreamStateByGuidHandler(_store.Events, streamId, _parent.Tenant.TenantId);
            return AddItem(handler, null);
        }

        public Task<IReadOnlyList<IEvent>> FetchStream(Guid streamId, int version = 0, DateTime? timestamp = null)
        {
            var selector = new EventSelector(_store.Events, _store.Serializer);
            var handler = new EventQueryHandler<Guid>(selector, streamId, version, timestamp, _store.Events.TenancyStyle, _parent.Tenant.TenantId);

            return AddItem(handler, null);
        }

        public Task<T> AddItem<T>(IQueryHandler<T> handler, QueryStatistics stats)
        {
            _parent.Tenant.EnsureStorageExists(handler.SourceType);

            var item = new BatchQueryItem<T>(handler, stats);
            _items.Add(item);

            return item.Result;
        }

        private Task<T> load<T>(object id) where T : class
        {
            if (_identityMap.Has<T>(id))
                return Task.FromResult(_identityMap.Retrieve<T>(id));

            var mapping = _parent.Tenant.MappingFor(typeof(T)).ToQueryableDocument();

            return AddItem(new LoadByIdHandler<T>(_parent.Tenant.StorageFor<T>(), mapping, id), null);
        }

        public Task<bool> Any<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return AddItem(queryable.ToLinqQuery().ToAny(), null);
        }

        public Task<long> Count<TDoc>(IMartenQueryable<TDoc> queryable)
        {
            return AddItem(queryable.ToLinqQuery().ToCount<long>(), null);
        }

        internal Task<IReadOnlyList<T>> Query<T>(IMartenQueryable<T> queryable)
        {
            var expression = queryable.Expression;

            var query = QueryParser.GetParsedQuery(expression);

            return AddItem(new LinqQuery<T>(_store, query, queryable.Includes.ToArray(), queryable.Statistics).ToList(), queryable.Statistics);
        }

        public Task<T> First<T>(IMartenQueryable<T> queryable)
        {
            var query = queryable.ToLinqQuery();

            return AddItem(OneResultHandler<T>.First(query), queryable.Statistics);
        }

        public Task<T> FirstOrDefault<T>(IMartenQueryable<T> queryable)
        {
            var query = queryable.ToLinqQuery();

            return AddItem(OneResultHandler<T>.FirstOrDefault(query), queryable.Statistics);
        }

        public Task<T> Single<T>(IMartenQueryable<T> queryable)
        {
            var query = queryable.ToLinqQuery();

            return AddItem(OneResultHandler<T>.Single(query), queryable.Statistics);
        }

        public Task<T> SingleOrDefault<T>(IMartenQueryable<T> queryable)
        {
            var query = queryable.ToLinqQuery();

            return AddItem(OneResultHandler<T>.SingleOrDefault(query), queryable.Statistics);
        }

        public Task<TResult> Min<TResult>(IQueryable<TResult> queryable)
        {
            var linqQuery = queryable.As<IMartenQueryable<TResult>>().ToLinqQuery();
            return AddItem(AggregateQueryHandler<TResult>.Min(linqQuery), null);
        }

        public Task<TResult> Max<TResult>(IQueryable<TResult> queryable)
        {
            var linqQuery = queryable.As<IMartenQueryable<TResult>>().ToLinqQuery();

            return AddItem(AggregateQueryHandler<TResult>.Max(linqQuery), null);
        }

        public Task<TResult> Sum<TResult>(IQueryable<TResult> queryable)
        {
            var linqQuery = queryable.As<IMartenQueryable<TResult>>().ToLinqQuery();

            return AddItem(AggregateQueryHandler<TResult>.Sum(linqQuery), null);
        }

        public Task<double> Average<T>(IQueryable<T> queryable)
        {
            var linqQuery = queryable.As<IMartenQueryable<T>>().ToLinqQuery();

            return AddItem(AggregateQueryHandler<double>.Average(linqQuery), null);
        }

        public class BatchLoadByKeys<TDoc> : IBatchLoadByKeys<TDoc> where TDoc : class
        {
            private readonly BatchedQuery _parent;

            public BatchLoadByKeys(BatchedQuery parent)
            {
                _parent = parent;
            }

            public Task<IList<TDoc>> ById<TKey>(params TKey[] keys)
            {
                return load(keys);
            }

            public Task<IList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys)
            {
                return load(keys.ToArray());
            }

            private Task<IList<TDoc>> load<TKey>(TKey[] keys)
            {
                var tenant = _parent._parent.Tenant;
                var resolver = tenant.StorageFor<TDoc>();
                var mapping = tenant.MappingFor(typeof(TDoc)).ToQueryableDocument();

                return _parent.AddItem(new LoadByIdArrayHandler<TDoc, TKey>(resolver, mapping, keys), null);
            }
        }
    }
}