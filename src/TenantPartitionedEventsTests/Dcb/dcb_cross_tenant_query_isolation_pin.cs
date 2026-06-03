#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Tags;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Dcb;

/// <summary>
/// #4617 section 3f deferred — pin the currently-BROKEN cross-tenant query
/// isolation for DCB <see cref="DcbStorageMode.TagTables"/> mode under
/// <c>UseTenantPartitionedEvents</c>.
///
/// <para>
/// Root cause (Marten/Events/EventStore.Dcb.cs:159–161): the non-HStore tag
/// query path joins
/// <code>from mt_events e left join mt_event_tag_xxx t on e.seq_id = t.seq_id</code>
/// Under <c>UseTenantPartitionedEvents</c> the per-tenant
/// <c>mt_events_sequence_{suffix}</c> sequences mean <c>seq_id = 1</c> exists
/// in EVERY tenant's mt_events partition. The trailing <c>e.tenant_id = @p</c>
/// filter only constrains the events side, so alpha's event row gets joined
/// to BOTH alpha's tag row AND beta's tag row (both have seq_id 1) — the
/// result set duplicates alpha's event N-ways where N = the number of
/// tenants that happen to share that tag value with the same per-tenant
/// seq_id. The leak shape is DUPLICATED-OWN-EVENTS, not cross-tenant DATA
/// bleed (the WHERE filter still scrubs other tenants' event rows), but
/// it's still incorrect.
/// </para>
///
/// <para>
/// SUT fix needed: tighten the JOIN to <c>on e.seq_id = t.seq_id AND
/// e.tenant_id = t.tenant_id</c> (the tag table's PK already includes
/// tenant_id under conjoined tenancy, so the predicate is well-defined).
/// </para>
///
/// <para>
/// Pin captures the CURRENT broken behavior so the SUT fix flips this
/// assertion intentionally. <see cref="DcbStorageMode.HStore"/> is unaffected
/// because the tag predicate is on the event row itself, not a JOIN. The
/// theory only covers <c>TagTables</c> to keep the broken-state pin focused.
/// </para>
/// </summary>
public class dcb_cross_tenant_query_isolation_pin : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_dcbx_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(_schema); } catch { }
        }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            // TagTables is the broken-JOIN path. HStore stores the tag on the
            // event row itself so the cross-tenant query is intrinsically
            // safe; this pin is specifically about the JOIN under TagTables.
            opts.Events.DcbStorageMode = DcbStorageMode.TagTables;

            opts.Events.AddEventType<DcbXtPayment>();
            opts.Events.RegisterTagType<DcbXtCustomerId>("dcbxt_customer");
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task tag_query_under_partitioning_currently_BLEEDS_across_tenants_pin()
    {
        // BOTH tenants tag an event with the SAME customer id string. Because
        // each tenant's per-tenant mt_events_sequence_{suffix} starts at 1,
        // both events end up with seq_id = 1 in their respective partitions.
        //
        // Tenant A's tag query for that customer SHOULD see only tenant A's
        // event. Today it sees BOTH because the JOIN matches on seq_id alone.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        // Pick a tag value both tenants will register.
        var sharedCustomer = new DcbXtCustomerId(Guid.NewGuid());

        await using (var session = _store.LightweightSession("alpha"))
        {
            var evt = session.Events.BuildEvent(new DcbXtPayment("ALPHA-PAYMENT"));
            evt.WithTag(sharedCustomer);
            session.Events.Append(Guid.NewGuid(), evt);
            await session.SaveChangesAsync();
        }

        await using (var session = _store.LightweightSession("beta"))
        {
            var evt = session.Events.BuildEvent(new DcbXtPayment("BETA-PAYMENT"));
            evt.WithTag(sharedCustomer);
            session.Events.Append(Guid.NewGuid(), evt);
            await session.SaveChangesAsync();
        }

        // Query from alpha — under correct behavior we should see only the
        // ALPHA-PAYMENT event. Today the broken JOIN bleeds beta's event in.
        var query = new EventTagQuery().Or<DcbXtCustomerId>(sharedCustomer);

        await using var alphaQuery = _store.LightweightSession("alpha");
        var events = await alphaQuery.Events.QueryByTagsAsync(query);

        // PIN — broken state. EventStore.Dcb.cs:159 JOIN is on `e.seq_id =
        // t.seq_id` only; under per-tenant sequences alpha's event row joins
        // BOTH alpha's tag row AND beta's tag row, duplicating alpha's event
        // in the result. The WHERE filter `e.tenant_id = @p` scrubs beta's
        // EVENTS but doesn't deduplicate the join product.
        //
        // Future SUT fix tightens the JOIN:
        //     on e.seq_id = t.seq_id AND e.tenant_id = t.tenant_id
        // Once applied, this assertion flips to `events.Count.ShouldBe(1)`.
        events.Count.ShouldBeGreaterThan(1,
            "EXPECTED-FAILURE PIN: under the broken JOIN, alpha's event row gets duplicated by " +
            "the cross-tenant tag-row match (per-tenant seq_id 1 exists in both partitions). " +
            "Fix: tighten EventStore.Dcb.cs:159 JOIN to also require e.tenant_id = t.tenant_id. " +
            "When fixed, flip this to events.Count.ShouldBe(1) and drop the deduplication sub-pin below.");

        // Sub-pin: every row returned IS alpha's data (the WHERE filter still
        // scrubs beta's event rows from the result — leak is duplication, not
        // cross-tenant data bleed). All rows have Reference == "ALPHA-PAYMENT".
        foreach (var e in events)
        {
            e.TenantId.ShouldBe("alpha");
            e.Data.ShouldBeOfType<DcbXtPayment>().Reference.ShouldBe("ALPHA-PAYMENT");
        }
    }
}

public record DcbXtCustomerId(Guid Value);
public record DcbXtPayment(string Reference);
