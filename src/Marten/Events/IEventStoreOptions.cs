#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Fetching;
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
        ///     Opt in caching of aggregate snapshots between FetchForWriting calls. Disabled for every
        ///     aggregate type by default; see <see cref="CacheAggregatesForWriting{T}" />.
        /// </summary>
        /// <remarks>
        ///     Both members are declared here as well as inherited by <see cref="EventGraph" /> from
        ///     JasperFx's <c>EventRegistry</c>, because <c>StoreOptions.Events</c> is typed as this
        ///     interface rather than as the graph — so without them <c>opts.Events</c> could not reach
        ///     either one.
        /// </remarks>
        AggregateWriteCacheOptions AggregateWriteCaching { get; }

        /// <summary>
        ///     Keep recently fetched snapshots of <typeparamref name="T" /> in a node local cache so that a
        ///     subsequent FetchForWriting can skip loading the stored snapshot and read only the events
        ///     after it. Effectively an identity map for aggregates with a lifetime longer than a session.
        ///     <para>
        ///     The cached snapshot is only ever a baseline: the stream version and any newer events are
        ///     still read from the database on every call, and the optimistic concurrency assertion on
        ///     append is untouched. A stale entry therefore costs a larger delta query, never a wrong
        ///     aggregate. See <see cref="JasperFx.Events.Fetching.IAggregateWriteCache" /> for the full
        ///     semantics, which are shared across the Critter Stack.
        ///     </para>
        ///     <para>
        ///     Supported for the Live, Async and Inline lifecycles; see
        ///     <see cref="EventGraph.CacheAggregatesForWriting{T}" /> for how they differ.
        ///     </para>
        /// </summary>
        /// <param name="sizeLimit">Maximum number of cached aggregates, when the default cache is built</param>
        void CacheAggregatesForWriting<T>(int sizeLimit = 1000) where T : class;

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
        /// Opt-in flag, defaults to false. Enabling it also turns on per-tenant
        /// async-daemon agent distribution for hosts that distribute agents per
        /// identity (see <c>IEventStore.DistributesAgentsPerTenant</c>) — no
        /// further opt-in is needed. See
        /// https://martendb.io/events/multitenancy#per-tenant-event-partitioning.
        /// </para>
        /// <para>
        /// Constraint: requires <c>TenancyStyle.Conjoined</c> on the event
        /// store. Setting this with <c>TenancyStyle.Single</c> throws at
        /// <c>DocumentStore</c> construction — there is nothing to partition
        /// by when every event lives in the default tenant.
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
        ///     <para>
        ///         Typed as the promoted <see cref="JasperFx.Events.IEventBinarySerializer"/> since
        ///         9.26 (jasperfx#669) so a store-agnostic serializer can be registered. Marten's
        ///         own <see cref="IEventBinarySerializer"/> derives from it, so existing
        ///         implementations still assign here unchanged.
        ///     </para>
        /// </summary>
        public JasperFx.Events.IEventBinarySerializer? DefaultBinarySerializer { get; set; }

        /// <summary>
        ///     Opt a single event type into binary serialization (#4515). The event's
        ///     payload is written to the <c>bdata</c> bytea column instead of the
        ///     <c>data</c> jsonb column; existing JSON rows for the same type continue
        ///     to read through the JSON path. Calling this also adds the event type to
        ///     the registry (no separate <see cref="AddEventType{TEvent}"/> call needed).
        /// </summary>
        /// <typeparam name="TEvent">CLR event type to opt in.</typeparam>
        /// <param name="serializer">
        ///     Per-type serializer to use for this event. Widened to the promoted
        ///     <see cref="JasperFx.Events.IEventBinarySerializer"/> in 9.26 (jasperfx#669); Marten's
        ///     own <see cref="IEventBinarySerializer"/> derives from it, so existing call sites are
        ///     unaffected.
        /// </param>
        /// <returns>Event store options, to allow fluent definition.</returns>
        IEventStoreOptions UseBinarySerializer<TEvent>(JasperFx.Events.IEventBinarySerializer serializer);

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
        /// Derive a DCB tag from an event's own data, for every event appended through this store.
        /// <para>
        /// Tag inference can only find a tag when the event exposes a property whose <i>type</i> is the tag
        /// type, and it only runs on the <c>IEventBoundary</c> path. A rule closes both gaps: it works for
        /// an event that names its identifiers as primitives, and it applies wherever the event is built --
        /// ordinary appends, <c>StartStream</c>, aggregate handlers and bulk inserts alike.
        /// </para>
        /// <para>
        /// Return <c>null</c> to leave an event untagged. A rule declared on a base type or interface
        /// applies to every event assignable to it, and several rules may contribute different tag types to
        /// one event. A tag type already on the event is left alone, so a rule never fights an explicit
        /// <c>WithTag</c>.
        /// </para>
        /// </summary>
        /// <typeparam name="TEvent">The event type, or a base type or interface of it</typeparam>
        /// <param name="rule">Returns the tag value for an event, or null to leave it untagged</param>
        void TagWith<TEvent>(Func<TEvent, object?> rule) where TEvent : notnull;

        /// <summary>
        /// Derive every DCB tag an event carries from one store-wide rule, for applications that already
        /// have a single place that knows what an event is about.
        /// <para>
        /// <see cref="TagWith{TEvent}" /> states one tag for one event type. This states all of them for
        /// all of them, so a translator an application already owns can be handed over whole instead of
        /// being restated as one registration per tag type.
        /// </para>
        /// <para>
        /// Return an empty sequence or <c>null</c> to leave an event untagged. The rule receives the event
        /// body: the stream is assigned after the event is built, so it is not available here.
        /// </para>
        /// </summary>
        /// <param name="tagger">Returns the tags for an event body, or nothing to leave it untagged</param>
        void TagEventsBy(Func<object, IEnumerable<object>?> tagger);

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
        /// Opt into building the GIN index over the <c>tags</c> hstore column without blocking writes.
        /// Only has any effect in <see cref="DcbStorageMode.HStore"/> mode. Default is false.
        /// <para>
        /// A plain <c>CREATE INDEX</c> holds ACCESS EXCLUSIVE on <c>mt_events</c> for the whole build,
        /// which on an existing event store of any size is a write outage rather than a migration.
        /// With this on, Marten emits <c>CREATE INDEX CONCURRENTLY</c> instead — or, under
        /// <see cref="UseTenantPartitionedEvents"/>, where PostgreSQL refuses <c>CONCURRENTLY</c> on a
        /// partitioned parent outright, the sequence it does accept: the index created on only the
        /// parent, one concurrent index per tenant partition, and each attached to the parent.
        /// </para>
        /// <para>
        /// The trade-off is that a concurrent build cannot run inside a transaction, so the SQL that
        /// <c>db-patch</c> and <c>db-dump</c> write is no longer runnable as one transactional script.
        /// It is still correct applied against a live database. Use <see cref="IgnoreIndex"/> instead if
        /// you would rather own the index yourself.
        /// </para>
        /// </summary>
        bool BuildHStoreTagIndexConcurrently { get; set; }

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
