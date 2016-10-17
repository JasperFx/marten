using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Transforms;
using Npgsql;

namespace Marten
{
    /// <summary>
    ///     StoreOptions supplies all the necessary configuration
    ///     necessary to customize and bootstrap a working
    ///     DocumentStore
    /// </summary>
    public class StoreOptions
    {
        /// <summary>
        ///     The default database schema used 'public'.
        /// </summary>
        public const string DefaultDatabaseSchemaName = "public";

        public const string PatchDoc = "patch_doc";

        private readonly ConcurrentDictionary<Type, DocumentMapping> _documentMappings =
            new ConcurrentDictionary<Type, DocumentMapping>();

        private readonly ConcurrentDictionary<Type, IDocumentMapping> _mappings =
            new ConcurrentDictionary<Type, IDocumentMapping>();


        /// <summary>
        ///     Add, remove, or reorder global session listeners
        /// </summary>
        public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

        /// <summary>
        ///     Modify the document and event store database mappings for indexes and searching options
        /// </summary>
        public readonly MartenRegistry Schema;

        private string _databaseSchemaName = DefaultDatabaseSchemaName;
        private IConnectionFactory _factory;
        private IMartenLogger _logger = new NulloMartenLogger();
        private ISerializer _serializer;

        /// <summary>
        ///     Whether or Marten should attempt to create any missing database schema objects at runtime. This
        ///     property is "All" by default for more efficient development, but can be set to lower values for production usage.
        /// </summary>
        public AutoCreate AutoCreateSchemaObjects = AutoCreate.All;


        public StoreOptions()
        {
            Events = new EventGraph(this);
            Schema = new MartenRegistry(this);
            Transforms = new Transforms.Transforms(this);

            var patching = new TransformFunction(this, PatchDoc, SchemaBuilder.GetJavascript(this, "mt_patching"));
            patching.OtherArgs.Add("patch");

            Transforms.Load(patching);
        }

        public IEnumerable<DocumentMapping> AllDocumentMappings => _documentMappings.Values;

        /// <summary>
        ///     Sets the database default schema name used to store the documents.
        /// </summary>
        public string DatabaseSchemaName
        {
            get { return _databaseSchemaName; }
            set { _databaseSchemaName = value?.ToLowerInvariant(); }
        }

        /// <summary>
        ///     Global default parameters for Hilo sequences within the DocumentStore. Can be overridden per document
        ///     type as well
        /// </summary>
        public HiloSettings HiloSequenceDefaults { get; } = new HiloSettings();

        /// <summary>
        ///     Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() /
        ///     IUnitOfWork.ApplyChanges()
        /// </summary>
        public int UpdateBatchSize { get; set; } = 500;

        /// <summary>
        ///     Set the default Id strategy for the document mapping.
        /// </summary>
        public Func<IDocumentMapping, StoreOptions, IIdGeneration> DefaultIdStrategy { get; set; }

        /// <summary>
        ///     Configuration of event streams and projections
        /// </summary>
        public EventGraph Events { get; }

        /// <summary>
        ///     Extension point to add custom Linq query parsers
        /// </summary>
        public LinqCustomizations Linq { get; } = new LinqCustomizations();

        public DocumentMapping MappingFor(Type documentType)
        {
            return _documentMappings.GetOrAdd(documentType, type =>
            {
                var mapping = typeof(DocumentMapping<>).CloseAndBuildAs<DocumentMapping>(this, documentType);

                if (mapping.IdMember == null)
                {
                    throw new InvalidDocumentException(
                        $"Could not determine an 'id/Id' field or property for requested document type {documentType.FullName}");
                }

                return mapping;
            });
        }

        /// <summary>
        ///     Supply the connection string to the Postgresql database
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connection(string connectionString)
        {
            _factory = new ConnectionFactory(connectionString);
        }

        /// <summary>
        ///     Supply a source for the connection string to a Postgresql database
        /// </summary>
        /// <param name="connectionSource"></param>
        public void Connection(Func<string> connectionSource)
        {
            _factory = new ConnectionFactory(connectionSource);
        }

        /// <summary>
        ///     Supply a mechanism for resolving an NpgsqlConnection object to
        ///     the Postgresql database
        /// </summary>
        /// <param name="source"></param>
        public void Connection(Func<NpgsqlConnection> source)
        {
            _factory = new LambdaConnectionFactory(source);
        }

        /// <summary>
        ///     Override the JSON serialization by ISerializer type
        /// </summary>
        /// <param name="serializer"></param>
        public void Serializer(ISerializer serializer)
        {
            _serializer = serializer;
        }

        /// <summary>
        ///     Use the default serialization (ilmerged Newtonsoft.Json) with Enum values
        ///     stored as either integers or strings
        /// </summary>
        /// <param name="enumStyle"></param>
        public void UseDefaultSerialization(EnumStorage enumStyle)
        {
            Serializer(new JsonNetSerializer {EnumStorage = enumStyle});
        }

        /// <summary>
        ///     Override the JSON serialization by an ISerializer of type "T"
        /// </summary>
        /// <typeparam name="T">The ISerializer type</typeparam>
        public void Serializer<T>() where T : ISerializer, new()
        {
            _serializer = new T();
        }

        public ITransforms Transforms { get; } 

        /// <summary>
        ///     Force Marten to create document mappings for type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public void RegisterDocumentType<T>()
        {
            RegisterDocumentType(typeof(T));
        }

        /// <summary>
        ///     Force Marten to create a document mapping for the document type
        /// </summary>
        /// <param name="documentType"></param>
        public void RegisterDocumentType(Type documentType)
        {
            if (MappingFor(documentType) == null)
                throw new Exception("Unable to create document mapping for " + documentType);
        }

        /// <summary>
        ///     Force Marten to create document mappings for all the given document types
        /// </summary>
        /// <param name="documentTypes"></param>
        public void RegisterDocumentTypes(IEnumerable<Type> documentTypes)
        {
            documentTypes.Each(RegisterDocumentType);
        }

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


        public readonly IList<IInitialData> InitialData = new List<IInitialData>();


        /// <summary>
        /// Allows you to modify how the DDL for document tables and upsert functions is
        /// written
        /// </summary>
        public DdlRules DdlRules { get; } = new DdlRules();

        /// <summary>
        /// Used to validate database object name lengths against Postgresql's NAMEDATALEN property to avoid
        /// Marten getting confused when comparing database schemas against the configuration. See https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html
        /// for more information. This does NOT adjust NAMEDATALEN for you.
        /// </summary>
        public int NameDataLength { get; set; } = 64;

        public void AssertValidIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new PostgresqlIdentifierInvalidException(name);
            if (name.IndexOf(' ') >= 0)
                throw new PostgresqlIdentifierInvalidException(name);
            if (name.Length < NameDataLength) return;
            throw new PostgresqlIdentifierTooLongException(NameDataLength, name);
        }

        internal IDocumentMapping FindMapping(Type documentType)
        {
            return _mappings.GetOrAdd(documentType, type =>
            {
                var subclass =  AllDocumentMappings.SelectMany(x => x.SubClasses)
                        .FirstOrDefault(x => x.DocumentType == type) as IDocumentMapping;

                return subclass ?? MappingFor(documentType);
            });
        }

        internal void AddMapping(IDocumentMapping mapping)
        {
            _mappings[mapping.DocumentType] = mapping;
        }
    }
}