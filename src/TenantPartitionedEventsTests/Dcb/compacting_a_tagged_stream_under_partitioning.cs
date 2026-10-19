#nullable enable
using System;
using System.Linq;
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
///     Compaction deletes the DCB tag rows of the events it folds before the events themselves, because the
///     tag tables' foreign key to <c>mt_events</c> does not cascade. Under <c>UseTenantPartitionedEvents</c>
///     every tenant draws from its own sequence, so the same <c>seq_id</c> is an event in every tenant (#5234):
///     the tag delete has to carry the tenant predicate as well, or compacting one tenant's stream deletes
///     another tenant's tag rows.
/// </summary>
public class compacting_a_tagged_stream_under_partitioning: PartitionedStoreContext
{
    protected override string SchemaPrefix => "tp_compact_tags";

    protected override void ConfigureStore(StoreOptions opts)
    {
        opts.Events.AddEventType<TaggedCompactProbeEvent>();
        opts.Events.RegisterTagType<CompactProbeTag>("compact_probe");
    }

    [Fact]
    public async Task compacting_one_tenants_tagged_stream_leaves_another_tenants_tag_rows_alone()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var streamId = Guid.NewGuid();
        var tag = new CompactProbeTag("probe-" + Guid.NewGuid().ToString("N"));

        foreach (var tenant in new[] { "alpha", "beta" })
        {
            await using var session = Store.LightweightSession(tenant);
            var events = Enumerable.Range(0, 3).Select(_ =>
            {
                var e = session.Events.BuildEvent(new TaggedCompactProbeEvent());
                e.WithTag(tag);
                return (object)e;
            }).ToArray();

            session.Events.StartStream<TaggedCompactProbeAggregate>(streamId, events);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // The collision this test depends on: both tenants' streams are seq_id 1..3
        (await sequencesOf(streamId, "alpha")).ShouldBe(await sequencesOf(streamId, "beta"));

        await using (var session = Store.LightweightSession("alpha"))
        {
            await session.Events.CompactStreamAsync<TaggedCompactProbeAggregate>(streamId);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await countTagRows(tag.Value, "alpha")).ShouldBe(1);
        (await countTagRows(tag.Value, "beta")).ShouldBe(3, "compacting alpha's stream must leave beta's tag rows alone");

        await using var query = Store.QuerySession("beta");
        (await query.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken)).Count.ShouldBe(3);
    }

    [Fact]
    public async Task deleting_all_of_a_tenants_data_takes_its_tag_rows()
    {
        await Store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var streamId = Guid.NewGuid();
        var tag = new CompactProbeTag("probe-" + Guid.NewGuid().ToString("N"));

        foreach (var tenant in new[] { "alpha", "beta" })
        {
            await using var session = Store.LightweightSession(tenant);
            var e = session.Events.BuildEvent(new TaggedCompactProbeEvent());
            e.WithTag(tag);
            session.Events.StartStream<TaggedCompactProbeAggregate>(streamId, e);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await Store.Advanced.DeleteAllTenantDataAsync("alpha", CancellationToken.None);

        (await countTagRows(tag.Value, "alpha")).ShouldBe(0);
        (await countTagRows(tag.Value, "beta")).ShouldBe(1);
    }

    private async Task<long[]> sequencesOf(Guid streamId, string tenantId)
    {
        await using var query = Store.QuerySession(tenantId);
        var events = await query.Events.FetchStreamAsync(streamId, token: TestContext.Current.CancellationToken);
        return events.Select(x => x.Sequence).ToArray();
    }

    private async Task<long> countTagRows(string value, string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand(
            $"select count(*) from {Schema}.mt_event_tag_compact_probe where value = :v and tenant_id = :t");
        cmd.Parameters.AddWithValue("v", value);
        cmd.Parameters.AddWithValue("t", tenantId);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

public record CompactProbeTag(string Value);

public class TaggedCompactProbeEvent
{
}

public class TaggedCompactProbeAggregate
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public void Apply(TaggedCompactProbeEvent _) => Count++;
}
