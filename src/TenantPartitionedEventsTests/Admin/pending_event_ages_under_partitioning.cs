using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Storage;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// jasperfx#1150. <c>FetchFirstEventTimestampsAfterAsync</c> under <c>UseTenantPartitionedEvents</c>, which
/// the shared PendingEventAgeCompliance suite cannot switch on. Every tenant draws seq_id from its own
/// sequence, so the same position means a different event in each tenant's partition, and a tenant's
/// answer must come from its own partition only.
/// </summary>
[Collection("guid-partitioned")]
public class pending_event_ages_under_partitioning
{
    private readonly GuidPartitionedFixture _fixture;

    public pending_event_ages_under_partitioning(GuidPartitionedFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task each_tenant_waits_on_the_next_event_in_its_own_sequence()
    {
        var alpha = PartitionedFixtureBase.NewTenant();
        var beta = PartitionedFixtureBase.NewTenant();
        await _fixture.Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        // alpha gets seq_id 1 and 2, committed apart so their timestamps differ; beta gets its own seq_id 1
        var alphaStream = await _fixture.AppendNEventsAsync(alpha, 1);
        await Task.Delay(50);
        await using (var session = _fixture.Store.LightweightSession(alpha))
        {
            session.Events.Append(alphaStream, new TripLeg(2.0));
            await session.SaveChangesAsync();
        }

        await Task.Delay(50);
        var betaStream = await _fixture.AppendNEventsAsync(beta, 1);

        var alphaEvents = await fetchAsync(alpha, alphaStream);
        var betaEvents = await fetchAsync(beta, betaStream);
        alphaEvents.Select(x => x.Sequence).ShouldBe([1L, 2L]);
        betaEvents.Single().Sequence.ShouldBe(1L);

        var db = (MartenDatabase)_fixture.Store.Storage.Database;
        var ages = await db.FetchFirstEventTimestampsAfterAsync([(alpha, 0), (beta, 0), (alpha, 1), (beta, 1)]);

        ages.Select(x => (x.TenantId, x.Sequence)).ShouldBe([(alpha, 0L), (beta, 0L), (alpha, 1L), (beta, 1L)]);

        ages[0].Timestamp.ShouldBe(alphaEvents[0].Timestamp);

        // Both tenants hold a seq_id 1; beta's answer is beta's own event
        ages[1].Timestamp.ShouldBe(betaEvents[0].Timestamp);

        ages[2].Timestamp.ShouldBe(alphaEvents[1].Timestamp);

        // alpha's seq_id 2 is not beta's: beta is caught up
        ages[3].Timestamp.ShouldBeNull();
    }

    private async Task<IReadOnlyList<IEvent>> fetchAsync(string tenantId, Guid streamId)
    {
        await using var query = _fixture.Store.QuerySession(tenantId);
        return await query.Events.FetchStreamAsync(streamId);
    }
}
