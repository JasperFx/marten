#nullable enable
using System.Collections.Generic;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Subscriptions;
using Marten.Events.Aggregation;
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
    /// Opt into having Marten process "side effects" on aggregation projections (SingleStreamProjection/MultiStreamProjection) while
    /// running in an Inline lifecycle. Default is false;
    /// </summary>
    bool EnableSideEffectsOnInlineProjections { get; }

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
    bool UseOptimizedProjectionRebuilds { get; set; }

    /// <summary>
    /// Does Marten require a stream type for any new event streams? This will also
    /// validate that an event stream already exists as part of appending events. Default in 7.0 is false,
    /// but this will be true in 8.0
    /// </summary>
    bool UseMandatoryStreamTypeDeclaration { get; set; }
}
