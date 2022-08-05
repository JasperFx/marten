using System;
using System.Collections.Generic;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Services.Json;
using Marten.Storage;
#nullable enable
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
        EventJsonTransformation MapEventType<TEvent>(string eventTypeName) where TEvent : class;

        /// <summary>
        /// Maps CLR event type as particular event type name. This is useful for event type migration.
        /// See more in docs: https://martendb.io/events/versioning.html#event-type-name-migration
        /// </summary>
        /// <param name="eventType">Event type name</param>
        /// <param name="eventTypeName">Mapped CLR event type</param>
        EventJsonTransformation MapEventType(Type eventType, string eventTypeName);

        public MetadataConfig MetadataConfig { get; }
    }
}
