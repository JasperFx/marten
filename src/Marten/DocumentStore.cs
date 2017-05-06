using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Transforms;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{

    /// <summary>
    /// The main entry way to using Marten
    /// </summary>
    public class DocumentStore : IDocumentStore
    {
        private readonly IQueryParser _parser = new MartenQueryParser();

        /// <summary>
        /// Quick way to stand up a DocumentStore to the given database connection
        /// in the "development" mode for auto-creating schema objects as needed
        /// with the default behaviors
        /// </summary>
        /// <param name="connectionString"></param>
        /// <returns></returns>
        public static DocumentStore For(string connectionString)
        {
            return For(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Connection(connectionString);
            });
        }

        /// <summary>
        /// Configures a DocumentStore for an existing StoreOptions type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static DocumentStore For<T>() where T : StoreOptions, new()
        {
            return new DocumentStore(new T());
        }

        /// <summary>
        /// Configures a DocumentStore by defining the StoreOptions settings first
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static DocumentStore For(Action<StoreOptions> configure)
        {
            var options = new StoreOptions();
            configure(options);

            return new DocumentStore(options);
        }

        /// <summary>
        /// Creates a new DocumentStore with the supplied StoreOptions
        /// </summary>
        /// <param name="options"></param>
        public DocumentStore(StoreOptions options)
        {
            options.CreatePatching();

            Options = options;
            _connectionFactory = options.ConnectionFactory;

            _logger = options.Logger();

            Tenants = options.Tenancy;
            Tenants.Initialize();

            // TODO -- this needs to hang off of Tenancy
            Schema = new TenantSchema(options, Tenants.Default.As<Tenant>());

            Storage = options.Storage;

            Storage.CompileSubClasses();

            Serializer = options.Serializer();

            var cleaner = new DocumentCleaner(this, Tenants.Default);
            if (options.UseCharBufferPooling)
            {
                _writerPool = new CharArrayTextWriter.Pool();
            }

            Advanced = new AdvancedOptions(this, cleaner, _writerPool);

            Diagnostics = new Diagnostics(this);

            Transform = new DocumentTransforms(this, _connectionFactory, Tenants.Default);

            options.InitialData.Each(x => x.Populate(this));

            Parser = new MartenExpressionParser(Serializer, options);

            HandlerFactory = new QueryHandlerFactory(this);

            Events = options.Events;
        }

        public ITenancy Tenants { get; }

        public EventGraph Events { get; }

        internal IQueryHandlerFactory HandlerFactory { get; }

        internal MartenExpressionParser Parser { get; }

        [Obsolete("get rid of this")]
        private readonly IConnectionFactory _connectionFactory;
        private readonly IMartenLogger _logger;
        private readonly CharArrayTextWriter.IPool _writerPool;

        public StorageFeatures Storage { get; }
        public ISerializer Serializer { get; }

        public virtual void Dispose()
        {
            _writerPool.Dispose();
        }


        public StoreOptions Options { get; }

        public IDocumentSchema Schema { get; }
        public AdvancedOptions Advanced { get; }

        public void BulkInsert<T>(T[] documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenants.Default, Options, _writerPool);
            bulkInsertion.BulkInsert(documents, mode, batchSize);
        }

        public void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenants.Default, Options, _writerPool);
            bulkInsertion.BulkInsertDocuments(documents, mode, batchSize);
        }

        public void BulkInsert<T>(string tenantId, T[] documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenants[tenantId], Options, _writerPool);
            bulkInsertion.BulkInsert(documents, mode, batchSize);
        }

        public void BulkInsertDocuments(string tenantId, IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
            int batchSize = 1000)
        {
            var bulkInsertion = new BulkInsertion(Tenants[tenantId], Options, _writerPool);
            bulkInsertion.BulkInsertDocuments(documents, mode, batchSize);
        }

        public IDiagnostics Diagnostics { get; }

        public IDocumentSession OpenSession(SessionOptions options)
        {
            return openSession(options);
        }

        public IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return openSession(new SessionOptions
            {
                Tracking = tracking,
                IsolationLevel = isolationLevel
            });
        }

        public IDocumentSession OpenSession(string tenantId, DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return openSession(new SessionOptions
            {
                Tracking = tracking,
                IsolationLevel = isolationLevel,
                TenantId = tenantId
            });
        }

        private IDocumentSession openSession(SessionOptions options)
        {
            var sessionPool = CreateWriterPool();
            var map = createMap(options.Tracking, sessionPool, options.Listeners);

            var tenant = Tenants[options.TenantId];
            var connection = tenant.OpenConnection(CommandRunnerMode.Transactional, options.IsolationLevel, options.Timeout);
            

            var session = new DocumentSession(this, connection, _parser, map, tenant, options.Listeners);
            connection.BeginSession();

            session.Logger = _logger.StartSession(session);

            return session;
        }

        internal CharArrayTextWriter.Pool CreateWriterPool()
        {
            return Options.UseCharBufferPooling ? new CharArrayTextWriter.Pool(_writerPool) : null;
        }

        private IIdentityMap createMap(DocumentTracking tracking, CharArrayTextWriter.IPool sessionPool, IEnumerable<IDocumentSessionListener> localListeners)
        {
            switch (tracking)
            {
                case DocumentTracking.None:
                    return new NulloIdentityMap(Serializer);

                case DocumentTracking.IdentityOnly:
                    return new IdentityMap(Serializer, Options.Listeners.Concat(localListeners));

                case DocumentTracking.DirtyTracking:
                    return new DirtyTrackingIdentityMap(Serializer, Options.Listeners.Concat(localListeners), sessionPool);

                default:
                    throw new ArgumentOutOfRangeException(nameof(tracking));
            }
        }

        public IDocumentSession DirtyTrackedSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(DocumentTracking.DirtyTracking, isolationLevel);
        }

        public IDocumentSession DirtyTrackedSession(string tenantId, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(tenantId, DocumentTracking.DirtyTracking, isolationLevel);
        }

        public IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(DocumentTracking.None, isolationLevel);
        }

        public IDocumentSession LightweightSession(string tenantId, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(tenantId, DocumentTracking.None, isolationLevel);
        }

        public IQuerySession QuerySession(SessionOptions options)
        {
            var parser = new MartenQueryParser();

            var tenant = Tenants[options.TenantId];
            var connection = tenant.OpenConnection(CommandRunnerMode.ReadOnly);

            var session = new QuerySession(this,
                connection, parser,
                new NulloIdentityMap(Serializer), tenant);

            session.Logger = _logger.StartSession(session);

            return session;
        }

        public IQuerySession QuerySession()
        {
            return QuerySession(Marten.Storage.Tenancy.DefaultTenantId);
        }

        public IQuerySession QuerySession(string tenantId)
        {
            var parser = new MartenQueryParser();

            var tenant = Tenants[tenantId];
            var connection = tenant.OpenConnection(CommandRunnerMode.ReadOnly);

            var session = new QuerySession(this,
                connection, parser,
                new NulloIdentityMap(Serializer), tenant);

            session.Logger = _logger.StartSession(session);

            return session;
        }

        public IDocumentTransforms Transform { get; }

        public IDaemon BuildProjectionDaemon(Type[] viewTypes = null, IDaemonLogger logger = null, DaemonSettings settings = null, IProjection[] projections = null)
        {
            Tenants.Default.EnsureStorageExists(typeof(EventStream));

            if (projections == null)
            {
                projections = viewTypes?.Select(x => Events.ProjectionFor(x)).Where(x => x != null).ToArray() ?? Events.AsyncProjections.ToArray();
            }

            return new Daemon(this, Tenants.Default, settings ?? new DaemonSettings(), logger ?? new NulloDaemonLogger(), projections);
        }
    }
}