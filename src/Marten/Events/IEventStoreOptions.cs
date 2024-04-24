#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Exceptions;
using Marten.Services.Json.Transformations;
using Marten.Storage;
using Marten.Subscriptions;
using static Marten.Events.EventMappingExtensions;

namespace Marten.Events
{
    public interface IEventStoreOptions
    {
        /// <summary>
        ///     Configure whether event streams are identified with Guid or strings
        /// </summary>
        StreamIdentity StreamIdentity { get; set; }

        /// <summary>
        ///     Configure the event sourcing storage for multi-tenancy
        /// </summary>
        TenancyStyle TenancyStyle { get; set; }

        /// <summary>
        /// Enables global project projections (with single tenancy style) for events with conjoined tenancy
        /// </summary>
        bool EnableGlobalProjectionsForConjoinedTenancy { get; set; }

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
        TimeProvider TimeProvider { get; set; }

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
        options.MapEventType<TEvent>(GetEventTypeNameWithSuffix(eventTypeName, suffix));
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
        return options.Upcast(eventType, GetEventTypeName(eventType), jsonTransformation);
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
