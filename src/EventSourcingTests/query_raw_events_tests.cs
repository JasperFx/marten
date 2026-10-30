#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Documents;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// What <c>IReadOnlyEventStore.QueryRawEvents()</c> (jasperfx#1094, marten#5792) promises: the same
/// <c>Where</c> and <c>OrderBy</c> over <see cref="IEvent"/> metadata, written only against the JasperFx
/// abstractions, returns the seeded rows in the expected order; a tenant scope reads that tenant only and is
/// refused where there is no tenant dimension; and a member Marten cannot translate fails naming it.
/// </summary>
public class query_raw_events_tests: OneOffConfigurationsContext
{
    private static readonly Guid StreamA = Guid.NewGuid();
    private static readonly Guid StreamB = Guid.NewGuid();

    private async Task seed()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.MetadataConfig.CorrelationIdEnabled = true;
            opts.Events.MetadataConfig.CausationIdEnabled = true;
        });

        await using (var a = theStore.LightweightSession("tenant-a"))
        {
            a.CorrelationId = "corr-1";
            a.CausationId = "cause-1";
            a.Events.StartStream(StreamA, new CargoLoaded("Coal", 100), new CargoLoaded("Ore", 200));
            await a.SaveChangesAsync();
        }

        await using (var b = theStore.LightweightSession("tenant-b"))
        {
            b.CorrelationId = "corr-2";
            b.CausationId = "cause-2";
            b.Events.StartStream(StreamB, new CargoLoaded("Grain", 300));
            await b.SaveChangesAsync();
        }
    }

    // A reader's own scope is its session's tenant: the default tenant here, which holds no events.
    private IReadOnlyEventStore readerFor(string tenantId) => ((IEventStore)theStore).OpenReadOnlyEventStore(tenantId);

    [Fact]
    public async Task where_and_order_by_over_event_metadata()
    {
        await seed();

        var events = await DocumentQueryableExtensions.ToListAsync(
            readerFor("tenant-a").QueryRawEvents()
                .Where(x => x.CorrelationId == "corr-1" && x.StreamId == StreamA)
                .OrderByDescending(x => x.Sequence),
            CancellationToken.None);

        events.Select(x => x.Version).ShouldBe([2, 1]);
        events.ShouldAllBe(x => x.TenantId == "tenant-a" && x.CausationId == "cause-1");
        events.ShouldAllBe(x => x.EventTypeName == "cargo_loaded");

        var byTenant = await DocumentQueryableExtensions.ToListAsync(
            readerFor("tenant-b").QueryRawEvents().Where(x => x.TenantId == "tenant-b").OrderBy(x => x.Timestamp),
            CancellationToken.None);
        byTenant.ShouldHaveSingleItem().CausationId.ShouldBe("cause-2");

        (await DocumentQueryableExtensions.CountAsync(readerFor("tenant-a").QueryRawEvents(), CancellationToken.None))
            .ShouldBe(2);
    }

    [Fact]
    public async Task a_tenant_scope_reads_that_tenant_only()
    {
        await seed();

        var forA = await DocumentQueryableExtensions.ToListAsync(
            readerFor("tenant-b").QueryRawEvents("tenant-a").OrderBy(x => x.Sequence), CancellationToken.None);
        forA.Select(x => x.StreamId).ShouldBe([StreamA, StreamA]);

        var forB = await DocumentQueryableExtensions.ToListAsync(
            readerFor("tenant-a").QueryRawEvents("tenant-b"), CancellationToken.None);
        forB.ShouldHaveSingleItem().StreamId.ShouldBe(StreamB);
    }

    [Fact]
    public void a_tenant_scope_on_a_tenantless_store_is_refused_by_name()
    {
        var ex = Should.Throw<NotSupportedException>(
            () => ((IReadOnlyEventStore)theSession.Events).QueryRawEvents("tenant-a"));

        ex.Message.ShouldContain("tenant-a");
        ex.Message.ShouldContain("QueryRawEvents");
    }

    [Fact]
    public async Task an_untranslatable_member_is_refused_not_silently_matched()
    {
        theSession.Events.StartStream(Guid.NewGuid(), new CargoLoaded("Coal", 100));
        await theSession.SaveChangesAsync();

        var query = ((IReadOnlyEventStore)theSession.Events).QueryRawEvents()
            .Where(x => x.EventTypeName.GetHashCode() == 7);

        var ex = await Should.ThrowAsync<Marten.Exceptions.BadLinqExpressionException>(
            () => DocumentQueryableExtensions.ToListAsync(query, CancellationToken.None));

        ex.Message.ShouldContain("GetHashCode");
    }
}
