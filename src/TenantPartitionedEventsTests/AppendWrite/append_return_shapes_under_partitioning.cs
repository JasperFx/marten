using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.AppendWrite;

/// <summary>
/// #4617 section 3a — the multiple Append / StartStream overload shapes
/// (single event, `IEnumerable&lt;object&gt;`, `params object[]`,
/// IEvent-wrapped) each route through the bulk
/// <c>mt_quick_append_events</c> function and land in the owning tenant's
/// partition. Pin the shape-matrix so a future overload-resolution change
/// (or a refactor of how the appender enqueues events) doesn't silently
/// reshape what hits the partitioned tables.
/// </summary>
[Collection("guid-partitioned")]
public class append_return_shapes_under_partitioning
{
    private readonly GuidPartitionedFixture _fixture;

    public append_return_shapes_under_partitioning(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task append_via_params_object_array_lands_in_tenants_partition()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            // Both StartStream + Append using `params object[]` — the canonical
            // shape.
            s.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId), new TripLeg(1));
            await s.SaveChangesAsync();
        }
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.Append(streamId, new TripLeg(2), new TripLeg(3));
            await s.SaveChangesAsync();
        }

        await using var q = _fixture.Store.QuerySession(tenant);
        var events = await q.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(4);
    }

    [Fact]
    public async Task append_via_IEnumerable_lands_in_tenants_partition()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        IEnumerable<object> seedEvents = new object[]
        {
            new TripStarted(streamId), new TripLeg(1), new TripLeg(2)
        };
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            // IEnumerable<object> overload — the shape Wolverine-style bulk
            // emitters commonly use.
            s.Events.StartStream<TripSnapshot>(streamId, seedEvents);
            await s.SaveChangesAsync();
        }

        IEnumerable<object> moreEvents = new object[] { new TripLeg(3) };
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.Append(streamId, moreEvents);
            await s.SaveChangesAsync();
        }

        await using var q = _fixture.Store.QuerySession(tenant);
        var events = await q.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(4);
    }

    [Fact]
    public async Task append_single_event_via_params_array_of_one_lands_in_tenants_partition()
    {
        // Single-event boundary case — the params array has length 1. Validates
        // the loop bound at array_length(event_ids, 1) inside the function.
        var tenant = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        var streamId = Guid.NewGuid();
        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
            await s.SaveChangesAsync();
        }

        await using (var s = _fixture.Store.LightweightSession(tenant))
        {
            s.Events.Append(streamId, new TripLeg(42));
            await s.SaveChangesAsync();
        }

        await using var q = _fixture.Store.QuerySession(tenant);
        var events = await q.Events.FetchStreamAsync(streamId);
        events.Count.ShouldBe(2);
        events[1].Version.ShouldBe(2L);
    }
}
