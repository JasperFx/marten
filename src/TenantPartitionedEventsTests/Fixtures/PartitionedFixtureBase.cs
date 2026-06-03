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

            // Projection registration is stream-identity-bound (Guid vs string id
            // type on the projection's aggregate must match opts.Events.StreamIdentity),
            // so each fixture flavor registers its own parallel set. Base default =
            // Guid-id registration to preserve the original GuidPartitionedFixture
            // contract for tests already pinned to TripDistance/TripCount/TripSnapshot.
            RegisterProjections(opts);
        });

        // Sanity: the schema applies cleanly on its own. Caller tests register
        // tenants on-demand via Store.Advanced.AddMartenManagedTenantsAsync.
        await Store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    /// <summary>
    /// Stream-identity-flavored projection registration. Default = Guid-id
    /// projections (matches <see cref="GuidPartitionedFixture"/>). The
    /// <see cref="StringPartitionedFixture"/> overrides this to register
    /// string-id parallels so its store builds cleanly.
    /// </summary>
    /// <remarks>
    /// Representative projections registered up front. Short DocumentAlias
    /// matters: nested type names + tenant suffix overflow PG's 64-byte
    /// identifier limit. Projection .Name is set in each projection's
    /// constructor to a deterministic value so ShardName.Compose(...) from
    /// test code stays stable.
    /// </remarks>
    protected virtual void RegisterProjections(StoreOptions opts)
    {
        opts.Projections.Add<TripDistanceProjection>(ProjectionLifecycle.Async);
        opts.Projections.Add<TripCountInlineProjection>(ProjectionLifecycle.Inline);
        opts.Projections.LiveStreamAggregation<TripSnapshot>();

        opts.Schema.For<TripDistance>().Identity(x => x.Id).DocumentAlias("p2c_trip_dist");
        opts.Schema.For<TripCount>().Identity(x => x.Id).DocumentAlias("p2c_trip_count");
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

    /// <summary>
    /// All rows in the per-store <c>mt_event_progression</c> table whose name
    /// starts with <paramref name="namePrefix"/>. Used by admin-API tests that
    /// seed progression rows for a specific projection name and want to read
    /// just those rows back without picking up the high-water row + sibling
    /// projections from the shared store.
    /// </summary>
    public async Task<System.Collections.Generic.IReadOnlyList<(string Name, long LastSeqId)>>
        ReadProgressionRowsAsync(string schemaName, string namePrefix)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select name, last_seq_id from {schemaName}.mt_event_progression where name like @n order by name";
        cmd.Parameters.AddWithValue("n", namePrefix + "%");
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new System.Collections.Generic.List<(string, long)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetInt64(1)));
        }
        return rows;
    }

    /// <summary>
    /// Append <paramref name="count"/> TripLeg events to a fresh stream for the
    /// given tenant. Returns the freshly-minted stream id so tests can reuse it
    /// for subsequent assertions.
    /// </summary>
    public async Task<Guid> AppendNEventsAsync(string tenantId, int count)
    {
        var streamId = Guid.NewGuid();
        await using var session = Store.LightweightSession(tenantId);
        session.Events.StartStream<TripSnapshot>(streamId, new TripStarted(streamId));
        for (var i = 0; i < count - 1; i++)
        {
            session.Events.Append(streamId, new TripLeg(1.0));
        }
        await session.SaveChangesAsync();
        return streamId;
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
    public const string ProjectionName = "TripDistance";
    public TripDistanceProjection() { Name = ProjectionName; }
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
    public const string ProjectionName = "TripCount";
    public TripCountInlineProjection() { Name = ProjectionName; }
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
