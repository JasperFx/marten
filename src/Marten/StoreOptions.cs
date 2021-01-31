using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.CompiledQueries;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Metadata;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Services.Json;
using Marten.Storage;
using Marten.Transforms;
using Marten.Util;
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

        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, ChildDocument>> _childDocs
            = new();

        public StorageFeatures Storage { get; }
        public readonly IList<IInitialData> InitialData = new List<IInitialData>();

        /// <summary>
        ///     Add, remove, or reorder global session listeners
        /// </summary>
        public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

        /// <summary>
        ///     Modify the document and event store database mappings for indexes and searching options
        /// </summary>
        public readonly MartenRegistry Schema;

        private string _databaseSchemaName = DefaultDatabaseSchemaName;

        private IMartenLogger _logger = new NulloMartenLogger();
        private ISerializer _serializer;
        private EnumStorage? _duplicatedFieldEnumStorage;

        private IRetryPolicy _retryPolicy = new NulloRetryPolicy();

        /// <summary>
        ///     Whether or Marten should attempt to create any missing database schema objects at runtime. This
        ///     property is "All" by default for more efficient development, but can be set to lower values for production usage.
        /// </summary>
        public AutoCreate AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

        /// <summary>
        /// Configure Marten to create databases for tenants in case databases do not exist or need to be dropped & re-created
        /// </summary>
        /// <remarks>Creating and dropping databases requires the CREATEDB privilege</remarks>
        public void CreateDatabasesForTenants(Action<IDatabaseCreationExpressions> configure)
        {
            CreateDatabases = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        public Action<IDatabaseCreationExpressions> CreateDatabases { get; set; }

        public IProviderGraph Providers { get; }

        public StoreOptions()
        {
            EventGraph = new EventGraph(this);
            Schema = new MartenRegistry(this);
            Transforms = new Transforms.Transforms(this);
            Storage = new StorageFeatures(this);

            Providers = new ProviderGraph(this);
        }

        internal EventGraph EventGraph { get; }

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
        public HiloSettings HiloSequenceDefaults { get; } = new();

        /// <summary>
        ///     Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() /
        ///     IUnitOfWork.ApplyChanges()
        /// </summary>
        public int UpdateBatchSize { get; set; } = 500;

        /// <summary>
        ///     Configures the store to use char buffer pooling, greatly reducing allocations for serializing documents and events.
        ///     The default is true.
        /// </summary>
        public bool UseCharBufferPooling { get; set; } = true;

        /// <summary>
        ///     Set the default Id strategy for the document mapping.
        /// </summary>
        public Func<DocumentMapping, StoreOptions, IIdGeneration> DefaultIdStrategy { get; set; }

        /// <summary>
        ///     Configuration of event streams and projections
        /// </summary>
        public IEventStoreOptions Events => EventGraph;

        /// <summary>
        ///     Extension point to add custom Linq query parsers
        /// </summary>
        public LinqParsing Linq { get; } = new LinqParsing();

        public ITransforms Transforms { get; }

        /// <summary>
        ///     Allows you to modify how the DDL for document tables and upsert functions is
        ///     written
        /// </summary>
        public DdlRules DdlRules { get; } = new DdlRules();

        /// <summary>
        ///     Used to validate database object name lengths against Postgresql's NAMEDATALEN property to avoid
        ///     Marten getting confused when comparing database schemas against the configuration. See
        ///     https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html
        ///     for more information. This does NOT adjust NAMEDATALEN for you.
        /// </summary>
        public int NameDataLength { get; set; } = 64;

        /// <summary>
        ///     Gets Enum values stored as either integers or strings
        /// </summary>
        public EnumStorage EnumStorage => Serializer().EnumStorage;

        /// <summary>
        ///     Sets Enum values stored as either integers or strings for DuplicatedField.
        ///     Please use only for migration from Marten 2.*. It might be removed in the next major version.
        /// </summary>
        [Obsolete("Please use only for migration from Marten 2.*. It might be removed in the next major version.")]
        public EnumStorage DuplicatedFieldEnumStorage
        {
            get { return _duplicatedFieldEnumStorage ?? EnumStorage; }
            set
            {
                _duplicatedFieldEnumStorage = value;
            }
        }

        /// <summary>
        ///     Decides if `timestamp without time zone` database type should be used for `DateTime` DuplicatedField.
        ///     Please use only for migration from Marten 2.*. It might be removed in the next major version.
        /// </summary>
        [Obsolete("Please use only for migration from Marten 2.*. It might be removed in the next major versions")]
        public bool DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime { get; set; } = true;

        internal void CreatePatching()
        {
            if (PLV8Enabled)
            {
                var patching = new TransformFunction(this, PatchDoc, SchemaBuilder.GetJavascript(this, "mt_patching"));
                patching.OtherArgs.Add("patch");

                Transforms.Load(patching);
            }
        }

        internal ChildDocument GetChildDocument(string locator, Type documentType)
        {
            var byType = _childDocs.GetOrAdd(documentType, _ => new ConcurrentDictionary<string, ChildDocument>());

            return byType.GetOrAdd(locator, _ => new ChildDocument(locator, documentType, this));
        }

        /// <summary>
        ///     Supply the connection string to the Postgresql database
        /// </summary>
        /// <param name="connectionString"></param>
        public void Connection(string connectionString)
        {
            Tenancy = new DefaultTenancy(new ConnectionFactory(connectionString), this);
        }

        /// <summary>
        ///     Supply a source for the connection string to a Postgresql database
        /// </summary>
        /// <param name="connectionSource"></param>
        public void Connection(Func<string> connectionSource)
        {
            Tenancy = new DefaultTenancy(new ConnectionFactory(connectionSource), this);
        }

        /// <summary>
        ///     Supply a mechanism for resolving an NpgsqlConnection object to
        ///     the Postgresql database
        /// </summary>
        /// <param name="source"></param>
        public void Connection(Func<NpgsqlConnection> source)
        {
            Tenancy = new DefaultTenancy(new LambdaConnectionFactory(source), this);
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
        /// <param name="enumStorage"></param>
        /// <param name="casing">Casing style to be used in serialization</param>
        /// <param name="collectionStorage">Allow to set collection storage as raw arrays (without explicit types)</param>
        /// <param name="nonPublicMembersStorage">Allow non public members to be used during deserialization</param>
        public void UseDefaultSerialization(
            EnumStorage enumStorage = EnumStorage.AsInteger,
            Casing casing = Casing.Default,
            CollectionStorage collectionStorage = CollectionStorage.Default,
            NonPublicMembersStorage nonPublicMembersStorage = NonPublicMembersStorage.Default,
            SerializerType serializerType = SerializerType.Newtonsoft
        )
        {
            var serializer = SerializerFactory.New(serializerType,
                new SerializerOptions
                {
                    EnumStorage = enumStorage,
                    Casing = casing,
                    CollectionStorage = collectionStorage,
                    NonPublicMembersStorage = nonPublicMembersStorage
                });

            Serializer(serializer);
        }

        /// <summary>
        ///     Override the JSON serialization by an ISerializer of type "T"
        /// </summary>
        /// <typeparam name="T">The ISerializer type</typeparam>
        public void Serializer<T>() where T : ISerializer, new()
        {
            _serializer = new T();
        }

        public ISerializer Serializer()
        {
            return _serializer ?? SerializerFactory.New();
        }

        public IMartenLogger Logger()
        {
            return _logger ?? new NulloMartenLogger();
        }

        public void Logger(IMartenLogger logger)
        {
            _logger = logger;
        }

        public IRetryPolicy RetryPolicy()
        {
            return _retryPolicy ?? new NulloRetryPolicy();
        }

        public void RetryPolicy(IRetryPolicy retryPolicy)
        {
            _retryPolicy = retryPolicy;
        }

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
            Storage.RegisterDocumentType(documentType);
        }

        /// <summary>
        ///     Force Marten to create document mappings for all the given document types
        /// </summary>
        /// <param name="documentTypes"></param>
        public void RegisterDocumentTypes(IEnumerable<Type> documentTypes)
        {
            documentTypes.Each(RegisterDocumentType);
        }

        public void AssertValidIdentifier(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new PostgresqlIdentifierInvalidException(name);
            if (name.IndexOf(' ') >= 0)
                throw new PostgresqlIdentifierInvalidException(name);
            if (name.Length < NameDataLength)
                return;
            throw new PostgresqlIdentifierTooLongException(NameDataLength, name);
        }

        internal void ApplyConfiguration()
        {
            Storage.BuildAllMappings();

            foreach (var mapping in Storage.AllDocumentMappings)
            {
                mapping.CompileAndValidate();
            }
        }

        public ITenancy Tenancy { get; set; }

        private readonly IList<IDocumentPolicy> _policies = new List<IDocumentPolicy>
        {
            new VersionedPolicy(), new SoftDeletedPolicy(), new TrackedPolicy(), new TenancyPolicy()
        };

        internal void applyPolicies(DocumentMapping mapping)
        {
            foreach (var policy in _policies)
            {
                policy.Apply(mapping);
            }
        }

        /// <summary>
        /// Validate that minimal options to initialize a document store have been specified
        /// </summary>
        internal void Validate()
        {
            if (Tenancy == null)
            {
                throw new InvalidOperationException("Tenancy not specified - provide either connection string or connection factory through Connection(..)");
            }
        }

        /// <summary>
        /// Apply conventional policies to how documents are mapped
        /// </summary>
        public PoliciesExpression Policies => new PoliciesExpression(this);

        public bool PLV8Enabled { get; set; } = true;
        public IList<IFieldSource> FieldSources { get; } = new List<IFieldSource>();

        /// <summary>
        /// Option to enable or disable usage of default tenant when using multi-tenanted documents
        /// </summary>
        public bool DefaultTenantUsageEnabled { get; set; } = true;

        private ImHashMap<Type, IFieldMapping> _childFieldMappings = ImHashMap<Type, IFieldMapping>.Empty;

        /// <summary>
        /// These mappings should only be used for Linq querying within the SelectMany() body
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        internal IFieldMapping ChildTypeMappingFor(Type type)
        {
            if (_childFieldMappings.TryFind(type, out var mapping))
            {
                return mapping;
            }

            mapping = new FieldMapping("d.data", type, this);

            _childFieldMappings = _childFieldMappings.AddOrUpdate(type, mapping);

            return mapping;

        }

        public class PoliciesExpression
        {
            private readonly StoreOptions _parent;

            public PoliciesExpression(StoreOptions parent)
            {
                _parent = parent;
            }

            /// <summary>
            /// Add a pre-built Marten document policy
            /// </summary>
            /// <typeparam name="T"></typeparam>
            /// <returns></returns>
            public PoliciesExpression OnDocuments<T>() where T : IDocumentPolicy, new()
            {
                return OnDocuments(new T());
            }

            /// <summary>
            /// Add a pre-built Marten document policy
            /// </summary>
            /// <param name="policy"></param>
            /// <returns></returns>
            public PoliciesExpression OnDocuments(IDocumentPolicy policy)
            {
                _parent._policies.Insert(0, policy);
                return this;
            }

            /// <summary>
            /// Apply configuration to the persistence of all Marten document
            /// types
            /// </summary>
            /// <param name="configure"></param>
            /// <returns></returns>
            public PoliciesExpression ForAllDocuments(Action<DocumentMapping> configure)
            {
                return OnDocuments(new LambdaDocumentPolicy(configure));
            }

            /// <summary>
            /// Unless explicitly marked otherwise, all documents should
            /// use conjoined multi-tenancy
            /// </summary>
            /// <returns></returns>
            public PoliciesExpression AllDocumentsAreMultiTenanted()
            {
                return ForAllDocuments(_ => _.TenancyStyle = TenancyStyle.Conjoined);
            }

            /// <summary>
            /// Turn off the informational metadata columns
            /// in storage like the last modified, version, and
            /// dot net type for leaner storage
            /// </summary>
            public PoliciesExpression DisableInformationalFields()
            {
                return ForAllDocuments(x =>
                {
                    x.Metadata.LastModified.Enabled = false;
                    x.Metadata.DotNetType.Enabled = false;
                    x.Metadata.Version.Enabled = false;
                });
            }
        }

        private ImHashMap<Type, ICompiledQuerySource> _querySources = ImHashMap<Type, ICompiledQuerySource>.Empty;

        internal ICompiledQuerySource GetCompiledQuerySourceFor<TDoc, TOut>(ICompiledQuery<TDoc,TOut> query, IMartenSession session)
        {
            if (_querySources.TryFind(query.GetType(), out var source))
            {
                return source;
            }

            if (typeof(TOut).CanBeCastTo<Task>())
            {
                throw InvalidCompiledQueryException.ForCannotBeAsync(query.GetType());
            }

            var plan = QueryCompiler.BuildPlan(session, query, this);
            source = new CompiledQuerySourceBuilder(plan, this).Build();
            _querySources = _querySources.AddOrUpdate(query.GetType(), source);

            return source;
        }
    }

    public interface IDocumentPolicy
    {
        void Apply(DocumentMapping mapping);
    }

    internal class LambdaDocumentPolicy: IDocumentPolicy
    {
        private readonly Action<DocumentMapping> _modify;

        public LambdaDocumentPolicy(Action<DocumentMapping> modify)
        {
            _modify = modify;
        }

        public void Apply(DocumentMapping mapping)
        {
            _modify(mapping);
        }
    }
}
