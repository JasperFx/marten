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
using Marten.Events.Fetching;
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
    /// <summary>
    ///     The name of the GIN index over the <c>tags</c> hstore column in
    ///     <see cref="DcbStorageMode.HStore" /> mode. Pass it to <see cref="IgnoreIndex" /> to keep the
    ///     index out of schema migrations and build it yourself.
    /// </summary>
    /// <remarks>
    ///     #5268. Turning HStore mode on for an existing store adds two things to <c>mt_events</c>: the
    ///     nullable <c>tags</c> column, which is a metadata-only add, and this index, which is not.
    ///     Marten emits a plain <c>CREATE INDEX</c> by default, which holds ACCESS EXCLUSIVE for the whole
    ///     build — on a large event table that is a write outage rather than a migration.
    ///     <para>
    ///     There are two ways out. <see cref="BuildHStoreTagIndexConcurrently" /> has Marten build it
    ///     without blocking writes, on any event table that is not tenant-partitioned. Ignoring it instead
    ///     takes it out of the schema diff in both directions, so Marten will neither create it nor treat
    ///     an index you built yourself as drift — which is the route for a partitioned <c>mt_events</c>,
    ///     where PostgreSQL refuses <c>CREATE INDEX CONCURRENTLY</c> on the parent outright.
    ///     </para>
    /// </remarks>
    public const string HStoreTagIndexName = "idx_mt_events_tags";

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
    private readonly List<IEventTagRule> _tagRules = new();

    // Roots EventMapping<>'s constructor for trimming / Native AOT. Without this root the constructor metadata is trimmed
    // and Activator throws MissingMethodException at StoreOptions construction.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(EventMapping<>))]
    internal EventGraph(StoreOptions options)
    {
        StreamIdentity = StreamIdentity.AsGuid;
        Options = options;
        _events.OnMissing = eventType =>
        {
            // #4917 follow-up: build EventMapping<eventType> through GenericFactoryCache rather than a raw
            // CloseAndBuildAs. GenericFactoryCache caches a compiled factory delegate per closed type, so the
            // MakeGenericType + Activator reflection runs once per event type instead of on every cache miss,
            // and it is the delegate-cached pattern the rest of Marten already funnels this kind of open-generic
            // construction through. The [DynamicDependency] on this constructor is what actually keeps
            // EventMapping<>'s constructor from being trimmed; without it Activator throws MissingMethodException.
            var mapping = GenericFactoryCache.BuildAs<EventMapping>(
                typeof(EventMapping<>),
                eventType,
                this,
                static closed => parent => (EventMapping)Activator.CreateInstance(closed, parent)!);
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
        // #5264: MappingFor() is not a read-only probe -- it registers a DocumentMapping in
        // StorageFeatures as a side effect. A [BoundaryAggregate] has no identity at all, so
        // that mapping later throws InvalidDocumentException ("Could not determine an 'id/Id'
        // field") the first time anything enumerates AllActiveFeatures -- which is every
        // full-schema operation: ResetAllData, ApplyAllConfiguredChangesToDatabaseAsync,
        // AssertDatabaseMatchesConfigurationAsync, db-patch and db-apply. Decide the id type
        // from the attribute before touching storage, so no mapping is ever created.
        //
        // TId is unconditionally string for these: the source generator emits
        // IGeneratedSyncEvolver<TDoc, string> (jasperfx#324) and the id is vestigial -- it
        // only has to match the SingleStreamProjection<T, string> built here so the dispatcher
        // lookup hits, regardless of StreamIdentity or of any stray Id member on the type.
        if (typeof(TDoc).IsDefined(typeof(BoundaryAggregateAttribute), inherit: false))
        {
            return typeof(SingleStreamProjection<,>)
                .CloseAndBuildAs<IAggregatorSource<IQuerySession>>(typeof(TDoc), typeof(string));
        }

        var mapping = Options.Storage.MappingFor(typeof(TDoc));
        var idType = mapping.IdType;

        // For the quite legitimate case of doing a live aggregation when
        // there is no Id member
        if (idType == null)
        {
            // #5264: same hazard as above, one step over. An explicitly registered document
            // type with no Id would already have thrown inside MappingFor's CompileAndValidate,
            // so reaching here means this is a fallback mapping materialized for aggregation
            // only. It can never be stored, so keep it out of schema generation rather than
            // letting it fail DDL for the whole store later.
            mapping.SkipSchemaGeneration = true;

            idType = StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
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
            if (_tagRules.Count > 0) applyTagRules(e);
            return e;
        }
        else
        {
            var mapping = EventMappingFor(eventData.GetType());
            var wrapped = mapping.Wrap(eventData);
            if (_tagRules.Count > 0) applyTagRules(wrapped);
            return wrapped;
        }


    }

    public bool UseOptimizedProjectionRebuilds { get; set; }
    public bool UseMandatoryStreamTypeDeclaration { get; set; }
    public bool UseMonitoredAdvisoryLock { get; set; } = true;

    public bool UseAdvisoryLockTransaction { get; set; } = true;

    public bool EnableAdvancedAsyncTracking { get; set; }
    public bool EnableEventSkippingInProjectionsOrSubscriptions { get; set; }

    /// <summary>
    /// When enabled, adds heartbeat, agent_status, pause_reason, running_on_node, and
    /// warning/critical-behind-threshold columns to the event progression table for
    /// CritterWatch monitoring.
    /// </summary>
    public bool EnableExtendedProgressionTracking
    {
        get;
        set;
    }

    /// <summary>
    /// Optional best-effort observer invoked after each successful <c>SaveChangesAsync</c> with the
    /// events that were appended in that unit of work. Backs
    /// <see cref="JasperFx.Events.IEventStoreInstrumentation.AppendObserver"/> so storage-agnostic
    /// lifecycle tooling (CritterWatch) can record runtime-observed "appends" edges. Each
    /// <see cref="IEvent"/> carries its event type, stream id/key, aggregate type, tenant id, and
    /// timestamp. See #4782.
    /// </summary>
    public Action<IReadOnlyList<IEvent>>? AppendObserver { get; set; }


    public bool UseArchivedStreamPartitioning { get; set; }
    public bool UseListenNotifyForEventAppends { get; set; }

    /// <summary>
    /// When enabled, adds FOR UPDATE to the stream version SELECT inside
    /// mt_quick_append_events for OCC (optimistic concurrency) appends.
    /// This prevents a READ COMMITTED race where two concurrent transactions
    /// both pass the version check before either commits, both call nextval(),
    /// and the loser fails with a 23505 duplicate key violation — leaving a
    /// permanent gap in mt_events_sequence that stalls QueryForNonStaleData.
    /// With this option the losing transaction blocks at the SELECT until the
    /// winner commits, reads the updated version, and raises MT003 before any
    /// nextval() call. Non-OCC appends are unaffected.
    /// Defaults to false to preserve existing throughput characteristics.
    /// </summary>
    public bool UseExclusiveLockOnConcurrentAppends { get; set; }

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
    /// Partitions mt_events / mt_streams by tenant_id, gives each tenant its own
    /// event sequence, keys mt_event_progression by (name, tenant_id), and runs
    /// the async daemon with per-tenant high-water tracking, per-tenant agent
    /// distribution, and per-tenant rebuild isolation. Validated at
    /// <c>DocumentStore</c> construction: requires <see cref="TenancyStyle.Conjoined"/>
    /// and a quick append mode (rejects <see cref="EventAppendMode.Rich"/> and
    /// <see cref="UseArchivedStreamPartitioning"/>). See
    /// <see cref="Marten.Events.IEventStoreOptions.UseTenantPartitionedEvents"/>.
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
        // Keep the historical, unconstrained signature (#4917 had tightened this to `where TEvent : class` so it
        // could new up EventMapping<TEvent> directly -- EventMapping<T> is class-constrained -- but that was a
        // source-breaking change for callers with a `where T : notnull` generic relay). Route through the
        // runtime-Type overload instead; construction goes through the GenericFactoryCache factory in the
        // constructor, and the [DynamicDependency] there keeps it trim-safe.
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Type, JasperFx.Events.IEventBinarySerializer> _binarySerializerByType = new();

    /// <inheritdoc />
    public JasperFx.Events.IEventBinarySerializer? DefaultBinarySerializer { get; set; }

    /// <inheritdoc />
    public IEventStoreOptions UseBinarySerializer<TEvent>(JasperFx.Events.IEventBinarySerializer serializer)
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
    internal JasperFx.Events.IEventBinarySerializer? ResolveBinarySerializerFor(Type eventType)
    {
        if (_binarySerializerByType.TryGetValue(eventType, out var explicitSerializer))
        {
            return explicitSerializer;
        }

        // jasperfx#669/#672: BOTH spellings are honored, in ONE lookup. Marten.Events
        // .BinaryEventAttribute is what every existing user wrote; JasperFx.Events
        // .BinaryEventAttribute is the promoted one an event type shared across Marten / Polecat /
        // Fisher can declare. Marten's now derives from the promoted one (2.51.0 unsealed it for
        // exactly this), and IsDefined matches attributes assignable to the requested type, so the
        // base lookup finds the subclass. `inherit: false` is unrelated and stays as it was — it
        // governs the EVENT type's hierarchy, not the attribute class's.
        if (eventType.IsDefined(typeof(JasperFx.Events.BinaryEventAttribute), inherit: false))
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

    /// <summary>
    /// Derive a DCB tag from an event's own data, for every event appended through this store.
    /// <para>
    /// <see cref="Tags.EventTagInference" /> can only find a tag when the event exposes a property whose
    /// <i>type</i> is the tag type, and it only runs on the <c>IEventBoundary</c> path. A rule closes both
    /// gaps: it works for an event that names its identifiers as primitives, and it applies wherever the
    /// event is built -- ordinary appends, <c>StartStream</c>, aggregate handlers and bulk inserts alike.
    /// </para>
    /// <para>
    /// Return <c>null</c> to leave an event untagged. A rule declared on a base type or interface applies
    /// to every event assignable to it, and several rules may contribute different tag types to one event.
    /// A tag type already present on the event is left alone, so a rule never fights an explicit
    /// <c>WithTag</c> call.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// opts.Events.RegisterTagType&lt;InvoiceId&gt;("invoice");
    /// opts.Events.TagWith&lt;InvoiceCreated&gt;(e =&gt; new InvoiceId(e.Invoice));
    /// </code>
    /// </example>
    public void TagWith<TEvent>(Func<TEvent, object?> rule) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(rule);
        _tagRules.Add(new EventTagRule<TEvent>(rule));
    }

    /// <summary>
    /// Derive every DCB tag an event carries from one store-wide rule. See
    /// <see cref="IEventStoreOptions.TagEventsBy" />.
    /// </summary>
    public void TagEventsBy(Func<object, IEnumerable<object>?> tagger)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        _tagRules.Add(new StoreWideEventTagRule(tagger));
    }

    /// <summary>
    /// The registered tag rules. See <see cref="TagWith{TEvent}" /> and <see cref="TagEventsBy" />.
    /// </summary>
    public IReadOnlyList<IEventTagRule> TagRules => _tagRules;

    private void applyTagRules(IEvent @event)
    {
        for (var i = 0; i < _tagRules.Count; i++)
        {
            foreach (var tag in _tagRules[i].Resolve(@event.Data))
            {
                applyTag(@event, _tagRules[i], tag);
            }
        }
    }

    private void applyTag(IEvent @event, IEventTagRule rule, object? tag)
    {
        {
            if (tag == null) return;

            var tagType = tag.GetType();

            // An explicit WithTag wins, and re-building an event that already carries the tag must not add
            // it twice -- in HStore mode a second value of one tag type throws rather than overwriting.
            if (@event.Tags != null)
            {
                var alreadyTagged = false;
                for (var t = 0; t < @event.Tags.Count; t++)
                {
                    if (@event.Tags[t].TagType == tagType)
                    {
                        alreadyTagged = true;
                        break;
                    }
                }

                if (alreadyTagged) return;
            }

            if (!_tagTypes.Any(x => x.TagType == tagType))
            {
                throw new InvalidOperationException(
                    $"The tag rule for '{rule.Description}' produced a tag of type "
                    + $"'{tagType.FullName}', which is not a registered tag type. A tag Marten does not know "
                    + "cannot be stored or queried, so this would silently write nothing. Call "
                    + $"RegisterTagType<{tagType.Name}>() when configuring the store.");
            }

            @event.AddTag(new EventTag(tagType, tag));
        }
    }

    private DcbStorageMode _dcbStorageMode = DcbStorageMode.TagTables;

    /// <summary>
    /// Opt into building <see cref="HStoreTagIndexName" /> without blocking writes to <c>mt_events</c>.
    /// Only has any effect in <see cref="DcbStorageMode.HStore" /> mode. Default is false.
    /// </summary>
    /// <remarks>
    ///     #5268. Turning HStore mode on for an existing store is otherwise a maintenance window: the
    ///     plain <c>CREATE INDEX</c> holds ACCESS EXCLUSIVE for the whole GIN build. With this on the
    ///     index is marked concurrent, and Weasel emits <c>CREATE INDEX CONCURRENTLY</c> as its own
    ///     command outside the migration's transaction.
    ///     <para>
    ///     Off by default because a concurrent build cannot run inside a transaction, so it changes what
    ///     the generated <c>db-patch</c> / <c>db-dump</c> script is: still correct against a live
    ///     database, no longer runnable as one transactional block.
    ///     </para>
    ///     <para>
    ///     Rejected at configuration time alongside <see cref="UseTenantPartitionedEvents" />. PostgreSQL
    ///     refuses <c>CONCURRENTLY</c> on a partitioned parent and wants an <c>ON ONLY</c> parent index,
    ///     a concurrent index per partition, and an <c>ALTER INDEX ... ATTACH PARTITION</c> for each —
    ///     and Weasel takes that partition list from the table's declared partitions, which is empty when
    ///     the partitions belong to Marten's tenant partition manager. See StoreOptions.Validate.
    ///     </para>
    /// </remarks>
    public bool BuildHStoreTagIndexConcurrently { get; set; }

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
        // #4680: pin the mapping as an upcast target so EventDocumentStorage.Resolve skips
        // its dotnet_type alt-mapping swap on read. Without this, a typed Append of the
        // SOURCE type into the same store registers a concrete EventMapping<TOld> whose
        // DotNetTypeName matches the stored dotnet_type, and the swap shadows the upcaster
        // -- events read back as TOld instead of TNew. See the Bug_4680 regression test.
        eventMapping.IsUpcastTarget = true;

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

    /// <summary>
    ///     Keep recently fetched snapshots of <typeparamref name="T" /> in a node local cache so that a
    ///     subsequent FetchForWriting can skip loading the stored snapshot and read only the events after
    ///     it. Effectively an identity map for aggregates with a lifetime longer than a session.
    ///     <para>
    ///     The cache is never trusted blindly. The stream version is still read from the database on every
    ///     call, and the optimistic concurrency assertion on append is untouched, so a stale entry costs an
    ///     extra query and never yields a wrong aggregate. See
    ///     <see cref="JasperFx.Events.Fetching.IAggregateWriteCache" /> for the full semantics, which are
    ///     shared with every other Critter Stack store (jasperfx#674).
    ///     </para>
    ///     <para>
    ///     Supported for both <see cref="ProjectionLifecycle.Async" /> and
    ///     <see cref="ProjectionLifecycle.Inline" />, but they behave differently:
    ///     </para>
    ///     <list type="bullet">
    ///         <item><b>Async</b> — the cached snapshot is a <i>baseline</i>. Events after the cached
    ///         version are read and folded onto it, so a stale entry just means a larger delta query. The
    ///         entry is written as soon as the fetch completes, because the daemon (not the session) applies
    ///         appended events, so the instance cannot drift ahead of the database.</item>
    ///         <item><b>Inline</b> — the stored snapshot is written in the same transaction as the events
    ///         and so is always exactly at the stream head. A cached entry is therefore only usable on an
    ///         <i>exact</i> version match, and anything else falls back to loading the snapshot. The entry
    ///         is written back only <i>after a successful commit</i>, because the inline projection applies
    ///         the caller's events to the fetched instance during that commit.</item>
    ///     </list>
    /// </summary>
    /// <param name="sizeLimit">Maximum number of cached aggregates, when the default cache is built</param>
    /// <remarks>
    ///     Overridden rather than merely inherited from <see cref="EventRegistry" /> because the write back
    ///     for the Inline lifecycle needs a session listener, and where a listener lives is Marten's own
    ///     business. The enrollment itself — the type set, the size limit, resolving the cache — is the
    ///     shared registry's.
    /// </remarks>
    public override void CacheAggregatesForWriting<T>(int sizeLimit = 1000)
    {
        base.CacheAggregatesForWriting<T>(sizeLimit);

        // Inline aggregates are written back to the cache only after a successful commit, which needs a
        // session listener. Harmless for Async-only usage -- the listener no-ops when nothing was tracked.
        if (!Options.Listeners.Contains(AggregateWriteCacheListener.Instance))
        {
            Options.Listeners.Add(AggregateWriteCacheListener.Instance);
        }
    }

    internal DocumentProvider<IEvent>? Provider { get; private set; }

    /// <summary>
    ///     Constructs the closed-shape event document storage adapter for this
    ///     EventGraph and caches it as <see cref="Provider"/>. The closed-shape
    ///     adapter is built reflectively at runtime — no codegen is involved.
    ///     ProviderGraph invokes this on the first IEvent storage request.
    /// </summary>
    internal void AttachTypesSynchronously()
    {
        var closedShape = new Marten.EventStorage.ClosedShapeEventDocumentStorage(Options);
        Provider = new DocumentProvider<IEvent>(closedShape, closedShape, closedShape, closedShape);
    }
}
