#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Dcb;

public record PartitionedLedgerTag(Guid Value);

public record PartitionedCredit(Guid Ledger, decimal Amount);

public record PartitionedFee(decimal Amount);

public class PartitionedLedgerBalance
{
    public Guid Id { get; set; }
    public decimal Balance { get; set; }
}

public class PartitionedLedgerBalanceProjection: MultiStreamProjection<PartitionedLedgerBalance, Guid>
{
    public PartitionedLedgerBalanceProjection()
    {
        Name = "PartitionedLedgerBalance";
        IdentityFromTag<PartitionedLedgerTag>();
    }

    public void Apply(PartitionedCredit e, PartitionedLedgerBalance balance) => balance.Balance += e.Amount;

    public void Apply(PartitionedFee e, PartitionedLedgerBalance balance) => balance.Balance -= e.Amount;
}

/// <summary>
///     marten#6117: a multi-stream projection grouped with <c>IdentityFromTag</c> under
///     <c>UseTenantPartitionedEvents</c>. Every tenant has its own sequence, so the same sequence number names one event
///     in each tenant, and the daemon has to read each event's tags in its own tenant.
/// </summary>
public class tag_grouped_projection_under_partitioning: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_tagproj";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.RegisterTagType<PartitionedLedgerTag>("partitioned_ledger");
        opts.Events.TagWith<PartitionedCredit>(e => new PartitionedLedgerTag(e.Ledger));
        opts.Projections.Add<PartitionedLedgerBalanceProjection>(ProjectionLifecycle.Async);
    }

    private async Task<decimal?> balanceAsync(string tenantId, Guid ledger)
    {
        await using var query = Store.QuerySession(tenantId);
        return (await query.LoadAsync<PartitionedLedgerBalance>(ledger, TestContext.Current.CancellationToken))?.Balance;
    }

    [Fact]
    public async Task each_tenant_builds_its_own_document_from_its_own_tags()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "red", "blue");

        var ledger = Guid.NewGuid();
        var redOnly = Guid.NewGuid();

        // The same shape in both tenants, so their sequences line up: red's tags differ from blue's at each one
        await using (var red = Store.LightweightSession("red"))
        {
            red.Events.StartStream(Guid.NewGuid(), new PartitionedCredit(ledger, 10m));

            var fee = red.Events.BuildEvent(new PartitionedFee(1m));
            fee.WithTag(new PartitionedLedgerTag(redOnly));
            red.Events.StartStream(Guid.NewGuid(), fee);

            await red.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var blue = Store.LightweightSession("blue"))
        {
            blue.Events.StartStream(Guid.NewGuid(), new PartitionedCredit(ledger, 25m));

            var fee = blue.Events.BuildEvent(new PartitionedFee(2m));
            fee.WithTag(new PartitionedLedgerTag(ledger));
            blue.Events.StartStream(Guid.NewGuid(), fee);

            await blue.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await Store.WaitForNonStaleProjectionDataAsync(60.Seconds());

        (await balanceAsync("red", ledger)).ShouldBe(10m);
        (await balanceAsync("red", redOnly)).ShouldBe(-1m);
        (await balanceAsync("blue", ledger)).ShouldBe(23m);
        (await balanceAsync("blue", redOnly)).ShouldBeNull();

        // One identity rebuilt in one tenant, from that tenant's tags only
        await daemon.RebuildIdentityAsync("PartitionedLedgerBalance", "blue", ledger,
            TestContext.Current.CancellationToken);
        (await balanceAsync("blue", ledger)).ShouldBe(23m);
        (await balanceAsync("red", ledger)).ShouldBe(10m);

        await daemon.StopAllAsync();
    }
}
