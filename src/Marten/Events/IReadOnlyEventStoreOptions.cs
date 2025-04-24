#nullable enable
using System.Collections.Generic;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using JasperFx.Events.Subscriptions;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events;

public interface IReadOnlyEventStoreOptions
{
    /// <summary>
    ///     Advanced configuration for the asynchronous projection execution
    /// </summary>
    IReadOnlyDaemonSettings Daemon { get; }

    /// <summary>
    ///     Configure whether event streams are identified with Guid or strings
    /// </summary>
    public StreamIdentity StreamIdentity { get; }

    /// <summary>
    ///     Configure the event sourcing storage for multi-tenancy
    /// </summary>
    TenancyStyle TenancyStyle { get; }

    /// <summary>
    ///     Override the database schema name for event related tables. By default this
    ///     is the same schema as the document storage
    /// </summary>
    string DatabaseSchemaName { get; }

    /// <summary>
    ///     Metadata configuration
    /// </summary>
    IReadonlyMetadataConfig MetadataConfig { get; }

    /// <summary>
    /// Opt into having Marten create a unique index on Event.Id. The default is false. This may
    /// be helpful if you need to create an external reference id to another system, or need to
    /// load events by their Id
    /// </summary>
    bool EnableUniqueIndexOnEventId { get; set; }

    public EventAppendMode AppendMode { get; set; }

    /// <summary>
    ///     Configuration for all event store projections
    /// </summary>
    IReadOnlyList<ISubscriptionSource> Projections();

    IReadOnlyList<IEventType> AllKnownEventTypes();

    /// <summary>
    /// Opt into using PostgreSQL list partitioning. This can have significant performance and scalability benefits
    /// *if* you are also aggressively using event stream archiving
    /// </summary>
    bool UseArchivedStreamPartitioning { get; set; }

    /// <summary>
    /// Optional extension point to receive published messages as a side effect from
    /// aggregation projections
    /// </summary>
    IMessageOutbox MessageOutbox { get; set; }

    /// <summary>
    /// Opt into some performance optimizations for projection rebuilds for both single stream and
    /// multi-stream projections. This will result in new table columns and a potential database
    /// migration. This will be a default in Marten 8.
    /// </summary>
    bool UseOptimizedProjectionRebuilds { get; set; }

    /// <summary>
    /// Does Marten require a stream type for any new event streams? This will also
    /// validate that an event stream already exists as part of appending events. Default in 7.0 is false,
    /// but this will be true in 8.0
    /// </summary>
    bool UseMandatoryStreamTypeDeclaration { get; set; }
}
