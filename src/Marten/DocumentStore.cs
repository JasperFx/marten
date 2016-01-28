using System;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Remotion.Linq.Parsing.Structure;

namespace Marten
{
    /// <summary>
    /// The main entry way to using Marten
    /// </summary>
    public class DocumentStore : IDocumentStore
    {
        private readonly ICommandRunner _runner;
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
                _.AutoCreateSchemaObjects = true;
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
            _runner = new CommandRunner(options.ConnectionFactory());

            var creation = options.AutoCreateSchemaObjects
                ? (IDocumentSchemaCreation) new DevelopmentSchemaCreation(_runner)
                : new ProductionSchemaCreation();

            Schema = new DocumentSchema(_runner, creation) {StoreOptions = options};

            Schema.Alter(options.Schema);

            _serializer = options.Serializer();

            var cleaner = new DocumentCleaner(_runner, Schema);
            Advanced = new AdvancedOptions(cleaner, options);

            Diagnostics = new Diagnostics(Schema, new MartenQueryExecutor(_runner, Schema, _serializer, _parser));


            if (options.RequestCounterThreshold.HasThreshold)
            {
                _runnerForSession = () => new RequestCounter(_runner, options.RequestCounterThreshold);
            }
            else
            {
                _runnerForSession = () => _runner;
            }
        }

        private readonly Func<ICommandRunner> _runnerForSession;
        private readonly StoreOptions _options;

        public void Dispose()
        {
            
        }

        public IDocumentSchema Schema { get; }
        public AdvancedOptions Advanced { get; }


        public void BulkInsert<T>(T[] documents, int batchSize = 1000)
        {
            var storage = Schema.StorageFor(typeof(T)).As<IBulkLoader<T>>();

            _runner.ExecuteInTransaction((conn, tx) =>
            {
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
                        var batch = documents.Skip(page * batchSize).Take(batchSize).ToArray();
                        storage.Load(_serializer, conn, batch);

                        page++;
                        total += batch.Length;
                    }
                }
            });
        }

        public IDiagnostics Diagnostics { get; }

        public IDocumentSession OpenSession(DocumentTracking tracking = DocumentTracking.IdentityOnly)
        {
            var map = createMap(tracking);
            return new DocumentSession(_options, Schema, _serializer, _runnerForSession(), _parser, new MartenQueryExecutor(_runnerForSession(), Schema, _serializer, _parser), map);
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

        public IDocumentSession DirtyTrackedSession()
        {
            return OpenSession(DocumentTracking.DirtyTracking);
        }

        public IDocumentSession LightweightSession()
        {
            return OpenSession(DocumentTracking.None);
        }

        public IQuerySession QuerySession()
        {
            var parser = new MartenQueryParser();
            
            return new QuerySession(Schema, _serializer, _runnerForSession(), parser, new MartenQueryExecutor(_runnerForSession(), Schema, _serializer, parser), new NulloIdentityMap(_serializer));
        }


    }
}