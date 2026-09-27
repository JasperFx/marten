using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.MultiTenancy;

// #5516, the remaining two paths its "Ask" lists after PR #5521 covered bulk events and
// TenantIsOneOf: DeleteAllTenantDataAsync and the per-tenant rebuild.
//
// Both are written as behaviour over a mixed-case id under ForceLowerCase, reading back with the
// normalised spelling, so the fact can only pass if the operation and the ordinary session paths
// agree on the tenant id. Deliberately NOT asserted by inspecting tenant_id alone: that would still
// pass if the write and the read were wrong in the same direction.
public class tenant_id_style_on_admin_operations: OneOffConfigurationsContext
{
    public class Ledger
    {
        public Guid Id { get; set; }
        public int Count { get; set; }

        public void Apply(Recorded _) => Count++;
    }

    public record Recorded(string What);

    private async Task configureAsync()
    {
        StoreOptions(opts =>
        {
            opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<Ledger>(SnapshotLifecycle.Inline);
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Ledger));
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();
    }

    // DeleteAllTenantDataAsync issues DELETE ... WHERE tenant_id = :p against every tenanted table,
    // so a raw id here deletes NOTHING and reports success -- the worst shape this bug can take,
    // since the caller is usually honouring an erasure request.
    [Fact]
    public async Task delete_all_tenant_data_matches_the_corrected_tenant_id()
    {
        await configureAsync();

        var id = Guid.NewGuid();

        await using (var session = theStore.LightweightSession("erasable"))
        {
            session.Store(new Ledger { Id = id, Count = 1 });
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.DeleteAllTenantDataAsync("ERASABLE", CancellationToken.None);

        await using var query = theStore.QuerySession("erasable");
        (await query.LoadAsync<Ledger>(id)).ShouldBeNull();
    }

    // The two rebuild facts below PASSED on master, before the fix in this commit -- they are
    // characterisation, not bug coverage, and are labelled so rather than left to look like the latter.
    //
    // #5516 listed the per-tenant rebuild as a suspect path and it is not one. The two overloads do
    // reach their session differently -- the string-key one calls LightweightSession(tenantId), which
    // corrects the id; the Guid one builds a SessionOptions and assigns TenantId raw -- but
    // SessionOptions resolves its Tenant through MaybeCorrectTenantId (SessionOptions.cs:117/236) and
    // the session stamps Tenant.TenantId, so the raw value never reaches the data. Reading the Guid
    // overload alone makes it look broken, which is why these were written and run against master
    // before anything was changed.
    //
    // Kept because nothing else pins either overload's tenant handling, and a refactor of
    // SessionOptions' tenant resolution would silently break both.
    [Fact]
    public async Task rebuild_single_stream_by_guid_uses_the_corrected_tenant_id()
    {
        await configureAsync();

        var streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession("rebuildguid"))
        {
            session.Events.StartStream<Ledger>(streamId, new Recorded("a"), new Recorded("b"));
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.RebuildSingleStreamAsync<Ledger>(streamId, "REBUILDGUID",
            CancellationToken.None);

        await using var query = theStore.QuerySession("rebuildguid");
        var ledger = await query.LoadAsync<Ledger>(streamId);

        ledger.ShouldNotBeNull();
        ledger.Count.ShouldBe(2);
    }

    [Fact]
    public async Task rebuild_single_stream_by_key_uses_the_corrected_tenant_id()
    {
        StoreOptions(opts =>
        {
            opts.TenantIdStyle = TenantIdStyle.ForceLowerCase;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Projections.Snapshot<StringLedger>(SnapshotLifecycle.Inline);
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(StringLedger));
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var streamKey = "ledger/" + Guid.NewGuid();

        await using (var session = theStore.LightweightSession("rebuildkey"))
        {
            session.Events.StartStream<StringLedger>(streamKey, new Recorded("a"));
            await session.SaveChangesAsync();
        }

        await theStore.Advanced.RebuildSingleStreamAsync<StringLedger>(streamKey, "REBUILDKEY",
            CancellationToken.None);

        await using var query = theStore.QuerySession("rebuildkey");
        var ledger = await query.LoadAsync<StringLedger>(streamKey);

        ledger.ShouldNotBeNull();
        ledger.Count.ShouldBe(1);
    }

    public class StringLedger
    {
        public string Id { get; set; } = null!;
        public int Count { get; set; }

        public void Apply(Recorded _) => Count++;
    }
}
