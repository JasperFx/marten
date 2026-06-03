#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Fixtures;

/// <summary>
/// Shared base for the two stream-identity-flavored fixtures. Each fixture
/// builds ONE <see cref="DocumentStore"/> shared by every test in its collection
/// — per-test isolation lives on the <see cref="NewTenant"/> tenant-id, not on
/// the schema. The store is built once in <see cref="InitializeAsync"/> and the
/// same projection set is registered up front (projection registration is
/// immutable after the store is built).
/// </summary>
/// <remarks>
/// <para>
/// Why share a store at all: every existing per-tenant-partitioning test today
/// builds a fresh <see cref="DocumentStore"/> and drops the schema in-between —
/// schema creation under <c>UseTenantPartitionedEvents</c> is the hot path that
/// trips 42P07/23505 partition-name races when two TFMs race to CREATE the same
/// partition. One store + per-test unique tenants neutralizes the race
/// structurally and keeps the suite fast.
/// </para>
/// <para>
/// Schema name carries <see cref="Environment.ProcessId"/> so net9 + net10 runs
/// in the same DB never collide.
/// </para>
/// </remarks>
public abstract class PartitionedFixtureBase: IAsyncLifetime
{
    /// <summary>
    /// Lowercase, hyphen-free, leading-digit-safe (#4567), under the 63-byte
    /// suffix limit. Format <c>t_{12 hex chars}</c> — fits comfortably inside
    /// <c>mt_events_sequence_t_xxxxxxxxxxxx</c> at 32 chars total, well under 63.
    /// </summary>
    public static string NewTenant() => "t_" + Guid.NewGuid().ToString("N")[..12];

    public DocumentStore Store { get; private set; } = null!;

    /// <summary>
    /// Subclass picks the schema name (suffixed by ProcessId in the call) +
    /// stream-identity flavor + a unique <see cref="EventGraphProjections.DaemonLockId"/>.
    /// </summary>
    protected abstract void ConfigureStore(StoreOptions opts);

    public virtual async Task InitializeAsync()
    {
        Store = DocumentStore.For(opts =>
        {
            ConfigureStore(opts);

            // Common config required by UseTenantPartitionedEvents:
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            // Representative projections registered up front. Short DocumentAlias
            // matters: nested type names + tenant suffix overflow PG's 64-byte
            // identifier limit.
            opts.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
            opts.Projections.Add<TripCountInlineProjection>(ProjectionLifecycle.Inline);
            opts.Projections.LiveStreamAggregation<TripSnapshot>();

            opts.Schema.For<TripDistance>().Identity(x => x.Id).DocumentAlias("p2c_trip_dist");
            opts.Schema.For<TripCount>().Identity(x => x.Id).DocumentAlias("p2c_trip_count");
        });

        // Sanity: the schema applies cleanly on its own. Caller tests register
        // tenants on-demand via Store.Advanced.AddMartenManagedTenantsAsync.
        await Store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public virtual Task DisposeAsync()
    {
        Store?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Read the `mt_events_sequence_{suffix}` last-value for a tenant — handy
    /// for per-tenant monotonicity assertions.
    /// </summary>
    public async Task<long> ReadSequenceLastValueAsync(string tenantId, string schemaName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        // pg_sequences.last_value: NULL until the first nextval.
        var raw = await conn.CreateCommand(
            $"select last_value from {schemaName}.mt_events_sequence_{tenantId}")
            .ExecuteScalarAsync();
        return raw is long v ? v : 0L;
    }

    /// <summary>
    /// Counted via <c>mt_events</c> directly, scoped to one tenant. Useful when a
    /// test wants to assert isolation without going through a query-builder.
    /// </summary>
    public async Task<long> CountEventsForTenantAsync(string tenantId, string schemaName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        return (long)(await conn.CreateCommand(
            $"select count(*) from {schemaName}.mt_events where tenant_id = :t")
            .With("t", tenantId)
            .ExecuteScalarAsync())!;
    }
}

// ---- Representative projection / aggregate types ----
// Kept here (not in test files) so the projection registration is wired to
// stable types the fixture controls. Short DocumentAliases bake the 64-byte
// identifier limit constraint into the type design.

public class TripDistance
{
    public Guid Id { get; set; }
    public double Distance { get; set; }
    public int Version { get; set; }
}

public partial class TripDistanceProjection: SingleStreamProjection<TripDistance, Guid>
{
    public void Apply(TripDistance agg, TripLeg @event) => agg.Distance += @event.Distance;
}

public class TripCount
{
    public Guid Id { get; set; }
    public int Count { get; set; }
    public int Version { get; set; }
}

public partial class TripCountInlineProjection: SingleStreamProjection<TripCount, Guid>
{
    public void Apply(TripCount agg, TripLeg @event) => agg.Count++;
}

public class TripSnapshot
{
    public Guid Id { get; set; }
    public double Distance { get; set; }
    public int LegCount { get; set; }
    public int Version { get; set; }

    public void Apply(TripLeg @event)
    {
        Distance += @event.Distance;
        LegCount++;
    }

    public static TripSnapshot Create(TripStarted @event) => new() { Id = @event.Id };
}

public record TripStarted(Guid Id);
public record TripLeg(double Distance);
