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
using Marten.Util;
using Npgsql;
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

            // TODO -- think this is temporary
            var tenant = new Tenant(options.Storage, options, options.ConnectionFactory, "default");
            DefaultTenant = tenant;
            Schema = new TenantSchema(options, _connectionFactory, tenant);



            Storage = options.Storage;

            Storage.CompileSubClasses();

            Serializer = options.Serializer();

            var cleaner = new DocumentCleaner(_connectionFactory, this, DefaultTenant);
            if (options.UseCharBufferPooling)
            {
                _writerPool = new CharArrayTextWriter.Pool();
            }

            Advanced = new AdvancedOptions(this, cleaner, _writerPool);

            Diagnostics = new Diagnostics(this);


            seedSchemas();

            Transform = new DocumentTransforms(this, _connectionFactory, DefaultTenant);

            options.InitialData.Each(x => x.Populate(this));

            Parser = new MartenExpressionParser(Serializer, options);

            HandlerFactory = new QueryHandlerFactory(this);

            Events = options.Events;
        }

        public EventGraph Events { get; }

        public ITenant DefaultTenant { get; }

        internal IQueryHandlerFactory HandlerFactory { get; }

        internal MartenExpressionParser Parser { get; }

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

        private void seedSchemas()
        {
            if (Options.AutoCreateSchemaObjects == AutoCreate.None) return;

            var allSchemaNames = Storage.AllSchemaNames();
            var generator = new DatabaseSchemaGenerator(DefaultTenant);
            generator.Generate(Options, allSchemaNames);
        }

        public void BulkInsert<T>(T[] documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000)
        {
            if (typeof (T) == typeof (object))
            {
                BulkInsertDocuments(documents.OfType<object>(), mode: mode);
            }
            else
            {
                using (var conn = _connectionFactory.Create())
                {
                    conn.Open();
                    var tx = conn.BeginTransaction();

                    try
                    {
                        bulkInsertDocuments(documents, batchSize, conn, mode);

                        tx.Commit();
                    }
                    catch (Exception)
                    {
                        tx.Rollback();
                        throw;
                    }
                }
            }
        }

        public void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000)
        {
            var groups =
                documents.Where(x => x != null)
                         .GroupBy(x => x.GetType())
                         .Select(group => typeof(BulkInserter<>).CloseAndBuildAs<IBulkInserter>(@group, @group.Key))
                         .ToArray();

            using (var conn = _connectionFactory.Create())
            {
                conn.Open();
                var tx = conn.BeginTransaction();

                try
                {
                    groups.Each(x => x.BulkInsert(batchSize, conn, this, mode));

                    tx.Commit();
                }
                catch (Exception)
                {
                    tx.Rollback();
                    throw;
                }
            }
        }


        internal interface IBulkInserter
        {
            void BulkInsert(int batchSize, NpgsqlConnection connection, DocumentStore parent, BulkInsertMode mode);
        }

        internal class BulkInserter<T> : IBulkInserter
        {
            private readonly T[] _documents;

            public BulkInserter(IEnumerable<object> documents)
            {
                _documents = documents.OfType<T>().ToArray();
            }

            public void BulkInsert(int batchSize, NpgsqlConnection connection, DocumentStore parent, BulkInsertMode mode)
            {
                parent.bulkInsertDocuments(_documents, batchSize, connection, mode);
            }
        }

        private void bulkInsertDocuments<T>(T[] documents, int batchSize, NpgsqlConnection conn, BulkInsertMode mode)
        {
            var loader = DefaultTenant.BulkLoaderFor<T>();

            if (mode != BulkInsertMode.InsertsOnly)
            {
                var sql = loader.CreateTempTableForCopying();
                conn.RunSql(sql);
            }

            var writer = Options.UseCharBufferPooling ? _writerPool.Lease() : null;
            try
            {
                if (documents.Length <= batchSize)
                {
                    if (mode == BulkInsertMode.InsertsOnly)
                    {
                        loader.Load(Serializer, conn, documents, writer);
                    }
                    else
                    {
                        loader.LoadIntoTempTable(Serializer, conn, documents, writer);
                    }

                }
                else
                {
                    var total = 0;
                    var page = 0;

                    while (total < documents.Length)
                    {
                        var batch = documents.Skip(page * batchSize).Take(batchSize).ToArray();

                        if (mode == BulkInsertMode.InsertsOnly)
                        {
                            loader.Load(Serializer, conn, batch, writer);
                        }
                        else
                        {
                            loader.LoadIntoTempTable(Serializer, conn, batch, writer);
                        }


                        page++;
                        total += batch.Length;
                    }
                }
            }
            finally
            {
                if (writer != null)
                {
                    _writerPool.Release(writer);
                }
            }

            if (mode == BulkInsertMode.IgnoreDuplicates)
            {
                var copy = loader.CopyNewDocumentsFromTempTable();

                conn.RunSql(copy);
            }
            else if (mode == BulkInsertMode.OverwriteExisting)
            {
                var overwrite = loader.OverwriteDuplicatesFromTempTable();
                var copy = loader.CopyNewDocumentsFromTempTable();
                
                conn.RunSql(overwrite, copy);
            }
        }

        public IDiagnostics Diagnostics { get; }

        public IDocumentSession OpenSession(SessionOptions options)
        {
            var connection = new ManagedConnection(_connectionFactory, CommandRunnerMode.Transactional, options.IsolationLevel, options.Timeout);
            return openSession(options.Tracking, connection, options.Listeners);
        }

        public IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            var connection = new ManagedConnection(_connectionFactory, CommandRunnerMode.Transactional, isolationLevel);
            return openSession(tracking, connection, new List<IDocumentSessionListener>());
        }

        private IDocumentSession openSession(DocumentTracking tracking, ManagedConnection connection, IList<IDocumentSessionListener> localListeners)
        {
            var sessionPool = CreateWriterPool();
            var map = createMap(tracking, sessionPool, localListeners);

            // TODO -- need to pass in the default tenant instead when that exists
            var session = new DocumentSession(this, connection, _parser, map, DefaultTenant, localListeners);
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

        public IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return OpenSession(DocumentTracking.None, isolationLevel);
        }

        public IQuerySession QuerySession(SessionOptions options)
        {
            var parser = new MartenQueryParser();

            var session = new QuerySession(this,
                new ManagedConnection(_connectionFactory, CommandRunnerMode.ReadOnly, options.IsolationLevel, options.Timeout), parser,
                new NulloIdentityMap(Serializer), DefaultTenant);

            session.Logger = _logger.StartSession(session);

            return session;
        }

        public IQuerySession QuerySession()
        {
            var parser = new MartenQueryParser();

            var session = new QuerySession(this,
                new ManagedConnection(_connectionFactory, CommandRunnerMode.ReadOnly), parser,
                new NulloIdentityMap(Serializer), DefaultTenant);

            session.Logger = _logger.StartSession(session);

            return session;
        }

        public IDocumentTransforms Transform { get; }
        public IDaemon BuildProjectionDaemon(Type[] viewTypes = null, IDaemonLogger logger = null, DaemonSettings settings = null, IProjection[] projections = null)
        {
            DefaultTenant.EnsureStorageExists(typeof(EventStream));

            if (projections == null)
            {
                projections = viewTypes?.Select(x => Events.ProjectionFor(x)).Where(x => x != null).ToArray() ?? Events.AsyncProjections.ToArray();
            }

            return new Daemon(this, DefaultTenant, settings ?? new DaemonSettings(), logger ?? new NulloDaemonLogger(), projections);
        }
    }
}