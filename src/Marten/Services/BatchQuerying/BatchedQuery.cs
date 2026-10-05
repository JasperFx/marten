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
using System.Diagnostics.CodeAnalysis;

namespace Marten.Services.BatchQuerying;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
internal partial class BatchedQuery: IBatchedQuery
{
    private readonly List<Type> _documentTypes = new();
    private readonly IList<IBatchQueryItem> _items = new List<IBatchQueryItem>();

    // The session transaction an exclusive fetch in this batch needs. Started once, however many
    // exclusive fetches the batch holds, and awaited by Execute() before the batch is sent.
    private Task? _transactionStart;

    // Set when a handler that brackets its own SQL in `begin transaction … end` is enlisted. Such a
    // handler COMMITS the session's transaction when its `end` runs, so it cannot share a batch with
    // an exclusive fetch, whose row lock has to survive until SaveChangesAsync. See assertNoTransactionConflict.
    private bool _holdsSelfTransactingItem;

    private const string MixedBatchMessage =
        "A batched FetchForExclusiveWriting cannot share a batch with a non-exclusive FetchForWriting for an "
        + "aggregate projected with ProjectionLifecycle.Async. The non-exclusive fetch wraps its reads in "
        + "'begin transaction isolation level repeatable read read only' … 'end' so they share one snapshot, and "
        + "that 'end' commits the session's transaction — releasing the exclusive fetch's row lock before "
        + "SaveChangesAsync can use it. Use separate batches, or fetch both exclusively.";

    /// <summary>
    ///     #5532 follow-up. Refuse the combination at the call that creates it, rather than letting it
    ///     surface later as <c>InvalidOperationException: This NpgsqlTransaction has completed</c> out of
    ///     <c>SaveChangesAsync</c> — an Npgsql message several layers from the mistake, naming nothing the
    ///     caller wrote.
    /// </summary>
    /// <remarks>
    ///     This combination has never worked. Before #5532 the warm path already failed this way and the
    ///     cold path silently ran the whole batch in autocommit, committing with no lock held at all; #5532
    ///     made the cold path fail like the warm one. So refusing it takes nothing away that functioned —
    ///     it only replaces the diagnosis.
    /// </remarks>
    /// <summary>
    ///     Names the offending call in terms the caller wrote, not the internal handler type.
    /// </summary>
    private static string describeSelfTransactingHandler(object handler)
    {
        // The implementors are FetchAsyncPlan's non-exclusive handlers, whose own names --
        // ForUpdateQueryHandler and ExpectedVersionQueryHandler -- are no use here, the first of them
        // actively misleading since this IS the not-for-update case.
        //
        // The closed type arguments are read off the nested handler type itself, NOT off its
        // DeclaringType. For a nested type inside a constructed generic, DeclaringType hands back the
        // generic type DEFINITION -- FetchAsyncPlan<TDoc, TId>, whose GenericTypeArguments is empty --
        // so this always fell through to the unnamed fallback below, which is not what #5535 claimed
        // to do. typeof(Outer<string, int>.Inner).GenericTypeArguments is [string, int]; its
        // DeclaringType.GenericTypeArguments is [].
        var aggregate = handler.GetType().GenericTypeArguments.FirstOrDefault();
        return aggregate == null
            ? "FetchForWriting for an aggregate projected with ProjectionLifecycle.Async"
            : $"FetchForWriting<{aggregate.Name}> (projected with ProjectionLifecycle.Async)";
    }

    private void assertNoTransactionConflict()
    {
        if (_holdsSelfTransactingItem && _transactionStart != null)
        {
            throw new InvalidOperationException(MixedBatchMessage);
        }
    }

    private void startTransaction()
    {
        if (_transactionStart == null)
        {
            _transactionStart = Parent.BeginTransactionAsync(CancellationToken.None).AsTask();

            // Only Execute() awaits this task, so a batch abandoned after enlisting an exclusive fetch --
            // a second fetch faulting out of FindFetchPlan and the caller returning without calling
            // Execute() -- would never observe it. An unobserved faulted Task escalates at finalization
            // through TaskScheduler.UnobservedTaskException, and fatally in a host that sets
            // ThrowUnobservedTaskExceptions. Observing the fault here is harmless for the normal path:
            // Execute()'s await still sees the same exception.
            _ = _transactionStart.ContinueWith(static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        assertNoTransactionConflict();
    }

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

        // Refuse the batch here as well as at enlist time. The enlist-time check throws SYNCHRONOUSLY only
        // when the non-exclusive fetch is added second, because FetchForExclusiveWriting is async and an
        // exception in an async method's body arrives as a faulted task instead. In that ordering the
        // caller following the documented shape -- enlist, Execute(), then await the items -- would reach
        // Execute() before observing anything, and without this the bad batch would be sent.
        assertNoTransactionConflict();

        // An exclusive fetch's row lock is only held if its `for update` runs inside the session's
        // transaction, and starting that transaction may still be in flight (see FetchForExclusiveWriting).
        if (_transactionStart != null)
        {
            await _transactionStart.ConfigureAwait(false);

            // #5532 follow-up, and a latency fix rather than a correctness one -- measured, not assumed.
            // The wait itself deliberately does NOT observe the token, because abandoning a transaction
            // start would let it reassign the session's connection behind us later. So a cancelled request
            // sat here for as long as opening a connection takes (the Npgsql Timeout, 15s by default) before
            // anything noticed. The SQL was never actually issued either way -- ExecuteReaderAsync below
            // observes the token -- so this only stops the pointless wait on the work after it.
            token.ThrowIfCancellationRequested();
        }

        foreach (var type in _documentTypes.Distinct())
            await Parent.Database.EnsureStorageExistsAsync(type, token).ConfigureAwait(false);

        var command = Parent.BuildCommand(_items.Select(x => x.Handler));

        await using var reader = await Parent.ExecuteReaderAsync(command, token).ConfigureAwait(false);
        await _items[0].ReadAsync(reader, Parent, token).ConfigureAwait(false);

        // 9.0 (#4375): indexed loop avoids the Skip+ToArray buffer allocation per batch query.
        for (var i = 1; i < _items.Count; i++)
        {
            var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

            if (!hasNext)
            {
                throw new InvalidOperationException("There is no next result to read over.");
            }

            await _items[i].ReadAsync(reader, Parent, token).ConfigureAwait(false);
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
        if (handler is IOpensItsOwnTransaction { OpensItsOwnTransaction: true })
        {
            // Set BEFORE either check can throw, so Execute()'s own guard stays armed even if a caller
            // swallows the exception raised here.
            _holdsSelfTransactingItem = true;

            // Order matters. Both conditions can hold at once -- an exclusive fetch enlisted first is also
            // "something already in the batch" -- and the mixed-batch one is the more specific and the more
            // dangerous of the two (a silently dropped row lock rather than a refused batch), so it wins.
            // Catches the ordering where the non-exclusive fetch is enlisted SECOND; the exclusive overloads
            // call startTransaction(), which checks the other direction.
            assertNoTransactionConflict();

            // #5535. `begin transaction isolation level repeatable read read only` is only legal as the
            // FIRST statement in a transaction, so this handler can only be the first item in its batch.
            // Enlisted after anything else it failed at Execute() with PostgreSQL's
            // `25001: SET TRANSACTION ISOLATION LEVEL must be called before any query`, which names an
            // isolation level the caller never mentioned and says nothing about ordering. Nothing about
            // CreateBatchQuery() suggests one of its members has to go first, so refuse it where the
            // ordering is actually decided.
            if (_items.Count > 0)
            {
                throw new InvalidOperationException(
                    $"A batched {describeSelfTransactingHandler(handler)} has to be the FIRST operation in its "
                    + $"batch, but {_items.Count} operation(s) are already enlisted. It wraps its reads in "
                    + "'begin transaction isolation level repeatable read read only' … 'end' so they share one "
                    + "snapshot, and PostgreSQL only accepts that as the first statement in a transaction. "
                    + "Enlist it first, or give it its own batch.");
            }
        }

        var item = new BatchQueryItem<T>(handler);
        _items.Add(item);

        return item.Result;
    }

    public Task<T> QueryByPlan<T>(IBatchQueryPlan<T> plan)
    {
        return plan.Fetch(this);
    }

    public Task<bool> CheckExists<T>(string id) where T : class
    {
        return checkExists<T, string>(id);
    }

    public Task<bool> CheckExists<T>(int id) where T : class
    {
        return checkExists<T, int>(id);
    }

    public Task<bool> CheckExists<T>(long id) where T : class
    {
        return checkExists<T, long>(id);
    }

    public Task<bool> CheckExists<T>(Guid id) where T : class
    {
        return checkExists<T, Guid>(id);
    }

    public Task<bool> CheckExists<T>(object id) where T : class
    {
        var checker = typeof(ExistsChecker<>).CloseAndBuildAs<IExistsChecker>(id.GetType());
        return checker.CheckExists<T>(id, this);
    }

    private Task<bool> checkExists<T, TId>(TId id) where T : class where TId : notnull
    {
        _documentTypes.Add(typeof(T));
        var storage = Parent.StorageFor<T>();
        if (storage is IDocumentStorage<T, TId> s)
        {
            var handler = new CheckExistsByIdHandler<T, TId>(s, id);
            return AddItem(handler);
        }

        throw new DocumentIdTypeMismatchException(storage, typeof(TId));
    }

    private interface IExistsChecker
    {
        Task<bool> CheckExists<T>(object id, BatchedQuery parent) where T : class;
    }

    private class ExistsChecker<TId>: IExistsChecker where TId : notnull
    {
        public Task<bool> CheckExists<T>(object id, BatchedQuery parent) where T : class
        {
            return parent.checkExists<T, TId>((TId)id);
        }
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
