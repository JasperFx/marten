#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
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

public record TpTeamId(Guid Value);

public record TpMatchPlayed(TpTeamId Home, TpTeamId Away);

/// <summary>
/// #5265 under <c>UseTenantPartitionedEvents</c>. The reported bug was framed as "Append loses the
/// second tag, StartStream keeps it", but the real split is bulk function vs per-event: a
/// non-partitioned <c>StartStream</c> takes the per-event <c>InsertStream</c> +
/// <c>QuickAppendEventWithVersion</c> route and queues one tag insert per tag, while everything
/// else routes through <c>mt_quick_append_events</c>, whose signature has one slot per (event, tag
/// type).
/// <para>
/// Under per-tenant partitioning <c>forceBulkFunction</c> sends <c>StartStream</c> down that same
/// bulk route (only the bulk function honors the per-tenant sequence pick), so the StartStream case
/// that passes on an ordinary store fails here. That is the case this pins.
/// </para>
/// </summary>
public class Bug_5265_multiple_tags_under_partitioning
{
    private static async Task<DocumentStore> BuildStoreAsync(string schema)
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(schema); } catch { }
        }

        var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.DcbStorageMode = DcbStorageMode.TagTables;
            opts.Events.AddEventType<TpMatchPlayed>();
            opts.Events.RegisterTagType<TpTeamId>("tp_team");
        });

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
        return store;
    }

    [Fact]
    public async Task start_stream_keeps_every_tag_of_one_type_when_partitioning_forces_the_bulk_function()
    {
        var schema = $"tp_5265_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);
        using var store = await BuildStoreAsync(schema);

        const string TenantId = "acme";
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, TenantId);

        var home = new TpTeamId(Guid.NewGuid());
        var away = new TpTeamId(Guid.NewGuid());

        await using (var session = store.LightweightSession(TenantId))
        {
            var evt = session.Events.BuildEvent(new TpMatchPlayed(home, away));
            evt.WithTag(home);
            evt.WithTag(away);

            // A StartStream, which on a non-partitioned store would take the per-event route and
            // never have exercised this at all.
            session.Events.StartStream(Guid.NewGuid(), evt);
            await session.SaveChangesAsync();
        }

        await using var query = store.LightweightSession(TenantId);

        (await query.Events.QueryByTagsAsync(new EventTagQuery().Or<TpTeamId>(home)))
            .ShouldHaveSingleItem();
        (await query.Events.QueryByTagsAsync(new EventTagQuery().Or<TpTeamId>(away)))
            .ShouldHaveSingleItem();
    }
}
