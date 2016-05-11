using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Events;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Npgsql;

namespace Marten
{
    /// <summary>
    /// StoreOptions supplies all the necessary configuration
    /// necessary to customize and bootstrap a working
    /// DocumentStore
    /// </summary>
    public class StoreOptions
    {
        private ISerializer _serializer;
        private IConnectionFactory _factory;
        private IMartenLogger _logger = new NulloMartenLogger();

        private readonly ConcurrentDictionary<Type, DocumentMapping> _documentMappings =
            new ConcurrentDictionary<Type, DocumentMapping>();

        private string _databaseSchemaName = DefaultDatabaseSchemaName;

        /// <summary>
        /// The default database schema used 'public'.
        /// </summary>
        public const string DefaultDatabaseSchemaName = "public";

        public StoreOptions()
        {
            Events = new EventGraph(this);
            Schema = new MartenRegistry(this);
        }

        public DocumentMapping MappingFor(Type documentType)
        {
            return _documentMappings.GetOrAdd(documentType, type =>
            {
                var mapping = new DocumentMapping(type, this);

                if (mapping.IdMember == null)
                {
                    throw new InvalidDocumentException(
                        $"Could not determine an 'id/Id' field or property for requested document type {documentType.FullName}");
                }

                return mapping;
            });
        }

        /// <summary>
        /// Add, remove, or reorder global session listeners
        /// </summary>
        public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>(); 

        public IEnumerable<DocumentMapping> AllDocumentMappings => _documentMappings.Values; 

        /// <summary>
        /// Upsert syntax options. Defaults to Postgresql >= 9.5, but you can opt into
        /// the older upsert style for Postgresql 9.4
        /// </summary>
        public PostgresUpsertType UpsertType { get; set; } = PostgresUpsertType.Standard;

        /// <summary>
        /// Sets the database default schema name used to store the documents.
        /// </summary>
        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName; }
            set { _databaseSchemaName = value?.ToLowerInvariant(); }
        }

        /// <summary>
        /// Supply the connection string to the Postgresql database
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connection(string connectionString)
        {
            _factory = new ConnectionFactory(connectionString);
        }

        /// <summary>
        /// Supply a source for the connection string to a Postgresql database
        /// </summary>
        /// <param name="connectionSource"></param>
        public void Connection(Func<string> connectionSource)
        {
            _factory = new ConnectionFactory(connectionSource);
        }

        /// <summary>
        /// Supply a mechanism for resolving an NpgsqlConnection object to
        /// the Postgresql database
        /// </summary>
        /// <param name="source"></param>
        public void Connection(Func<NpgsqlConnection> source)
        {
            _factory = new LambdaConnectionFactory(source);
        }

        /// <summary>
        /// Override the JSON serialization by ISerializer type
        /// </summary>
        /// <param name="serializer"></param>
        public void Serializer(ISerializer serializer)
        {
            _serializer = serializer;
        }

        /// <summary>
        /// Override the JSON serialization by an ISerializer of type "T"
        /// </summary>
        /// <typeparam name="T">The ISerializer type</typeparam>
        public void Serializer<T>() where T : ISerializer, new()
        {
            _serializer = new T();
        }

        /// <summary>
        /// Modify the document and event store database mappings for indexes and searching options
        /// </summary>
        public readonly MartenRegistry Schema;

        /// <summary>
        /// Whether or Marten should attempt to create any missing database schema objects at runtime. This
        /// property is "All" by default for more efficient development, but can be set to lower values for production usage.
        /// </summary>
        public AutoCreate AutoCreateSchemaObjects = AutoCreate.All;

        /// <summary>
        /// Global default parameters for Hilo sequences within the DocumentStore. Can be overridden per document
        /// type as well
        /// </summary>
        public HiloSettings HiloSequenceDefaults { get; } = new HiloSettings();

        /// <summary>
        /// Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() / IUnitOfWork.ApplyChanges()
        /// </summary>
        public int UpdateBatchSize { get; set; } = 500;

        /// <summary>
        /// Set the default Id strategy for the document mapping.
        /// </summary>
        public Func<IDocumentMapping, StoreOptions, IIdGeneration> DefaultIdStrategy { get; set; }

        /// <summary>
        /// Force Marten to create document mappings for type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void RegisterDocumentType<T>()
        {
            RegisterDocumentType(typeof (T));
        }

        /// <summary>
        /// Force Marten to create a document mapping for the document type
        /// </summary>
        /// <param name="documentType"></param>
        public void RegisterDocumentType(Type documentType)
        {
            if (MappingFor(documentType) == null) throw new Exception("Unable to create document mapping for " + documentType);
        }

        /// <summary>
        /// Force Marten to create document mappings for all the given document types
        /// </summary>
        /// <param name="documentTypes"></param>
        public void RegisterDocumentTypes(IEnumerable<Type> documentTypes)
        {
            documentTypes.Each(RegisterDocumentType);
        }

        /// <summary>
        /// Configuration of event streams and projections
        /// </summary>
        public IEventStoreConfiguration Events { get; }

        public ISerializer Serializer()
        {
            return _serializer ?? new JsonNetSerializer();
        }

        internal IConnectionFactory ConnectionFactory()
        {
            if (_factory == null) throw new InvalidOperationException("No database connection source is configured");

            return _factory;
        }

        public IMartenLogger Logger()
        {
            return _logger ?? new NulloMartenLogger();
        }

        public void Logger(IMartenLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Extension point to add custom Linq query parsers
        /// </summary>
        public LinqCustomizations Linq { get; } = new LinqCustomizations();

    }

    public class LinqCustomizations
    {
        /// <summary>
        /// Add custom Linq expression parsers for your own methods
        /// </summary>
        public readonly IList<IMethodCallParser> MethodCallParsers = new List<IMethodCallParser>(); 
    }
}