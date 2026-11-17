using System;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// #5234: the tenant-aware <c>MarkEventsAsSkipped(tenantId, sequences)</c> outside
/// <c>UseTenantPartitionedEvents</c>, which TenantPartitionedEventsTests covers. Conjoined event
/// tenancy filters by the tenant, any other tenancy style leaves the database to be the scope, and
/// the tenant-less overload keeps working wherever one global sequence makes a sequence unique.
/// </summary>
public class marking_events_as_skipped_by_tenant: OneOffConfigurationsContext
{
    [Fact]
    public async Task conjoined_tenancy_skips_only_the_named_tenants_events()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
        });

        var alphaStream = await startStream("alpha");
        var betaStream = await startStream("beta");

        var sequences = new[] { await sequenceOf(alphaStream, "alpha"), await sequenceOf(betaStream, "beta") };

        await theStore.Storage.Database.MarkEventsAsSkipped("alpha", sequences);

        (await eventCountOf(alphaStream, "alpha")).ShouldBe(0);
        (await eventCountOf(betaStream, "beta")).ShouldBe(1, "beta's event does not belong to alpha");
    }

    [Fact]
    public async Task conjoined_tenancy_without_partitioning_keeps_the_tenantless_overload()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
        });

        var alphaStream = await startStream("alpha");
        var betaStream = await startStream("beta");

        // One global sequence here, so a sequence names exactly one event
        await theStore.Storage.Database.MarkEventsAsSkipped([await sequenceOf(alphaStream, "alpha")]);

        (await eventCountOf(alphaStream, "alpha")).ShouldBe(0);
        (await eventCountOf(betaStream, "beta")).ShouldBe(1);
    }

    [Fact]
    public async Task single_tenancy_does_not_consult_the_tenant()
    {
        StoreOptions(opts => opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true);

        var stream = await startStream(StorageConstants.DefaultTenantId);

        await theStore.Storage.Database.MarkEventsAsSkipped("other",
            [await sequenceOf(stream, StorageConstants.DefaultTenantId)]);

        (await eventCountOf(stream, StorageConstants.DefaultTenantId)).ShouldBe(0);
    }

    private async Task<Guid> startStream(string tenantId)
    {
        await using var session = theStore.LightweightSession(tenantId);
        var streamId = session.Events.StartStream(new AEvent()).Id;
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task<long> sequenceOf(Guid streamId, string tenantId)
    {
        await using var query = theStore.QuerySession(tenantId);
        var events = await query.Events.FetchStreamAsync(streamId);
        return events[0].Sequence;
    }

    private async Task<int> eventCountOf(Guid streamId, string tenantId)
    {
        await using var query = theStore.QuerySession(tenantId);
        var events = await query.Events.FetchStreamAsync(streamId);
        return events.Count;
    }
}
