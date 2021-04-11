using System.Collections.Generic;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;
#nullable enable
namespace Marten.Events
{
    public interface IReadOnlyEventStoreOptions
    {
        /// <summary>
        /// Advanced configuration for the asynchronous projection execution
        /// </summary>
        IReadOnlyDaemonSettings Daemon { get; }

        /// <summary>
        /// Configure whether event streams are identified with Guid or strings
        /// </summary>
        StreamIdentity StreamIdentity { get; }

        /// <summary>
        /// Configure the event sourcing storage for multi-tenancy
        /// </summary>
        TenancyStyle TenancyStyle { get; }

        /// <summary>
        /// Override the database schema name for event related tables. By default this
        /// is the same schema as the document storage
        /// </summary>
        string DatabaseSchemaName { get;  }

        /// <summary>
        /// Configuration for all event store projections
        /// </summary>
        IReadOnlyList<IProjectionSource> Projections();

        IReadOnlyList<IEventType> AllKnownEventTypes();

        /// <summary>
        /// Metadata configuration
        /// </summary>
        IReadonlyMetadataConfig MetadataConfig { get; }
    }
}
