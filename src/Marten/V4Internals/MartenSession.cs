using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Linq;
using Marten.Patching;
using Marten.Schema;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Marten.Storage;
using Npgsql;

namespace Marten.V4Internals
{
    public class MartenSession : IMartenSession, IDocumentSession
    {
        private readonly IDatabase _database;
        private readonly StorageStyle _storageStyle;
        private readonly IDocumentStorageGraph _storage;
        private readonly IList<IStorageOperation> _pendingOperations = new List<IStorageOperation>();

        public MartenSession(IDatabase database, StorageStyle storageStyle, ISerializer serializer, ITenant tenant, IDocumentStorageGraph storage)
        {
            _database = database;
            _storageStyle = storageStyle;
            _storage = storage;
            Serializer = serializer;
            Tenant = tenant;
        }

        public ISerializer Serializer { get; }

        public VersionTracker Versions { get; } = new VersionTracker();
        public Task<T> ExecuteQuery<T>(IQueryHandler<T> handler, CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public T ExecuteQuery<T>(IQueryHandler<T> handler)
        {
            throw new NotImplementedException();
        }

        private IDocumentStorage<T, TId> storageFor<T, TId>()
        {
            var storage = storageFor<T>();
            if (storage is IDocumentStorage<T, TId> s) return s;

            throw new InvalidOperationException($"The identity type for {typeof(T).FullName} is {storage.IdType.FullName}, but {typeof(TId).FullName} was used as the Id type");
        }

        private IDocumentStorage<T> storageFor<T>()
        {
            return _storage.StorageFor<T>(_storageStyle);
        }


        public Guid? VersionFor<TDoc>(TDoc entity)
        {
            return storageFor<TDoc>().VersionFor(entity, this);
        }

        #region FullTextSearch

        public IReadOnlyList<TDoc> Search<TDoc>(string queryText, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<TDoc>> SearchAsync<TDoc>(string queryText, string regConfig = FullTextIndex.DefaultRegConfig,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<TDoc> PlainTextSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<TDoc>> PlainTextSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<TDoc> PhraseSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<TDoc>> PhraseSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<TDoc> WebStyleSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<TDoc>> WebStyleSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig,
            CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        #endregion

        public Dictionary<Type, object> ItemMap { get; } = new Dictionary<Type, object>();
        public T Load<T>(string id)
        {
            var handler = storageFor<T, string>().Load(id);
            return _database.Execute(handler, this);
        }

        public Task<T> LoadAsync<T>(string id, CancellationToken token = default(CancellationToken))
        {
            var handler = storageFor<T, string>().Load(id);
            return _database.ExecuteAsync(handler, this, token);
        }

        public T Load<T>(int id)
        {
            var handler = storageFor<T, int>().Load(id);
            return _database.Execute(handler, this);
        }

        public T Load<T>(long id)
        {
            var handler = storageFor<T, long>().Load(id);
            return _database.Execute(handler, this);
        }

        public T Load<T>(Guid id)
        {
            var handler = storageFor<T, Guid>().Load(id);
            return _database.Execute(handler, this);
        }

        public Task<T> LoadAsync<T>(int id, CancellationToken token = default(CancellationToken))
        {
            var handler = storageFor<T, int>().Load(id);
            return _database.ExecuteAsync(handler, this, token);
        }

        public Task<T> LoadAsync<T>(long id, CancellationToken token = default(CancellationToken))
        {
            var handler = storageFor<T, long>().Load(id);
            return _database.ExecuteAsync(handler, this, token);
        }

        public Task<T> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken))
        {
            var handler = storageFor<T, Guid>().Load(id);
            return _database.ExecuteAsync(handler, this, token);
        }

        public IMartenQueryable<T> Query<T>()
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<T> Query<T>(string sql, params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token = default(CancellationToken), params object[] parameters)
        {
            throw new NotImplementedException();
        }

        public IBatchedQuery CreateBatchQuery()
        {
            throw new NotImplementedException();
        }

        public NpgsqlConnection Connection { get; }
        public IMartenSessionLogger Logger { get; set; }
        public int RequestCount => _database.RequestCount;
        public IDocumentStore DocumentStore { get; }
        public TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            throw new NotImplementedException();
        }

        public Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<T> LoadMany<T>(params string[] ids)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<T> LoadMany<T>(params Guid[] ids)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<T> LoadMany<T>(params int[] ids)
        {
            throw new NotImplementedException();
        }

        public IReadOnlyList<T> LoadMany<T>(params long[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params string[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params Guid[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params int[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params long[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params int[] ids)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids)
        {
            throw new NotImplementedException();
        }

        public IJsonLoader Json { get; }
        public ITenant Tenant { get; }
        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(T entity)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(int id)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(long id)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(Guid id)
        {
            throw new NotImplementedException();
        }

        public void Delete<T>(string id)
        {
            throw new NotImplementedException();
        }

        public void DeleteWhere<T>(Expression<Func<T, bool>> expression)
        {
            throw new NotImplementedException();
        }

        public void SaveChanges()
        {
            throw new NotImplementedException();
        }

        public Task SaveChangesAsync(CancellationToken token = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public void Store<T>(params T[] entities)
        {
            // TODO -- there's more to do here. See old session code

            var storage = storageFor<T>();
            foreach (var entity in entities)
            {
                storage.Store(this, entity);
                var operation = storage.Upsert(entity, this);
                _pendingOperations.Add(operation);
            }
        }

        public void Store<T>(string tenantId, params T[] entities)
        {
            throw new NotImplementedException();
        }

        public void Store<T>(T entity, Guid version)
        {
            throw new NotImplementedException();
        }

        public void Insert<T>(params T[] entities)
        {
            throw new NotImplementedException();
        }

        public void Update<T>(params T[] entities)
        {
            throw new NotImplementedException();
        }

        public void InsertObjects(IEnumerable<object> documents)
        {
            throw new NotImplementedException();
        }

        public IUnitOfWork PendingChanges { get; }
        public void StoreObjects(IEnumerable<object> documents)
        {
            throw new NotImplementedException();
        }

        public IEventStore Events { get; }
        public ConcurrencyChecks Concurrency { get; }
        public IList<IDocumentSessionListener> Listeners { get; }
        public IPatchExpression<T> Patch<T>(int id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(long id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(string id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(Guid id)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(Expression<Func<T, bool>> @where)
        {
            throw new NotImplementedException();
        }

        public IPatchExpression<T> Patch<T>(IWhereFragment fragment)
        {
            throw new NotImplementedException();
        }

        public void QueueOperation(Services.IStorageOperation storageOperation)
        {
            throw new NotImplementedException();
        }

        public void Eject<T>(T document)
        {
            throw new NotImplementedException();
        }

        public void EjectAllOfType(Type type)
        {
            throw new NotImplementedException();
        }

        public IQueryable CreateQuery(Expression expression)
        {
            throw new NotImplementedException();
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            throw new NotImplementedException();
        }

        public object Execute(Expression expression)
        {
            throw new NotImplementedException();
        }

        public TResult Execute<TResult>(Expression expression)
        {
            throw new NotImplementedException();
        }
    }
}
