using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImTools;
using JasperFx.Blocks;
using JasperFx.CodeGeneration;
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

namespace Marten.Events;

public partial class EventGraph: EventRegistry, IEventStoreOptions, IReadOnlyEventStoreOptions,
    IDisposable, IAsyncDisposable,
    IAggregationSourceFactory<IQuerySession>, IDescribeMyself, ICodeFileCollection
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
            if (StreamIdentity == StreamIdentity.AsGuid)
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
    public bool EnableAdvancedAsyncTracking { get; set; }
    public bool EnableEventSkippingInProjectionsOrSubscriptions { get; set; }

    /// <summary>
    /// When enabled, adds heartbeat, agent_status, pause_reason, and running_on_node
    /// columns to the event progression table for CritterWatch monitoring
    /// </summary>
    public bool EnableExtendedProgressionTracking { get; set; }
    public bool UseArchivedStreamPartitioning { get; set; }
    public IMessageOutbox MessageOutbox { get; set; } = new NulloMessageOutbox();


    public bool EnableUniqueIndexOnEventId { get; set; } = false;

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
        foreach (var mapping in _events)
        {
            mapping.JsonTransformation(null);
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

    IReadOnlyList<ICodeFile> ICodeFileCollection.BuildFiles()
    {
        return [this];
    }

    string ICodeFileCollection.ChildNamespace => "EventStore";

    GenerationRules ICodeFileCollection.Rules => Options.CreateGenerationRules();
    public List<Type> GlobalAggregates { get; } = new();
}
