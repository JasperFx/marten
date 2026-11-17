#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Xunit;

namespace TenantPartitionedEventsTests.Admin;

/// <summary>
/// #5742. A <c>[NaturalKey]</c> aggregate keeps an <c>mt_natural_key_*</c> lookup table that is not partitioned
/// by tenant and has a foreign key to <c>mt_streams(id, tenant_id)</c>. That key cascades on delete, but a
/// cascade does not apply to a partition drop, so removing a tenant failed with 23503 while the tenant's
/// natural-key rows still pointed into its <c>mt_streams</c> partition. Same class as the DCB tag rows (#5694,
/// #5720).
/// </summary>
public class removing_a_tenant_with_natural_key_rows: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_rm_nk";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Projections.Snapshot<RemovalCase>(SnapshotLifecycle.Inline);
    }

    private async Task<Guid> OpenCaseAsync(string tenantId, string number)
    {
        var id = Guid.NewGuid();
        await using var session = Store.LightweightSession(tenantId);
        session.Events.StartStream<RemovalCase>(id, new RemovalCaseOpened(id, number));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return id;
    }

    private async Task<RemovalCase?> FetchByNumberAsync(string tenantId, string number)
    {
        await using var session = Store.LightweightSession(tenantId);
        return await session.Events.FetchLatest<RemovalCase, RemovalCaseNumber>(new RemovalCaseNumber(number),
            TestContext.Current.CancellationToken);
    }

    private async Task<long> CountNaturalKeyRowsAsync(string tenantId)
    {
        await using var conn = Store.Storage.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        var command = conn.CreateCommand();
        command.CommandText = $"select count(*) from {Schema}.mt_natural_key_removalcase where tenant_id = @tenant";
        command.Parameters.AddWithValue("tenant", tenantId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveMartenManagedTenantsAsync_takes_the_tenants_natural_key_rows_and_leaves_another_tenants_alone()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "nk_alpha", "nk_beta");
        await OpenCaseAsync("nk_alpha", "CASE-1");
        var beta = await OpenCaseAsync("nk_beta", "CASE-1");

        await Store.Advanced.RemoveMartenManagedTenantsAsync(["nk_alpha"], CancellationToken.None);

        (await CountNaturalKeyRowsAsync("nk_alpha")).ShouldBe(0);
        (await CountNaturalKeyRowsAsync("nk_beta")).ShouldBe(1);
        (await FetchByNumberAsync("nk_beta", "CASE-1")).ShouldNotBeNull().Id.ShouldBe(beta);
    }

    [Fact]
    public async Task DeleteAllTenantDataAsync_takes_the_tenants_natural_key_rows()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "nk_gamma", "nk_delta");
        await OpenCaseAsync("nk_gamma", "CASE-2");
        await OpenCaseAsync("nk_delta", "CASE-2");

        await Store.Advanced.DeleteAllTenantDataAsync("nk_gamma", CancellationToken.None);

        (await CountNaturalKeyRowsAsync("nk_gamma")).ShouldBe(0);
        (await CountNaturalKeyRowsAsync("nk_delta")).ShouldBe(1);
    }

    [Fact]
    public async Task removing_a_guid_tenant_by_its_partition_suffix_takes_its_natural_key_rows()
    {
        // The natural-key rows carry the canonical Guid string; the removal names the hyphen-free suffix (#4567)
        var alpha = Guid.NewGuid();
        var beta = Guid.NewGuid();
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);
        await OpenCaseAsync(alpha.ToString(), "CASE-3");
        await OpenCaseAsync(beta.ToString(), "CASE-3");

        await Store.Advanced.RemoveMartenManagedTenantsAsync([alpha.ToString("N")], CancellationToken.None);

        (await CountNaturalKeyRowsAsync(alpha.ToString())).ShouldBe(0);
        (await CountNaturalKeyRowsAsync(beta.ToString())).ShouldBe(1);
    }

    [Fact]
    public async Task a_removed_and_re_added_tenant_can_reuse_its_natural_keys()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "nk_epsilon");
        await OpenCaseAsync("nk_epsilon", "CASE-4");

        await Store.Advanced.RemoveMartenManagedTenantsAsync(["nk_epsilon"], CancellationToken.None);
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "nk_epsilon");

        var reopened = await OpenCaseAsync("nk_epsilon", "CASE-4");
        (await FetchByNumberAsync("nk_epsilon", "CASE-4")).ShouldNotBeNull().Id.ShouldBe(reopened);
    }
}

public sealed record RemovalCaseNumber(string Value);

public sealed record RemovalCaseOpened(Guid CaseId, string Number);

public sealed record RemovalCase
{
    public Guid Id { get; set; }

    [NaturalKey]
    public RemovalCaseNumber Number { get; set; } = null!;

    [NaturalKeySource]
    public static RemovalCase Create(RemovalCaseOpened e) => new() { Id = e.CaseId, Number = new RemovalCaseNumber(e.Number) };
}
