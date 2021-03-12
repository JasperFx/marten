using System;
using System.Collections.Generic;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events
{
    public interface IEventStoreOptions
    {
        /// <summary>
        /// Advanced configuration for the asynchronous projection execution
        /// </summary>
        DaemonSettings Daemon { get; }

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
        /// Configuration for all event store projections
        /// </summary>
        ProjectionCollection Projections { get; }

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
    }
}
