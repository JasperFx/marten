using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Projections.Flattened;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Projections;

/// <summary>
/// #4617 section 3c (#4650) — pin <see cref="FlatTableProjection"/> under
/// <c>UseTenantPartitionedEvents</c>.
///
/// <list type="bullet">
///   <item>DDL applies cleanly under partitioning — no FK / partition errors
///     (the projection's own table coexists with the partitioned mt_events
///     and mt_streams; relevant to the #4606 carve-out).</item>
///   <item>Inline writes from per-tenant sessions land rows in the flat
///     table for that tenant's events.</item>
///   <item><b>Limitation pin:</b> <see cref="FlatTableProjection"/> is NOT
///     multi-tenant-aware by default. Its <c>SetValue</c> /
///     <c>Map</c> / <c>Increment</c> API only sees event-payload columns
///     (no <c>IEvent</c> metadata access), so users can't transparently
///     route the framework's <c>event.TenantId</c> into a flat-table
///     column. If two tenants append events with the same PK, the second
///     write silently overwrites the first row. Tenant-aware flat tables
///     require the user to add an explicit tenant field to their event
///     payload AND map it into the flat-table PK.</item>
/// </list>
///
/// <para>
/// Own-store because <see cref="FlatTableProjection"/> registration shapes the
/// schema; the shared fixture intentionally registers a minimal projection
/// set.
/// </para>
/// </summary>
public class flat_table_projection_per_tenant : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_flat_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(_schema); } catch { }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<FtCounterIncremented>();

            // Inline so writes from a tenant session land synchronously in
            // the flat table — keeps the assertions deterministic.
            opts.Projections.Add(new FtCounterProjection(), ProjectionLifecycle.Inline);
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ddl_applies_cleanly_under_partitioning()
    {
        // EnsureStorageExistsAsync in InitializeAsync already triggered the
        // events feature; touch the projection's table explicitly to force
        // its DDL to apply. With UseTenantPartitionedEvents on, no FK /
        // partition error should appear (related to the #4606 carve-out
        // that dropped the explicit mt_events → mt_streams FK).
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select count(*) from information_schema.tables where table_schema = @s and table_name = 'ft_counters'";
        cmd.Parameters.AddWithValue("s", _schema);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.ShouldBe(1L, "FlatTableProjection's ft_counters table must exist after schema apply");
    }

    [Fact]
    public async Task per_tenant_inline_writes_land_in_flat_table_for_unique_pks()
    {
        // Each tenant uses a DIFFERENT counterId — proves the inline write path
        // works end-to-end under partitioning. (Same-PK across tenants is the
        // collision pin below.)
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var alphaCounter = Guid.NewGuid();
        var betaCounter = Guid.NewGuid();

        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaCounter, new FtCounterIncremented(alphaCounter, 10));
            session.Events.Append(alphaCounter, new FtCounterIncremented(alphaCounter, 5));
            await session.SaveChangesAsync();
        }
        await using (var session = _store.LightweightSession("beta"))
        {
            session.Events.StartStream(betaCounter, new FtCounterIncremented(betaCounter, 100));
            await session.SaveChangesAsync();
        }

        var alphaTotal = await ReadTotalAsync(alphaCounter);
        var betaTotal = await ReadTotalAsync(betaCounter);

        alphaTotal.ShouldBe(15, "alpha's 10 + 5 should land via inline FlatTableProjection writes");
        betaTotal.ShouldBe(100, "beta's 100 should land via inline FlatTableProjection writes");
    }

    [Fact]
    public async Task cross_tenant_same_pk_silently_overwrites_pin_user_managed_isolation_required()
    {
        // Limitation pin: FlatTableProjection is NOT multi-tenant-aware by
        // default. Two tenants appending events with the SAME counterId
        // collide on the (id) PK. The last write wins — silent overwrite,
        // no exception, no warning.
        //
        // Users who want tenant-isolated flat tables MUST add a tenant field
        // to their event payload and map it into the flat-table PK
        // explicitly. The framework's IEvent.TenantId metadata is NOT
        // reachable from the FlatTableProjection Map/SetValue/Increment API
        // (the lambdas operate on the event payload type, not IEvent<T>).
        //
        // Pinned so a future change that auto-tenants flat tables flips
        // this assertion intentionally.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var sharedCounterId = Guid.NewGuid();

        // alpha writes first.
        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream(sharedCounterId, new FtCounterIncremented(sharedCounterId, 10));
            await session.SaveChangesAsync();
        }
        var afterAlpha = await ReadTotalAsync(sharedCounterId);
        afterAlpha.ShouldBe(10, "alpha's initial 10 lands in the row");

        // beta increments the SAME row (silent overwrite of the row owner —
        // the framework happily applies beta's session writes to the same PK).
        await using (var session = _store.LightweightSession("beta"))
        {
            session.Events.StartStream(sharedCounterId, new FtCounterIncremented(sharedCounterId, 100));
            await session.SaveChangesAsync();
        }
        var afterBeta = await ReadTotalAsync(sharedCounterId);

        // The row's total is now alpha's 10 + beta's 100 = 110 — cross-tenant
        // arithmetic leak because there's no tenant isolation on the flat
        // table's PK. The single row is visible to both tenants reading the
        // table directly.
        afterBeta.ShouldBe(110,
            "without tenant_id in the PK, FlatTableProjection silently merges cross-tenant writes — " +
            "documented limitation per #4650");
    }

    private async Task<int> ReadTotalAsync(Guid counterId)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select total from {_schema}.ft_counters where id = @id";
        cmd.Parameters.AddWithValue("id", counterId);
        var raw = await cmd.ExecuteScalarAsync();
        return raw == null || raw is DBNull ? 0 : (int)raw;
    }
}

public record FtCounterIncremented(Guid CounterId, int Delta);

/// <summary>
/// Single-PK FlatTableProjection — the default shape that's NOT tenant-
/// isolated. Users wanting per-tenant isolation must add a tenant field to
/// their event payload AND map it into the PK; see <c>flat_table_projection_per_tenant.cs</c>
/// summary for the framework limitation.
/// </summary>
public class FtCounterProjection : FlatTableProjection
{
    public FtCounterProjection() : base("ft_counters", SchemaNameSource.EventSchema)
    {
        Name = "FtCounters";

        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Table.AddColumn<int>("total").NotNull().DefaultValue(0);

        // No tablePrimaryKeySource override → PK = stream id (Guid), set by
        // the stream-identification path on the upsert. Just increment total
        // by the event's Delta.
        Project<FtCounterIncremented>(cmd =>
        {
            cmd.Increment(e => e.Delta, "total");
        });
    }
}
