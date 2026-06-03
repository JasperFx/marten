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
/// #4617 section 3f — pin cross-tenant query isolation for DCB
/// <see cref="DcbStorageMode.TagTables"/> mode under
/// <c>UseTenantPartitionedEvents</c>. Verifies the #4645 fix.
///
/// <para>
/// Pre-fix bug (Marten/Events/EventStore.Dcb.cs:159–161): the non-HStore tag
/// query path joined
/// <code>from mt_events e left join mt_event_tag_xxx t on e.seq_id = t.seq_id</code>
/// Under per-tenant <c>mt_events_sequence_{suffix}</c> sequences <c>seq_id=1</c>
/// existed in EVERY tenant's mt_events partition, so alpha's event row got
/// joined to BOTH alpha's tag row AND beta's tag row, duplicating alpha's
/// event N-ways in the result set. The leak shape was DUPLICATED-OWN-EVENTS
/// rather than cross-tenant DATA bleed (the trailing <c>e.tenant_id = @p</c>
/// WHERE filter scrubbed other tenants' event rows) — still incorrect.
/// </para>
///
/// <para>
/// Fix: tightened the JOIN to also require <c>e.tenant_id = t.tenant_id</c>
/// under conjoined tenancy (the tag table's PK includes tenant_id, so the
/// predicate is well-defined). For non-conjoined tenancy the tag table
/// doesn't have a tenant_id column AND seq_id is unique store-wide via the
/// single global mt_events_sequence — predicate is skipped there.
/// </para>
///
/// <para>
/// <see cref="DcbStorageMode.HStore"/> is unaffected because the tag predicate
/// is on the event row itself, not a JOIN. The theory only covers
/// <c>TagTables</c> to keep the pin focused on the path that was broken.
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
    public async Task tag_query_under_partitioning_returns_only_own_tenant_event()
    {
        // BOTH tenants tag an event with the SAME customer id string. Because
        // each tenant's per-tenant mt_events_sequence_{suffix} starts at 1,
        // both events end up with seq_id = 1 in their respective partitions.
        //
        // Tenant A's tag query for that customer must see ONLY tenant A's
        // event — the #4645 fix tightened the JOIN to also match on tenant_id
        // so the per-tenant-seq-id collision no longer produces a duplicate
        // (or — if the WHERE clause hadn't held — a cross-tenant leak).
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

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

        var query = new EventTagQuery().Or<DcbXtCustomerId>(sharedCustomer);

        await using var alphaQuery = _store.LightweightSession("alpha");
        var events = await alphaQuery.Events.QueryByTagsAsync(query);

        // Exactly one row — alpha's own event. The fix to EventStore.Dcb.cs:
        // adding `AND e.tenant_id = t.tenant_id` to the JOIN means alpha's
        // event row no longer multiplies through beta's tag row.
        events.Count.ShouldBe(1,
            "alpha's tag query must return ONLY alpha's own matching event under partitioning — " +
            "duplication via cross-tenant seq_id collision was fixed in #4645 by tightening the JOIN");

        events[0].TenantId.ShouldBe("alpha");
        events[0].Data.ShouldBeOfType<DcbXtPayment>().Reference.ShouldBe("ALPHA-PAYMENT");
    }
}

public record DcbXtCustomerId(Guid Value);
public record DcbXtPayment(string Reference);
