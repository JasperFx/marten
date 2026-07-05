#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Subscriptions;
using JasperFx.Events.Tags;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Exceptions;
using Marten.Services.Json.Transformations;
using Marten.Storage;
using Marten.Subscriptions;
using static JasperFx.Events.EventTypeExtensions;

namespace Marten.Events
{
    public interface IEventStoreOptions
    {
        /// <summary>
        ///     Configure whether event streams are identified with Guid or strings
        /// </summary>
        public StreamIdentity StreamIdentity { get; set; }

        /// <summary>
        ///     Configure the event sourcing storage for multi-tenancy
        /// </summary>
        TenancyStyle TenancyStyle { get; set; }

        /// <summary>
        /// Opt into having Marten process "side effects" on aggregation projections (SingleStreamProjection/MultiStreamProjection) while
        /// running in an Inline lifecycle. Default is false;
        /// </summary>
        bool EnableSideEffectsOnInlineProjections { get; set; }

        /// <summary>
        /// Opt into a performance optimization that directs Marten to always use the identity map for an
        /// as much as possible for FetchForWriting() or FetchLatest(). Note that this optimization is only
        /// appropriate if using either immutable aggregations or when you do not mutate the aggregate yourself
        /// outside of Marten internals
        /// </summary>
        bool UseIdentityMapForAggregates { get; set; }

        /// <summary>
        ///     Override the database schema name for event related tables. By default this
        ///     is the same schema as the document storage
        /// </summary>
        string DatabaseSchemaName { get; set; }

        public MetadataConfig MetadataConfig { get; }

        /// <summary>
        /// TimeProvider used for event timestamping metadata. Replace for controlling the timestamps
        /// in testing
        /// </summary>
        public TimeProvider TimeProvider { get; set; }

        /// <summary>
        /// Opt into having Marten create a unique index on Event.Id. The default is false. This may
        /// be helpful if you need to create an external reference id to another system, or need to
        /// load events by their Id
        /// </summary>
        public bool EnableUniqueIndexOnEventId { get; set; }

        /// <summary>
        /// Opt into adding a composite index on (type, seq_id) to the mt_events table.
        /// This can dramatically improve performance for projection rebuilds and async
        /// projections that filter on a small subset of event types.
        /// </summary>
        public bool EnableEventTypeIndex { get; set; }

        /// <summary>
        /// Opt into using bigint (64-bit) types for event version, sequence, and return
        /// values in PostgreSQL functions. Prevents integer overflow when sequence values
        /// exceed int32 range. Default is false. Will become true in Marten 9.0.
        /// </summary>
        public bool EnableBigIntEvents { get; set; }

        public EventAppendMode AppendMode { get; set; }

        /// <summary>
        /// Opt into more robust tracking of asynchronous projection behavior. Default is false. This will add
        /// extra tables, functions, and columns to your Marten event store schema
        /// </summary>
        public bool EnableAdvancedAsyncTracking { get; set; }

        /// <summary>
        /// Opt into using PostgreSQL list partitioning. This can have significant performance and scalability benefits
        /// *if* you are also aggressively using event stream archiving
        /// </summary>
        public bool UseArchivedStreamPartitioning { get; set; }

        /// <summary>
        /// Opt into a global, partition-spanning unique constraint on stream
        /// identity (id / key). When enabled, Marten writes each new stream's
        /// identity into a non-partitioned <c>mt_streams_identity</c> tracking
        /// table at append time and translates a unique violation there into
        /// <see cref="ExistingStreamIdCollisionException"/>. This catches reuse
        /// of an identity even after the original stream was archived under
        /// <see cref="UseArchivedStreamPartitioning"/>. Defaults to false.
        /// </summary>
        public bool EnableStrictStreamIdentityEnforcement { get; set; }

        /// <summary>
        /// Per-tenant partitioning master flag (CritterStack #209 / Marten #4596).
        /// When enabled, the event store partitions <c>mt_events</c> and
        /// <c>mt_streams</c> by <c>tenant_id</c>, uses one event sequence per
        /// tenant (<c>mt_events_sequence_{tenant_suffix}</c>), keys
        /// <c>mt_event_progression</c> by <c>(name, tenant_id)</c>, and runs
        /// the async daemon with a vectorized per-tenant high-water mark plus
        /// per-tenant rebuild isolation.
        ///
        /// <para>
        /// Opt-in flag, defaults to false. Currently in Phase 0 — surface only.
        /// The flag exists so consumer code can be written against the future
        /// behavior, but enabling it is not yet meaningful at runtime.
        /// </para>
        /// <para>
        /// Constraint: only the quick append modes
        /// (<see cref="EventAppendMode.Quick"/> /
        /// <see cref="EventAppendMode.QuickWithServerTimestamps"/>) are
        /// supported. Setting this with <see cref="EventAppendMode.Rich"/>
        /// throws at <c>DocumentStore</c> construction — the per-tenant
        /// sequence pick is wired into the <c>QuickAppendEventFunction</c>
        /// code path only.
        /// </para>
        /// </summary>
        public bool UseTenantPartitionedEvents { get; set; }

        /// <summary>
        /// Opt-in (default false): when a node-distributed async daemon fans agents out per (shard, tenant)
        /// under sharded tenancy, assign them with database affinity — every agent for one shard database on
        /// the same node — so a node opens connection pools only to the databases it owns and pool count
        /// scales with shard databases rather than nodes × databases (which otherwise exhausts a shared
        /// server's max_connections). Only takes effect with <see cref="UseTenantPartitionedEvents"/> +
        /// sharded-database tenancy + a distribution-aware host (e.g. Wolverine-managed event-subscription
        /// distribution). See JasperFx/marten#4806.
        /// </summary>
        public bool UseDatabaseAffineAgentAssignment { get; set; }

        /// <summary>
        /// Optional extension point to receive published messages as a side effect from
        /// aggregation projections
        /// </summary>
        public IMessageOutbox MessageOutbox { get; set; }

        /// <summary>
        /// Opt into some performance optimizations for projection rebuilds for both single stream and
        /// multi-stream projections. This will result in new table columns and a potential database
        /// migration. This will be a default in Marten 8.
        /// </summary>
        public bool UseOptimizedProjectionRebuilds { get; set; }

        /// <summary>
        /// Does Marten require a stream type for any new event streams? This will also
        /// validate that an event stream already exists as part of appending events. Default in 7.0 is false,
        /// but this will be true in 8.0
        /// </summary>
        public bool UseMandatoryStreamTypeDeclaration { get; set; }

        /// <summary>
        /// Enables a background monitor to detect if the advisory lock is lost due to database restart or fail-over. Prevents situations where concurrent running of async daemons may occur on system recovery.
        /// Only relevant when using the async daemon in HotCold mode. Enabled by default.
        /// </summary>
        /// <remarks>
        /// This will show up as a SELECT SLEEP query with a 60-second sleep interval. This does not add any additional load to your database, regardless of what your monitoring tools might say.
        /// </remarks>
        public bool UseMonitoredAdvisoryLock { get; set; }

        /// <summary>
        /// Uses a transaction-scoped advisory lock instead of a session-scoped one. This improves compatibility with PGBouncer and suppresses some irrelevant warning spam in Postgres logs. Enabled by default.
        /// </summary>
        public bool UseAdvisoryLockTransaction { get; set; }

        /// <summary>
        /// Opt into different aliasing styles for .NET event types
        /// </summary>
        public EventNamingStyle EventNamingStyle { get; set; }

        /// <summary>
        /// This is an "opt in" feature to add the capability to mark some events as "skipped" in the database
        /// meaning that they do not apply to projections or subscriptions. Use this to "cure" bad events
        /// </summary>
        public bool EnableEventSkippingInProjectionsOrSubscriptions { get; set; }

        /// <summary>
        /// When enabled, uses PostgreSQL LISTEN/NOTIFY to wake the async projection daemon
        /// immediately when new events are appended, instead of relying solely on polling.
        /// This provides near-instant projection updates while still falling back to polling
        /// as a safety net. Default is false.
        /// </summary>
        public bool UseListenNotifyForEventAppends { get; set; }

        /// <summary>
        /// When enabled, adds FOR UPDATE to the stream version SELECT inside
        /// mt_quick_append_events for OCC (optimistic concurrency) appends.
        /// This prevents a READ COMMITTED race where two concurrent transactions
        /// both pass the version check before either commits, both call nextval(),
        /// and the loser fails with a 23505 — leaving a permanent gap in
        /// mt_events_sequence that stalls QueryForNonStaleData.
        /// Defaults to false to preserve existing throughput characteristics.
        /// </summary>
        public bool UseExclusiveLockOnConcurrentAppends { get; set; }

        /// <summary>
        ///     Directs the schema migration functionality to ignore the presence of the named index
        ///     on the event-store tables (<c>mt_events</c>, <c>mt_streams</c>, <c>mt_event_progression</c>).
        ///     Use this when an external mechanism (e.g. a custom <c>IFeatureSchema</c>) declares an index
        ///     on a Marten-managed event-store table that Marten itself shouldn't try to manage.
        /// </summary>
        /// <param name="indexName">The PostgreSQL index name to ignore</param>
        /// <returns>Event store options, to allow fluent definition</returns>
        IEventStoreOptions IgnoreIndex(string indexName);

        /// <summary>
        ///     Index names that the schema migration functionality should ignore on the event-store tables.
        /// </summary>
        IReadOnlyList<string> IgnoredIndexes { get; }

        /// <summary>
        ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
        ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
        ///     the event type. It can also be used for the event namespace migration.
        ///     See more in <a href="https://martendb.io/events/versioning.html#namespace-migration">documentation</a>
        /// </summary>
        /// <typeparam name="TEvent">CLR event type</typeparam>
        /// <returns>Event store options, to allow fluent definition</returns>
        IEventStoreOptions AddEventType<TEvent>();

        /// <summary>
        ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
        ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
        ///     the event type. It can also be used for the event namespace migration.
        ///     See more in <a href="https://martendb.io/events/versioning.html#namespace-migration">documentation</a>
        /// </summary>
        /// <param name="eventType"></param>
        void AddEventType(Type eventType);

        /// <summary>
        ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
        ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
        ///     the event type. It can also be used for the event namespace migration.
        ///     See more in <a href="https://martendb.io/events/versioning.html#namespace-migration">documentation</a>
        /// </summary>
        /// <param name="types"></param>
        void AddEventTypes(IEnumerable<Type> types);

        /// <summary>
        ///     Store-wide fallback <see cref="IEventBinarySerializer"/> used for event types
        ///     marked with <see cref="BinaryEventAttribute"/> when no explicit per-type
        ///     serializer was wired via <see cref="UseBinarySerializer{TEvent}"/>. Default
        ///     is <c>null</c>; setting this is what makes attribute-only opt-in work for
        ///     the common case of one binary serializer per store. See #4515.
        /// </summary>
        public IEventBinarySerializer? DefaultBinarySerializer { get; set; }

        /// <summary>
        ///     Opt a single event type into binary serialization (#4515). The event's
        ///     payload is written to the <c>bdata</c> bytea column instead of the
        ///     <c>data</c> jsonb column; existing JSON rows for the same type continue
        ///     to read through the JSON path. Calling this also adds the event type to
        ///     the registry (no separate <see cref="AddEventType{TEvent}"/> call needed).
        /// </summary>
        /// <typeparam name="TEvent">CLR event type to opt in.</typeparam>
        /// <param name="serializer">Per-type serializer to use for this event.</param>
        /// <returns>Event store options, to allow fluent definition.</returns>
        IEventStoreOptions UseBinarySerializer<TEvent>(IEventBinarySerializer serializer);

        /// <summary>
        ///     Maps CLR event type as particular event type name. This is useful for event type migration.
        ///     See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a>
        /// </summary>
        /// <param name="eventTypeName">Event type name</param>
        /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
        void MapEventType<TEvent>(string eventTypeName) where TEvent : class;

        /// <summary>
        ///     Maps CLR event type as particular event type name. This is useful for event type migration.
        ///     See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a>
        /// </summary>
        /// <param name="eventType">Event type name</param>
        /// <param name="eventTypeName">Event type name</param>
        void MapEventType(Type eventType, string eventTypeName);

        /// <summary>
        /// Add a new event subscription to this store
        /// </summary>
        /// <param name="subscription"></param>
        void Subscribe(ISubscription subscription);

        /// <summary>
        /// Add a new event subscription to this store with the option to configure the filtering
        /// and async daemon behavior
        /// </summary>
        /// <param name="subscription"></param>
        /// <param name="configure"></param>
        void Subscribe(ISubscription subscription, Action<ISubscriptionOptions>? configure = null);

        /// <summary>
        ///     <para>
        ///         Method defines the JSON payload transformation. It "upcasts" one event schema into another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         By calling it, you tell that for provided event type name, you'd like to get the particular CLR event type.
        ///         JSON transformation defines the custom mapping from JSON string to the CLR object.
        ///     </para>
        ///     <para>
        ///         When you define it, default deserialization for the particular event type won't be used.
        ///         See more in
        ///         <a href="https://martendb.io/events/versioning.html#raw-json-transformation-with-json-net">documentation</a>
        ///     </para>
        /// </summary>
        /// <param name="eventTypeName">Event type name</param>
        /// <param name="jsonTransformation">Event payload transformation</param>
        /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
        /// <returns>Event store options, to allow fluent definition</returns>
        IEventStoreOptions Upcast<TEvent>(
            string eventTypeName,
            JsonTransformation jsonTransformation
        ) where TEvent : class;

        /// <summary>
        ///     <para>
        ///         Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         By calling it, you tell that for provided event type name, you'd like to get the particular CLR event type.
        ///         JSON transformation defines the custom mapping from JSON string to the CLR object.
        ///     </para>
        ///     <para>
        ///         When you define it, default deserialization for the particular event type won't be used.
        ///         See more in
        ///         <a href="https://martendb.io/events/versioning.html#raw-json-transformation-with-json-net">documentation</a>
        ///     </para>
        /// </summary>
        /// <param name="eventType">Mapped CLR event type</param>
        /// <param name="eventTypeName">Event type name</param>
        /// <param name="jsonTransformation">Event payload transformation</param>
        /// <returns>Event store options, to allow fluent definition</returns>
        IEventStoreOptions Upcast(
            Type eventType,
            string eventTypeName,
            JsonTransformation jsonTransformation
        );

        /// <summary>
        ///     <para>
        ///         Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         By calling it, you tell that instead of the old CLR type, for the specific event type name,
        ///         you'd like to get the new CLR event type.
        ///         Provided function takes the deserialized object of the old event type and returns the new, mapped one.
        ///     </para>
        ///     <para>
        ///         Internally it uses default deserialization and event type mapping for old CLR type
        ///         and calls the mapping function.
        ///         In your application code, you should use only the new event type in the aggregation and projection logic.
        ///         See more in
        ///         <a href="https://martendb.io/events/versioning.html#transformation-with-clr-types-will-look-like-this">documentation</a>
        ///     </para>
        /// </summary>
        /// <param name="eventTypeName">Event type name</param>
        /// <param name="upcast">Event payload transformation, upcasting object of old CLR event type into the new one</param>
        /// <typeparam name="TOldEvent">Old CLR event type</typeparam>
        /// <typeparam name="TEvent">New CLR event type</typeparam>
        /// <returns>Event store options, to allow fluent definition</returns>
        public IEventStoreOptions Upcast<TOldEvent, TEvent>(string eventTypeName, Func<TOldEvent, TEvent> upcast)
            where TOldEvent : class
            where TEvent : class;

        /// <summary>
        ///     <para>
        ///         Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         By calling it, you tell that instead of the old CLR type, you'd like to get the new CLR event type.
        ///         Provided function takes the deserialized object of the old event type and returns the new, mapped one.
        ///     </para>
        ///     <para>
        ///         Internally it uses default deserialization and event type mapping for old CLR type
        ///         and calls the mapping function.
        ///         In your application code, you should use only the new event type in the aggregation and projection logic.
        ///         See more in
        ///         <a href="https://martendb.io/events/versioning.htmltransformation-with-clr-types-will-look-like-this">documentation</a>
        ///     </para>
        /// </summary>
        /// <param name="upcast">Event payload transformation, upcasting object of old CLR event type into the new one</param>
        /// <typeparam name="TOldEvent">Old CLR event type</typeparam>
        /// <typeparam name="TEvent">New CLR event type</typeparam>
        /// <returns>Event store options, to allow fluent definition</returns>
        public IEventStoreOptions Upcast<TOldEvent, TEvent>(Func<TOldEvent, TEvent> upcast)
            where TOldEvent : class
            where TEvent : class;

        /// <summary>
        ///     <para>
        ///         Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         By calling it, you tell that instead of the old CLR type, for the specific event type name,
        ///         you'd like to get the new CLR event type.
        ///         Provided function takes the deserialized object of the old event type and returns the new, mapped one.
        ///     </para>
        ///     <para>
        ///         Internally it uses default deserialization and event type mapping for old CLR type
        ///         and calls the mapping function.
        ///         In your application code, you should use only the new event type in the aggregation and projection logic
        ///         See more in <a href="https://martendb.io/events/versioning.html#function-with-clr-types">documentation</a>
        ///     </para>
        ///     <para>
        ///         <b>WARNING!</b> Transformation will only be run in the async API and throw exceptions when run in sync method
        ///         calls.
        ///     </para>
        /// </summary>
        /// <param name="eventTypeName">Event type name</param>
        /// <param name="upcastAsync">
        ///     Async only event payload transformation, upcasting object of old CLR event type into the new
        ///     one
        /// </param>
        /// <typeparam name="TOldEvent">Old CLR event type</typeparam>
        /// <typeparam name="TEvent">New CLR event type</typeparam>
        /// <returns>Event store options, to allow fluent definition</returns>
        /// <exception cref="MartenException">when provided transformation is called in sync API</exception>
        public IEventStoreOptions Upcast<TOldEvent, TEvent>(
            string eventTypeName,
            Func<TOldEvent, CancellationToken, Task<TEvent>> upcastAsync
        )
            where TOldEvent : class
            where TEvent : class;

        /// <summary>
        ///     <para>
        ///         Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         By calling it, you tell that instead of the old CLR type, you'd like to get the new CLR event type.
        ///         Provided function takes the deserialized object of the old event type and returns the new, mapped one.
        ///     </para>
        ///     <para>
        ///         Internally it uses default deserialization and event type mapping for old CLR type
        ///         and calls the mapping function.
        ///         In your application code, you should use only the new event type in the aggregation and projection logic.
        ///         See more in <a href="https://martendb.io/events/versioning.html#function-with-clr-types">documentation</a>
        ///     </para>
        ///     <para>
        ///         <b>WARNING!</b> Transformation will only be run in the async API and throw exceptions when run in sync method
        ///         calls.
        ///     </para>
        /// </summary>
        /// <param name="upcastAsync">
        ///     Async only event payload transformation, upcasting object of old CLR event type into the new
        ///     one
        /// </param>
        /// <typeparam name="TOldEvent">Old CLR event type</typeparam>
        /// <typeparam name="TEvent">New CLR event type</typeparam>
        /// <exception cref="MartenException">when provided transformation is called in sync API</exception>
        /// <returns>Event store options, to allow fluent definition</returns>
        public IEventStoreOptions Upcast<TOldEvent, TEvent>(
            Func<TOldEvent, CancellationToken, Task<TEvent>> upcastAsync)
            where TOldEvent : class
            where TEvent : class;

        /// <summary>
        ///     <para>
        ///         Method defines the set of event JSON payload transformations. Each of them "upcasts" one event schema into
        ///         another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         See more in <a href="https://martendb.io/events/versioning.html#upcasting-with-classes">documentation</a>
        ///     </para>
        /// </summary>
        /// <param name="upcasters">List of upcasters transforming ("upcasting") events JSON payloads from one schema to another.</param>
        /// <returns>Event store options, to allow fluent definition</returns>
        IEventStoreOptions Upcast(params IEventUpcaster[] upcasters);

        /// <summary>
        ///     <para>
        ///         Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
        ///         You can use it to handle the event schema versioning/migration.
        ///     </para>
        ///     <para>
        ///         See more in <a href="https://martendb.io/events/versioning.html#upcasting-with-classes">documentation</a>
        ///     </para>
        /// </summary>
        /// <param name="upcasters">Upcaster type transforming ("upcasting") event JSON payload from one schema to another.</param>
        /// <returns>Event store options, to allow fluent definition</returns>
        IEventStoreOptions Upcast<TUpcaster>() where TUpcaster : IEventUpcaster, new();

        /// <summary>
        /// Register a policy for how to remove or mask protected information
        /// for an event type "T" or series of event types that can be cast
        /// to "T"
        /// </summary>
        /// <param name="action">Action to mask the current object</param>
        /// <typeparam name="T"></typeparam>
        void AddMaskingRuleForProtectedInformation<T>(Action<T> action);

        /// <summary>
        /// Register a policy for how to remove or mask protected information
        /// for an event type "T" or series of event types that can be cast
        /// to "T"
        /// </summary>
        /// <param name="func">Function to replace the event with a masked event</param>
        /// <typeparam name="T"></typeparam>
        void AddMaskingRuleForProtectedInformation<T>(Func<T, T> func);

        /// <summary>
        /// Register a strong-typed identifier as a tag type for Dynamic Consistency Boundary (DCB) support.
        /// This creates a dedicated tag table for efficient cross-stream querying and consistency checks.
        /// </summary>
        /// <typeparam name="TTag">A strong-typed identifier type (e.g., StudentId)</typeparam>
        /// <returns>The tag type registration for further configuration</returns>
        ITagTypeRegistration RegisterTagType<TTag>() where TTag : notnull;

        /// <summary>
        /// Register a strong-typed identifier as a tag type with a custom table name suffix.
        /// </summary>
        /// <typeparam name="TTag">A strong-typed identifier type</typeparam>
        /// <param name="tableSuffix">Custom table name suffix (e.g., "custom_student")</param>
        /// <returns>The tag type registration for further configuration</returns>
        ITagTypeRegistration RegisterTagType<TTag>(string tableSuffix) where TTag : notnull;

        /// <summary>
        /// The registered tag types for DCB support.
        /// </summary>
        IReadOnlyList<ITagTypeRegistration> TagTypes { get; }

        /// <summary>
        /// How DCB tags are physically stored. Defaults to <see cref="DcbStorageMode.TagTables"/>
        /// (one Postgres table per tag type). Set to <see cref="DcbStorageMode.HStore"/> to
        /// store tags inline on <c>mt_events.tags</c> using Postgres' <c>hstore</c> extension
        /// and eliminate the per-query LEFT JOINs across tag tables.
        /// </summary>
        DcbStorageMode DcbStorageMode { get; set; }

        /// <summary>
        /// When enabled, adds heartbeat, agent_status, pause_reason, running_on_node, and
        /// warning/critical-behind-threshold columns to the event progression table for
        /// CritterWatch monitoring.
        /// <para>
        /// This is the long-standing Marten-side toggle; #4686 added the storage-agnostic
        /// <see cref="IEventStoreInstrumentation.ExtendedProgressionEnabled"/> as a sibling so
        /// store-agnostic monitoring tooling (e.g. <c>Wolverine.CritterWatch.Marten</c>) can flip
        /// the switch via the JasperFx.Events abstraction without referencing Marten's concrete
        /// option type. Both names refer to the same underlying field; new code is encouraged to
        /// prefer <c>ExtendedProgressionEnabled</c> on the interface.
        /// </para>
        /// </summary>
        public bool EnableExtendedProgressionTracking { get; set; }
    }
}

public static class EventStoreOptionsExtensions
{
    /// <summary>
    ///     Maps CLR event type as particular event type name and suffix. This is useful for event type migration.
    ///     See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="eventTypeName">Event type name</param>
    /// <param name="suffix">Event type name suffix</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    public static IEventStoreOptions MapEventTypeWithNameSuffix<TEvent>(
        this IEventStoreOptions options,
        string eventTypeName,
        string suffix
    )
        where TEvent : class
    {
        options.MapEventType<TEvent>(eventTypeName.GetEventTypeNameWithSuffix(suffix));
        return options;
    }

    /// <summary>
    ///     Maps CLR event type as particular event type name and suffix. This is useful for event type migration.
    ///     See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="suffix">Event type name suffix</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    public static IEventStoreOptions MapEventTypeWithNameSuffix<TEvent>(
        this IEventStoreOptions options,
        string suffix
    )
        where TEvent : class
    {
        options.MapEventType<TEvent>(GetEventTypeNameWithSuffix<TEvent>(suffix));
        return options;
    }

    /// <summary>
    ///     Maps CLR event type as particular event type name and suffix. This is useful for event type migration.
    ///     See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="eventTypeName">Event type name</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    public static IEventStoreOptions MapEventTypeWithSchemaVersion<TEvent>(
        this IEventStoreOptions options,
        uint schemaVersion
    )
        where TEvent : class
    {
        options.MapEventType<TEvent>(GetEventTypeNameWithSchemaVersion(typeof(TEvent), schemaVersion));
        return options;
    }

    /// <summary>
    ///     Maps CLR event type as particular event type name and suffix. This is useful for event type migration.
    ///     See more in <a href="https://martendb.io/events/versioning.html#event-type-name-migration">documentation</a>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="eventTypeName">Event type name</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    public static IEventStoreOptions MapEventTypeWithSchemaVersion<TEvent>(
        this IEventStoreOptions options,
        string eventTypeName,
        uint schemaVersion
    )
        where TEvent : class
    {
        options.MapEventType<TEvent>(GetEventTypeNameWithSchemaVersion(eventTypeName, schemaVersion));
        return options;
    }

    /// <summary>
    ///     <para>
    ///         Method defines the JSON payload transformation. It "upcasts" one event schema into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By calling it, you tell that you'd like to get the particular CLR event type.
    ///         Event type name will be used from the default <c>TEvent</c> mapping.
    ///         JSON transformation defines the custom mapping from JSON string to the CLR object.
    ///     </para>
    ///     <para>
    ///         When you define it, default deserialization for the particular event type won't be used.
    ///         See more in
    ///         <a href="https://martendb.io/events/versioning.html#raw-json-transformation-with-json-net">documentation</a>
    ///     </para>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="eventTypeName">Event type name</param>
    /// <param name="jsonTransformation">Event payload transformation</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    /// <returns>Event store options, to allow fluent definition</returns>
    public static IEventStoreOptions Upcast<TEvent>(
        this IEventStoreOptions options,
        JsonTransformation jsonTransformation
    ) where TEvent : class
    {
        return options.Upcast<TEvent>(GetEventTypeName<TEvent>(), jsonTransformation);
    }

    /// <summary>
    ///     <para>
    ///         Method defines the event JSON payload transformation. It "upcasts" one event schema into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By calling it, you tell that you'd like to get the particular CLR event type.
    ///         Event type name will be used from the default <see cref="eventType" /> mapping.
    ///         JSON transformation defines the custom mapping from JSON string to the CLR object.
    ///     </para>
    ///     <para>
    ///         When you define it, default deserialization for the particular event type won't be used.
    ///         See more in
    ///         <a href="https://martendb.io/events/versioning.html#raw-json-transformation-with-json-net">documentation</a>
    ///     </para>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="eventType">Mapped CLR event type</param>
    /// <param name="jsonTransformation">Event payload transformation</param>
    /// <returns>Event store options, to allow fluent definition</returns>
    public static IEventStoreOptions Upcast(
        this IEventStoreOptions options,
        Type eventType,
        JsonTransformation jsonTransformation
    )
    {
        return options.Upcast(eventType, eventType.GetEventTypeName(), jsonTransformation);
    }

    /// <summary>
    ///     <para>
    ///         Method defines the JSON payload transformation. It "upcasts" one event schema version into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By calling it, you tell that for provided event type name, you'd like to get the particular CLR event type.
    ///         JSON transformation defines the custom mapping from JSON string to the CLR object.
    ///     </para>
    ///     <para>
    ///         When you define it, default deserialization for the particular event type won't be used.
    ///         See more in
    ///         <a href="https://martendb.io/events/versioning.html#raw-json-transformation-with-json-net">documentation</a>
    ///     </para>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <param name="jsonTransformation">Event payload transformation</param>
    /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
    /// <returns>Event store options, to allow fluent definition</returns>
    public static IEventStoreOptions Upcast<TEvent>(
        this IEventStoreOptions options,
        uint schemaVersion,
        JsonTransformation jsonTransformation
    ) where TEvent : class
    {
        return options.Upcast<TEvent>(GetEventTypeNameWithSchemaVersion<TEvent>(schemaVersion), jsonTransformation);
    }

    /// <summary>
    ///     <para>
    ///         Method defines the event JSON payload transformation. It "upcasts" one event schema version into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By calling it, you tell that for provided event type name, you'd like to get the particular CLR event type.
    ///         JSON transformation defines the custom mapping from JSON string to the CLR object.
    ///     </para>
    ///     <para>
    ///         When you define it, default deserialization for the particular event type won't be used.
    ///         See more in
    ///         <a href="https://martendb.io/events/versioning.html#raw-json-transformation-with-json-net">documentation</a>
    ///     </para>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="eventType">Mapped CLR event type</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <param name="jsonTransformation">Event payload transformation</param>
    /// <returns>Event store options, to allow fluent definition</returns>
    public static IEventStoreOptions Upcast(
        this IEventStoreOptions options,
        Type eventType,
        uint schemaVersion,
        JsonTransformation jsonTransformation
    )
    {
        return options.Upcast(eventType, GetEventTypeNameWithSchemaVersion(eventType, schemaVersion),
            jsonTransformation);
    }

    /// <summary>
    ///     <para>
    ///         Method defines the event JSON payload transformation. It "upcasts" one event schema version into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By calling it, you tell that instead of the old CLR type, for the specific event type name,
    ///         you'd like to get the new CLR event type.
    ///         Provided function takes the deserialized object of the old event type and returns the new, mapped one.
    ///     </para>
    ///     <para>
    ///         Internally it uses default deserialization and event type mapping for old CLR type
    ///         and calls the mapping function.
    ///         In your application code, you should use only the new event type in the aggregation and projection logic.
    ///         See more in
    ///         <a href="https://martendb.io/events/versioning.html#transformation-with-clr-types-will-look-like-this">documentation</a>
    ///     </para>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <param name="upcast">Event payload transformation, upcasting object of old CLR event type into the new one</param>
    /// <typeparam name="TOldEvent">Old CLR event type</typeparam>
    /// <typeparam name="TEvent">New CLR event type</typeparam>
    /// <returns>Event store options, to allow fluent definition</returns>
    public static IEventStoreOptions Upcast<TOldEvent, TEvent>(
        this IEventStoreOptions options,
        uint schemaVersion,
        Func<TOldEvent, TEvent> upcast
    )
        where TOldEvent : class
        where TEvent : class
    {
        return options.Upcast(GetEventTypeNameWithSchemaVersion<TOldEvent>(schemaVersion), upcast);
    }


    /// <summary>
    ///     <para>
    ///         Method defines the event JSON payload transformation. It "upcasts" one event schema version into another.
    ///         You can use it to handle the event schema versioning/migration.
    ///     </para>
    ///     <para>
    ///         By calling it, you tell that instead of the old CLR type, for the specific event type name,
    ///         you'd like to get the new CLR event type.
    ///         Provided function takes the deserialized object of the old event type and returns the new, mapped one.
    ///     </para>
    ///     <para>
    ///         Internally it uses default deserialization and event type mapping for old CLR type
    ///         and calls the mapping function.
    ///         In your application code, you should use only the new event type in the aggregation and projection logic
    ///         See more in <a href="https://martendb.io/events/versioning.html#function-with-clr-types">documentation</a>
    ///     </para>
    ///     <para>
    ///         <b>WARNING!</b> Transformation will only be run in the async API and throw exceptions when run in sync method
    ///         calls.
    ///     </para>
    /// </summary>
    /// <param name="options">Event store options</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <param name="upcastAsync">
    ///     Async only event payload transformation, upcasting object of old CLR event type into the new
    ///     one
    /// </param>
    /// <typeparam name="TOldEvent">Old CLR event type</typeparam>
    /// <typeparam name="TEvent">New CLR event type</typeparam>
    /// <returns>Event store options, to allow fluent definition</returns>
    /// <exception cref="MartenException">when provided transformation is called in sync API</exception>
    public static IEventStoreOptions Upcast<TOldEvent, TEvent>(
        this IEventStoreOptions options,
        uint schemaVersion,
        Func<TOldEvent, CancellationToken, Task<TEvent>> upcastAsync
    )
        where TOldEvent : class
        where TEvent : class
    {
        return options.Upcast(GetEventTypeNameWithSchemaVersion<TOldEvent>(schemaVersion), upcastAsync);
    }



}
