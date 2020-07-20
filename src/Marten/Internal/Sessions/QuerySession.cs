using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Internal.CodeGeneration;
using Marten.Internal.DirtyTracking;
using Marten.Internal.Linq;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Services;
using Marten.Services.BatchQuerying;
using Marten.Storage;
using Marten.Util;
using Npgsql;

namespace Marten.Internal.Sessions
{
    public class QuerySession : IMartenSession, IQuerySession
    {
        private readonly IProviderGraph _providers;
        private bool _disposed;
        public VersionTracker Versions { get; } = new VersionTracker();
        public IManagedConnection Database { get; }
        public ISerializer Serializer { get; }
        public Dictionary<Type, object> ItemMap { get; } = new Dictionary<Type, object>();
        public ITenant Tenant { get; }
        public StoreOptions Options { get; }

        public void MarkAsAddedForStorage(object id, object document)
        {
            foreach (var listener in Listeners)
            {
                listener.DocumentAddedForStorage(id, document);
            }
        }

        public void MarkAsDocumentLoaded(object id, object document)
        {
            if (document == null) return;

            foreach (var listener in Listeners)
            {
                listener.DocumentLoaded(id, document);
            }
        }

        public IList<IChangeTracker> ChangeTrackers { get; } = new List<IChangeTracker>();

        public IList<IDocumentSessionListener> Listeners { get; } = new List<IDocumentSessionListener>();

        public QuerySession(DocumentStore store, SessionOptions sessionOptions, IManagedConnection database,
            ITenant tenant)
        {
            DocumentStore = store;

            Listeners.AddRange(store.Options.Listeners);
            if (sessionOptions != null)
            {
                if (sessionOptions.Timeout.HasValue && sessionOptions.Timeout.Value < 0)
                {
                    throw new ArgumentOutOfRangeException("CommandTimeout can't be less than zero");
                }

                Listeners.AddRange(sessionOptions.Listeners);
            }

            _providers = tenant.Providers ?? throw new ArgumentNullException(nameof(ITenant.Providers));

            Database = database;
            Serializer = store.Serializer;
            Tenant = tenant;
            Options = store.Options;
        }

        protected virtual IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
        {
            return provider.QueryOnly;
        }

        public IDocumentStorage StorageFor(Type documentType)
        {
            // TODO -- possible optimization opportunity
            return typeof(StorageFinder<>).CloseAndBuildAs<IStorageFinder>(documentType).Find(this);
        }

        private interface IStorageFinder
        {
            IDocumentStorage Find(QuerySession session);
        }

        private class StorageFinder<T>: IStorageFinder
        {
            public IDocumentStorage Find(QuerySession session)
            {
                return session.StorageFor<T>();
            }
        }

        protected IDocumentStorage<T, TId> storageFor<T, TId>()
        {
            var storage = StorageFor<T>();
            if (storage is IDocumentStorage<T, TId> s) return s;

            throw new InvalidOperationException($"The identity type for {typeof(T).FullName} is {storage.IdType.FullName}, but {typeof(TId).FullName} was used as the Id type");
        }

        public IDocumentStorage<T> StorageFor<T>()
        {
            return selectStorage(_providers.StorageFor<T>());
        }

        public ConcurrencyChecks Concurrency { get; protected set; } = ConcurrencyChecks.Enabled;
        private int _tableNumber;
        public string NextTempTableName()
        {
            return LinqConstants.IdListTableName + ++_tableNumber;
        }


        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Database?.Dispose();
            GC.SuppressFinalize(this);
        }

        protected void assertNotDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("This session has been disposed");
        }

        public T Load<T>(string id)
        {
            assertNotDisposed();
            var document = storageFor<T, string>().Load(id, this);

            return document;
        }

        public async Task<T> LoadAsync<T>(string id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            var document = await storageFor<T, string>().LoadAsync(id, this, token).ConfigureAwait(false);

            return document;
        }

        public T Load<T>(int id)
        {
            assertNotDisposed();

            var storage = StorageFor<T>();

            T document = default;
            if (storage is IDocumentStorage<T, int> i)
            {
                document = i.Load(id, this);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                document = l.Load(id, this);
            }
            else
            {
                throw new InvalidOperationException($"The identity type for document type {typeof(T).FullNameInCode()} is not numeric");
            }

            return document;
        }

        public async Task<T> LoadAsync<T>(int id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();

            var storage = StorageFor<T>();

            T document = default;
            if (storage is IDocumentStorage<T, int> i)
            {
                document = await i.LoadAsync(id, this, token).ConfigureAwait(false);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                document = await l.LoadAsync(id, this, token).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException($"The identity type for document type {typeof(T).FullNameInCode()} is not numeric");
            }


            return document;
        }

        public T Load<T>(long id)
        {
            assertNotDisposed();
            var document = storageFor<T, long>().Load(id, this);

            return document;
        }

        public async Task<T> LoadAsync<T>(long id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            var document = await storageFor<T, long>().LoadAsync(id, this, token).ConfigureAwait(false);

            return document;
        }

        public T Load<T>(Guid id)
        {
            assertNotDisposed();
            var document = storageFor<T, Guid>().Load(id, this);

            return document;
        }

        public async Task<T> LoadAsync<T>(Guid id, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            var document = await storageFor<T, Guid>().LoadAsync(id, this, token).ConfigureAwait(false);

            return document;
        }


        public IMartenQueryable<T> Query<T>()
        {
            return new MartenLinqQueryable<T>(this);
        }

        public IReadOnlyList<T> Query<T>(string sql, params object[] parameters)
        {
            assertNotDisposed();
            var handler = new UserSuppliedQueryHandler<T>(this, sql, parameters);
            var provider = new LinqQueryProvider(this);
            return provider.ExecuteHandler(handler);
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token = default(CancellationToken), params object[] parameters)
        {
            assertNotDisposed();
            var handler = new UserSuppliedQueryHandler<T>(this, sql, parameters);
            var provider = new LinqQueryProvider(this);
            return provider.ExecuteHandlerAsync(handler, token);
        }

        public IBatchedQuery CreateBatchQuery()
        {
            return new BatchedQuery(Database, this);
        }

        public NpgsqlConnection Connection => Database.Connection;

        public IMartenSessionLogger Logger
        {
            get
            {
                return Database.As<ManagedConnection>().Logger;
            }
            set
            {
                Database.As<ManagedConnection>().Logger = value;
            }
        }

        public int RequestCount => Database.RequestCount;
        public IDocumentStore DocumentStore { get; }

        public TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
        {
            var source = Options.GetCompiledQuerySourceFor(query, this);
            var handler = (IQueryHandler<TOut>)source.Build(query, this);

            return ExecuteHandler(handler);
        }

        public Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default(CancellationToken))
        {
            var source = Options.GetCompiledQuerySourceFor(query, this);
            var handler = (IQueryHandler<TOut>)source.Build(query, this);

            return ExecuteHandlerAsync(handler, token);
        }

        public IReadOnlyList<T> LoadMany<T>(params string[] ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadMany(ids, this);
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<string> ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadMany(ids.ToArray(), this);

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params string[] ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids, this, default(CancellationToken));

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<string> ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids.ToArray(), this, default(CancellationToken));
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids, this, token);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<string> ids)
        {
            assertNotDisposed();
            return storageFor<T, string>().LoadManyAsync(ids.ToArray(), this, token);
        }



        public IReadOnlyList<T> LoadMany<T>(params int[] ids)
        {
            assertNotDisposed();

            var storage = StorageFor<T>();
            if (storage is IDocumentStorage<T, int> i)
            {
                return i.LoadMany(ids, this);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                return l.LoadMany(ids.Select(x => (long)x).ToArray(), this);
            }


            throw new InvalidOperationException($"The identity type for document type {typeof(T).FullNameInCode()} is not numeric");
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<int> ids)
        {
            return LoadMany<T>(ids.ToArray());
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params int[] ids)
        {
            return LoadManyAsync<T>(CancellationToken.None, ids);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<int> ids)
        {
            return LoadManyAsync<T>(ids.ToArray());
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params int[] ids)
        {
            assertNotDisposed();

            var storage = StorageFor<T>();
            if (storage is IDocumentStorage<T, int> i)
            {
                return i.LoadManyAsync(ids, this, token);
            }
            else if (storage is IDocumentStorage<T, long> l)
            {
                return l.LoadManyAsync(ids.Select(x => (long)x).ToArray(), this, token);
            }


            throw new InvalidOperationException($"The identity type for document type {typeof(T).FullNameInCode()} is not numeric");
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<int> ids)
        {
            return LoadManyAsync<T>(token, ids.ToArray());
        }




        public IReadOnlyList<T> LoadMany<T>(params long[] ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadMany(ids, this);
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<long> ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadMany(ids.ToArray(), this);

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params long[] ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids, this, default(CancellationToken));

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<long> ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids.ToArray(), this, default(CancellationToken));
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids, this, token);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<long> ids)
        {
            assertNotDisposed();
            return storageFor<T, long>().LoadManyAsync(ids.ToArray(), this, token);
        }




        public IReadOnlyList<T> LoadMany<T>(params Guid[] ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadMany(ids, this);
        }

        public IReadOnlyList<T> LoadMany<T>(IEnumerable<Guid> ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadMany(ids.ToArray(), this);

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(params Guid[] ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids, this, default(CancellationToken));

        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<Guid> ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids.ToArray(), this, default(CancellationToken));
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids, this, token);
        }

        public Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<Guid> ids)
        {
            assertNotDisposed();
            return storageFor<T, Guid>().LoadManyAsync(ids.ToArray(), this, token);
        }





        public IJsonLoader Json => new JsonLoader(this);
        public Guid? VersionFor<TDoc>(TDoc entity)
        {
            return StorageFor<TDoc>().VersionFor(entity, this);
        }

        public IReadOnlyList<TDoc> Search<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.Search(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> SearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.Search(searchTerm, regConfig)).ToListAsync(token: token);
        }

        public IReadOnlyList<TDoc> PlainTextSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.PlainTextSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> PlainTextSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.PlainTextSearch(searchTerm, regConfig)).ToListAsync(token: token);
        }

        public IReadOnlyList<TDoc> PhraseSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.PhraseSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> PhraseSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.PhraseSearch(searchTerm, regConfig)).ToListAsync(token: token);
        }

        public IReadOnlyList<TDoc> WebStyleSearch<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig)
        {
            return Query<TDoc>().Where(d => d.WebStyleSearch(searchTerm, regConfig)).ToList();
        }

        public Task<IReadOnlyList<TDoc>> WebStyleSearchAsync<TDoc>(string searchTerm, string regConfig = FullTextIndex.DefaultRegConfig, CancellationToken token = default)
        {
            return Query<TDoc>().Where(d => d.WebStyleSearch(searchTerm, regConfig)).ToListAsync(token: token);
        }

        public DocumentMetadata MetadataFor<T>(T entity)
        {
            assertNotDisposed();
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var storage = StorageFor<T>();
            var id = storage.IdentityFor(entity);
            var handler = new EntityMetadataQueryHandler(id, (IDocumentMapping) storage.Fields);

            return ExecuteHandler(handler);
        }

        public Task<DocumentMetadata> MetadataForAsync<T>(T entity, CancellationToken token = default(CancellationToken))
        {
            assertNotDisposed();
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var storage = StorageFor<T>();
            var id = storage.IdentityFor(entity);
            var handler = new EntityMetadataQueryHandler(id, (IDocumentMapping) storage.Fields);

            return ExecuteHandlerAsync(handler, token);
        }

        // TODO -- there's some duplication in the code below and how it builds up a command
        // and executes it.
        public async Task<T> ExecuteHandlerAsync<T>(IQueryHandler<T> handler, CancellationToken token)
        {
            var cmd = new NpgsqlCommand();
            var builder = new CommandBuilder(cmd);
            handler.ConfigureCommand(builder, this);

            cmd.CommandText = builder.ToString();

            // TODO -- Like this to be temporary
            if (cmd.CommandText.Contains(CommandBuilder.TenantIdArg))
            {
                cmd.AddNamedParameter(TenantIdArgument.ArgName, Tenant.TenantId);
            }

            using (var reader = await Database.ExecuteReaderAsync(cmd, token).ConfigureAwait(false))
            {
                return await handler.HandleAsync(reader, this, token).ConfigureAwait(false);
            }
        }

        public T ExecuteHandler<T>(IQueryHandler<T> handler)
        {
            var cmd = new NpgsqlCommand();
            var builder = new CommandBuilder(cmd);
            handler.ConfigureCommand(builder, this);

            cmd.CommandText = builder.ToString();

            // TODO -- Like this to be temporary
            if (cmd.CommandText.Contains(CommandBuilder.TenantIdArg))
            {
                cmd.AddNamedParameter(TenantIdArgument.ArgName, Tenant.TenantId);
            }

            using (var reader = Database.ExecuteReader(cmd))
            {
                return handler.Handle(reader, this);
            }
        }
    }
}
