using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.BulkLoading;
using Marten.Services;
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
        private readonly ISerializer _serializer;
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
            _options = options;
            _connectionFactory = options.ConnectionFactory();

            _logger = options.Logger();

            Schema = new DocumentSchema(_options, _connectionFactory, _logger);


            _serializer = options.Serializer();

            var cleaner = new DocumentCleaner(_connectionFactory, Schema.As<DocumentSchema>());
            Advanced = new AdvancedOptions(cleaner, options, _serializer, Schema);

            Diagnostics = new Diagnostics(Schema);

            EventStore = new EventStoreAdmin(Schema, _connectionFactory, _options, _serializer);

            CreateDatabaseObjects();

            options.InitialData.Each(x => x.Populate(this));
        }

        private readonly StoreOptions _options;
        private readonly IConnectionFactory _connectionFactory;
        private readonly IMartenLogger _logger;

        public virtual void Dispose()
        {
        }

        

        public IDocumentSchema Schema { get; }
        public AdvancedOptions Advanced { get; }

        private void CreateDatabaseObjects()
        {
            if (_options.AutoCreateSchemaObjects == AutoCreate.None) return;

            var allSchemaNames = Schema.AllSchemaNames();
            var generator = new DatabaseSchemaGenerator(Advanced);
            generator.Generate(allSchemaNames);

            if (Schema.Events.IsActive)
            {
                EventStore.InitializeEventStoreInDatabase();
            }
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
                    .Select(
                        group => typeof (BulkInserter<>).CloseAndBuildAs<IBulkInserter>(@group, @group.Key))
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

        public IEventStoreAdmin EventStore { get; }

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
            var loader = Schema.BulkLoaderFor<T>();

            if (mode != BulkInsertMode.InsertsOnly)
            {
                var sql = loader.CreateTempTableForCopying();
                conn.RunSql(sql);
            }

            if (documents.Length <= batchSize)
            {
                if (mode == BulkInsertMode.InsertsOnly)
                {
                    loader.Load(_serializer, conn, documents);
                }
                else
                {
                    loader.LoadIntoTempTable(_serializer, conn, documents);
                }
                
            }
            else
            {
                var total = 0;
                var page = 0;

                while (total < documents.Length)
                {
                    var batch = documents.Skip(page*batchSize).Take(batchSize).ToArray();

                    if (mode == BulkInsertMode.InsertsOnly)
                    {
                        loader.Load(_serializer, conn, batch);
                    }
                    else
                    {
                        loader.LoadIntoTempTable(_serializer, conn, batch);
                    }


                    page++;
                    total += batch.Length;
                }
            }

            if (mode == BulkInsertMode.IgnoreDuplicates)
            {
                var copy = loader.CopyNewDocumentsFromTempTable();

                conn.RunSql(copy);
            }
            else if (mode == BulkInsertMode.OverwriteExisting)
            {
                var copy = loader.CopyNewDocumentsFromTempTable();
                var overwrite = loader.OverwriteDuplicatesFromTempTable();

                conn.RunSql(copy, overwrite);
            }
        }

        public IDiagnostics Diagnostics { get; }

        public IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly,
            IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            var map = createMap(tracking);
            var session = new DocumentSession(this, _options, Schema, _serializer,
                new ManagedConnection(_connectionFactory, CommandRunnerMode.Transactional, isolationLevel), _parser, map);

            session.Logger = _logger.StartSession(session);

            return session;
        }

        private IIdentityMap createMap(DocumentTracking tracking)
        {
            switch (tracking)
            {
                case DocumentTracking.None:
                    return new NulloIdentityMap(_serializer);

                case DocumentTracking.IdentityOnly:
                    return new IdentityMap(_serializer, _options.Listeners);

                case DocumentTracking.DirtyTracking:
                    return new DirtyTrackingIdentityMap(_serializer, _options.Listeners);

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

        public IQuerySession QuerySession()
        {
            var parser = new MartenQueryParser();

            var session = new QuerySession(this, Schema, _serializer,
                new ManagedConnection(_connectionFactory, CommandRunnerMode.ReadOnly), parser,
                new NulloIdentityMap(_serializer), _options);

            session.Logger = _logger.StartSession(session);

            return session;
        }
    }
}