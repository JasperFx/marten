#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten.Events.Dcb;

namespace Marten.Events;

internal partial class EventStore
{
    public async Task<IEventBoundary<T>> FetchForWritingByTags<T>(EventTagQuery query,
        CancellationToken cancellation = default) where T : class
    {
        if (query.Conditions.Count == 0)
        {
            throw new ArgumentException("EventTagQuery must have at least one condition.");
        }

        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), cancellation).ConfigureAwait(false);

        // #4591: route the non-batched path through the same handler used by
        // BatchedQuery.FetchForWritingByTags. The handler is now responsible
        // for (a) selecting events, (b) reading the captured per-tag versions
        // from the mt_dcb_tag_version side table, and (c) queuing the
        // DcbTagVersionAssertion that bumps those rows at SaveChangesAsync
        // time. Keeping the two entry points pointed at one implementation
        // means the boundary serialization can't drift between them.
        var handler = new FetchForWritingByTagsHandler<T>(_store, query);
        return await _session.ExecuteHandlerAsync(handler, cancellation).ConfigureAwait(false);
    }
}
