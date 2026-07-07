using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Events.TenantPartitioning;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using TenantPartitionedEventsTests.Fixtures;
using Weasel.Core;
using Xunit;

namespace TenantPartitionedEventsTests.Migration;

/// <summary>
/// marten#4682 end-to-end: migrate a conjoined event store into a per-tenant-partitioned one via
/// <see cref="ConjoinedToPartitionedMigration"/>, with <c>BulkInsertEventStreamAsync</c> in
/// <see cref="BulkEventSequenceMode.PreserveSourceSequence"/> mode (marten#4879) as the engine.
/// One source + one target store pair is shared by the whole class; per-fact isolation comes from
/// fresh tenant ids plus the migration's <see cref="ConjoinedToPartitionedMigration.TenantIds"/> filter,
/// so facts never see each other's tenants.
/// </summary>
public class conjoined_to_partitioned_migration: IAsyncLifetime
{
    public record Started(Guid Id);
    public record Progressed(double Amount);

    private readonly string _sourceSchema = $"cjp_src_p{Environment.ProcessId}";
    private readonly string _targetSchema = $"cjp_tgt_p{Environment.ProcessId}";

    private DocumentStore _source = null!;
    private DocumentStore _target = null!;

    public async Task InitializeAsync()
    {
        _source = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _sourceSchema;
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AddEventType<Started>();
            opts.Events.AddEventType<Progressed>();
            opts.Projections.DaemonLockId = 74682001;
        });

        _target = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _targetSchema;
            opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
            opts.Events.AddEventType<Started>();
            opts.Events.AddEventType<Progressed>();
            opts.Projections.DaemonLockId = 74682002;
        });

        await _source.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await _target.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _source?.Dispose();
        _target?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Seed the SOURCE with interleaved appends across the given tenants — round-robin so each tenant's
    /// slice of the shared global sequence is gappy, exactly like a real conjoined store. Returns one
    /// stream id per tenant.
    /// </summary>
    private async Task<Dictionary<string, Guid>> seedInterleavedAsync(string[] tenants, int rounds)
    {
        var streams = tenants.ToDictionary(t => t, _ => Guid.NewGuid());

        foreach (var tenant in tenants)
        {
            await using var session = _source.LightweightSession(tenant);
            session.Events.StartStream(streams[tenant], new Started(streams[tenant]));
            await session.SaveChangesAsync();
        }

        for (var round = 0; round < rounds; round++)
        {
            foreach (var tenant in tenants)
            {
                await using var session = _source.LightweightSession(tenant);
                session.Events.Append(streams[tenant], new Progressed(round + 1));
                await session.SaveChangesAsync();
            }
        }

        return streams;
    }

    private async Task<List<long>> seqIdsAsync(string schema, string tenant)
    {
        var seqs = new List<long>();
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"select seq_id from {schema}.mt_events where tenant_id = @t order by seq_id", conn);
        cmd.Parameters.AddWithValue("t", tenant);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            seqs.Add(reader.GetInt64(0));
        }

        return seqs;
    }

    private async Task<T?> scalarAsync<T>(string sql, params (string, object)[] parameters)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var raw = await cmd.ExecuteScalarAsync();
        return raw is T typed ? typed : default;
    }

    [Fact]
    public async Task migrates_a_conjoined_store_into_per_tenant_partitions_end_to_end()
    {
        var tenants = new[]
        {
            PartitionedFixtureBase.NewTenant(), PartitionedFixtureBase.NewTenant(),
            PartitionedFixtureBase.NewTenant()
        };
        var streams = await seedInterleavedAsync(tenants, rounds: 3);

        // Archive one tenant's stream: the archived flag must survive the migration on both the
        // stream row and its events.
        var archivedTenant = tenants[0];
        await using (var session = _source.LightweightSession(archivedTenant))
        {
            session.Events.ArchiveStream(streams[archivedTenant]);
            await session.SaveChangesAsync();
        }

        var migration = new ConjoinedToPartitionedMigration(_source, _target)
        {
            TenantIds = tenants, BatchSize = 2
        };

        // Phase 1 — the dry-run inventory matches the seeded reality and moves nothing.
        var plan = await migration.BuildPlanAsync();
        plan.Tenants.Count.ShouldBe(3);
        plan.TotalEvents.ShouldBe(12); // 3 tenants x (1 Started + 3 Progressed)
        foreach (var item in plan.Tenants)
        {
            item.EventCount.ShouldBe(4);
            item.StreamCount.ShouldBe(1);
            item.MaxSequence.ShouldBe((await seqIdsAsync(_sourceSchema, item.TenantId)).Max());
            item.AlreadyCompleted.ShouldBeFalse();
        }

        // Tenant-scoped: the target schema is shared with the class's other facts.
        foreach (var tenant in tenants)
        {
            (await seqIdsAsync(_targetSchema, tenant)).ShouldBeEmpty();
        }

        // Phase 2 — execute.
        var result = await migration.ExecuteAsync();
        result.MigratedTenants.Count.ShouldBe(3);
        result.SkippedTenants.ShouldBeEmpty();
        result.EventsCopied.ShouldBe(12);

        foreach (var tenant in tenants)
        {
            var sourceSeqs = await seqIdsAsync(_sourceSchema, tenant);

            // The data policy: seq_ids are EXACTLY the source's — gappy, never renumbered.
            (await seqIdsAsync(_targetSchema, tenant)).ShouldBe(sourceSeqs);

            // Per-tenant high water seeded at the tenant's max.
            (await scalarAsync<long?>(
                    $"select last_seq_id from {_targetSchema}.mt_event_progression where name = @n",
                    ("n", $"HighWaterMark:{tenant}")))
                .ShouldBe(sourceSeqs.Max());

            // Migration log records completion.
            (await scalarAsync<bool>(
                    $"select completed is not null from {_targetSchema}.{ConjoinedToPartitionedMigration.LogTableName} where tenant_id = @t",
                    ("t", tenant)))
                .ShouldBeTrue();
        }

        // Archived flag survived on the stream row and its events.
        (await scalarAsync<bool>(
                $"select is_archived from {_targetSchema}.mt_streams where tenant_id = @t",
                ("t", archivedTenant)))
            .ShouldBeTrue();
        (await scalarAsync<long>(
                $"select count(*) from {_targetSchema}.mt_events where tenant_id = @t and is_archived",
                ("t", archivedTenant)))
            .ShouldBe(4);

        // Store-global high water advanced to the migrated maximum (legacy readers).
        var globalMax = plan.Tenants.Max(x => x.MaxSequence);
        (await scalarAsync<long?>(
                $"select last_seq_id from {_targetSchema}.mt_event_progression where name = @n",
                ("n", "HighWaterMark")))
            .ShouldBe(globalMax);

        // The acceptance test that matters most (marten#4682): appending fresh events to every tenant
        // works on the FIRST try — no PK collision, no sequence collision, lands above the history.
        foreach (var tenant in tenants)
        {
            var maxBefore = (await seqIdsAsync(_targetSchema, tenant)).Max();
            await using var session = _target.LightweightSession(tenant);
            if (tenant == archivedTenant)
            {
                // The migrated stream is archived — and the target correctly refuses appends to it
                // (the archived flag really came across) — so this tenant continues on a NEW stream.
                session.Events.StartStream(Guid.NewGuid(), new Started(Guid.NewGuid()));
            }
            else
            {
                session.Events.Append(streams[tenant], new Progressed(99));
            }

            await session.SaveChangesAsync();

            var after = await seqIdsAsync(_targetSchema, tenant);
            after.Count.ShouldBe(5);
            after.Max().ShouldBeGreaterThan(maxBefore);
        }

        // Reading the migrated stream back through the TARGET store yields the same events in order.
        await using (var query = _target.QuerySession(tenants[1]))
        {
            var fetched = await query.Events.FetchStreamAsync(streams[tenants[1]]);
            fetched.Count.ShouldBe(5); // 4 migrated + 1 live append
            fetched[0].Data.ShouldBeOfType<Started>().Id.ShouldBe(streams[tenants[1]]);
            fetched.Take(4).Select(x => x.Version).ShouldBe(new long[] { 1, 2, 3, 4 });
        }

        // Resume — a second run skips every completed tenant and moves nothing.
        var secondRun = await new ConjoinedToPartitionedMigration(_source, _target)
        {
            TenantIds = tenants
        }.ExecuteAsync();

        secondRun.MigratedTenants.ShouldBeEmpty();
        secondRun.SkippedTenants.Count.ShouldBe(3);
        foreach (var tenant in tenants)
        {
            (await seqIdsAsync(_targetSchema, tenant)).Count.ShouldBe(5);
        }
    }

    [Fact]
    public async Task tenant_subset_migrates_only_the_requested_tenants()
    {
        var wanted = PartitionedFixtureBase.NewTenant();
        var unwanted = PartitionedFixtureBase.NewTenant();
        await seedInterleavedAsync(new[] { wanted, unwanted }, rounds: 1);

        var result = await new ConjoinedToPartitionedMigration(_source, _target)
        {
            TenantIds = new[] { wanted }
        }.ExecuteAsync();

        result.MigratedTenants.ShouldBe(new[] { wanted });
        (await seqIdsAsync(_targetSchema, wanted)).Count.ShouldBe(2);
        (await seqIdsAsync(_targetSchema, unwanted)).ShouldBeEmpty();
    }

    [Fact]
    public void source_must_be_conjoined_and_target_must_be_partitioned()
    {
        using var plainSource = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _sourceSchema + "_plain";
        });

        Should.Throw<InvalidOperationException>(() => new ConjoinedToPartitionedMigration(plainSource, _target))
            .Message.ShouldContain("Conjoined");

        Should.Throw<InvalidOperationException>(() => new ConjoinedToPartitionedMigration(_source, _source))
            .Message.ShouldContain("UseTenantPartitionedEvents");
    }

    [Fact]
    public async Task refuses_to_run_when_target_is_the_same_database_and_schema()
    {
        using var sameSchemaTarget = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = _sourceSchema; // same schema, same database as the source
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.UseTenantPartitionedEvents = true;
            opts.Events.AppendMode = EventAppendMode.QuickWithServerTimestamps;
            opts.Policies.AllDocumentsAreMultiTenanted();
        });

        var migration = new ConjoinedToPartitionedMigration(_source, sameSchemaTarget);
        (await Should.ThrowAsync<InvalidOperationException>(() => migration.ExecuteAsync()))
            .Message.ShouldContain("same database");
    }
}
