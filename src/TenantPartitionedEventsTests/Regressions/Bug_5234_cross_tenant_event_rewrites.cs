#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// #5234: under <c>UseTenantPartitionedEvents</c> every tenant draws from its own
/// <c>mt_events_sequence_{suffix}</c>, so <c>seq_id = 1</c> exists in EVERY tenant's partition.
/// Three operations that rewrite <c>mt_events</c> keyed their WHERE on <c>seq_id</c> alone, so the
/// write escaped the tenant the read had correctly scoped to:
///
/// <list type="bullet">
/// <item>masking (<c>OverwriteEventOperation</c>) destroyed another tenant's payload and replaced
/// it with the calling tenant's masked JSON — a cross-tenant write AND disclosure, in the feature
/// that exists to prevent exactly that;</item>
/// <item>compaction's delete (<c>DeleteEventsOperation</c>) permanently removed another tenant's
/// events;</item>
/// <item>compaction's snapshot write (<c>ReplaceEventOperation</c>) left the calling tenant's whole
/// aggregate state sitting in the other tenant's stream.</item>
/// </list>
///
/// Nothing threw in any of the three: all are <c>NoDataReturnedCall</c>.
/// </summary>
public class Bug_5234_cross_tenant_event_rewrites: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_5234";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<MaskProbeEvent>();
        opts.Events.AddEventType<CompactProbeEvent>();

        opts.Events.AddMaskingRuleForProtectedInformation<MaskProbeEvent>(x => x.Secret = "***");

        opts.Projections.Snapshot<CompactProbeAggregate>(SnapshotLifecycle.Inline);
    }

    [Fact]
    public async Task masking_one_tenant_must_not_rewrite_another_tenants_events()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaStream = Guid.NewGuid();
        var betaStream = Guid.NewGuid();

        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaStream, new MaskProbeEvent { Secret = "alpha-secret" });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = Store.LightweightSession("beta"))
        {
            session.Events.StartStream(betaStream, new MaskProbeEvent { Secret = "beta-secret" });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The collision this bug depends on: each tenant has its own sequence, so both events are
        // seq_id 1. If this ever stops being true the test below stops proving anything.
        (await sequenceOf(alphaStream, "alpha")).ShouldBe(await sequenceOf(betaStream, "beta"));

        await Store.Advanced.ApplyEventDataMasking(x =>
        {
            x.ForTenant("alpha");
            x.IncludeStream(alphaStream);
        }, CancellationToken.None);

        await using var query = Store.QuerySession("beta");
        var betaEvent = (await query.Events.FetchStreamAsync(betaStream, token: TestContext.Current.CancellationToken)).Single();

        betaEvent.Data.ShouldBeOfType<MaskProbeEvent>().Secret.ShouldBe("beta-secret",
            "masking tenant alpha must not touch tenant beta's same-numbered event");
    }

    [Fact]
    public async Task masking_still_masks_the_tenant_it_was_asked_to()
    {
        // The guard must not be so tight that the feature stops working.
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaStream = Guid.NewGuid();

        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaStream, new MaskProbeEvent { Secret = "alpha-secret" });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await Store.Advanced.ApplyEventDataMasking(x =>
        {
            x.ForTenant("alpha");
            x.IncludeStream(alphaStream);
        }, CancellationToken.None);

        await using var query = Store.QuerySession("alpha");
        var alphaEvent = (await query.Events.FetchStreamAsync(alphaStream, token: TestContext.Current.CancellationToken)).Single();

        alphaEvent.Data.ShouldBeOfType<MaskProbeEvent>().Secret.ShouldBe("***");
    }

    [Fact]
    public async Task compacting_one_tenants_stream_must_not_delete_another_tenants_events()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaStream = Guid.NewGuid();
        var betaStream = Guid.NewGuid();

        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.StartStream<CompactProbeAggregate>(alphaStream,
                new CompactProbeEvent(), new CompactProbeEvent(), new CompactProbeEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = Store.LightweightSession("beta"))
        {
            session.Events.StartStream<CompactProbeAggregate>(betaStream,
                new CompactProbeEvent(), new CompactProbeEvent(), new CompactProbeEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = Store.LightweightSession("alpha"))
        {
            await session.Events.CompactStreamAsync<CompactProbeAggregate>(alphaStream);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = Store.QuerySession("beta");
        var betaEvents = await query.Events.FetchStreamAsync(betaStream, token: TestContext.Current.CancellationToken);

        // Pre-fix this was 1 -- beta's three events deleted, and the survivor was alpha's
        // Compacted<CompactProbeAggregate> snapshot sitting in beta's stream.
        betaEvents.Count.ShouldBe(3, "compacting alpha's stream must leave beta's events alone");
        betaEvents.ShouldAllBe(x => x.Data is CompactProbeEvent);
    }

    [Fact]
    public async Task compaction_still_compacts_the_stream_it_was_asked_to()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaStream = Guid.NewGuid();

        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.StartStream<CompactProbeAggregate>(alphaStream,
                new CompactProbeEvent(), new CompactProbeEvent(), new CompactProbeEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = Store.LightweightSession("alpha"))
        {
            await session.Events.CompactStreamAsync<CompactProbeAggregate>(alphaStream);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = Store.QuerySession("alpha");
        var alphaEvents = await query.Events.FetchStreamAsync(alphaStream, token: TestContext.Current.CancellationToken);

        alphaEvents.Single().Data.ShouldBeOfType<Compacted<CompactProbeAggregate>>();
    }

    private async Task<long> sequenceOf(Guid streamId, string tenantId)
    {
        await using var query = Store.QuerySession(tenantId);
        var events = await query.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken);
        return events[0].Sequence;
    }
}

public class MaskProbeEvent
{
    public string Secret { get; set; } = string.Empty;
}

public class CompactProbeEvent
{
}

public class CompactProbeAggregate
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(CompactProbeEvent _) => Count++;
}
