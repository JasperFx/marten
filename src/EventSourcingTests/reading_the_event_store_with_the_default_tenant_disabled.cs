using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Documents;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// marten#5513 / jasperfx#885. <c>IEventStore.OpenReadOnlyEventStore()</c> takes no tenant and opens
/// the store's DEFAULT session, so on a store with <c>DefaultTenantUsageEnabled = false</c> — the
/// automatic state once database-per-tenant tenancy is configured — the entire read-only tier was
/// unreachable. The session is refused before any tenant scope is applied, which is why
/// <c>QueryStreamStates(tenantId)</c>'s own parameter could not work around it and the stream
/// compaction policy selector (jasperfx#740) could not select streams at all.
/// </summary>
public class reading_the_event_store_with_the_default_tenant_disabled : OneOffConfigurationsContext
{
    private const string TenantA = "tenant_a";
    private const string TenantB = "tenant_b";

    private void ConfigureTenancy()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();

            // The line that made the whole surface unreachable.
            opts.Advanced.DefaultTenantUsageEnabled = false;
        });
    }

    private async Task<Guid> AppendAsync(string tenantId, params object[] events)
    {
        var streamId = Guid.NewGuid();

        await using var session = theStore.LightweightSession(tenantId);
        session.Events.StartStream<DefaultTenantDisabledAggregate>(streamId, events);
        await session.SaveChangesAsync();

        return streamId;
    }

    [Fact]
    public void the_tenant_less_overload_is_still_refused()
    {
        ConfigureTenancy();

        // Not a regression to fix: a store-global read genuinely has nowhere to go here, and the
        // refusal is the honest answer. What was wrong was that it was the ONLY answer available.
        Should.Throw<DefaultTenantUsageDisabledException>(
            () => ((IEventStore)theStore).OpenReadOnlyEventStore());
    }

    [Fact]
    public async Task query_stream_states_is_reachable_for_a_tenant()
    {
        ConfigureTenancy();

        var first = await AppendAsync(TenantA, new DefaultTenantDisabledEvent("one"));
        var second = await AppendAsync(TenantA, new DefaultTenantDisabledEvent("two"),
            new DefaultTenantDisabledEvent("three"));
        var other = await AppendAsync(TenantB, new DefaultTenantDisabledEvent("elsewhere"));

        var readOnly = ((IEventStore)theStore).OpenReadOnlyEventStore(TenantA);

        // Fully qualified: Marten's own QueryableExtensions.ToListAsync and JasperFx's shared
        // DocumentQueryableExtensions.ToListAsync are both in scope and equally applicable here.
        var streams = await DocumentQueryableExtensions.ToListAsync(readOnly.QueryStreamStates(TenantA));

        streams.Select(x => x.Id).OrderBy(x => x)
            .ShouldBe(new[] { first, second }.OrderBy(x => x));

        // Both directions: tenant B's stream must not be in tenant A's answer.
        streams.Select(x => x.Id).ShouldNotContain(other);
        streams.Single(x => x.Id == second).Version.ShouldBe(2);
    }

    [Fact]
    public async Task the_readers_tenant_less_members_answer_within_the_tenant()
    {
        ConfigureTenancy();

        var streamId = Guid.NewGuid();

        // One stream id under two tenants — under conjoined tenancy the identity of a stream is
        // (tenant, id), so a reader that ignored its session's tenant would answer with the wrong
        // version here rather than failing outright.
        await using (var session = theStore.LightweightSession(TenantA))
        {
            session.Events.StartStream<DefaultTenantDisabledAggregate>(streamId,
                new DefaultTenantDisabledEvent("a1"), new DefaultTenantDisabledEvent("a2"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(TenantB))
        {
            session.Events.StartStream<DefaultTenantDisabledAggregate>(streamId,
                new DefaultTenantDisabledEvent("b1"));
            await session.SaveChangesAsync();
        }

        var forA = ((IEventStore)theStore).OpenReadOnlyEventStore(TenantA);
        var stateForA = await forA.FetchStreamStateAsync(streamId);
        stateForA.ShouldNotBeNull();
        stateForA.Version.ShouldBe(2);
        (await forA.FetchStreamAsync(streamId)).Count.ShouldBe(2);

        var forB = ((IEventStore)theStore).OpenReadOnlyEventStore(TenantB);
        var stateForB = await forB.FetchStreamStateAsync(streamId);
        stateForB.ShouldNotBeNull();
        stateForB.Version.ShouldBe(1);
        (await forB.FetchStreamAsync(streamId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task a_null_tenant_keeps_the_store_global_behaviour()
    {
        // Without DefaultTenantUsageEnabled = false there is a default session to open, and passing a
        // null tenant has to behave exactly as the tenant-less overload does.
        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream<DefaultTenantDisabledAggregate>(streamId,
                new DefaultTenantDisabledEvent("plain"));
            await session.SaveChangesAsync();
        }

        var readOnly = ((IEventStore)theStore).OpenReadOnlyEventStore(null);

        var state = await readOnly.FetchStreamStateAsync(streamId);
        state.ShouldNotBeNull();
        state.Version.ShouldBe(1);
    }

    /// <summary>
    /// jasperfx#910 — the action one call after the selector. A compaction policy could select tenant
    /// A's streams through <c>OpenReadOnlyEventStore(tenantId)</c> and then compact none of them: the
    /// untyped <c>CompactStreamAsync</c> opened the default session, refused on this store.
    /// </summary>
    [Fact]
    public async Task the_untyped_compaction_runs_in_the_tenants_scope()
    {
        ConfigureTenancy();

        var streamId = Guid.NewGuid();

        // One stream id under two tenants, so a compaction that ignored its tenant could not pass by
        // compacting "some" stream with this id.
        await using (var session = theStore.LightweightSession(TenantA))
        {
            session.Events.StartStream<DefaultTenantDisabledAggregate>(streamId,
                new DefaultTenantDisabledEvent("a1"), new DefaultTenantDisabledEvent("a2"),
                new DefaultTenantDisabledEvent("a3"));
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession(TenantB))
        {
            session.Events.StartStream<DefaultTenantDisabledAggregate>(streamId,
                new DefaultTenantDisabledEvent("b1"), new DefaultTenantDisabledEvent("b2"));
            await session.SaveChangesAsync();
        }

        await ((IEventStore)theStore).CompactStreamAsync(streamId, TenantA);

        var forA = await ((IEventStore)theStore).OpenReadOnlyEventStore(TenantA).FetchStreamAsync(streamId);
        forA.ShouldHaveSingleItem().Data.ShouldBeOfType<Compacted<DefaultTenantDisabledAggregate>>()
            .Snapshot.Count.ShouldBe(3);

        // Tenant B's stream of the same id is untouched.
        (await ((IEventStore)theStore).OpenReadOnlyEventStore(TenantB).FetchStreamAsync(streamId)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task the_tenant_less_untyped_compaction_is_still_refused()
    {
        ConfigureTenancy();

        var streamId = await AppendAsync(TenantA, new DefaultTenantDisabledEvent("one"));

        // As with the reader: a store-global compaction genuinely has nowhere to go here.
        await Should.ThrowAsync<DefaultTenantUsageDisabledException>(
            () => ((IEventStore)theStore).CompactStreamAsync(streamId));
    }
}

/// <summary>
/// jasperfx#914 — IEventStore.HasEventStore is Marten's EventGraph.IsActive, made reachable. The
/// document-only case is the one that matters: the interface default is true, so an unimplemented
/// member would pass every other fact and fail only that one.
/// </summary>
public class has_event_store : OneOffConfigurationsContext
{
    [Fact]
    public void a_document_only_store_has_no_event_store()
    {
        StoreOptions(opts => opts.Schema.For<DefaultTenantDisabledAggregate>());

        ((IEventStore)theStore).HasEventStore.ShouldBeFalse();
    }

    [Fact]
    public void a_registered_event_type_is_an_event_store()
    {
        StoreOptions(opts => opts.Events.AddEventType<DefaultTenantDisabledEvent>());

        ((IEventStore)theStore).HasEventStore.ShouldBeTrue();
    }
}

public record DefaultTenantDisabledEvent(string Name);

public class DefaultTenantDisabledAggregate
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(DefaultTenantDisabledEvent _) => Count++;
}
