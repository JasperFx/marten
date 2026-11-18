#nullable enable
using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
///     #6087: in a database-per-tenant store the <c>mt_dcb_tag_version</c> rows carry the real tenant id, so
///     <c>DeleteSingleEventStreamAsync(id)</c> with no tenant, which is what <c>CompositeDocumentCleaner</c> runs
///     against every database, has to find them by stream alone.
/// </summary>
public class Bug_6087_delete_stream_without_a_tenant_in_a_database_per_tenant_store: OneOffConfigurationsContext
{
    private const string Tenant = "bug6087";

    private void configure(DcbStorageMode mode)
    {
        StoreOptions(opts =>
        {
            opts.MultiTenantedDatabases(x => x.AddSingleTenantDatabase(ConnectionSource.ConnectionString, Tenant));
            opts.Events.AddEventType<Bug6035Happened>();
            opts.Events.RegisterTagType<Bug6035TagId>("bug6087");
            opts.Events.RegisterTagType<Bug6035OtherTag>("bug6087_other");
            opts.Events.DcbStorageMode = mode;
            opts.Events.DcbConflictScope = DcbConflictScope.TagValueAndEventType;
        });
    }

    private async Task<Guid> startStream(object data, params object[] tags)
    {
        var streamId = Guid.NewGuid();
        await using var session = theStore.LightweightSession(Tenant);
        var e = session.Events.BuildEvent(data);
        e.WithTag(tags);
        session.Events.StartStream(streamId, e);
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // Every row holding the value, the per-event-type versions included
    private Task<long> rowsAsync(object tagValue)
        => scalarAsync($"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_value = '{tagValue}'");

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task deleting_a_stream_without_a_tenant_takes_the_rows_no_other_stream_carries(DcbStorageMode mode)
    {
        configure(mode);
        var mine = new Bug6035TagId(Guid.NewGuid());
        var shared = new Bug6035OtherTag("shared-" + Guid.NewGuid().ToString("N"));
        var doomed = await startStream(new Bug6035Happened("one"), mine, shared);
        var survivor = await startStream(new Bug6035Happened("one"), shared);

        // The rows carry the tenant that appended, not *DEFAULT*
        (await scalarAsync(
                $"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_value = '{mine.Value}' and tenant_id = '{Tenant}'"))
            .ShouldBeGreaterThan(0);

        var sharedVersion = await scalarAsync(
            $"select max(version) from {SchemaName}.mt_dcb_tag_version where tag_value = '{shared.Value}'");

        await theStore.Advanced.Clean.DeleteSingleEventStreamAsync(doomed);

        (await rowsAsync(mine.Value)).ShouldBe(0);

        // Another stream still carries this one, so its rows stay, moved
        (await rowsAsync(shared.Value)).ShouldBeGreaterThan(0);
        (await scalarAsync(
                $"select min(version) from {SchemaName}.mt_dcb_tag_version where tag_value = '{shared.Value}'"))
            .ShouldBeGreaterThan(sharedVersion);

        await using var query = theStore.QuerySession(Tenant);
        (await query.Events.FetchStreamStateAsync(doomed)).ShouldBeNull();
        (await query.Events.FetchStreamAsync(survivor)).Count.ShouldBe(1);
    }
}
