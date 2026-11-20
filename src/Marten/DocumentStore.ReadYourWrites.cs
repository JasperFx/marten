#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Daemon;

namespace Marten;

public partial class DocumentStore
{
    /// <summary>
    ///     #6004: wait until the asynchronous projections in <paramref name="projections" /> have processed
    ///     <paramref name="position" />. See
    ///     <see cref="IDocumentStore.WaitForProjectionsAsync(EventStorePosition, IReadOnlyList{Type}, TimeSpan, CancellationToken)" />.
    /// </summary>
    public async Task<ProjectionWaitResult> WaitForProjectionsAsync(EventStorePosition position,
        IReadOnlyList<Type> projections, TimeSpan timeout, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(position);

        // Not ((IEventStore)this).WaitForProjectionsAsync: this public method IS that member's implementation
        var shards = ProjectionPositionWait.ShardsFor(this, projections);
        var database = await ProjectionPositionWait.FindDatabaseAsync(this, position, token).ConfigureAwait(false);
        return await ProjectionPositionWait.WaitAsync(database, position, shards, timeout, token).ConfigureAwait(false);
    }

    /// <summary>
    ///     #6004: <see cref="WaitForProjectionsAsync(EventStorePosition, IReadOnlyList{Type}, TimeSpan, CancellationToken)" />
    ///     by projection or subscription name.
    /// </summary>
    public async Task<ProjectionWaitResult> WaitForProjectionsAsync(EventStorePosition position,
        IReadOnlyList<string> projectionNames, TimeSpan timeout, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(position);

        var shards = ProjectionPositionWait.ShardsNamed(((IEventStore)this).RegisteredShardNames(), projectionNames);
        var database = await ProjectionPositionWait.FindDatabaseAsync(this, position, token).ConfigureAwait(false);
        return await ProjectionPositionWait.WaitAsync(database, position, shards, timeout, token).ConfigureAwait(false);
    }
}
