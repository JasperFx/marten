using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Daemon;

/// <summary>
///     Used within the async daemon as a buffer for custom projections
///     to run events by tenant
/// </summary>
internal class TenantedEventRangeGroup: EventRangeGroup
{
    private readonly IMartenDatabase _daemonDatabase;
    private readonly IProjection _projection;
    private readonly AsyncOptions _asyncOptions;
    private readonly DocumentStore _store;

    public TenantedEventRangeGroup(
        IDocumentStore store,
        IMartenDatabase daemonDatabase,
        IProjection projection,
        AsyncOptions asyncOptions,
        EventRange range,
        CancellationToken shardCancellation): base(range, shardCancellation)
    {
        _store = (DocumentStore)store;
        _daemonDatabase = daemonDatabase;
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _asyncOptions = asyncOptions;

        buildGroups();
    }

    public IList<TenantActionGroup> Groups { get; } = new List<TenantActionGroup>();

    private void buildGroups()
    {
        var byTenant = Range.Events.GroupBy(x => x.TenantId);
        foreach (var group in byTenant)
        {
            var tenant = new Tenant(group.Key, _daemonDatabase);

            var actions = _store.Events.StreamIdentity switch
            {
                StreamIdentity.AsGuid => group.GroupBy(x => x.StreamId)
                    .Select(events => StreamAction.For(events.Key, events.ToList())),

                StreamIdentity.AsString => group.GroupBy(x => x.StreamKey)
                    .Select(events => StreamAction.For(events.Key, events.ToList())),

                _ => null
            };

            Groups.Add(new TenantActionGroup(tenant, actions));
        }
    }

    public override ValueTask SkipEventSequence(long eventSequence, IMartenDatabase database)
    {
        Range.SkipEventSequence(eventSequence);
        Groups.Clear();
        buildGroups();
        return default;
    }

    protected override void reset()
    {
        // Nothing
    }

    public override void Dispose()
    {
        // Nothing
    }

    public override string ToString()
    {
        return $"Tenant Group Range for: {Range}";
    }

    public override Task ConfigureUpdateBatch(IShardAgent shardAgent, ProjectionUpdateBatch batch)
    {
        return Parallel.ForEachAsync(Groups, Cancellation,
            async (tenantGroup, token) =>
                await tenantGroup.ApplyEvents(batch, _projection, _asyncOptions, _store, token).ConfigureAwait(false));
    }
}
