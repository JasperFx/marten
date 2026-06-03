using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Linq;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.ReadQuery;

/// <summary>
/// #4617 section 3b deferred — pin the behavior of
/// <c>EnableUniqueIndexOnEventId</c> under <c>UseTenantPartitionedEvents</c>.
///
/// <para>
/// Marten emits <c>CREATE UNIQUE INDEX idx_mt_events_event_id ON mt_events (id)</c>
/// when this flag is on. PostgreSQL requires that a unique index on a
/// partitioned table include the partition key — so the unique-on-id-alone
/// constraint cannot be enforced at the parent level; per-tenant
/// partitioning relaxes it to PER-TENANT uniqueness (local index per
/// partition, scoped by tenant_id). Same event id in two different tenants'
/// partitions is allowed; same event id within ONE tenant's partition
/// trips a uniqueness violation.
/// </para>
///
/// <para>
/// Own-store because <c>EnableUniqueIndexOnEventId</c> is a store-level flag
/// — flipping it on the shared fixture would affect every sibling test.
/// </para>
/// </summary>
public class enable_unique_index_on_event_id_under_partitioning : IAsyncLifetime
{
    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_eui_{Environment.ProcessId}_{Guid.NewGuid():N}".Substring(0, 32);

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
            opts.Events.EnableUniqueIndexOnEventId = true;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<UniqEvent>();
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task same_event_id_in_two_tenants_does_NOT_violate_unique_index_under_partitioning()
    {
        // The pin: under partitioning, the unique index on id is per-tenant
        // (local per partition). The same Guid event id can land in two
        // tenants' partitions without violation — each partition's local
        // index sees only its own rows.
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        var sharedEventId = Guid.NewGuid();
        var alphaStream = Guid.NewGuid();
        var betaStream = Guid.NewGuid();

        await using (var session = _store.LightweightSession("alpha"))
        {
            session.Events.StartStream(alphaStream, new Event<UniqEvent>(new UniqEvent("a")) { Id = sharedEventId });
            await session.SaveChangesAsync();
        }

        // Beta appends an event with the SAME id — should succeed because the
        // unique index is scoped per tenant partition.
        await using (var session = _store.LightweightSession("beta"))
        {
            session.Events.StartStream(betaStream, new Event<UniqEvent>(new UniqEvent("b")) { Id = sharedEventId });
            await session.SaveChangesAsync();
        }

        // Sanity: both events live in their respective partitions.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand(
            $"select count(*) from {_schema}.mt_events where id = @id");
        cmd.Parameters.AddWithValue("id", sharedEventId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        count.ShouldBe(2L,
            "two events share the same id across two tenant partitions — the unique " +
            "index relaxes to per-tenant scope under partitioning");
    }
}

public record UniqEvent(string Label);
