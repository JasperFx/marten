#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Storage;

namespace Marten;

// marten#6071: Marten's side of the per-identity rebuild from jasperfx#1201
public partial class DocumentStore
{
    // Every daemon this store built in this process, by hand or through a coordinator. Weak, so a daemon nobody
    // disposed is not kept alive by the store.
    private readonly ConditionalWeakTable<ProjectionDaemon, object> _daemons = new();

    internal void TrackDaemon(ProjectionDaemon daemon) => _daemons.AddOrUpdate(daemon, daemon);

    internal IReadOnlyList<ProjectionDaemon> DaemonsInThisProcess() => _daemons.Select(x => x.Key).ToList();

    bool IEventStore<IDocumentOperations, IQuerySession>.SupportsIdentityRebuild => true;

    Task<IReadOnlyList<IEvent>> IEventStore<IDocumentOperations, IQuerySession>.LoadEventsForIdentityRebuildAsync(
        IEventDatabase database, IdentityRebuildEventRequest request, CancellationToken token)
        => new IdentityRebuildEventLoader(this, (MartenDatabase)database).LoadAsync(request, token);

    /// <summary>
    ///     The asynchronous single-stream projection that writes <paramref name="documentType" />, or null when
    ///     none does.
    /// </summary>
    internal IProjectionSource<IDocumentOperations, IQuerySession>? AsyncSingleStreamProjectionFor(Type documentType)
        => Options.Projections.All.FirstOrDefault(x =>
            x is IAggregateProjection
            {
                Scope: AggregationScope.SingleStream,
                Lifecycle: ProjectionLifecycle.Async
            } aggregate
            && aggregate.AggregateType == documentType);

    /// <summary>
    ///     The first two paths of <c>RebuildSingleStreamAsync</c>: true when a daemon of this store in this process
    ///     rebuilt the identity, a <see cref="IdentityRebuildRefusedException" /> when the projection's shard is live
    ///     somewhere this store cannot reach, and false when neither applies and the caller rebuilds offline.
    /// </summary>
    internal async Task<bool> TryRebuildIdentityThroughDaemonAsync(
        IProjectionSource<IDocumentOperations, IQuerySession> projection, IMartenDatabase database, string tenantId,
        object streamId, CancellationToken token)
    {
        var name = projection.Name;

        // The shard for every tenant, plus this tenant's own under per-tenant partitioning
        var shards = projection.Shards().Select(x => x.Name).ToList();
        if (Options.Events.UseTenantPartitionedEvents)
        {
            shards.AddRange(shards.Select(x => x.ForTenant(tenantId)).ToArray());
        }

        foreach (var daemon in DaemonsInThisProcess())
        {
            if (!string.Equals(daemon.Database.Identifier, database.Identifier, StringComparison.OrdinalIgnoreCase)
                || !shards.Any(x => daemon.StatusFor(x.Identity) == AgentStatus.Running))
            {
                continue;
            }

            var identity = projectedIdentityFor(((IAggregateProjection)projection).IdentityType, streamId);
            await daemon.RebuildIdentityAsync(name, tenantId, identity, token).ConfigureAwait(false);
            return true;
        }

        var progression = await ((MartenDatabase)database)
            .FetchProjectionProgressFor(shards.ToArray(), token).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        foreach (var state in progression)
        {
            IdentityRebuildGuard.AssertNotLiveElsewhere(name, streamId, state, now);
        }

        return false;
    }

    // The stream's Guid or string as the projection keys it: itself, or wrapped in the strong-typed id
    private static object projectedIdentityFor(Type identityType, object streamId)
    {
        if (identityType.IsInstanceOfType(streamId)) return streamId;

        var target = Nullable.GetUnderlyingType(identityType) ?? identityType;
        return StrongTypedIdResolver.TryResolve(target, out var info) && info.SimpleType == streamId.GetType()
            ? StrongTypedIdResolver.Wrap(info, streamId)
            : streamId;
    }

    internal static object DocumentIdentityFor(Type idType, object streamId) => projectedIdentityFor(idType, streamId);
}
