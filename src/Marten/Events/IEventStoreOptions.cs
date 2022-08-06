#nullable enable

using System;
using System.Collections.Generic;
using Marten.Services.Json.Transformations;
using Marten.Storage;

namespace Marten.Events
{
    public interface IEventStoreOptions
    {
        /// <summary>
        /// Configure whether event streams are identified with Guid or strings
        /// </summary>
        StreamIdentity StreamIdentity { get; set; }

        /// <summary>
        /// Configure the event sourcing storage for multi-tenancy
        /// </summary>
        TenancyStyle TenancyStyle { get; set; }

        /// <summary>
        /// Override the database schema name for event related tables. By default this
        /// is the same schema as the document storage
        /// </summary>
        string DatabaseSchemaName { get; set; }

        /// <summary>
        /// Register an event type with Marten. This isn't strictly necessary for normal usage,
        /// but can help Marten with asynchronous projections where Marten hasn't yet encountered
        /// the event type
        /// </summary>
        /// <param name="eventType"></param>
        void AddEventType(Type eventType);

        /// <summary>
        /// Register an event type with Marten. This isn't strictly necessary for normal usage,
        /// but can help Marten with asynchronous projections where Marten hasn't yet encountered
        /// the event type
        /// </summary>
        /// <param name="types"></param>
        void AddEventTypes(IEnumerable<Type> types);

        /// <summary>
        /// Maps CLR event type as particular event type name. This is useful for event type migration.
        /// See more in docs: https://martendb.io/events/versioning.html#event-type-name-migration
        /// </summary>
        /// <param name="eventTypeName">Event type name</param>
        /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
        void MapEventType<TEvent>(string eventTypeName) where TEvent : class;

        /// <summary>
        /// Maps CLR event type as particular event type name. This is useful for event type migration.
        /// See more in docs: https://martendb.io/events/versioning.html#event-type-name-migration
        /// </summary>
        /// <param name="eventType">Event type name</param>
        /// <param name="eventTypeName">Mapped CLR event type</param>
        void MapEventType(Type eventType, string eventTypeName);


        /// <summary>
        /// Maps CLR event type as particular event type name allowing to provide custom transformation.
        /// This is useful for event type migration.
        /// See more in docs: https://martendb.io/events/versioning.html#event-type-name-migration
        /// </summary>
        /// <param name="eventTypeName">Event type name</param>
        /// <param name="jsonTransformation">Event payload transformation</param>
        /// <typeparam name="TEvent">Mapped CLR event type</typeparam>
        IEventStoreOptions Upcast<TEvent>(
            string eventTypeName,
            JsonTransformation jsonTransformation
        ) where TEvent : class;

        /// <summary>
        /// Maps CLR event type as particular event type name allowing to provide custom transformation.
        /// This is useful for event type migration.
        /// See more in docs: https://martendb.io/events/versioning.html#event-type-name-migration
        /// </summary>
        /// <param name="eventType">Event type name</param>
        /// <param name="eventTypeName">Mapped CLR event type</param>
        /// <param name="jsonTransformation">Event payload transformation</param>
        IEventStoreOptions Upcast(
            Type eventType,
            string eventTypeName,
            JsonTransformation jsonTransformation
        );

        /// <summary>
        /// Maps CLR event type as particular event type name allowing to provide custom transformation.
        /// This is useful for event type migration.
        /// See more in docs: https://martendb.io/events/versioning.html#event-type-name-migration
        /// </summary>
        /// <param name="upcaster">List of upcasters transforming event</param>
        /// <returns>Store options allowing to continue mapping definition</returns>
        IEventStoreOptions Upcast(params IEventUpcaster[] upcasters);

        /// <summary>
        /// Maps CLR event type as particular event type name allowing to provide custom transformation.
        /// This is useful for event type migration.
        /// See more in docs: https://martendb.io/events/versioning.html#event-type-name-migration
        /// </summary>
        /// <typeparam name="TUpcaster">Upcaster class type. It has to derive from IUpcaster and have default public constructor</typeparam>
        /// <returns>Store options allowing to continue mapping definition</returns>
        IEventStoreOptions Upcast<TUpcaster>() where TUpcaster: IEventUpcaster, new ();

        public MetadataConfig MetadataConfig { get; }
    }
}
