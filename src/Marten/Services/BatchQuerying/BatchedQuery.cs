#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Events.Querying;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Services.BatchQuerying;

internal class BatchedQuery: IBatchedQuery, IBatchEvents
{
    private readonly List<Type> _documentTypes = new();
    private readonly IList<IBatchQueryItem> _items = new List<IBatchQueryItem>();
    private readonly QuerySession _parent;

    public BatchedQuery(QuerySession parent)
    {
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
        _documentTypes.Add(typeof(TDoc));
        return new BatchLoadByKeys<TDoc>(this);
    }

    public Task<IReadOnlyList<T>> Query<T>(string sql, params object[] parameters) where T : class
    {
        var handler = new UserSuppliedQueryHandler<T>(_parent, sql, parameters);
        if (!handler.SqlContainsCustomSelect)
        {
            _documentTypes.Add(typeof(T));
        }

        return AddItem(handler);
    }

    public IBatchedQueryable<T> Query<T>() where T : class
    {
        _documentTypes.Add(typeof(T));
        return new BatchedQueryable<T>(this, _parent.Query<T>());
    }

    public async Task Execute(CancellationToken token = default)
    {
        if (!_items.Any())
        {
            return;
        }

        foreach (var type in _documentTypes.Distinct())
            await _parent.Database.EnsureStorageExistsAsync(type, token).ConfigureAwait(false);

        var command = _parent.BuildCommand(_items.Select(x => x.Handler));

        await using var reader = await _parent.ExecuteReaderAsync(command, token).ConfigureAwait(false);
        await _items[0].ReadAsync(reader, _parent, token).ConfigureAwait(false);

        var others = _items.Skip(1).ToArray();

        foreach (var item in others)
        {
            var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

            if (!hasNext)
            {
                throw new InvalidOperationException("There is no next result to read over.");
            }

            await item.ReadAsync(reader, _parent, token).ConfigureAwait(false);
        }
    }

    public void ExecuteSynchronously()
    {
        if (!_items.Any())
        {
            return;
        }

        foreach (var type in _documentTypes.Distinct()) _parent.Database.EnsureStorageExists(type);

        var command = _parent.BuildCommand(_items.Select(x => x.Handler));


        using var reader = _parent.ExecuteReader(command);
        _items[0].Read(reader, _parent);

        foreach (var item in _items.Skip(1))
        {
            var hasNext = reader.NextResult();

            if (!hasNext)
            {
                throw new InvalidOperationException("There is no next result to read over.");
            }

            item.Read(reader, _parent);
        }
    }

    public Task<TResult> Query<TDoc, TResult>(ICompiledQuery<TDoc, TResult> query)
    {
        _documentTypes.Add(typeof(TDoc));
        // Smelly downcast, but we'll allow it
        var source = _parent.DocumentStore.As<DocumentStore>().GetCompiledQuerySourceFor(query, _parent);
        var handler = (IQueryHandler<TResult>)source.Build(query, _parent);

        return AddItem(handler);
    }


    public Task<IEvent> Load(Guid id)
    {
        _documentTypes.Add(typeof(IEvent));
        var handler = new SingleEventQueryHandler(id, _parent.EventStorage());
        return AddItem(handler);
    }

    public Task<StreamState> FetchStreamState(Guid streamId)
    {
        _documentTypes.Add(typeof(IEvent));
        var handler = _parent.EventStorage()
            .QueryForStream(StreamAction.ForReference(streamId, _parent.TenantId));

        return AddItem(handler);
    }

    public Task<IReadOnlyList<IEvent>> FetchStream(Guid streamId, long version = 0, DateTime? timestamp = null,
        long fromVersion = 0)
    {
        _documentTypes.Add(typeof(IEvent));
        var selector = _parent.EventStorage();
        var statement = new EventStatement(selector)
        {
            StreamId = streamId,
            Version = version,
            Timestamp = timestamp,
            TenantId = _parent.TenantId,
            FromVersion = fromVersion
        };

        IQueryHandler<IReadOnlyList<IEvent>> handler = new ListQueryHandler<IEvent>(statement, selector);

        return AddItem(handler);
    }

    public Task<T> AddItem<T>(IQueryHandler<T> handler)
    {
        var item = new BatchQueryItem<T>(handler);
        _items.Add(item);

        return item.Result;
    }

    private Task<T> load<T, TId>(TId id) where T : class where TId : notnull
    {
        _documentTypes.Add(typeof(T));
        var storage = _parent.StorageFor<T>();
        if (storage is IDocumentStorage<T, TId> s)
        {
            var handler = new LoadByIdHandler<T, TId>(s, id);
            return AddItem(handler);
        }

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

    public Task<T?> FirstOrDefault<T>(IMartenQueryable<T> queryable)
    {
        return addItem<T, T?>(queryable, LinqConstants.FirstOrDefaultOperator);
    }

    public Task<T> Single<T>(IMartenQueryable<T> queryable)
    {
        return addItem<T, T>(queryable, LinqConstants.SingleOperator);
    }

    public Task<T?> SingleOrDefault<T>(IMartenQueryable<T> queryable)
    {
        return addItem<T, T?>(queryable, LinqConstants.SingleOrDefaultOperator);
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

    internal class BatchLoadByKeys<TDoc>: IBatchLoadByKeys<TDoc> where TDoc : class
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
