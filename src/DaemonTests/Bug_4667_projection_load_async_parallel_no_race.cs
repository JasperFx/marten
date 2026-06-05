using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DaemonTests;

/// <summary>
/// #4667 Phase 3 acceptance: a user-supplied aggregation projection whose Apply
/// reaches into <see cref="Marten.IDocumentOperations.LoadAsync{T}"/> from inside
/// the async daemon's 10-wide parallel <c>Block&lt;EventSliceExecution&gt;</c>
/// fan-out must not throw and must produce correct aggregates. Before Phase 3
/// closed the user-code escape hatch, those LoadAsync calls per-row-wrote into
/// the shared session's <c>Versions</c> / <c>ItemMap</c> / <c>ChangeTrackers</c>
/// — a documented race source under the daemon's shared-session model.
/// </summary>
public class Bug_4667_projection_load_async_parallel_no_race: BugIntegrationContext
{
    [Fact]
    public async Task user_load_async_inside_apply_under_parallel_daemon_fanout()
    {
        // 1000-stream rebuild against an aggregation projection whose Apply
        // calls session.LoadAsync<User> per event — large enough to push the
        // daemon's Block(10, ...) fan-out across multiple parallel waves but
        // small enough to keep the test under a minute. UseIdentityMapForAggregates
        // is left at its default (false in the daemon path) — the path
        // Phase 3 closes.
        StoreOptions(opts => opts.Projections.Add(new OrderProjection(), ProjectionLifecycle.Async));

        // Side document the projection's Apply reaches for. One per stream
        // so every event hits a fresh LoadAsync — exercising the chokepoint
        // on every iteration rather than re-using a cached identity-map hit.
        const int streamCount = 250;
        const int eventsPerStream = 4;

        var customerIds = Enumerable.Range(0, streamCount).Select(_ => Guid.NewGuid()).ToArray();
        await using (var session = theStore.LightweightSession())
        {
            foreach (var id in customerIds)
            {
                session.Store(new Bug4667Customer { Id = id, Name = $"customer-{id:N}" });
            }
            await session.SaveChangesAsync();
        }

        var streamIds = new Guid[streamCount];
        await using (var session = theStore.LightweightSession())
        {
            for (var i = 0; i < streamCount; i++)
            {
                streamIds[i] = Guid.NewGuid();
                var customerId = customerIds[i];
                session.Events.StartStream<Bug4667Order>(
                    streamIds[i],
                    Enumerable.Range(0, eventsPerStream)
                        .Select(j => (object)new Bug4667ItemPicked(customerId, j))
                        .ToArray());
            }
            await session.SaveChangesAsync();
        }

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<Bug4667Order>(CancellationToken.None);

        await using var query = theStore.QuerySession();
        var aggregates = await query.LoadManyAsync<Bug4667Order>(streamIds);

        aggregates.Count.ShouldBe(streamCount);
        foreach (var agg in aggregates)
        {
            // The Apply set CustomerName from the side-loaded Bug4667Customer
            // and incremented PickedCount per event. If the parallel race the
            // chokepoint guards against fires, expect either a thrown
            // CollectionMutated / KeyNotFound or an aggregate with missing
            // CustomerName / wrong PickedCount.
            agg.PickedCount.ShouldBe(eventsPerStream);
            agg.CustomerName.ShouldNotBeNull();
            agg.CustomerName.ShouldStartWith("customer-");
        }
    }
}

public class Bug4667Order
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int PickedCount { get; set; }
}

public class Bug4667Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public record Bug4667ItemPicked(Guid CustomerId, int Index);

public partial class OrderProjection: SingleStreamProjection<Bug4667Order, Guid>
{
    public async Task Apply(Bug4667ItemPicked @event, Bug4667Order order, IQuerySession session)
    {
        // The session here is the daemon's shared ProjectionDocumentSession
        // for the (range, tenant). LoadAsync<Bug4667Customer> routes through
        // the QuerySession.ExecuteLoadOneAsync chokepoint and, by Phase 3,
        // dispatches to IDocumentStorage<,>.LoadProjectedAsync — bypassing
        // every session-shared dictionary.
        var customer = await session.LoadAsync<Bug4667Customer>(@event.CustomerId);
        if (customer is not null)
        {
            order.CustomerName = customer.Name;
        }
        order.PickedCount++;
    }
}
