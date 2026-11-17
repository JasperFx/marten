#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// The fourth <c>where seq_id = ?</c> site #5234 flagged, left open by #5236 because it is public
/// API: <c>IMartenDatabase.MarkEventsAsSkipped(long[])</c> ran
/// <c>update mt_events set is_skipped = TRUE where seq_id = ANY(:sequences)</c> with no tenant
/// predicate. Under <c>UseTenantPartitionedEvents</c> every tenant draws from its own
/// <c>mt_events_sequence_{suffix}</c>, so <c>seq_id = 1</c> exists in EVERY tenant's partition, and
/// skipping one tenant's event silently skipped the same-numbered event of every other tenant: it
/// vanished from their <c>FetchStreamAsync</c>, live aggregation, rebuilds and subscription replays.
/// </summary>
public class Bug_5234_mark_events_as_skipped_per_tenant: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_5234s";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<SkipProbeEvent>();
        opts.Events.EnableEventSkippingInProjectionsOrSubscriptions = true;
    }

    [Fact]
    public async Task the_tenantless_overload_must_not_skip_another_tenants_event()
    {
        var (_, betaStream, sequence) = await appendOneEventPerTenant();

        // The documented call, handed a sequence that was read through tenant alpha. It cannot
        // say which tenant that sequence belongs to.
        var thrown = await Record.ExceptionAsync(() =>
            Store.Storage.Database.MarkEventsAsSkipped([sequence], TestContext.Current.CancellationToken));

        (await isSkipped("beta", sequence)).ShouldBeFalse(
            "skipping tenant alpha's event must not skip tenant beta's same-numbered event");
        (await eventCountOf(betaStream, "beta")).ShouldBe(1);

        thrown.ShouldBeOfType<InvalidOperationException>()
            .Message.ShouldContain("MarkEventsAsSkipped(string tenantId, long[] sequences");
    }

    [Fact]
    public async Task skipping_by_tenant_skips_only_that_tenants_event()
    {
        var (alphaStream, betaStream, sequence) = await appendOneEventPerTenant();

        await Store.Storage.Database.MarkEventsAsSkipped("alpha", [sequence],
            TestContext.Current.CancellationToken);

        (await isSkipped("alpha", sequence)).ShouldBeTrue();
        (await eventCountOf(alphaStream, "alpha")).ShouldBe(0, "alpha's event is skipped, so its stream reads empty");

        (await isSkipped("beta", sequence)).ShouldBeFalse(
            "skipping tenant alpha's event must not skip tenant beta's same-numbered event");
        (await eventCountOf(betaStream, "beta")).ShouldBe(1);
    }

    private async Task<(Guid alphaStream, Guid betaStream, long sequence)> appendOneEventPerTenant()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaStream = Guid.NewGuid();
        var betaStream = Guid.NewGuid();

        await using (var session = Store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaStream, new SkipProbeEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var session = Store.LightweightSession("beta"))
        {
            session.Events.StartStream(betaStream, new SkipProbeEvent());
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The collision this bug depends on: each tenant has its own sequence, so both events are
        // seq_id 1. If this ever stops being true the tests above stop proving anything.
        var alphaSequence = await sequenceOf(alphaStream, "alpha");
        alphaSequence.ShouldBe(await sequenceOf(betaStream, "beta"));

        return (alphaStream, betaStream, alphaSequence);
    }

    private async Task<long> sequenceOf(Guid streamId, string tenantId)
    {
        await using var query = Store.QuerySession(tenantId);
        var events = await query.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken);
        return events[0].Sequence;
    }

    private async Task<int> eventCountOf(Guid streamId, string tenantId)
    {
        await using var query = Store.QuerySession(tenantId);
        var events = await query.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken);
        return events.Count;
    }

    // Read the column itself rather than only through Marten's skipped-event filters, so the
    // assertion cannot pass because some read path stopped honouring is_skipped.
    private async Task<bool> isSkipped(string tenantId, long sequence)
    {
        await using var conn = Store.Storage.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand(
            $"select is_skipped from {Schema}.mt_events where tenant_id = :tenant and seq_id = :seq");
        cmd.Parameters.AddWithValue("tenant", tenantId);
        cmd.Parameters.AddWithValue("seq", sequence);

        var raw = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return (bool)raw!;
    }
}

public class SkipProbeEvent
{
}
