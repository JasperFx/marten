#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq;
using Marten.Metadata;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Services;
using Marten.Services.Json;
using Marten.Storage;
using Marten.Util;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Npgsql;
using Polly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Connections;

namespace Marten;

/// <summary>
///     StoreOptions supplies all the necessary configuration
///     necessary to customize and bootstrap a working
///     DocumentStore
/// </summary>
public partial class StoreOptions: IReadOnlyStoreOptions, IMigrationLogger, IDocumentSchemaResolver
{
    public const int DefaultTimeout = 5;

    internal static readonly Func<string, NpgsqlDataSourceBuilder> DefaultNpgsqlDataSourceBuilderFactory =
        connectionString => new NpgsqlDataSourceBuilder(connectionString);

    internal Func<string, NpgsqlDataSourceBuilder> NpgsqlDataSourceBuilderFactory
    {
        get => _npgsqlDataSourceBuilderFactory;
        private set => _npgsqlDataSourceBuilderFactory = value;
    }

    internal INpgsqlDataSourceFactory NpgsqlDataSourceFactory
    {
        get => _npgsqlDataSourceFactory;
        private set => _npgsqlDataSourceFactory = value;
    }

    internal readonly List<Action<ISerializer>> SerializationConfigurations = new();

    private readonly IList<IDocumentPolicy> _policies = new List<IDocumentPolicy>
    {
        new VersionedPolicy(), new SoftDeletedPolicy(), new TrackedPolicy(), new TenancyPolicy(), new ProjectionDocumentPolicy()
    };

    /// <summary>
    ///     Register "initial data loads" that will be applied to the DocumentStore when it is
    ///     bootstrapped
    /// </summary>
    internal readonly IList<IInitialData> InitialData = new List<IInitialData>();

    /// <summary>
    ///     Add, remove, or reorder global session listeners
    /// </summary>
    public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

    /// <summary>
    /// Used to enable or disable Marten's OpenTelemetry features for just this session.
    /// </summary>
    public OpenTelemetryOptions OpenTelemetry { get; } = new();

    /// <summary>
    ///     Modify the document and event store database mappings for indexes and searching options
    /// </summary>
    public readonly MartenRegistry Schema;

    private string _databaseSchemaName = SchemaConstants.DefaultSchema;

    private IMartenLogger _logger = new NulloMartenLogger();

    private ISerializer? _serializer;

    /// <summary>
    ///     Whether or Marten should attempt to create any missing database schema objects at runtime. This
    ///     property is "CreateOrUpdate" by default for more efficient development, but can be set to lower values for production usage.
    /// </summary>
    public AutoCreate AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    public StoreOptions()
    {
        _eventGraph = new EventGraph(this);
        Schema = new MartenRegistry(this);
        _storage = new StorageFeatures(this);

        _providers = new ProviderGraph(this);
        _advanced = new AdvancedOptions(this);

        _projections = new ProjectionOptions(this);

        _linq = new LinqParsing(this);

        // Default Polly setup
        ResiliencePipeline = new ResiliencePipelineBuilder().AddMartenDefaults().Build();

        // Add logging into our NpgsqlDataSource
        NpgsqlDataSourceFactory = new DefaultNpgsqlDataSourceFactory(connectionString =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            if (LogFactory != null && !DisableNpgsqlLogging)
            {
                builder.UseLoggerFactory(LogFactory);
            }

            return builder;
        });
    }

    /// <summary>
    /// Npgsql logging is absurdly noisy, you may want to disable the logging. Default is false
    /// </summary>
    public bool DisableNpgsqlLogging { get; set; }

    /// <summary>
    /// Configure and override the Polly error handling policies for this DocumentStore
    /// </summary>
    /// <param name="configure"></param>
    public void ConfigurePolly(Action<ResiliencePipelineBuilder> configure)
    {
        var builder = new ResiliencePipelineBuilder();
        configure(builder);

        ResiliencePipeline = builder.Build();
    }

    /// <summary>
    /// Extend default error handling policies for this DocumentStore.
    /// Any user supplied policies will take precedence over the default policies.
    /// </summary>
    public void ExtendPolly(Action<ResiliencePipelineBuilder> configure)
    {
        var builder = new ResiliencePipelineBuilder();
        configure(builder);

        ResiliencePipeline = builder.AddMartenDefaults().Build();
    }

    /// <summary>
    /// Direct Marten to use the <= V6 behavior of keeping a connection open
    /// in an IQuerySession or IDocumentSession upon first usage until the session
    /// is disposed. In V7 and later, the default behavior is an aggressive "use and close"
    /// policy that tries to close and released used database connections as soon as possible
    /// Default is false
    /// </summary>
    public bool UseStickyConnectionLifetimes { get; set; } = false;

    internal IList<Type> CompiledQueryTypes => _compiledQueryTypes;

    /// <summary>
    /// Polly policies for retries within Marten command execution
    /// </summary>
    internal ResiliencePipeline ResiliencePipeline { get; set; }

    /// <summary>
    ///     Advisory lock id is used by the ApplyChangesOnStartup() option to serialize access to making
    ///     schema changes from multiple application nodes
    /// </summary>
    public int ApplyChangesLockId
    {
        get => _applyChangesLockId;
        set => _applyChangesLockId = value;
    }

    /// <summary>
    ///     Used internally by the MartenActivator
    /// </summary>
    internal bool ShouldApplyChangesOnStartup
    {
        get => _shouldApplyChangesOnStartup;
        set => _shouldApplyChangesOnStartup = value;
    }

    /// <summary>
    ///     Used internally by the MartenActivator
    /// </summary>
    internal bool ShouldAssertDatabaseMatchesConfigurationOnStartup
    {
        get => _shouldAssertDatabaseMatchesConfigurationOnStartup;
        set => _shouldAssertDatabaseMatchesConfigurationOnStartup = value;
    }

    /// <summary>
    ///     Configuration for all event store projections
    /// </summary>
    public ProjectionOptions Projections => _projections;

    /// <summary>
    ///     Direct Marten to either generate code at runtime (Dynamic), or attempt to load types from the entry assembly
    /// </summary>
    public TypeLoadMode GeneratedCodeMode
    {
        get => _generatedCodeMode;
        set => _generatedCodeMode = value;
    }

    /// <summary>
    ///     Access to adding custom schema features to this Marten-enabled Postgresql database
    /// </summary>
    public StorageFeatures Storage => _storage;

    internal Action<IDatabaseCreationExpressions>? CreateDatabases
    {
        get => _createDatabases;
        set => _createDatabases = value;
    }

    internal IProviderGraph Providers => _providers;

    /// <summary>
    ///     Advanced configuration options for this DocumentStore
    /// </summary>
    public AdvancedOptions Advanced => _advanced;

    internal EventGraph EventGraph => _eventGraph;

    /// <summary>
    ///     Configuration of event streams and projections
    /// </summary>
    public IEventStoreOptions Events => EventGraph;

    /// <summary>
    ///     Extension point to add custom Linq query parsers
    /// </summary>
    public LinqParsing Linq => _linq;

    /// <summary>
    ///     Apply conventional policies to how documents are mapped
    /// </summary>
    public PoliciesExpression Policies => new(this);


    void IMigrationLogger.SchemaChange(string sql)
    {
        Logger().SchemaChange(sql);
    }

    void IMigrationLogger.OnFailure(DbCommand command, Exception ex)
    {
        throw new MartenSchemaException("All Configured Changes", command.CommandText, ex);
    }

    IReadOnlyAdvancedOptions IReadOnlyStoreOptions.Advanced => Advanced;

    /// <summary>
    ///     Sets the database default schema name used to store the documents.
    /// </summary>
    public string DatabaseSchemaName
    {
        get => _databaseSchemaName;
        set => _databaseSchemaName = value.ToLowerInvariant();
    }

    /// <summary>
    ///     Used to validate database object name lengths against Postgresql's NAMEDATALEN property to avoid
    ///     Marten getting confused when comparing database schemas against the configuration. See
    ///     https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html
    ///     for more information. This does NOT adjust NAMEDATALEN for you.
    /// </summary>
    public int NameDataLength
    {
        get => Advanced.Migrator.NameDataLength;
        set => Advanced.Migrator.NameDataLength = value;
    }

    /// <summary>
    ///     Gets Enum values stored as either integers or strings. This is configured on your ISerializer
    /// </summary>
    public EnumStorage EnumStorage => Serializer().EnumStorage;


    /// <summary>
    ///     Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() /
    ///     IUnitOfWork.ApplyChanges()
    /// </summary>
    public int UpdateBatchSize
    {
        get => _updateBatchSize;
        set => _updateBatchSize = value;
    }

    /// <summary>
    ///     Retrieve the currently configured serializer
    /// </summary>
    /// <returns></returns>
    public ISerializer Serializer()
    {
        if (_serializer == null)
        {
            _serializer = SerializerFactory.New();
            foreach (var configure in SerializationConfigurations)
            {
                configure(_serializer);
            }
        }

        return _serializer;
    }

    /// <summary>
    ///     Retrieve the currently configured logger for this DocumentStore
    /// </summary>
    /// <returns></returns>
    public IMartenLogger Logger()
    {
        return _logger ?? new NulloMartenLogger();
    }

    IReadOnlyList<IDocumentType> IReadOnlyStoreOptions.AllKnownDocumentTypes()
    {
        return Storage.AllDocumentMappings.OfType<IDocumentType>().ToList();
    }


    IDocumentType IReadOnlyStoreOptions.FindOrResolveDocumentType(Type documentType)
    {
        return (Storage.FindMapping(documentType).Root as IDocumentType)!;
    }

    void IReadOnlyStoreOptions.AssertDocumentTypeIsSoftDeleted(Type documentType)
    {
        var mapping = Storage.FindMapping(documentType) as IDocumentMapping;
        if (mapping is null || mapping.DeleteStyle == DeleteStyle.Remove)
        {
            throw new InvalidOperationException(
                $"Document type {documentType.FullNameInCode()} is not configured as soft deleted");
        }
    }

    /// <summary>
    ///     Get or set the tenancy model for this DocumentStore
    /// </summary>
    public ITenancy Tenancy
    {
        get => (_tenancy ?? throw new InvalidOperationException(
                "No tenancy is configured! Ensure that you provided connection string in `AddMarten` method or called `UseNpgsqlDataSource`"))
            .Value;
        set => _tenancy = new Lazy<ITenancy>(() => value);
    }

    private Lazy<ITenancy>? _tenancy;
    private Func<string, NpgsqlDataSourceBuilder> _npgsqlDataSourceBuilderFactory = DefaultNpgsqlDataSourceBuilderFactory;
    private INpgsqlDataSourceFactory _npgsqlDataSourceFactory;
    private readonly IList<Type> _compiledQueryTypes = new List<Type>();
    private int _applyChangesLockId = 4004;
    private bool _shouldApplyChangesOnStartup = false;
    private bool _shouldAssertDatabaseMatchesConfigurationOnStartup = false;
    private readonly ProjectionOptions _projections;
    private TypeLoadMode _generatedCodeMode = TypeLoadMode.Dynamic;
    private readonly StorageFeatures _storage;
    private Action<IDatabaseCreationExpressions>? _createDatabases;
    private readonly IProviderGraph _providers;
    private readonly AdvancedOptions _advanced;
    private readonly EventGraph _eventGraph;
    private readonly LinqParsing _linq;
    private int _updateBatchSize = 500;

    IReadOnlyEventStoreOptions IReadOnlyStoreOptions.Events => EventGraph;

    IReadOnlyLinqParsing IReadOnlyStoreOptions.Linq => Linq;

    public int CommandTimeout { get; set; } = DefaultTimeout;

    // This is used to move logging into the >v7 async daemon
    internal ILoggerFactory? LogFactory { get; set; }

    // This is used mostly for testing to provide *some* sort of logging
    // within the async daemon
    internal ILogger? DotNetLogger { get; set; }

    /// <summary>
    ///     Configure Marten to create databases for tenants in case databases do not exist or need to be dropped & re-created.
    /// You will need to also use the ApplyAllDatabaseChangesOnStartup() option when configuring Marten to make this function correctly
    /// </summary>
    /// <remarks>Creating and dropping databases requires the CREATEDB privilege</remarks>
    public void CreateDatabasesForTenants(Action<IDatabaseCreationExpressions> configure)
    {
        CreateDatabases = configure ?? throw new ArgumentNullException(nameof(configure));
    }

    /// <summary>
    ///     Sets custom `NpgsqlDataSource` factory to manage database connections
    /// </summary>
    /// <param name="dataSourceFactory"></param>
    /// <param name="connectionString"></param>
    public void DataSourceFactory(INpgsqlDataSourceFactory dataSourceFactory, string? connectionString = null)
    {
        NpgsqlDataSourceFactory = dataSourceFactory;

        if (_tenancy == null && connectionString != null)
            Connection(connectionString);
    }

    /// <summary>
    ///     Supply the connection string to the Postgresql database
    /// </summary>
    /// <param name="connectionString"></param>
    public void Connection(string connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);

            if (builder.CommandTimeout > 0) CommandTimeout = builder.CommandTimeout;
        }
        catch (Exception)
        {
            // Just swallow this one
        }

        _tenancy = new Lazy<ITenancy>(() =>
            new DefaultTenancy(NpgsqlDataSourceFactory.Create(connectionString), this));
    }

    /// <summary>
    ///     Supply a source for the connection string to a Postgresql database
    /// </summary>
    /// <param name="connectionSource"></param>
    [Obsolete("Use version with connection string. This will be removed in Marten 8")]
    public void Connection(Func<string> connectionSource)
    {
        throw new NotSupportedException(
            "Sorry, but this feature is no longer supported. Please use the overload that uses NpgsqlDataSource instead for similar functionality");
    }

    /// <summary>
    ///     Supply a mechanism for resolving an NpgsqlConnection object to
    ///     the Postgresql database
    /// </summary>
    /// <param name="source"></param>
    [Obsolete("Use one of the overloads that takes a connection string, an NpgsqlDataSource, or an INpgsqlDataSourceFactory. This will be removed in Marten 8")]
    public void Connection(Func<NpgsqlConnection> source)
    {
        throw new NotSupportedException(
            "Use one of the overloads that takes a connection string, an NpgsqlDataSource, or an INpgsqlDataSourceFactory");
    }


    /// <summary>
    ///     Supply a mechanism for resolving an NpgsqlConnection object based on the NpgsqlDataSource
    /// </summary>
    /// <remarks>
    ///     When doing that you need to handle data source disposal.
    /// </remarks>
    /// <param name="dataSource"></param>
    public void Connection(NpgsqlDataSource dataSource) =>
        DataSourceFactory(
            new SingleNpgsqlDataSourceFactory(
                NpgsqlDataSourceBuilderFactory,
                dataSource
            ),
            dataSource.ConnectionString
        );

    /// <summary>
    ///     Supply a mechanism for resolving an NpgsqlConnection object based on the NpgsqlDataSource
    /// </summary>
    /// <remarks>
    ///     When doing that you need to handle data source disposal.
    /// </remarks>
    /// <param name="dataSourceBuilderFactory"></param>
    /// <param name="dataSource"></param>
    public void Connection(
        Func<string, NpgsqlDataSourceBuilder> dataSourceBuilderFactory,
        NpgsqlDataSource dataSource
    )
    {
        NpgsqlDataSourceBuilderFactory = dataSourceBuilderFactory;

        Connection(dataSource);
    }

    /// <summary>
    ///     Override the JSON serialization by ISerializer type
    /// </summary>
    /// <param name="serializer"></param>
    public void Serializer(ISerializer serializer)
    {
        _serializer = serializer;

        // Reapply any serialization addons
        foreach (var configure in SerializationConfigurations)
        {
            configure(_serializer);
        }
    }

    /// <summary>
    ///     Configure the default serializer settings
    /// </summary>
    /// <param name="enumStorage"></param>
    /// <param name="casing">Casing style to be used in serialization</param>
    /// <param name="collectionStorage">Allow to set collection storage as raw arrays (without explicit types)</param>
    /// <param name="nonPublicMembersStorage">Allow non public members to be used during deserialization</param>
    [Obsolete("Prefer UseNewtonsoftForSerialization or UseSystemTextJsonForSerialization to configure JSON options")]
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
    ///     Configure the Newtonsoft serializer settings
    /// </summary>
    /// <param name="enumStorage">Enum storage style</param>
    /// <param name="casing">Casing style to be used in serialization</param>
    /// <param name="collectionStorage">Allow to set collection storage as raw arrays (without explicit types)</param>
    /// <param name="nonPublicMembersStorage">Allow non public members to be used during deserialization</param>
    public void UseNewtonsoftForSerialization(
        EnumStorage enumStorage = EnumStorage.AsInteger,
        Casing casing = Casing.Default,
        CollectionStorage collectionStorage = CollectionStorage.Default,
        NonPublicMembersStorage nonPublicMembersStorage = NonPublicMembersStorage.Default,
        Action<JsonSerializerSettings>? configure = null)
    {
        var serializer = new JsonNetSerializer
        {
            EnumStorage = enumStorage,
            Casing = casing,
            CollectionStorage = collectionStorage,
            NonPublicMembersStorage = nonPublicMembersStorage
        };

        if (configure is not null)
            serializer.Configure(configure);

        Serializer(serializer);
    }

    /// <summary>
    ///     Configure the System.Text.Json serializer settings
    /// </summary>
    /// <param name="enumStorage">Enum storage style</param>
    /// <param name="casing">Casing style to be used in serialization</param>
    public void UseSystemTextJsonForSerialization(
        EnumStorage enumStorage = EnumStorage.AsInteger,
        Casing casing = Casing.Default,
        Action<JsonSerializerOptions>? configure = null)
    {
        var serializer = new SystemTextJsonSerializer() { EnumStorage = enumStorage, Casing = casing, };

        if(configure is not null)
            serializer.Configure(configure);

        Serializer(serializer);
    }

    /// <summary>
    ///     Override the JSON serialization by an ISerializer of type "T"
    /// </summary>
    /// <typeparam name="T">The ISerializer type</typeparam>
    public void Serializer<T>() where T : ISerializer, new()
    {
        _serializer = new T();
        foreach (var configuration in SerializationConfigurations)
        {
            configuration(_serializer);
        }
    }

    /// <summary>
    ///     Replace the Marten logging strategy
    /// </summary>
    /// <param name="logger"></param>
    public void Logger(IMartenLogger logger)
    {
        _logger = logger;
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

    /// <summary>
    ///     Register a compiled query type for the "generate ahead" code generation strategy
    /// </summary>
    /// <param name="queryType"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void RegisterCompiledQueryType(Type queryType)
    {
        if (!queryType.Closes(typeof(ICompiledQuery<,>)))
        {
            throw new ArgumentOutOfRangeException(nameof(queryType),
                $"{queryType.FullNameInCode()} is not a valid Marten compiled query type");
        }

        if (!queryType.HasDefaultConstructor())
        {
            throw new ArgumentOutOfRangeException(nameof(queryType),
                "Sorry, but Marten requires a no-arg constructor on compiled query types in order to opt into the 'code ahead' generation model.");
        }

        CompiledQueryTypes.Fill(queryType);
    }

    internal void ApplyConfiguration()
    {
        Storage.BuildAllMappings();

        Schema.For<DeadLetterEvent>().DatabaseSchemaName(Events.DatabaseSchemaName).SingleTenanted();

        foreach (var mapping in Storage.AllDocumentMappings) mapping.CompileAndValidate();
    }

    internal void applyPolicies(DocumentMapping mapping)
    {
        foreach (var policy in _policies) policy.Apply(mapping);
    }

    /// <summary>
    ///     Validate that minimal options to initialize a document store have been specified
    /// </summary>
    internal void Validate()
    {
        if (Tenancy == null)
        {
            throw new InvalidOperationException(
                "Tenancy not specified - provide either connection string or connection factory through Connection(..)");
        }
    }

    /// <summary>
    ///     Meant for testing scenarios to "help" .Net understand where the IHostEnvironment for the
    ///     Host. You may have to specify the relative path to the entry project folder from the AppContext.BaseDirectory
    /// </summary>
    /// <param name="services"></param>
    /// <param name="assembly"></param>
    /// <param name="hintPath"></param>
    /// <returns></returns>
    public void SetApplicationProject(Assembly assembly,
        string? hintPath = null)
    {
        ApplicationAssembly = assembly ?? throw new ArgumentNullException(nameof(assembly));

        // TODO -- pull this into LamarCodeGeneration itself.
        var path = AppContext.BaseDirectory.ToFullPath();
        if (hintPath.IsNotEmpty())
        {
            path = path.AppendPath(hintPath).ToFullPath();
        }
        else
        {
            try
            {
                path = path.TrimEnd(Path.DirectorySeparatorChar);
                while (!path.EndsWith("bin"))
                {
                    path = path.ParentDirectory();
                }

                // Go up once to get to the test project directory, then up again to the "src" level,
                // then "down" to the application directory
                path = path.ParentDirectory().ParentDirectory().AppendPath(assembly.GetName().Name);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to determine the ");
                Console.WriteLine(e);
                path = AppContext.BaseDirectory.ToFullPath();
            }
        }

        GeneratedCodeOutputPath = path.AppendPath("Internal", "Generated");
    }

    /// <summary>
    ///     Opt into a multi-tenancy per database strategy where all databases
    ///     are on the same Postgresql server instance
    /// </summary>
    /// <param name="masterConnectionString"></param>
    /// <returns></returns>
    public void MultiTenantedWithSingleServer(
        string masterConnectionString,
        Action<ISingleServerMultiTenancy>? configure = null
    )
    {
        Advanced.DefaultTenantUsageEnabled = false;

        _tenancy = new Lazy<ITenancy>(() =>
        {
            var tenancy =
                new SingleServerMultiTenancy(
                    NpgsqlDataSourceFactory,
                    masterConnectionString,
                    this
                );

            configure?.Invoke(tenancy);

            return tenancy;
        });
    }

    /// <summary>
    ///     Opt into multi-tenancy per database strategy where all the
    ///     databases and tenants have to be statically configured at
    ///     bootstrapping time
    /// </summary>
    /// <param name="configure"></param>
    public void MultiTenantedDatabases(Action<IStaticMultiTenancy> configure)
    {
        Advanced.DefaultTenantUsageEnabled = false;

        _tenancy = new Lazy<ITenancy>(() =>
        {
            var tenancy = new StaticMultiTenancy(NpgsqlDataSourceFactory, this);

            configure(tenancy);

            return tenancy;
        });
    }

    public class PoliciesExpression
    {
        private readonly StoreOptions _parent;

        public PoliciesExpression(StoreOptions parent)
        {
            _parent = parent;
        }

        /// <summary>
        ///     Add a pre-built Marten document policy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public PoliciesExpression OnDocuments<T>() where T : IDocumentPolicy, new()
        {
            return OnDocuments(new T());
        }

        /// <summary>
        ///     Add a pre-built Marten document policy
        /// </summary>
        /// <param name="policy"></param>
        /// <returns></returns>
        public PoliciesExpression OnDocuments(IDocumentPolicy policy)
        {
            _parent._policies.Insert(0, policy);
            return this;
        }

        /// <summary>
        ///     Apply configuration to the persistence of all Marten document
        ///     types
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public PoliciesExpression ForAllDocuments(Action<DocumentMapping> configure)
        {
            return OnDocuments(new LambdaDocumentPolicy(configure));
        }

        /// <summary>
        ///     Unless explicitly marked otherwise, all documents should
        ///     use conjoined multi-tenancy
        /// </summary>
        /// <returns></returns>
        public PoliciesExpression AllDocumentsAreMultiTenanted()
        {
            return ForAllDocuments(_ => _.TenancyStyle = TenancyStyle.Conjoined);
        }

        /// <summary>
        ///     Unless explicitly marked otherwise, all documents should
        ///     be soft-deleted
        /// </summary>
        /// <returns></returns>
        public PoliciesExpression AllDocumentsSoftDeleted()
        {
            return ForAllDocuments(_ => _.DeleteStyle = DeleteStyle.SoftDelete);
        }

        /// <summary>
        ///     Turn off the informational metadata columns
        ///     in storage like the last modified, version, and
        ///     dot net type for leaner storage
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

        /// <summary>
        ///     All document types will have optimistic concurrency checks
        /// </summary>
        /// <returns></returns>
        public PoliciesExpression AllDocumentsEnforceOptimisticConcurrency()
        {
            return ForAllDocuments(x =>
            {
                x.UseOptimisticConcurrency = true;
            });
        }
    }

    /// <summary>
    /// Multi-tenancy strategy where the tenant database connection strings are defined in a table
    /// named "mt_tenant_databases"
    /// </summary>
    /// <param name="connectionString">A connection string to the database that will hold the tenant database lookup table </param>
    /// <param name="schemaName">If specified, override the schema name where the tenant database lookup table wil be</param>
    public void MultiTenantedDatabasesWithMasterDatabaseTable(string connectionString, string? schemaName = "public")
    {
        var tenancy = new MasterTableTenancy(this, connectionString, schemaName);
        Advanced.DefaultTenantUsageEnabled = false;
        Tenancy = tenancy;
    }

    /// <summary>
    /// Multi-tenancy strategy where the tenant database connection strings are defined in a table
    /// named "mt_tenant_databases"
    /// </summary>
    /// <param name="configure"></param>
    public void MultiTenantedDatabasesWithMasterDatabaseTable(Action<MasterTableTenancyOptions> configure)
    {
        var configuration = new MasterTableTenancyOptions();
        configure(configuration);

        if (configuration.ConnectionString.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(nameof(configure),
                $"{nameof(MasterTableTenancyOptions.ConnectionString)} must be supplied");
        }

        var tenancy = new MasterTableTenancy(this, configuration);
        Advanced.DefaultTenantUsageEnabled = false;
        Tenancy = tenancy;
    }

    IDocumentSchemaResolver IReadOnlyStoreOptions.Schema => this;

    string IDocumentSchemaResolver.EventsSchemaName => Events.DatabaseSchemaName;

    string IDocumentSchemaResolver.For<TDocument>(bool qualified)
    {
        var docType = ((IReadOnlyStoreOptions)this).FindOrResolveDocumentType(typeof(TDocument));
        return qualified ? docType.TableName.QualifiedName : docType.TableName.Name;
    }

    public string For(Type documentType, bool qualified = true)
    {
        var docType = ((IReadOnlyStoreOptions)this).FindOrResolveDocumentType(documentType);
        return qualified ? docType.TableName.QualifiedName : docType.TableName.Name;
    }

    string IDocumentSchemaResolver.ForEvents(bool qualified)
    {
        return qualified ? _eventGraph.Table.QualifiedName : _eventGraph.Table.Name;
    }

    string IDocumentSchemaResolver.ForStreams(bool qualified)
    {
        return qualified ? _eventGraph.StreamsTable.QualifiedName : _eventGraph.StreamsTable.Name;
    }

    string IDocumentSchemaResolver.ForEventProgression(bool qualified)
    {
        return qualified ? _eventGraph.ProgressionTable.QualifiedName : _eventGraph.ProgressionTable.Name;
    }
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

public interface IReadOnlyAdvancedOptions
{
    /// <summary>
    ///     Sets Enum values stored as either integers or strings for DuplicatedField.
    /// </summary>
    EnumStorage DuplicatedFieldEnumStorage { get; }

    /// <summary>
    ///     Global default parameters for Hilo sequences within the DocumentStore. Can be overridden per document
    ///     type as well
    /// </summary>
    IReadOnlyHiloSettings HiloSequenceDefaults { get; }

    /// <summary>
    ///     Option to enable or disable usage of default tenant when using multi-tenanted documents
    /// </summary>
    bool DefaultTenantUsageEnabled { get; }
}

public class AdvancedOptions: IReadOnlyAdvancedOptions
{
    private readonly StoreOptions _storeOptions;
    private EnumStorage? _duplicatedFieldEnumStorage;

    internal AdvancedOptions(StoreOptions storeOptions)
    {
        _storeOptions = storeOptions;

        // Making the DDL generation be transactional can cause runtime errors.
        // Make the user opt into this
        Migrator.IsTransactional = false;
    }

    /// <summary>
    /// Register configurations to the ISerializer that will be applied at the last
    /// second to the application's serializer settings. This was meant for Marten
    /// add ons
    /// </summary>
    /// <param name="configure"></param>
    public void ModifySerializer(Action<ISerializer> configure)
    {
        // Apply it immediately...
        configure(_storeOptions.Serializer());

        _storeOptions.SerializationConfigurations.Add(configure);
    }


    /// <summary>
    ///     Global default parameters for Hilo sequences within the DocumentStore. Can be overridden per document
    ///     type as well
    /// </summary>
    public HiloSettings HiloSequenceDefaults { get; } = new();


    /// <summary>
    ///     Allows you to modify how the DDL for document tables and upsert functions is
    ///     written
    /// </summary>
    public PostgresqlMigrator Migrator { get; } = new();

    /// <summary>
    ///     Decides if `timestamp without time zone` database type should be used for `DateTime` DuplicatedField.
    /// </summary>
    public bool DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime { get; set; } = true;

    /// <summary>
    ///     Sets Enum values stored as either integers or strings for DuplicatedField.
    /// </summary>
    public EnumStorage DuplicatedFieldEnumStorage
    {
        get => _duplicatedFieldEnumStorage ?? _storeOptions.EnumStorage;
        set => _duplicatedFieldEnumStorage = value;
    }

    IReadOnlyHiloSettings IReadOnlyAdvancedOptions.HiloSequenceDefaults => HiloSequenceDefaults;


    /// <summary>
    ///     Option to enable or disable usage of default tenant when using multi-tenanted documents
    /// </summary>
    public bool DefaultTenantUsageEnabled { get; set; } = true;
}
