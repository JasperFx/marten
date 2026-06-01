using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Blocks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Descriptors;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using JasperFx.Events.Tags;
using Marten.Events.Aggregation;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Schema;
using Marten.Services.Json.Transformations;
using Marten.Storage;
using Marten.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using static JasperFx.Events.EventTypeExtensions;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Events;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Class-level: consumes RUC-annotated members (ISerializer, JasperFx.Events aggregator graph, CloseAndBuildAs / GenericFactoryCache fallbacks, FastExpressionCompiler). Document/event/projection types flow in from StoreOptions / Schema.For<T>() / projection registration and are preserved per the AOT publishing guide; AOT consumers supply a source-generator-backed serializer + pre-generated codegen artifacts.")]
[UnconditionalSuppressMessage("Trimming", "IL2057",
    Justification = "Class-level: Type.GetType(string) fallback for resolving aggregate / document types by .NET type name. Types preserved by registration on the caller side per the AOT publishing guide.")]
[UnconditionalSuppressMessage("Trimming", "IL2060",
    Justification = "Class-level: Expression.Call(Type, string, ...) on framework Queryable / Enumerable intrinsics that the trimmer preserves.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public partial class EventGraph: EventRegistry, IEventStoreOptions, IReadOnlyEventStoreOptions,
    IDisposable, IAsyncDisposable,
    IAggregationSourceFactory<IQuerySession>, IDescribeMyself
{
    private readonly Cache<Type, string> _aggregateNameByType =
        new(type => type.IsGenericType ? type.ShortNameInCode() : type.Name.ToTableAlias());

    private readonly Cache<string, Type> _aggregateTypeByName;

    private readonly Cache<string, EventMapping?> _byEventName = new();
    private readonly CancellationTokenSource _cancellation = new();

    private readonly Cache<Type, EventMapping> _events = new();

    private readonly Lazy<IInlineProjection<IDocumentOperations>[]> _inlineProjections;

    private readonly Ref<ImHashMap<string, Type>> _nameToType = Ref.Of(ImHashMap<string, Type>.Empty);

    private string? _databaseSchemaName;

    private bool _isDisposed;

    private DocumentStore _store;

    // The owning store, set in Initialize(store). Lets database-level operations
    // (e.g. MartenDatabase's dead-letter count reads, #4546) open a session via
    // SessionOptions.ForDatabase(...) to query Marten documents with LINQ.
    internal DocumentStore Store => _store;

    private readonly List<ITagTypeRegistration> _tagTypes = new();

    internal EventGraph(StoreOptions options)
    {
        StreamIdentity = StreamIdentity.AsGuid;
        Options = options;
        _events.OnMissing = eventType =>
        {
            var mapping = typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);
            Options.Storage.AddMapping(mapping);

            return mapping;
        };

        _byEventName.OnMissing = name => AllEvents().FirstOrDefault(x => x.EventTypeName == name);

        _inlineProjections =
            new Lazy<IInlineProjection<IDocumentOperations>[]>(() =>
                options.Projections.BuildInlineProjections(_store));

        _aggregateTypeByName = new Cache<string, Type>(findAggregateType);

        AddEventType<Archived>();

        // 9.0 (#default-flips): apply V9 default flips for new StoreOptions instances.
        // Callers wanting V8 semantics call StoreOptions.RestoreV8Defaults() to revert
        // these. Anything not listed here kept its V8 default. See docs/migration-guide.md
        // for the per-setting rationale + the consolidated RestoreV8Defaults recipe.
        AppendMode = EventAppendMode.QuickWithServerTimestamps;
        UseIdentityMapForAggregates = true;
        EnableBigIntEvents = true;
        EnableAdvancedAsyncTracking = true;
    }

    /// <summary>
    /// Opt into different aliasing styles for .NET event types
    /// </summary>
    public EventNamingStyle EventNamingStyle { get; set; } = EventNamingStyle.ClassicTypeName;

    internal NpgsqlDbType StreamIdDbType { get; private set; }

    internal StoreOptions Options { get; }

    internal DbObjectName Table => new PostgresqlObjectName(DatabaseSchemaName, "mt_events");

    internal EventMetadataCollection Metadata { get; } = new();

    public IAggregatorSource<IQuerySession>? Build<TDoc>()
    {
        var idType = Options.Storage.MappingFor(typeof(TDoc)).IdType;

        // For the quite legitimate case of doing a live aggregation when
        // there is no Id member
        if (idType == null)
        {
            // [BoundaryAggregate] SG emits TId=string (see jasperfx#324); match
            // it here so the dispatcher lookup hits regardless of StreamIdentity.
            if (typeof(TDoc).IsDefined(typeof(BoundaryAggregateAttribute), inherit: false))
            {
                idType = typeof(string);
            }
            else if (StreamIdentity == StreamIdentity.AsGuid)
            {
                idType = typeof(Guid);
            }
            else
            {
                idType = typeof(string);
            }
        }

        return typeof(SingleStreamProjection<,>)
            .CloseAndBuildAs<IAggregatorSource<IQuerySession>>(typeof(TDoc), idType);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_tombstones != null)
            {
                await _tombstones.DrainAsync().ConfigureAwait(false);
            }
        }
        catch (TaskCanceledException)
        {
            // Ignore this
        }
        catch (OperationCanceledException)
        {
            // Nothing, get out of here
        }

        Dispose();
    }

    OptionsDescription IDescribeMyself.ToDescription()
    {
        var description = new OptionsDescription(this);

        var set = description.AddChildSet("Events", _events);
        set.SummaryColumns = [nameof(EventMapping.EventType), nameof(EventMapping.EventTypeName)];

        return description;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _cancellation.Cancel();
        _cancellation.Dispose();
        _tombstones?.SafeDispose();
    }

    public override Type AggregateTypeFor(string aggregateTypeName)
    {
        return _aggregateTypeByName[aggregateTypeName];
    }

    public override string AggregateAliasFor(Type aggregateType)
    {
        var alias = _aggregateNameByType[aggregateType];

        _aggregateTypeByName.Fill(alias, aggregateType);

        return alias;
    }

    public override IEvent BuildEvent(object eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData is IEvent e)
        {
            var mapping = EventMappingFor(e.EventType);
            e.EventTypeName = mapping.EventTypeName;
            e.DotNetTypeName = mapping.DotNetTypeName;
            return e;
        }
        else
        {
            var mapping = EventMappingFor(eventData.GetType());
            return mapping.Wrap(eventData);
        }


    }

    public bool UseOptimizedProjectionRebuilds { get; set; }
    public bool UseMandatoryStreamTypeDeclaration { get; set; }
    public bool UseMonitoredAdvisoryLock { get; set; } = true;

    public bool UseAdvisoryLockTransaction { get; set; } = true;

    public bool EnableAdvancedAsyncTracking { get; set; }
    public bool EnableEventSkippingInProjectionsOrSubscriptions { get; set; }

    /// <summary>
    /// When enabled, adds heartbeat, agent_status, pause_reason, and running_on_node
    /// columns to the event progression table for CritterWatch monitoring
    /// </summary>
    public bool EnableExtendedProgressionTracking { get; set; }
    public bool UseArchivedStreamPartitioning { get; set; }
    public bool UseListenNotifyForEventAppends { get; set; }

    /// <summary>
    /// Opt into a global, partition-spanning unique constraint on stream identity by
    /// also writing each new stream id (or key) into a non-partitioned
    /// <c>mt_streams_identity</c> tracking table at append time. Causes
    /// <see cref="ExistingStreamIdCollisionException"/> to fire on
    /// <c>StartStream</c> when the same identity has already been used — even after
    /// the original stream was archived under <see cref="UseArchivedStreamPartitioning"/>.
    /// Defaults to false. See https://martendb.io/events/archiving for the
    /// recommended use cases (typically only needed when stream identities are
    /// produced outside Marten — e.g. user-supplied string keys).
    /// </summary>
    public bool EnableStrictStreamIdentityEnforcement { get; set; } = false;

    /// <summary>
    /// Per-tenant partitioning master flag (CritterStack #209 / Marten #4596).
    /// Phase 0 — surface only; opting in does not yet partition storage or
    /// alter daemon behavior. Validated at <c>DocumentStore</c> construction
    /// to reject combinations with <see cref="EventAppendMode.Rich"/>.
    /// </summary>
    public bool UseTenantPartitionedEvents { get; set; }

    public IMessageOutbox MessageOutbox { get; set; } = new NulloMessageOutbox();


    public bool EnableUniqueIndexOnEventId { get; set; } = false;

    private readonly List<string> _ignoredIndexes = new();

    public IReadOnlyList<string> IgnoredIndexes => _ignoredIndexes;

    public IEventStoreOptions IgnoreIndex(string indexName)
    {
        if (string.IsNullOrWhiteSpace(indexName))
            throw new ArgumentException("Index name must be supplied", nameof(indexName));

        if (!_ignoredIndexes.Contains(indexName))
            _ignoredIndexes.Add(indexName);

        return this;
    }

    /// <summary>
    /// Opt into adding a composite index on (type, seq_id) to the mt_events table.
    /// This can dramatically improve performance for projection rebuilds and async
    /// projections that filter on a small subset of event types, especially when
    /// there are large sequence gaps between matching events.
    /// </summary>
    public bool EnableEventTypeIndex { get; set; } = false;

    /// <summary>
    /// Opt into using bigint (64-bit) types for event version, sequence, and return
    /// values in the mt_quick_append_events and mt_get_next_hi PostgreSQL functions.
    /// This prevents integer overflow when sequence values exceed int32 range (~2.1 billion).
    /// Default is false for backward compatibility. Will become true by default in Marten 9.0.
    /// </summary>
    public bool EnableBigIntEvents { get; set; } = false;

    public bool EnableSideEffectsOnInlineProjections { get; set; } = false;

    /// <summary>
    ///     Configure whether event streams are identified with Guid or strings
    /// </summary>
    public override StreamIdentity StreamIdentity
    {
        get => base.StreamIdentity;
        set
        {
            base.StreamIdentity = value;
            StreamIdDbType = value == StreamIdentity.AsGuid ? NpgsqlDbType.Uuid : NpgsqlDbType.Varchar;
        }
    }

    /// <summary>
    ///     Configure the event sourcing storage for multi-tenancy
    /// </summary>
    public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

    public bool UseIdentityMapForAggregates { get; set; }

    /// <summary>
    ///     Configure the meta data required to be stored for events. By default meta data fields are disabled
    /// </summary>
    [ChildDescription]
    public MetadataConfig MetadataConfig => new(Metadata);

    /// <summary>
    ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
    ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
    ///     the event type. It can also be used for the event namespace migration.
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    /// <returns>Event store options, to allow fluent definition</returns>
    public IEventStoreOptions AddEventType<TEvent>()
    {
        AddEventType(typeof(TEvent));
        return this;
    }

    /// <summary>
    ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
    ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
    ///     the event type
    /// </summary>
    /// <param name="eventType"></param>
    public override void AddEventType(Type eventType)
    {
        _events.FillDefault(eventType);
    }

    // #4515: per-event-type binary serializer registry. EventMapping<T>'s
    // constructor calls ResolveBinarySerializerFor when the mapping is built
    // (lazily on first use); UseBinarySerializer<T> populates this dictionary
    // ahead of time so the resolution lands on the registered instance.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, IEventBinarySerializer> _binarySerializerByType = new();

    /// <inheritdoc />
    public IEventBinarySerializer? DefaultBinarySerializer { get; set; }

    /// <inheritdoc />
    public IEventStoreOptions UseBinarySerializer<TEvent>(IEventBinarySerializer serializer)
    {
        if (serializer == null) throw new ArgumentNullException(nameof(serializer));

        var eventType = typeof(TEvent);
        _binarySerializerByType[eventType] = serializer;

        // Make sure the mapping exists and is wired with this serializer.
        // EventMapping<T> reads its BinarySerializer from
        // ResolveBinarySerializerFor in its constructor, so if the mapping
        // already exists (e.g. another store-options call referenced the type
        // first) we need to refresh its serializer reference here too.
        AddEventType(eventType);
        var mapping = EventMappingFor(eventType);
        if (mapping is not null)
        {
            mapping.BinarySerializer = serializer;
        }

        return this;
    }

    /// <summary>
    ///     #4515: resolve the binary serializer for an event type. Called from
    ///     <see cref="EventMapping"/>'s constructor. Precedence: explicit
    ///     per-type registration via <see cref="UseBinarySerializer{TEvent}"/>
    ///     beats <see cref="BinaryEventAttribute"/> + <see cref="DefaultBinarySerializer"/>.
    ///     Returns <c>null</c> for plain JSON events.
    /// </summary>
    internal IEventBinarySerializer? ResolveBinarySerializerFor(Type eventType)
    {
        if (_binarySerializerByType.TryGetValue(eventType, out var explicitSerializer))
        {
            return explicitSerializer;
        }

        if (eventType.IsDefined(typeof(BinaryEventAttribute), inherit: false))
        {
            if (DefaultBinarySerializer is null)
            {
                throw new InvalidOperationException(
                    $"Event type '{eventType.FullName}' is marked with [BinaryEvent] but no IEventBinarySerializer was registered. " +
                    $"Either call opts.Events.UseBinarySerializer<{eventType.Name}>(...) explicitly, " +
                    $"or set opts.Events.DefaultBinarySerializer to a store-wide fallback.");
            }

            return DefaultBinarySerializer;
        }

        return null;
    }

    /// <summary>
    ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
    ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
    ///     the event type
    /// </summary>
    /// <param name="types"></param>
    public void AddEventTypes(IEnumerable<Type> types)
    {
        types.Each(AddEventType);
    }

    /// <summary>
    /// Register a strong-typed identifier as a tag type for DCB support.
    /// </summary>
    public ITagTypeRegistration RegisterTagType<TTag>() where TTag : notnull
    {
        var existing = _tagTypes.FirstOrDefault(t => t.TagType == typeof(TTag));
        if (existing != null) return existing;

        var registration = TagTypeRegistration.Create<TTag>();
        _tagTypes.Add(registration);
        return registration;
    }

    /// <summary>
    /// Register a strong-typed identifier as a tag type with a custom table name suffix.
    /// </summary>
    public ITagTypeRegistration RegisterTagType<TTag>(string tableSuffix) where TTag : notnull
    {
        var existing = _tagTypes.FirstOrDefault(t => t.TagType == typeof(TTag));
        if (existing != null) return existing;

        var registration = TagTypeRegistration.Create<TTag>(tableSuffix);
        _tagTypes.Add(registration);
        return registration;
    }

    /// <summary>
    /// The registered tag types for DCB support.
    /// </summary>
    public IReadOnlyList<ITagTypeRegistration> TagTypes => _tagTypes;

    private DcbStorageMode _dcbStorageMode = DcbStorageMode.TagTables;

    /// <summary>
    /// How Dynamic Consistency Boundary (DCB) tags are physically stored. Default is
    /// <see cref="DcbStorageMode.TagTables"/> (one table per tag type, the Marten 8 behavior).
    /// Set to <see cref="DcbStorageMode.HStore"/> to store all tags inline on
    /// <c>mt_events.tags</c> using Postgres' <c>hstore</c> extension and avoid LEFT JOINs
    /// on every DCB query.
    /// </summary>
    public DcbStorageMode DcbStorageMode
    {
        get => _dcbStorageMode;
        set
        {
            if (_dcbStorageMode == value) return;
            _dcbStorageMode = value;

            // When switching to HStore, ensure the `hstore` extension is installed AND
            // the Npgsql data source's type catalog is reloaded BEFORE the first user
            // command runs. Npgsql 9 loads its type catalog the first time a physical
            // connection opens; if that happens before `CREATE EXTENSION hstore` runs,
            // the data source never learns about the hstore type and parameter binding
            // for `NpgsqlDbType.Hstore` fails with "isn't present in your database".
            //
            // The physical-connection initializer fires on every newly-opened physical
            // connection, but we only need the extension-create + type-reload once per
            // data source. The captured `Interlocked.CompareExchange` flag ensures the
            // bootstrap runs exactly once; subsequent physical connections from the
            // same pool no-op the initializer.
            if (value == DcbStorageMode.HStore)
            {
                Options.ConfigureNpgsqlDataSourceBuilder(builder =>
                {
                    var bootstrapped = 0;
                    builder.UsePhysicalConnectionInitializer(
                        connection =>
                        {
                            if (Interlocked.CompareExchange(ref bootstrapped, 1, 0) != 0) return;
                            using var cmd = connection.CreateCommand();
                            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS hstore;";
                            cmd.ExecuteNonQuery();
                            connection.ReloadTypes();
                        },
                        async connection =>
                        {
                            if (Interlocked.CompareExchange(ref bootstrapped, 1, 0) != 0) return;
                            await using var cmd = connection.CreateCommand();
                            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS hstore;";
                            await cmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                            await connection.ReloadTypesAsync(CancellationToken.None).ConfigureAwait(false);
                        });
                });
            }
        }
    }

    /// <summary>
    /// Find a tag type registration by type, or null if not registered.
    /// </summary>
    public ITagTypeRegistration? FindTagType(Type tagType)
    {
        return _tagTypes.FirstOrDefault(t => t.TagType == tagType);
    }

    public void MapEventType<TEvent>(string eventTypeName) where TEvent : class
    {
        MapEventType(typeof(TEvent), eventTypeName);
    }

    public void MapEventType(Type eventType, string eventTypeName)
    {
        var eventMapping = EventMappingFor(eventType);
        eventMapping.EventTypeName = eventTypeName;
    }

    public IEventStoreOptions Upcast<TEvent>(
        string eventTypeName,
        JsonTransformation? jsonTransformation = null
    ) where TEvent : class
    {
        return Upcast(typeof(TEvent), eventTypeName, jsonTransformation);
    }

    public IEventStoreOptions Upcast(
        Type eventType,
        string eventTypeName,
        JsonTransformation? jsonTransformation = null
    )
    {
        var eventMapping = typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);
        eventMapping.EventTypeName = eventTypeName;
        eventMapping.JsonTransformation(jsonTransformation);

        _byEventName.Fill(eventTypeName, eventMapping);

        return this;
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        string eventTypeName,
        Func<TOldEvent, TEvent> upcast
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), eventTypeName, JsonTransformations.Upcast(upcast));
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        Func<TOldEvent, TEvent> upcast
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), GetEventTypeName<TOldEvent>(), JsonTransformations.Upcast(upcast));
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        string eventTypeName,
        Func<TOldEvent, CancellationToken, Task<TEvent>> upcastAsync
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), eventTypeName, JsonTransformations.Upcast(upcastAsync));
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        Func<TOldEvent, CancellationToken, Task<TEvent>> upcastAsync
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), GetEventTypeName<TOldEvent>(), JsonTransformations.Upcast(upcastAsync));
    }

    public IEventStoreOptions Upcast(params IEventUpcaster[] upcasters)
    {
        foreach (var upcaster in upcasters)
        {
            Upcast(
                upcaster.EventType,
                upcaster.EventTypeName,
                new JsonTransformation(upcaster.FromDbDataReader, upcaster.FromDbDataReaderAsync)
            );
        }

        return this;
    }

    public IEventStoreOptions Upcast<TUpcaster>() where TUpcaster : IEventUpcaster, new()
    {
        var upcaster = new TUpcaster();

        Upcast(
            upcaster.EventType,
            upcaster.EventTypeName,
            new JsonTransformation(upcaster.FromDbDataReader, upcaster.FromDbDataReaderAsync)
        );

        return this;
    }

    /// <summary>
    ///     Override the database schema name for event related tables. By default this
    ///     is the same schema as the document storage
    /// </summary>
    public string DatabaseSchemaName
    {
        get => _databaseSchemaName ?? Options.DatabaseSchemaName;
        set => _databaseSchemaName = value.ToLowerInvariant();
    }

    void IEventStoreOptions.Subscribe(ISubscription subscription)
    {
        Options.Projections.Subscribe(subscription);
    }

    void IEventStoreOptions.Subscribe(ISubscription subscription, Action<ISubscriptionOptions>? configure)
    {
        Options.Projections.Subscribe(subscription, configure);
    }

    IReadOnlyDaemonSettings IReadOnlyEventStoreOptions.Daemon => _store.Options.Projections;

    IReadOnlyList<ISubscriptionSource> IReadOnlyEventStoreOptions.Projections()
    {
        return Options.Projections.All.OfType<ISubscriptionSource>().ToList();
    }

    public IReadOnlyList<IEventType> AllKnownEventTypes()
    {
        return _events.OfType<IEventType>().ToList();
    }

    IReadonlyMetadataConfig IReadOnlyEventStoreOptions.MetadataConfig => MetadataConfig;

    public Type IdentityTypeFor(Type aggregateType)
    {
        return new DocumentMapping(aggregateType, Options).IdType;
    }

    private Type findAggregateType(string name)
    {
        foreach (var aggregateType in Options.Projections.AllAggregateTypes())
        {
            var possibleName = _aggregateNameByType[aggregateType];
            if (name.EqualsIgnoreCase(possibleName))
            {
                return aggregateType;
            }
        }

        return null;
    }

    public override EventMapping EventMappingFor(Type eventType)
    {
        return _events[eventType];
    }

    internal EventMapping EventMappingFor<T>() where T : class
    {
        return EventMappingFor(typeof(T));
    }

    internal IEnumerable<EventMapping> AllEvents()
    {
        return _events;
    }

    internal EventMapping? EventMappingFor(string eventType)
    {
        return _byEventName[eventType];
    }

    internal EventMapping? TryGetRegisteredMappingForDotNetTypeName(string dotnetTypeName)
    {
        return AllEvents().FirstOrDefault(x => x.DotNetTypeName == dotnetTypeName);
    }

    // Fetch additional event aliases that map to these types
    internal IReadOnlySet<string> AliasesForEvents(IReadOnlyCollection<Type> types)
    {
        var aliases = new HashSet<string>();

        foreach (var mapping in _byEventName)
        {
            if (mapping is null)
            {
                continue;
            }

            if (types.Contains(mapping.DocumentType))
            {
                aliases.Add(mapping.Alias);
            }
        }

        return aliases;
    }

    internal bool IsActive(StoreOptions options)
    {
        return _events.Any(x => x.DocumentType != typeof(Archived)) || Options.Projections.IsActive();
    }

    internal string GetStreamIdDBType()
    {
        return StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
    }

    internal Type GetStreamIdType()
    {
        return StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
    }

    internal Type TypeForDotNetName(string assemblyQualifiedName)
    {
        if (!_nameToType.Value.TryFind(assemblyQualifiedName, out var value))
        {
            if (assemblyQualifiedName.Contains(".Archived"))
            {
                value = typeof(Archived);
            }
            else if (assemblyQualifiedName.Contains(".Tombstone"))
            {
                value = typeof(Tombstone);
            }
            else
            {
                value = Type.GetType(assemblyQualifiedName);
            }

            if (value == null)
            {
                throw new UnknownEventTypeException($"Unable to load event type '{assemblyQualifiedName}'.");
            }

            _nameToType.Swap(n => n.AddOrUpdate(assemblyQualifiedName, value));
        }

        return value;
    }

    internal IEventStorage EnsureAsStringStorage(IMartenSession session)
    {
        if (StreamIdentity == StreamIdentity.AsGuid)
        {
            throw new InvalidOperationException(
                "This Marten event store is configured to identify streams with Guids");
        }

        return session.EventStorage();
    }

    internal IEventStorage EnsureAsGuidStorage(IMartenSession session)
    {
        if (StreamIdentity == StreamIdentity.AsString)
        {
            throw new InvalidOperationException(
                "This Marten event store is configured to identify streams with strings");
        }

        return session.EventStorage();
    }

    internal void Initialize(DocumentStore store)
    {
        _store = store;

        var logger = (_store.Options.Logger() as DefaultMartenLogger)?.Inner ?? NullLogger.Instance;


        _tombstones = new RetryBlock<UpdateBatch>(executeTombstoneBlock, logger, _cancellation.Token);

        // Pre-warm name->type so the first read of each event type from the database
        // doesn't fall through Type.GetType(assemblyQualifiedName) in TypeForDotNetName,
        // which is itself O(loaded-assemblies). Populate both AssemblyQualifiedName and
        // FullName since both shapes appear in event metadata over the lifetime of a
        // store. Done as a single Swap so we don't churn ImHashMap. Also pre-fill
        // _byEventName so EventMappingFor(string) skips its O(n) AllEvents() walk on
        // first lookup of every registered event-type alias.
        _nameToType.Swap(map =>
        {
            foreach (var mapping in _events)
            {
                mapping.JsonTransformation(null);

                var docType = mapping.DocumentType;
                if (docType.AssemblyQualifiedName is { } aqn)
                {
                    map = map.AddOrUpdate(aqn, docType);
                }
                if (docType.FullName is { } fullName)
                {
                    map = map.AddOrUpdate(fullName, docType);
                }

                _byEventName.Fill(mapping.EventTypeName, mapping);
            }

            return map;
        });

        // Pre-warm the aggregate-name -> aggregate-type cache so AggregateTypeFor
        // (and therefore the LINQ-from-aggregate-alias path) doesn't pay the linear
        // AllAggregateTypes() walk on the first lookup of each aggregate alias.
        // findAggregateType is the OnMissing on _aggregateTypeByName and would do
        // exactly this work lazily; pre-populating moves the cost off the request
        // path and into the once-per-store Initialize.
        foreach (var aggregateType in Options.Projections.AllAggregateTypes())
        {
            var alias = _aggregateNameByType[aggregateType];
            _aggregateTypeByName.Fill(alias, aggregateType);
        }

        autoDiscoverTagTypesFromProjections();
    }

    private static readonly HashSet<Type> PrimitiveIdentityTypes =
    [
        typeof(Guid), typeof(string), typeof(int), typeof(long), typeof(short)
    ];

    private static readonly System.Reflection.MethodInfo CreateTagTypeMethod =
        typeof(TagTypeRegistration).GetMethod(nameof(TagTypeRegistration.Create))!;

    private void autoDiscoverTagTypesFromProjections()
    {
        foreach (var projection in Options.Projections.All.OfType<IAggregateProjection>())
        {
            var identityType = projection.IdentityType;
            if (identityType == null || PrimitiveIdentityTypes.Contains(identityType)) continue;
            if (_tagTypes.Any(t => t.TagType == identityType)) continue;

            try
            {
                var generic = CreateTagTypeMethod.MakeGenericMethod(identityType);
                var registration = (ITagTypeRegistration)generic.Invoke(null, [null])!;
                registration.ForAggregate(projection.AggregateType);
                _tagTypes.Add(registration);
            }
            catch
            {
                // Not a valid strong-typed identifier — skip silently
            }
        }
    }

    public List<Type> GlobalAggregates { get; } = new();

    internal Marten.Internal.Storage.DocumentProvider<IEvent>? Provider { get; private set; }

    /// <summary>
    ///     Constructs the closed-shape event document storage adapter for this
    ///     EventGraph and caches it as <see cref="Provider"/>. The closed-shape
    ///     adapter is built reflectively at runtime — no codegen is involved.
    ///     ProviderGraph invokes this on the first IEvent storage request.
    /// </summary>
    internal void AttachTypesSynchronously()
    {
        var closedShape = new Marten.EventStorage.ClosedShapeEventDocumentStorage(Options);
        Provider = new Marten.Internal.Storage.DocumentProvider<IEvent>(null!, closedShape, closedShape, closedShape, closedShape);
    }
}
