#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Documents;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// jasperfx#1079 (marten#5768, CritterWatch#1464). The only store-wide stream read was
/// <c>OpenReadOnlyEventStore().QueryStreamStates()</c>, and <c>OpenReadOnlyEventStore()</c> opens a
/// DEFAULT-tenant session, so a conjoined store with <c>DefaultTenantUsageEnabled = false</c> could not be
/// searched store-wide at all. <c>IEventStore.QueryStreamStatesForAllTenants()</c> reads every tenant's rows
/// without opening one, and every row names its tenant through <c>StreamState.TenantId</c>.
/// </summary>
public class query_stream_states_for_all_tenants: OneOffConfigurationsContext
{
    private const string TenantA = "tenant_a";
    private const string TenantB = "tenant_b";

    private void conjoinedWithTheDefaultTenantDisabled()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Advanced.DefaultTenantUsageEnabled = false;
        });
    }

    private async Task appendAsync(string tenantId, Guid streamId, params object[] events)
    {
        await using var session = theStore.LightweightSession(tenantId);
        session.Events.StartStream<DefaultTenantDisabledAggregate>(streamId, events);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task the_reported_case_every_tenants_streams_with_the_default_tenant_disabled()
    {
        conjoinedWithTheDefaultTenantDisabled();

        var shared = Guid.NewGuid();
        var onlyA = Guid.NewGuid();
        await appendAsync(TenantA, shared, new DefaultTenantDisabledEvent("a1"), new DefaultTenantDisabledEvent("a2"));
        await appendAsync(TenantA, onlyA, new DefaultTenantDisabledEvent("a3"));
        await appendAsync(TenantB, shared, new DefaultTenantDisabledEvent("b1"));

        // What the issue reported: the store-wide reader is refused on this store.
        Should.Throw<DefaultTenantUsageDisabledException>(() => ((IEventStore)theStore).OpenReadOnlyEventStore());

        var all = await DocumentQueryableExtensions.ToListAsync(
            ((IEventStore)theStore).QueryStreamStatesForAllTenants());

        all.Count.ShouldBe(3);
        all.Where(x => x.Id == shared).Select(x => x.TenantId).OrderBy(x => x).ShouldBe([TenantA, TenantB]);
        all.Single(x => x.Id == shared && x.TenantId == TenantA).Version.ShouldBe(2);
        all.Single(x => x.Id == shared && x.TenantId == TenantB).Version.ShouldBe(1);
        all.Single(x => x.Id == onlyA).TenantId.ShouldBe(TenantA);
    }

    [Fact]
    public async Task the_tenant_is_translatable_in_where_and_order_by()
    {
        conjoinedWithTheDefaultTenantDisabled();

        var shared = Guid.NewGuid();
        await appendAsync(TenantA, shared, new DefaultTenantDisabledEvent("a1"));
        await appendAsync(TenantB, shared, new DefaultTenantDisabledEvent("b1"));
        await appendAsync(TenantB, Guid.NewGuid(), new DefaultTenantDisabledEvent("b2"));

        var queryable = ((IEventStore)theStore).QueryStreamStatesForAllTenants();

        var forB = await DocumentQueryableExtensions.ToListAsync(queryable.Where(x => x.TenantId == TenantB));
        forB.Count.ShouldBe(2);
        forB.ShouldAllBe(x => x.TenantId == TenantB);

        var ordered = await DocumentQueryableExtensions.ToListAsync(
            queryable.Where(x => x.Id == shared).OrderByDescending(x => x.TenantId));
        ordered.Select(x => x.TenantId).ShouldBe([TenantB, TenantA]);

        (await DocumentQueryableExtensions.CountAsync(queryable.Where(x => x.TenantId != TenantB))).ShouldBe(1);
    }

    [Fact]
    public async Task tenant_scoped_stream_states_carry_their_tenant()
    {
        conjoinedWithTheDefaultTenantDisabled();

        var stream = Guid.NewGuid();
        await appendAsync(TenantB, stream, new DefaultTenantDisabledEvent("b1"));

        var forB = await DocumentQueryableExtensions.ToListAsync(
            ((IEventStore)theStore).OpenReadOnlyEventStore(TenantB).QueryStreamStates(TenantB));

        forB.ShouldHaveSingleItem().TenantId.ShouldBe(TenantB);
    }

    [Fact]
    public async Task single_tenanted_events_read_as_the_tenant_less_query_with_the_default_tenant()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        theSession.Events.StartStream<DefaultTenantDisabledAggregate>(first, new DefaultTenantDisabledEvent("one"));
        theSession.Events.StartStream<DefaultTenantDisabledAggregate>(second, new DefaultTenantDisabledEvent("two"));
        await theSession.SaveChangesAsync();

        var all = await DocumentQueryableExtensions.ToListAsync(
            ((IEventStore)theStore).QueryStreamStatesForAllTenants());
        var tenantLess = await DocumentQueryableExtensions.ToListAsync(
            ((IEventStore)theStore).OpenReadOnlyEventStore().QueryStreamStates());

        all.Select(x => x.Id).ShouldBe(tenantLess.Select(x => x.Id), ignoreOrder: true);
        all.Select(x => x.Id).ShouldBe([first, second], ignoreOrder: true);
        all.ShouldAllBe(x => x.TenantId == StorageConstants.DefaultTenantId);
        tenantLess.ShouldAllBe(x => x.TenantId == StorageConstants.DefaultTenantId);
    }

    [Fact]
    public void a_store_spanning_several_databases_is_refused_not_answered_from_one()
    {
        var store = SeparateStore(opts => opts.MultiTenantedWithSingleServer(ConnectionSource.ConnectionString));

        var ex = Should.Throw<NotSupportedException>(() => ((IEventStore)store).QueryStreamStatesForAllTenants());
        ex.Message.ShouldContain("OpenReadOnlyEventStore(tenantId)");
    }
}
