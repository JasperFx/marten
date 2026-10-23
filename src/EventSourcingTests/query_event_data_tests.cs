#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public record CargoLoaded(string Cargo, int Tonnes);

/// <summary>
/// What <c>IReadOnlyEventStore.QueryEventData&lt;T&gt;()</c> (jasperfx#1073) promises beyond the shared
/// <c>EventDataQueryCompliance</c> suite, which does not exercise the tenant overload or the refusals: a tenant
/// scope on a store with no tenant dimension is refused, a tenant scope on a conjoined store reads that
/// tenant only, and a member Marten cannot translate fails rather than matching everything.
/// </summary>
public class query_event_data_tests: OneOffConfigurationsContext
{
    private IReadOnlyEventStore reader => (IReadOnlyEventStore)theSession.Events;

    [Fact]
    public void a_tenant_scope_on_a_tenantless_store_is_refused_by_name()
    {
        var ex = Should.Throw<NotSupportedException>(() => reader.QueryEventData<CargoLoaded>("tenant-a"));

        ex.Message.ShouldContain("tenant-a");
        ex.Message.ShouldContain("QueryEventData");
    }

    [Fact]
    public async Task an_untranslatable_member_is_refused_not_silently_matched()
    {
        theSession.Events.StartStream(Guid.NewGuid(), new CargoLoaded("Coal", 100));
        await theSession.SaveChangesAsync();

        var query = reader.QueryEventData<CargoLoaded>().Where(x => x.Cargo.GetHashCode() == 7);

        var ex = await Should.ThrowAsync<Marten.Exceptions.BadLinqExpressionException>(
            () => DocumentQueryableExtensions.ToListAsync(query, CancellationToken.None));

        ex.Message.ShouldContain("GetHashCode");
    }

    [Fact]
    public async Task a_tenant_scope_on_a_conjoined_store_reads_that_tenant_only()
    {
        StoreOptions(opts => opts.Events.TenancyStyle = JasperFx.MultiTenancy.TenancyStyle.Conjoined);

        await using (var a = theStore.LightweightSession("tenant-a"))
        {
            a.Events.StartStream(Guid.NewGuid(), new CargoLoaded("Coal", 100), new CargoLoaded("Ore", 200));
            await a.SaveChangesAsync();
        }

        await using (var b = theStore.LightweightSession("tenant-b"))
        {
            b.Events.StartStream(Guid.NewGuid(), new CargoLoaded("Grain", 300));
            await b.SaveChangesAsync();
        }

        var store = (IEventStore)theStore;

        var forA = await DocumentQueryableExtensions.ToListAsync(
            store.OpenReadOnlyEventStore().QueryEventData<CargoLoaded>("tenant-a"), CancellationToken.None);
        forA.Select(x => x.Cargo).OrderBy(x => x).ShouldBe(["Coal", "Ore"]);

        var forB = await DocumentQueryableExtensions.CountAsync(
            store.OpenReadOnlyEventStore().QueryEventData<CargoLoaded>("tenant-b").Where(x => x.Tonnes > 100),
            CancellationToken.None);
        forB.ShouldBe(1);

        // And the reader opened for a tenant answers in that tenant with no argument.
        var scoped = await DocumentQueryableExtensions.ToListAsync(
            store.OpenReadOnlyEventStore("tenant-b").QueryEventData<CargoLoaded>(), CancellationToken.None);
        scoped.ShouldHaveSingleItem().Cargo.ShouldBe("Grain");
    }
}
