using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.MultiTenancy;
using Marten;
using Marten.Events;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace MultiTenancyTests;

/// <summary>
/// #4596 Phase 1 Session 2 — per-tenant Postgres sequence + QuickAppendEventFunction rewrite.
/// Validates that with <c>UseTenantPartitionedEvents = true</c>:
///   (a) AddMartenManagedTenantsAsync also creates a <c>mt_events_sequence_{suffix}</c>
///       Postgres sequence for every freshly-registered partition.
///   (b) PerTenantEventSequences emits the IF-NOT-EXISTS CREATE statements as part of
///       the EventGraph feature schema, so a green-field apply over an empty database
///       (with pre-registered tenants) produces the sequences too.
///   (c) The kickoff requirement: two tenants append events independently and each
///       tenant's events draw a monotonically-increasing seq_id from THAT tenant's
///       sequence — the two tenants' seq_ids do not interleave through a single
///       shared global counter.
/// Session 3 (in-flight) will rewire mt_event_progression by (name, tenant_id);
/// admin overrides land in Session 4.
/// </summary>
public class use_tenant_partitioned_events_quick_append
{
    private const string Schema = "tenant_partitioned_events_session2";

    private static async Task ResetSchemaAsync(string schema)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        try { await conn.DropSchemaAsync(schema); } catch (Exception) { }
    }

    private static DocumentStore BuildStore(string schema)
    {
        return DocumentStore.For(o =>
        {
            o.Connection(ConnectionSource.ConnectionString);
            o.DatabaseSchemaName = schema;
            o.Events.TenancyStyle = TenancyStyle.Conjoined;
            o.Events.UseTenantPartitionedEvents = true;
            // QuickWithServerTimestamps is the default in 9.x — Rich would be rejected
            // by the config guard; this line is explicit for the test reader.
            o.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            o.Policies.AllDocumentsAreMultiTenanted();

            o.Events.AddEventType<RandomEvent>();
            o.Events.AddEventType<BEvent>();
        });
    }

    private static async Task<long[]> ReadSequenceCurrentValues(string schema, params string[] tenantSuffixes)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var values = new long[tenantSuffixes.Length];
        for (var i = 0; i < tenantSuffixes.Length; i++)
        {
            // last_value is only meaningful after at least one nextval; otherwise PG
            // returns the start. Use pg_sequences for a portable read.
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "select last_value from pg_sequences where schemaname = @s and sequencename = @n";
            cmd.Parameters.AddWithValue("s", schema);
            cmd.Parameters.AddWithValue("n", "mt_events_sequence_" + tenantSuffixes[i]);
            var result = await cmd.ExecuteScalarAsync();
            values[i] = result is long l ? l : (result is null ? 0 : Convert.ToInt64(result));
        }

        return values;
    }

    [Fact]
    public async Task per_tenant_sequences_are_created_on_AddMartenManagedTenantsAsync()
    {
        var schema = Schema + "_addtenants";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select sequencename from pg_sequences where schemaname = @s and sequencename like 'mt_events_sequence\\_%' escape '\\' order by sequencename";
        cmd.Parameters.AddWithValue("s", schema);
        await using var reader = await cmd.ExecuteReaderAsync();

        var sequences = new System.Collections.Generic.List<string>();
        while (await reader.ReadAsync())
        {
            sequences.Add(reader.GetString(0));
        }

        sequences.ShouldBe(new[] { "mt_events_sequence_alpha", "mt_events_sequence_beta" });
    }

    [Fact]
    public async Task per_tenant_sequences_are_created_when_partitions_registered_before_first_apply()
    {
        var schema = Schema + "_preregistered";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);

        // Register tenants BEFORE the events table is materialized — the schema-apply
        // path through PerTenantEventSequences must emit the CREATE SEQUENCE IF NOT
        // EXISTS statements alongside the tables.
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "gamma", "delta");

        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "select sequencename from pg_sequences where schemaname = @s and sequencename like 'mt_events_sequence\\_%' escape '\\' order by sequencename";
        cmd.Parameters.AddWithValue("s", schema);
        await using var reader = await cmd.ExecuteReaderAsync();

        var sequences = new System.Collections.Generic.List<string>();
        while (await reader.ReadAsync())
        {
            sequences.Add(reader.GetString(0));
        }

        sequences.ShouldBe(new[] { "mt_events_sequence_delta", "mt_events_sequence_gamma" });
    }

    [Fact]
    public async Task two_tenants_draw_independent_monotonic_sequences()
    {
        var schema = Schema + "_independent";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        // Register tenants first so the EnsureStorage CREATE statements include both
        // partitions on mt_events / mt_streams plus both sequences. The post-apply
        // dynamic-partition-add path is exercised separately by the existing
        // `marten_managed_tenant_id_partitioning` tests.
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha", "beta");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        // Tenant 1: append 5 events.
        var stream1 = Guid.NewGuid();
        await using (var session1 = store.LightweightSession("alpha"))
        {
            session1.Events.StartStream(stream1, new RandomEvent(), new BEvent(), new RandomEvent(), new BEvent(), new RandomEvent());
            await session1.SaveChangesAsync();
        }

        // Tenant 2: append 3 events.
        var stream2 = Guid.NewGuid();
        await using (var session2 = store.LightweightSession("beta"))
        {
            session2.Events.StartStream(stream2, new RandomEvent(), new BEvent(), new RandomEvent());
            await session2.SaveChangesAsync();
        }

        // The kickoff invariant: each tenant's seq_ids come from a private monotonic
        // sequence, NOT a single shared global counter. Pull every seq_id per tenant
        // and assert they're contiguous (1..N) from each tenant's perspective.
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var alphaSeqIds = new System.Collections.Generic.List<long>();
        await using (var c = conn.CreateCommand())
        {
            c.CommandText = $"select seq_id from {schema}.mt_events where tenant_id = 'alpha' order by seq_id";
            await using var rdr = await c.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) alphaSeqIds.Add(rdr.GetInt64(0));
        }

        var betaSeqIds = new System.Collections.Generic.List<long>();
        await using (var c = conn.CreateCommand())
        {
            c.CommandText = $"select seq_id from {schema}.mt_events where tenant_id = 'beta' order by seq_id";
            await using var rdr = await c.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) betaSeqIds.Add(rdr.GetInt64(0));
        }

        alphaSeqIds.Count.ShouldBe(5);
        betaSeqIds.Count.ShouldBe(3);

        // Independence check: each tenant's seq_ids start at 1 and are monotonic +1.
        // If the two tenants shared a global sequence, alpha would be 1..5 and beta
        // would be 6..8 — not 1..3.
        alphaSeqIds.ShouldBe(new long[] { 1, 2, 3, 4, 5 });
        betaSeqIds.ShouldBe(new long[] { 1, 2, 3 });

        // Direct sequence-state check too: both per-tenant sequences should advance
        // to the count of events appended for that tenant.
        var lastValues = await ReadSequenceCurrentValues(schema, "alpha", "beta");
        lastValues[0].ShouldBe(5L);
        lastValues[1].ShouldBe(3L);
    }

    [Fact]
    public async Task append_for_unregistered_tenant_raises_clear_error()
    {
        var schema = Schema + "_unregistered";
        await ResetSchemaAsync(schema);

        using var store = BuildStore(schema);
        await store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, "alpha");
        await store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));

        await using var session = store.LightweightSession("not_registered");
        session.Events.StartStream(Guid.NewGuid(), new RandomEvent());

        // Marten wraps PG exceptions in MartenCommandException; pull the inner.
        var wrapped = await Should.ThrowAsync<Marten.Exceptions.MartenCommandException>(async () =>
        {
            await session.SaveChangesAsync();
        });

        var pg = wrapped.InnerException.ShouldBeOfType<Npgsql.PostgresException>();
        // Function raises with SQLSTATE 'MT002' for unregistered-tenant append.
        pg.SqlState.ShouldBe("MT002");
        pg.Message.ShouldContain("not_registered");
        pg.Message.ShouldContain("AddMartenManagedTenantsAsync");
    }
}
