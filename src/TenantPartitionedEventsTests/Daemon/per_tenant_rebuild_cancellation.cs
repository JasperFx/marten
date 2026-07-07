#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Core;
using Xunit;

namespace TenantPartitionedEventsTests.Daemon;

/// <summary>
/// marten#4710 (2): the per-cell rebuild cancellation contract CritterWatch#309's
/// auto-resume path depends on. Cancelling
/// <c>IProjectionDaemon.RebuildProjectionAsync(name, tenantId, ct)</c> mid-flight must
/// leave <c>mt_event_progression</c> in a consistent state — the cell's progression is
/// either unchanged from pre-rebuild or at an actual partial position, never an
/// in-between torn state — and a subsequent rebuild of the same (projection, tenant)
/// cell must complete successfully without manual intervention.
///
/// <para>
/// Cancellation is made deterministic by gating the projection's ApplyAsync on a
/// TaskCompletionSource: the test cancels while the rebuild is provably in-flight
/// (first apply started, blocked on the gate), then releases the gate. No sleeps,
/// no drain loops.
/// </para>
/// </summary>
public class per_tenant_rebuild_cancellation: IAsyncLifetime
{
    private static readonly string SchemaName = $"rebuild_cancel_{Environment.ProcessId}";

    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            // Unique advisory-lock id so this store's daemon machinery never contends
            // with the shared partitioned fixtures running in sibling collections.
            opts.Projections.DaemonLockId = 4791;

            opts.Projections.Add(new GatedPerEventProjection(), ProjectionLifecycle.Async,
                GatedPerEventProjection.ProjectionName);
            opts.Schema.For<TallyDoc>().DocumentAlias("cancel_tally");
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task cancelling_a_per_tenant_rebuild_leaves_progression_consistent_and_the_cell_rebuildable()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        const int eventCount = 40;
        await using (var session = _store.LightweightSession(tenant))
        {
            for (var i = 0; i < 4; i++)
            {
                var stream = Guid.NewGuid();
                session.Events.StartStream(stream,
                    Enumerable.Range(0, eventCount / 4).Select(_ => (object)new TallyEvent()).ToArray());
            }

            await session.SaveChangesAsync();
        }

        // Arm the gate BEFORE the rebuild starts: the first ApplyAsync signals Started
        // and then blocks until the test releases it.
        GatedPerEventProjection.Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedPerEventProjection.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = new CancellationTokenSource();
        using var daemon = await _store.BuildProjectionDaemonAsync();

        var rebuildTask = daemon.RebuildProjectionAsync(
            GatedPerEventProjection.ProjectionName, tenant, cts.Token);

        // Deterministically in-flight: the first apply has started and is parked on the gate.
        (await Task.WhenAny(GatedPerEventProjection.Started.Task, Task.Delay(TimeSpan.FromSeconds(60))))
            .ShouldBe(GatedPerEventProjection.Started.Task, "rebuild never reached the projection");

        cts.Cancel();
        GatedPerEventProjection.Gate.TrySetResult(true);

        try
        {
            await rebuildTask;
        }
        catch (OperationCanceledException)
        {
            // Either outcome is acceptable — the contract is about the state left behind,
            // not the exception shape.
        }

        // Consistency: every progression row for this cell is a readable, sane position —
        // between 0 and the tenant's max appended sequence (per-tenant sequences are
        // independent, so eventCount is this tenant's ceiling). "Torn" would surface as
        // a value beyond the ceiling or an unreadable row.
        var positions = await ReadCellProgressionsAsync(tenant);
        foreach (var position in positions)
        {
            position.ShouldBeInRange(0, eventCount);
        }

        // The cell must be rebuildable afterwards with no manual repair.
        GatedPerEventProjection.Gate = null;
        GatedPerEventProjection.Started = null;

        await daemon.RebuildProjectionAsync(GatedPerEventProjection.ProjectionName, tenant,
            CancellationToken.None);

        await using (var query = _store.QuerySession(tenant))
        {
            (await query.Query<TallyDoc>().CountAsync()).ShouldBe(eventCount,
                "the follow-up rebuild must fully materialize the cell");
        }
    }

    [Fact]
    public async Task pre_cancelled_token_does_not_disturb_the_cell()
    {
        var tenant = PartitionedFixtureBase.NewTenant();
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, tenant);

        await using (var session = _store.LightweightSession(tenant))
        {
            session.Events.StartStream(Guid.NewGuid(), new TallyEvent(), new TallyEvent());
            await session.SaveChangesAsync();
        }

        GatedPerEventProjection.Gate = null;
        GatedPerEventProjection.Started = null;

        using var daemon = await _store.BuildProjectionDaemonAsync();

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        try
        {
            await daemon.RebuildProjectionAsync(GatedPerEventProjection.ProjectionName, tenant,
                cancelled.Token);
        }
        catch (OperationCanceledException)
        {
        }

        // And the cell still rebuilds cleanly.
        await daemon.RebuildProjectionAsync(GatedPerEventProjection.ProjectionName, tenant,
            CancellationToken.None);

        await using var query = _store.QuerySession(tenant);
        (await query.Query<TallyDoc>().CountAsync()).ShouldBe(2);
    }

    private async Task<IReadOnlyList<long>> ReadCellProgressionsAsync(string tenantId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select last_seq_id from {SchemaName}.mt_event_progression where name like @name and name like @tenant";
        cmd.Parameters.AddWithValue("name", GatedPerEventProjection.ProjectionName + "%");
        cmd.Parameters.AddWithValue("tenant", "%" + tenantId + "%");

        var results = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetInt64(0));
        }

        return results;
    }
}

public record TallyEvent;

public class TallyDoc
{
    public Guid Id { get; set; }
}

/// <summary>
/// Stores one TallyDoc per event so the post-rebuild doc count equals the event count
/// regardless of batching. The static Gate/Started pair lets a test hold the first
/// ApplyAsync mid-flight deterministically; both default to null (pass-through) so
/// sibling tests are unaffected.
/// </summary>
public class GatedPerEventProjection: IProjection
{
    public const string ProjectionName = "GatedTally";

    public static volatile TaskCompletionSource<bool>? Gate;
    public static volatile TaskCompletionSource<bool>? Started;

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<IEvent> events,
        CancellationToken cancellation)
    {
        Started?.TrySetResult(true);

        var gate = Gate;
        if (gate != null)
        {
            await gate.Task.ConfigureAwait(false);
        }

        foreach (var e in events.Where(x => x.Data is TallyEvent))
        {
            operations.Store(new TallyDoc { Id = e.Id });
        }
    }
}
