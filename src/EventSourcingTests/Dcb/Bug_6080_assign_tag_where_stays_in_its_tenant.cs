#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Tags;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Dcb;

public record Bug6080RegionId(Guid Value);

public record Bug6080Ordered(string Customer);

/// <summary>
///     #6080: under conjoined tenancy, <c>AssignTagWhere</c> tags only the session tenant's events, and moves only
///     that tenant's tag versions, whichever way the tags are stored.
/// </summary>
public class Bug_6080_assign_tag_where_stays_in_its_tenant: OneOffConfigurationsContext
{
    private const string Red = "red";
    private const string Blue = "blue";

    private void configure(DcbStorageMode mode, bool archivedPartitioning = true)
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseArchivedStreamPartitioning = archivedPartitioning;
            opts.Events.DcbStorageMode = mode;
            opts.Events.AddEventType<Bug6080Ordered>();
            opts.Events.RegisterTagType<Bug6080RegionId>("bug6080_region");
        });
    }

    private string orderedTypeName => theStore.Options.EventGraph.EventMappingFor<Bug6080Ordered>().EventTypeName;

    private async Task<Guid> order(string tenantId, string customer)
    {
        await using var session = theStore.LightweightSession(tenantId);
        var streamId = session.Events.StartStream(new Bug6080Ordered(customer)).Id;
        await session.SaveChangesAsync();
        return streamId;
    }

    private async Task<string[]> customersTagged(string tenantId, Bug6080RegionId region)
    {
        await using var query = theStore.QuerySession(tenantId);
        var events = await query.Events.QueryByTagsAsync(new EventTagQuery().Or(region));
        return events.Select(x => x.Data).OfType<Bug6080Ordered>().Select(x => x.Customer).OrderBy(x => x).ToArray();
    }

    private async Task<long> scalarAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    // Read straight from storage: a tag read filters by tenant, so it does not see a tag on another tenant's event
    private Task<long> taggedEventsOfAsync(DcbStorageMode mode, string tenantId)
        => mode == DcbStorageMode.HStore
            ? scalarAsync(
                $"select count(*) from {SchemaName}.mt_events where tenant_id = '{tenantId}' and tags ? 'bug6080_region'")
            : scalarAsync(
                $"select count(*) from {SchemaName}.mt_event_tag_bug6080_region t join {SchemaName}.mt_events e on e.seq_id = t.seq_id where e.tenant_id = '{tenantId}'");

    // A tag row's tenant is its event's tenant
    private Task<long> tagRowsOfAnotherTenantsEventAsync()
        => scalarAsync(
            $"select count(*) from {SchemaName}.mt_event_tag_bug6080_region t join {SchemaName}.mt_events e on e.seq_id = t.seq_id where t.tenant_id <> e.tenant_id");

    private Task<long> versionRowsAsync(string tenantId, Bug6080RegionId region)
        => scalarAsync(
            $"select count(*) from {SchemaName}.mt_dcb_tag_version where tag_value = '{region.Value}' and tenant_id = '{tenantId}'");

    private Task<long> highestVersionAsync(string tenantId, Bug6080RegionId region)
        => scalarAsync(
            $"select coalesce(max(version), 0) from {SchemaName}.mt_dcb_tag_version where tag_value = '{region.Value}' and tenant_id = '{tenantId}'");

    [Theory]
    [InlineData(DcbStorageMode.TagTables, true)]
    [InlineData(DcbStorageMode.TagTables, false)]
    [InlineData(DcbStorageMode.HStore, true)]
    [InlineData(DcbStorageMode.HStore, false)]
    public async Task assign_tag_where_tags_only_the_session_tenants_events(DcbStorageMode mode,
        bool archivedPartitioning)
    {
        configure(mode, archivedPartitioning);
        await order(Red, "Alice");
        await order(Red, "Bob");
        await order(Blue, "Carol");

        var region = new Bug6080RegionId(Guid.NewGuid());

        // The predicate matches every tenant's events. The "or" is there because the tenant filter is added to it.
        var typeName = orderedTypeName;
        await using (var session = theStore.LightweightSession(Red))
        {
            session.Events.AssignTagWhere(x => x.EventTypeName == typeName || x.EventTypeName == "nothing", region);
            await session.SaveChangesAsync();
        }

        (await taggedEventsOfAsync(mode, Red)).ShouldBe(2);
        (await taggedEventsOfAsync(mode, Blue)).ShouldBe(0);
        (await customersTagged(Red, region)).ShouldBe(["Alice", "Bob"]);
        (await customersTagged(Blue, region)).ShouldBeEmpty();

        (await versionRowsAsync(Red, region)).ShouldBeGreaterThan(0);
        (await versionRowsAsync(Blue, region)).ShouldBe(0);
        var redVersion = await highestVersionAsync(Red, region);

        // Blue's own assignment tags Blue's event and leaves Red's alone
        await using (var session = theStore.LightweightSession(Blue))
        {
            session.Events.AssignTagWhere(x => x.EventTypeName == typeName, region);
            await session.SaveChangesAsync();
        }

        (await taggedEventsOfAsync(mode, Red)).ShouldBe(2);
        (await taggedEventsOfAsync(mode, Blue)).ShouldBe(1);
        (await customersTagged(Red, region)).ShouldBe(["Alice", "Bob"]);
        (await customersTagged(Blue, region)).ShouldBe(["Carol"]);

        (await versionRowsAsync(Blue, region)).ShouldBe(await versionRowsAsync(Red, region));
        (await highestVersionAsync(Red, region)).ShouldBe(redVersion);

        if (mode == DcbStorageMode.TagTables)
        {
            (await tagRowsOfAnotherTenantsEventAsync()).ShouldBe(0);
        }
    }

    [Theory]
    [InlineData(DcbStorageMode.TagTables)]
    [InlineData(DcbStorageMode.HStore)]
    public async Task assign_tag_where_can_tag_an_archived_event(DcbStorageMode mode)
    {
        configure(mode);
        var archived = await order(Red, "Alice");
        await order(Red, "Bob");

        await using (var session = theStore.LightweightSession(Red))
        {
            session.Events.ArchiveStream(archived);
            await session.SaveChangesAsync();
        }

        var region = new Bug6080RegionId(Guid.NewGuid());
        var typeName = orderedTypeName;
        await using (var session = theStore.LightweightSession(Red))
        {
            session.Events.AssignTagWhere(x => x.EventTypeName == typeName, region);
            await session.SaveChangesAsync();
        }

        // A tag read leaves out archived events
        (await customersTagged(Red, region)).ShouldBe(["Bob"]);

        // and finds the archived one's tag once the stream is back
        await using (var session = theStore.LightweightSession(Red))
        {
            session.Events.UnArchiveStream(archived);
            await session.SaveChangesAsync();
        }

        (await customersTagged(Red, region)).ShouldBe(["Alice", "Bob"]);
    }
}
