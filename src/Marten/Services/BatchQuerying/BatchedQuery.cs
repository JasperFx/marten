using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Services.BatchQuerying;

internal partial class BatchedQuery: IBatchedQuery
{
    private readonly List<Type> _documentTypes = new();
    private readonly IList<IBatchQueryItem> _items = new List<IBatchQueryItem>();

    public BatchedQuery(QuerySession parent)
    {
        Parent = parent;
    }

    public QuerySession Parent { get; }

    public IBatchEvents Events => this;

    public Task<T?> Load<T>(object id) where T : class
    {
        var loader = typeof(Loader<>).CloseAndBuildAs<ILoader>(id.GetType());
        return loader.Load<T>(id, this);
    }

    public Task<T?> Load<T>(string id) where T : class
    {
        return load<T, string>(id);
    }

    public Task<T?> Load<T>(int id) where T : class
    {
        return load<T, int>(id);
    }

    public Task<T?> Load<T>(long id) where T : class
    {
        return load<T, long>(id);
    }

    public Task<T?> Load<T>(Guid id) where T : class
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
        return Query<T>(QuerySession.DefaultParameterPlaceholder, sql, parameters);
    }

    public Task<IReadOnlyList<T>> Query<T>(char placeholder, string sql, params object[] parameters) where T : class
    {
        var handler = new UserSuppliedQueryHandler<T>(Parent, placeholder, sql, parameters);
        if (!handler.SqlContainsCustomSelect)
        {
            _documentTypes.Add(typeof(T));
        }

        return AddItem(handler);
    }

    public IBatchedQueryable<T> Query<T>() where T : class
    {
        _documentTypes.Add(typeof(T));
        return new BatchedQueryable<T>(this, Parent.Query<T>());
    }

    public async Task Execute(CancellationToken token = default)
    {
        if (!_items.Any())
        {
            return;
        }

        foreach (var type in _documentTypes.Distinct())
            await Parent.Database.EnsureStorageExistsAsync(type, token).ConfigureAwait(false);

        var command = Parent.BuildCommand(_items.Select(x => x.Handler));

        await using var reader = await Parent.ExecuteReaderAsync(command, token).ConfigureAwait(false);
        await _items[0].ReadAsync(reader, Parent, token).ConfigureAwait(false);

        var others = _items.Skip(1).ToArray();

        foreach (var item in others)
        {
            var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

            if (!hasNext)
            {
                throw new InvalidOperationException("There is no next result to read over.");
            }

            await item.ReadAsync(reader, Parent, token).ConfigureAwait(false);
        }
    }

    public Task<TResult> Query<TDoc, TResult>(ICompiledQuery<TDoc, TResult> query) where TDoc : class
    {
        _documentTypes.Add(typeof(TDoc));
        // Smelly downcast, but we'll allow it
        var source = Parent.DocumentStore.As<DocumentStore>().GetCompiledQuerySourceFor(query, Parent);
        var handler = (IQueryHandler<TResult>)source.Build(query, Parent);

        return AddItem(handler);
    }

    public Task<T> AddItem<T>(IQueryHandler<T> handler)
    {
        var item = new BatchQueryItem<T>(handler);
        _items.Add(item);

        return item.Result;
    }

    public Task<T> QueryByPlan<T>(IBatchQueryPlan<T> plan)
    {
        return plan.Fetch(this);
    }

    private Task<T?> load<T, TId>(TId id) where T : class where TId : notnull
    {
        _documentTypes.Add(typeof(T));
        var storage = Parent.StorageFor<T>();
        if (storage is IDocumentStorage<T, TId> s)
        {
            var handler = new LoadByIdHandler<T, TId>(s, id);
            return AddItem(handler)!;
        }

        throw new DocumentIdTypeMismatchException(storage, typeof(TId));
    }

    private Task<TResult> addItem<TDoc, TResult>(IQueryable<TDoc> queryable, SingleValueMode? op) where TDoc : notnull
    {
        var handler = queryable.As<MartenLinqQueryable<TDoc>>().BuildHandler<TResult>(op);
        return AddItem(handler);
    }

    public Task<bool> Any<TDoc>(IMartenQueryable<TDoc> queryable) where TDoc : notnull
    {
        return addItem<TDoc, bool>(queryable, SingleValueMode.Any);
    }

    public Task<long> Count<TDoc>(IMartenQueryable<TDoc> queryable) where TDoc : notnull
    {
        return addItem<TDoc, long>(queryable, SingleValueMode.LongCount);
    }

    internal Task<IReadOnlyList<T>> Query<T>(IMartenQueryable<T> queryable) where T : notnull
    {
        var handler = queryable.As<MartenLinqQueryable<T>>().BuildHandler<IReadOnlyList<T>>();
        return AddItem(handler);
    }

    public Task<T> First<T>(IMartenQueryable<T> queryable) where T : notnull
    {
        return addItem<T, T>(queryable, SingleValueMode.First);
    }

    public Task<T?> FirstOrDefault<T>(IMartenQueryable<T> queryable) where T : notnull
    {
        return addItem<T, T?>(queryable, SingleValueMode.FirstOrDefault);
    }

    public Task<T> Single<T>(IMartenQueryable<T> queryable) where T : notnull
    {
        return addItem<T, T>(queryable, SingleValueMode.Single);
    }

    public Task<T?> SingleOrDefault<T>(IMartenQueryable<T> queryable) where T : notnull
    {
        return addItem<T, T?>(queryable, SingleValueMode.SingleOrDefault);
    }

    public Task<TResult> Min<TResult>(IQueryable<TResult> queryable) where TResult : notnull
    {
        return addItem<TResult, TResult>(queryable, SingleValueMode.Min);
    }

    public Task<TResult> Max<TResult>(IQueryable<TResult> queryable) where TResult : notnull
    {
        return addItem<TResult, TResult>(queryable, SingleValueMode.Max);
    }

    public Task<TResult> Sum<TResult>(IQueryable<TResult> queryable) where TResult : notnull
    {
        return addItem<TResult, TResult>(queryable, SingleValueMode.Sum);
    }

    public Task<double> Average<T>(IQueryable<T> queryable) where T : notnull
    {
        return addItem<T, double>(queryable, SingleValueMode.Average);
    }

    private interface ILoader
    {
        Task<T?> Load<T>(object id, BatchedQuery parent) where T : class;
    }

    private class Loader<TId>: ILoader
    {
        public Task<T?> Load<T>(object id, BatchedQuery parent) where T : class
        {
            return parent.load<T, TId>((TId)id);
        }
    }

    internal class BatchLoadByKeys<TDoc>: IBatchLoadByKeys<TDoc> where TDoc : class
    {
        private static readonly Type[] _identityTypes = [typeof(int), typeof(long), typeof(Guid), typeof(string)];
        private readonly BatchedQuery _parent;

        public BatchLoadByKeys(BatchedQuery parent)
        {
            _parent = parent;
        }

        public Task<IReadOnlyList<TDoc>> ById<TKey>(params TKey[] keys)
        {
            if (typeof(TKey).IsNullable())
                throw new ArgumentOutOfRangeException(nameof(TKey),
                    "Cannot use nullable types as the TKey, you may need to explicitly define the generic argument");

            return load(keys);
        }

        public Task<IReadOnlyList<TDoc>> ByIdList<TKey>(IEnumerable<TKey> keys)
        {
            return load(keys.ToArray());
        }

        private Task<IReadOnlyList<TDoc>> load<TKey>(TKey[] keys)
        {
            var storage = _parent.Parent.StorageFor<TDoc, TKey>();
            if (_identityTypes.Contains(typeof(TKey)))
            {
                return _parent.AddItem(new LoadByIdArrayHandler<TDoc, TKey>(storage, keys));
            }

            throw new ArgumentOutOfRangeException(nameof(keys),
                "Marten cannot (yet) handle this identity type for this operation");
        }
    }
}
