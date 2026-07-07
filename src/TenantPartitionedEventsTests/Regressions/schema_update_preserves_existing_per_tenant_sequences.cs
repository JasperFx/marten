#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace TenantPartitionedEventsTests.Regressions;

/// <summary>
/// A schema update that only needs to ADD a missing per-tenant event sequence must never touch the
/// sequences that already exist. The generic <c>SchemaObjectDelta.WriteUpdate</c> writes updates as
/// drop-then-create; routed through <c>PerTenantEventSequences</c> that emitted
/// <c>DROP SEQUENCE IF EXISTS mt_events_sequence_{suffix}</c> for EVERY registered tenant whenever any
/// ONE tenant's sequence was missing — resetting live tenants' event sequences to 1 (seq_id reuse =
/// silent event-store corruption). Observed on the ZOD-1714 canary while 500 tenants were being
/// provisioned: each shard's "All Configured Changes" saw the newest tenants' sequences as missing and
/// re-emitted drop+create for all of them. The 42P07 partition-name failure in the same batch rolled it
/// back before damage was done; with idempotent partition DDL (weasel#326) that accidental guard is
/// gone, so the delta itself must be additive-only.
/// </summary>
public class schema_update_preserves_existing_per_tenant_sequences : IAsyncLifetime
{
    private const string TenantA = "tenant_a";
    private const string TenantB = "tenant_b";

    private string _schema = null!;
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _schema = $"tp_seqpreserve_{Guid.NewGuid():N}".Substring(0, 32);

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            try { await conn.DropSchemaAsync(_schema); } catch { }
        }

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _schema;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();

            opts.Events.AddEventType<SeqPreserveEvent>();
        });

        await _store.Storage.Database.EnsureStorageExistsAsync(typeof(IEvent));
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task adding_a_missing_sequence_does_not_reset_existing_ones()
    {
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, TenantA);
        await _store.Advanced.AddMartenManagedTenantsAsync(CancellationToken.None, TenantB);

        // Advance tenant A's event sequence by appending real events.
        await using (var session = _store.LightweightSession(TenantA))
        {
            session.Events.StartStream(Guid.NewGuid(), new SeqPreserveEvent(1), new SeqPreserveEvent(2));
            await session.SaveChangesAsync();
        }

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var valueBefore = (long)(await conn
            .CreateCommand($"select last_value from \"{_schema}\".\"mt_events_sequence_{TenantA}\"")
            .ExecuteScalarAsync())!;
        valueBefore.ShouldBeGreaterThanOrEqualTo(2);

        // Simulate the canary scenario: a tenant is registered on the shared partition list but its
        // sequence is missing in this database (mid-provisioning / a racing applier's stale snapshot),
        // so the next schema apply computes an Update delta for the per-tenant sequences.
        await conn.CreateCommand($"drop sequence \"{_schema}\".\"mt_events_sequence_{TenantB}\"")
            .ExecuteNonQueryAsync();

        await _store.Storage.Database.ApplyAllConfiguredChangesToDatabaseAsync();

        // The missing sequence is (re)created...
        var tenantBExists = await conn
            .CreateCommand(
                "select count(*) from information_schema.sequences " +
                $"where sequence_schema = '{_schema}' and sequence_name = 'mt_events_sequence_{TenantB}'")
            .ExecuteScalarAsync();
        tenantBExists.ShouldBe(1L);

        // ...and tenant A's sequence kept its value: the update was additive-only, no drop+recreate.
        var valueAfter = (long)(await conn
            .CreateCommand($"select last_value from \"{_schema}\".\"mt_events_sequence_{TenantA}\"")
            .ExecuteScalarAsync())!;
        valueAfter.ShouldBe(valueBefore);

        // And the next append continues from where the sequence left off instead of colliding at 1.
        await using (var session = _store.LightweightSession(TenantA))
        {
            session.Events.StartStream(Guid.NewGuid(), new SeqPreserveEvent(3));
            await session.SaveChangesAsync();
        }

        var valueAfterAppend = (long)(await conn
            .CreateCommand($"select last_value from \"{_schema}\".\"mt_events_sequence_{TenantA}\"")
            .ExecuteScalarAsync())!;
        valueAfterAppend.ShouldBeGreaterThan(valueBefore);
    }
}

public record SeqPreserveEvent(int Number);
