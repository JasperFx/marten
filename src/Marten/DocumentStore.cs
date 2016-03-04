using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Npgsql;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    /// <summary>
    /// The main entry way to using Marten
    /// </summary>
    public class DocumentStore : IDocumentStore
    {
        private readonly IManagedConnection _runner;
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
            _runner = new ManagedConnection(_connectionFactory, CommandRunnerMode.ReadOnly);

            _logger = options.Logger();

            Schema = new DocumentSchema(_options, _connectionFactory, _logger);

            Schema.Alter(options.Schema);

            _serializer = options.Serializer();

            var cleaner = new DocumentCleaner(_connectionFactory, Schema);
            Advanced = new AdvancedOptions(cleaner, options);

            Diagnostics = new Diagnostics(Schema, new MartenQueryExecutor(_runner, Schema, _serializer, _parser, new NulloIdentityMap(_serializer)));

            EventStore = new EventStoreAdmin(_connectionFactory, _options, _serializer);

            if (Schema.Events.IsActive && options.AutoCreateSchemaObjects != AutoCreate.None)
            {
                Schema.EnsureStorageExists(typeof(EventStream));
                EventStore.InitializeEventStoreInDatabase();
            }
        }

        private readonly StoreOptions _options;
        private readonly IConnectionFactory _connectionFactory;
        private readonly IMartenLogger _logger;

        public void Dispose()
        {
            _runner.Dispose();
        }

        public IDocumentSchema Schema { get; }
        public AdvancedOptions Advanced { get; }


        public void BulkInsert<T>(T[] documents, int batchSize = 1000)
        {
            if (typeof (T) == typeof (object))
            {
                BulkInsertDocuments(documents.OfType<object>());
            }
            else
            {
                using (var conn = _connectionFactory.Create())
                {
                    conn.Open();
                    var tx = conn.BeginTransaction();

                    try
                    {
                        bulkInsertDocuments(documents, batchSize, conn);

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

        public void BulkInsertDocuments(IEnumerable<object> documents, int batchSize = 1000)
        {
            var groups = documents.Where(x => x != null).GroupBy(x => x.GetType()).Select(group =>
            {
                return typeof (BulkInserter<>).CloseAndBuildAs<IBulkInserter>(group, group.Key);
            }).ToArray();

            using (var conn = _connectionFactory.Create())
            {
                conn.Open();
                var tx = conn.BeginTransaction();

                try
                {
                    groups.Each(x => x.BulkInsert(batchSize, conn, this));

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
            void BulkInsert(int batchSize, NpgsqlConnection connection, DocumentStore parent);
        }

        internal class BulkInserter<T> : IBulkInserter
        {
            private readonly T[] _documents;

            public BulkInserter(IEnumerable<object> documents)
            {
                _documents = documents.OfType<T>().ToArray();
            }

            public void BulkInsert(int batchSize, NpgsqlConnection connection, DocumentStore parent)
            {
                parent.bulkInsertDocuments<T>(_documents, batchSize, connection);
            }
        }

        private void bulkInsertDocuments<T>(T[] documents, int batchSize, NpgsqlConnection conn)
        {
            var storage = Schema.StorageFor(typeof (T)).As<IBulkLoader<T>>();

            if (documents.Length <= batchSize)
            {
                storage.Load(_serializer, conn, documents);
            }
            else
            {
                var total = 0;
                var page = 0;

                while (total < documents.Length)
                {
                    var batch = documents.Skip(page*batchSize).Take(batchSize).ToArray();

                    storage.Load(_serializer, conn, batch);

                    page++;
                    total += batch.Length;
                }
            }
        }

        public IDiagnostics Diagnostics { get; }

        public IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly, IsolationLevel isolationLevel = IsolationLevel.ReadUncommitted)
        {
            var map = createMap(tracking);
            var session = new DocumentSession(_options, Schema, _serializer, new ManagedConnection(_connectionFactory, CommandRunnerMode.Transactional, isolationLevel), _parser, map);

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
                    return new IdentityMap(_serializer);

                case DocumentTracking.DirtyTracking:
                    return new DirtyTrackingIdentityMap(_serializer);

                default:
                    throw new ArgumentOutOfRangeException(nameof(tracking));
            }
        }

        public IDocumentSession DirtyTrackedSession(IsolationLevel isolationLevel = IsolationLevel.ReadUncommitted)
        {
            return OpenSession(DocumentTracking.DirtyTracking, isolationLevel);
        }

        public IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadUncommitted)
        {
            return OpenSession(DocumentTracking.None, isolationLevel);
        }

        public IQuerySession QuerySession()
        {
            var parser = new MartenQueryParser();
            
            var session = new QuerySession(Schema, _serializer, new ManagedConnection(_connectionFactory, CommandRunnerMode.ReadOnly), parser, new NulloIdentityMap(_serializer));

            session.Logger = _logger.StartSession(session);

            return session;
        }


    }
}