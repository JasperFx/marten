using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Daemon.HighWater;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon: JasperFxAsyncDaemon<IDocumentOperations, IQuerySession, IProjection>, IProjectionDaemon
{
    private readonly DocumentStore _store;

    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILoggerFactory loggerFactory,
        IHighWaterDetector detector)
        : base(store, database, loggerFactory, detector, store.Options.Projections)
    {
        _store = store;
        store.TrackDaemon(this);
        store.LogProjectionParkingOnce(loggerFactory.CreateLogger<ProjectionDaemon>());
    }

    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILogger logger,
        IHighWaterDetector detector)
        : base(store, database, logger, detector, store.Options.Projections)
    {
        _store = store;
        store.TrackDaemon(this);
        store.LogProjectionParkingOnce(logger);
    }

    /// <summary>
    ///     Rebuild one identity of a single-stream projection inside the shard that runs it (marten#6071,
    ///     jasperfx#1201). JasperFx's, with one refusal of Marten's own in front of it.
    /// </summary>
    /// <remarks>
    ///     A projection whose documents are kept in custom projection storage (an EF Core entity, say) is refused
    ///     with <see cref="IdentityRebuildRefusal.NotSupportedByStore" />. The re-fold clears the identity with
    ///     <c>IProjectionStorage.HardDeleteById</c> inside the shard's own batch, and that storage need not
    ///     implement it. Finding that out inside the batch would fail the batch, so it is refused here, before
    ///     anything is queued. Rebuild such a projection with <c>RebuildProjectionAsync</c>.
    /// </remarks>
    public new Task<IdentityRebuildResult> RebuildIdentityAsync(string projectionName, string? tenantId,
        object identity, CancellationToken token)
        => RebuildIdentityAsync(projectionName,
            new IdentityRebuildRequest(tenantId ?? StorageConstants.DefaultTenantId, identity), token);

    /// <inheritdoc cref="RebuildIdentityAsync(string, string, object, CancellationToken)" />
    public new Task<IdentityRebuildResult> RebuildIdentityAsync(string projectionName,
        IdentityRebuildRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(projectionName);
        ArgumentNullException.ThrowIfNull(request);

        if (_store.Options.Projections.TryFindProjection(projectionName, out var source)
            && source is IAggregateProjection aggregate
            && _store.Options.CustomProjectionStorageProviders.ContainsKey(aggregate.AggregateType))
        {
            return Task.FromException<IdentityRebuildResult>(new IdentityRebuildRefusedException(projectionName,
                request.Identity, IdentityRebuildRefusal.NotSupportedByStore,
                $"'{aggregate.AggregateType.Name}' is kept in custom projection storage, which Marten cannot clear "
                + "inside the shard's batch. Rebuild the projection with RebuildProjectionAsync."));
        }

        return base.RebuildIdentityAsync(projectionName, request, token);
    }

    public override string ToString()
    {
        return $"Daemon: {Database.DatabaseUri} ({Database.Identifier})";
    }
}
