#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Subscriptions;
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

        public EventAppendMode AppendMode { get; set; }

        /// <summary>
        /// Opt into using PostgreSQL list partitioning. This can have significant performance and scalability benefits
        /// *if* you are also aggressively using event stream archiving
        /// </summary>
        public bool UseArchivedStreamPartitioning { get; set; }

        /// <summary>
        /// Optional extension point to receive published messages as a side effect from
        /// aggregation projections
        /// </summary>
        public IMessageOutbox MessageOutbox { get; set; }

        /// <summary>
        /// Opt into some performance optimizations for projection rebuilds for both single stream and
        /// multi-stream projections. This will result in new table columns and a potential database
        /// migration.
        /// </summary>
        /// <remarks>
        /// <b>Important Limitations and Future Plans:</b>
        ///
        /// This optimized rebuild functionality is currently very limited. It is only
        /// usable if there is exactly *one* single stream projection consuming events from
        /// any given event stream. If your application has two or more single stream projection
        /// views for the same events (a perfectly valid and common use case), enabling these
        /// optimized rebuilds will **not result in correct behavior**.
        ///
        /// The MartenDB creator has noted that the current limitations of this optimization
        /// are a known area for improvement. This functionality may either be thoroughly
        /// re-addressed and fixed within MartenDB, or potentially externalized into a future
        /// commercial add-on product (e.g., "CritterWatch"), which is planned to offer more
        /// advanced event store tooling. Developers should be aware that while this feature
        /// offers performance benefits in specific, limited scenarios, its broader application
        /// for multiple single stream projections is currently not supported and may be subject
        /// to significant changes in its implementation or availability in future versions.
        /// </remarks>
        public bool UseOptimizedProjectionRebuilds { get; set; }

        /// <summary>
        /// Does Marten require a stream type for any new event streams? This will also
        /// validate that an event stream already exists as part of appending events. Default in 7.0 is false,
        /// but this will be true in 8.0
        /// </summary>
        public bool UseMandatoryStreamTypeDeclaration { get; set; }

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
        /// <param name="action"></param>
        /// <typeparam name="T"></typeparam>
        void AddMaskingRuleForProtectedInformation<T>(Action<T> action);
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
