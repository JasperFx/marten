#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Dcb;

/// <summary>
///     <c>RemoveMartenManagedTenantsAsync</c> drops a tenant's <c>mt_events</c> partition. Each DCB tag table has a
///     foreign key to <c>mt_events</c> that does not cascade, and the tag tables are not partitioned by tenant, so
///     the tenant's tag rows have to go before its partition does, as they do for <c>DeleteAllTenantDataAsync</c>
///     (#5694). The tag rows carry the tenant id while the removal names partition suffixes, which differ for Guid
///     tenants.
/// </summary>
public class removing_a_tenant_with_tagged_events: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_rm_tags";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<TaggedRemovalProbeEvent>();
        opts.Events.RegisterTagType<RemovalProbeTag>("removal_probe");
    }

    [Fact]
    public async Task removing_a_tenant_takes_its_tag_rows_and_leaves_another_tenants_alone()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var tag = new RemovalProbeTag("probe-" + Guid.NewGuid().ToString("N"));
        await appendTaggedAsync("alpha", tag);
        var betaStream = await appendTaggedAsync("beta", tag);

        await Store.Advanced.RemoveMartenManagedTenantsAsync(["alpha"], CancellationToken.None);

        (await tableExistsAsync("mt_events_alpha")).ShouldBeFalse();
        (await countTagRowsAsync(tag.Value, "alpha")).ShouldBe(0);
        (await countTagRowsAsync(tag.Value, "beta")).ShouldBe(1, "removing alpha must leave beta's tag rows alone");

        await using var query = Store.QuerySession("beta");
        (await query.Events.FetchStreamAsync(betaStream, token: TestContext.Current.CancellationToken)).Count
            .ShouldBe(1);
    }

    [Fact]
    public async Task removing_a_guid_tenant_by_its_partition_suffix_takes_its_tag_rows()
    {
        // A Guid tenant's partition suffix is its hyphen-free "N" form (#4567), but its events and tag rows
        // carry the canonical Guid string
        var alpha = Guid.NewGuid();
        var beta = Guid.NewGuid();
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, alpha, beta);

        var tag = new RemovalProbeTag("probe-" + Guid.NewGuid().ToString("N"));
        await appendTaggedAsync(alpha.ToString(), tag);
        await appendTaggedAsync(beta.ToString(), tag);

        await Store.Advanced.RemoveMartenManagedTenantsAsync([alpha.ToString("N")], CancellationToken.None);

        (await tableExistsAsync($"mt_events_{alpha:N}")).ShouldBeFalse();
        (await countTagRowsAsync(tag.Value, alpha.ToString())).ShouldBe(0);
        (await countTagRowsAsync(tag.Value, beta.ToString())).ShouldBe(1);
    }

    private async Task<Guid> appendTaggedAsync(string tenantId, RemovalProbeTag tag)
    {
        var streamId = Guid.NewGuid();
        await using var session = Store.LightweightSession(tenantId);
        var e = session.Events.BuildEvent(new TaggedRemovalProbeEvent());
        e.WithTag(tag);
        session.Events.StartStream(streamId, e);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        return streamId;
    }

    private async Task<bool> tableExistsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand("select to_regclass(:name) is not null");
        cmd.Parameters.AddWithValue("name", $"{Schema}.{tableName}");
        return (bool)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task<long> countTagRowsAsync(string value, string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand(
            $"select count(*) from {Schema}.mt_event_tag_removal_probe where value = :v and tenant_id = :t");
        cmd.Parameters.AddWithValue("v", value);
        cmd.Parameters.AddWithValue("t", tenantId);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

public record RemovalProbeTag(string Value);

public class TaggedRemovalProbeEvent
{
}
