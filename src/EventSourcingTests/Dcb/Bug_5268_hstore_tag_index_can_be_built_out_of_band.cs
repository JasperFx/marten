#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Dcb;

/// <summary>
/// #5268. Turning <see cref="DcbStorageMode.HStore" /> on for an existing store adds two things to
/// <c>mt_events</c>: a nullable <c>tags hstore</c> column, which is a metadata-only add, and a GIN index
/// over it, which is not. Marten emits a plain <c>CREATE INDEX</c>, holding ACCESS EXCLUSIVE for the
/// build — a write outage on a large event table rather than a migration. And under
/// <c>UseTenantPartitionedEvents</c> a <c>CONCURRENTLY</c> flag would not help anyway, because
/// PostgreSQL refuses <c>CREATE INDEX CONCURRENTLY</c> on a partitioned parent outright.
/// <para>
/// The escape hatch is <c>Events.IgnoreIndex</c>: the index drops out of the schema diff, so an operator
/// can build it out of band — <c>CREATE INDEX ... ON ONLY</c> the parent, <c>CONCURRENTLY</c> per
/// partition, then <c>ALTER INDEX ... ATTACH PARTITION</c> — without a maintenance window. These tests
/// pin that it genuinely works, because it is the difference between "documented workaround" and
/// "there is no non-blocking path".
/// </para>
/// </summary>
[Collection("OneOffs")]
public class Bug_5268_hstore_tag_index_can_be_built_out_of_band: OneOffConfigurationsContext
{
    private DocumentStore StoreFor(bool hstore, bool ignoreTagIndex, bool concurrentTagIndex = false,
        bool archivedPartitioning = false)
    {
        return StoreOptions(opts =>
        {
            // #5308: CREATE INDEX CONCURRENTLY cannot finish until every transaction that was open when
            // it started has finished, and that wait is cluster-wide -- transactions in completely
            // unrelated schemas count. A full single-process EventSourcingTests run keeps sessions open
            // across hundreds of schemas, so whichever method here happens to start while enough of them
            // are in flight waits behind them and blows Npgsql's 30s default. The symptom is a command
            // timeout rather than an assertion, on a different method each run, while the class passes
            // 8 of 8 in isolation -- which reads as a regression in whatever change you are validating.
            //
            // CI never tripped this, because it shards the suite per area and no job concentrates that
            // much concurrent load on one instance. But a test whose success depends on how much
            // unrelated work happens to be in flight is fragile regardless of whether CI currently
            // trips it, and validating a dependency bump by running everything at once and comparing
            // failure counts is a reasonable thing to want to do. This removes the dependency instead of
            // leaving it to be rediscovered.
            //
            // A longer timeout, not [Trait("Isolated")]: the contention is other transactions on the
            // same DATABASE, so moving this class to its own process does not remove it.
            opts.CommandTimeout = 300;

            opts.Events.AddEventType<StudentEnrolled>();
            opts.Events.UseArchivedStreamPartitioning = archivedPartitioning;

            if (hstore)
            {
                opts.Events.DcbStorageMode = DcbStorageMode.HStore;
                opts.Events.RegisterTagType<StudentId>("student");
                opts.Events.BuildHStoreTagIndexConcurrently = concurrentTagIndex;
            }

            if (ignoreTagIndex)
            {
                opts.Events.IgnoreIndex(EventGraph.HStoreTagIndexName);
            }
        }, cleanAll: false);
    }

    private async Task<bool> IndexExistsAsync(string indexName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from pg_indexes where schemaname = @schema and indexname = @name";
        cmd.Parameters.AddWithValue("schema", SchemaName);
        cmd.Parameters.AddWithValue("name", indexName);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private async Task<bool> ColumnExistsAsync(string column)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          select count(*) from information_schema.columns
                          where table_schema = @schema and table_name = 'mt_events' and column_name = @column
                          """;
        cmd.Parameters.AddWithValue("schema", SchemaName);
        cmd.Parameters.AddWithValue("column", column);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    /// <summary>
    /// Drop the whole schema and seed a store with the event tables but WITHOUT hstore mode, so the
    /// following migration is the real "turn hstore on for an existing store" case rather than a
    /// first-time create.
    /// </summary>
    private async Task SeedNonHStoreStoreAsync()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(SchemaName);
        }

        var store = StoreFor(hstore: false, ignoreTagIndex: false);

        await using var session = store.LightweightSession();
        session.Events.StartStream(Guid.NewGuid(), new StudentEnrolled("Alice", "Math"));
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task turning_hstore_on_adds_the_index_by_default()
    {
        // The baseline the escape hatch is measured against: without opting out, the migration adds it.
        await SeedNonHStoreStoreAsync();

        var store = StoreFor(hstore: true, ignoreTagIndex: false);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await ColumnExistsAsync("tags")).ShouldBeTrue();
        (await IndexExistsAsync(EventGraph.HStoreTagIndexName)).ShouldBeTrue();
    }

    [Fact]
    public async Task the_tag_index_can_be_left_to_the_operator()
    {
        await SeedNonHStoreStoreAsync();

        var store = StoreFor(hstore: true, ignoreTagIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // The column still arrives -- it is a nullable add, metadata only, and DCB cannot work without it.
        (await ColumnExistsAsync("tags")).ShouldBeTrue();

        // The expensive half did not.
        (await IndexExistsAsync(EventGraph.HStoreTagIndexName)).ShouldBeFalse();
    }

    [Fact]
    public async Task an_operator_built_index_is_left_alone_by_a_later_migration()
    {
        // The other half of the workaround, and the one that would make it useless if it failed: having
        // built the index out of band, a subsequent schema application must not decide it is drift and
        // try to recreate or drop it.
        await SeedNonHStoreStoreAsync();

        var store = StoreFor(hstore: true, ignoreTagIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            // What the operator would run out of band. CONCURRENTLY is the point of doing it by hand;
            // it is omitted here only because a test has no need to avoid the lock.
            cmd.CommandText =
                $"create index {EventGraph.HStoreTagIndexName} on {SchemaName}.mt_events using gin (tags)";
            await cmd.ExecuteNonQueryAsync();
        }

        (await IndexExistsAsync(EventGraph.HStoreTagIndexName)).ShouldBeTrue();

        await Should.NotThrowAsync(() => store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

        // Still there, and the store still reports itself as matching its configuration.
        (await IndexExistsAsync(EventGraph.HStoreTagIndexName)).ShouldBeTrue();
        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    /// <summary>
    /// Reads <c>indisvalid</c> rather than merely whether the index exists. A CONCURRENTLY build that
    /// fails leaves the index in place and INVALID, which PostgreSQL will not use — "it is there" is the
    /// assertion that would let a broken build pass.
    /// </summary>
    private async Task<bool?> IndexIsValidAsync(string indexName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          select i.indisvalid from pg_index i
                          join pg_class c on c.oid = i.indexrelid
                          join pg_namespace n on n.oid = c.relnamespace
                          where n.nspname = @schema and c.relname = @name
                          """;
        cmd.Parameters.AddWithValue("schema", SchemaName);
        cmd.Parameters.AddWithValue("name", indexName);

        return (bool?)await cmd.ExecuteScalarAsync();
    }

    [Fact]
    public async Task the_tag_index_can_be_built_without_blocking_writes()
    {
        // The other way out of the maintenance window, and the one that does not need an operator:
        // Marten builds the index itself with CREATE INDEX CONCURRENTLY. Nothing here can observe the
        // absence of the lock directly, so what is pinned is that the concurrent build actually
        // completed -- an index that exists but is invalid is one PostgreSQL ignores.
        await SeedNonHStoreStoreAsync();

        var store = StoreFor(hstore: true, ignoreTagIndex: false, concurrentTagIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await ColumnExistsAsync("tags")).ShouldBeTrue();
        (await IndexIsValidAsync(EventGraph.HStoreTagIndexName)).ShouldBe(true);
    }

    [Fact]
    public async Task a_concurrently_built_tag_index_is_not_drift()
    {
        // CONCURRENTLY is not part of what pg_get_indexdef reports back, so the canonical form Weasel
        // compares against has to ignore it. If it did not, every apply would rebuild the index -- which
        // is the outage the flag exists to avoid, now happening on every startup instead of once.
        await SeedNonHStoreStoreAsync();

        var store = StoreFor(hstore: true, ignoreTagIndex: false, concurrentTagIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
        await Should.NotThrowAsync(() => store.Storage.ApplyAllConfiguredChangesToDatabaseAsync());

        (await IndexIsValidAsync(EventGraph.HStoreTagIndexName)).ShouldBe(true);
    }

    [Fact]
    public async Task a_store_created_from_scratch_can_use_the_concurrent_index_too()
    {
        // Not just the migration path: the flag also has to survive a first-time create, where the index
        // is written as part of the table's own creation script rather than as a delta.
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(SchemaName);
        }

        var store = StoreFor(hstore: true, ignoreTagIndex: false, concurrentTagIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await IndexIsValidAsync(EventGraph.HStoreTagIndexName)).ShouldBe(true);
    }

    /// <summary>
    /// The third shape <c>mt_events</c> comes in, and the one #5291 left untested.
    /// </summary>
    /// <remarks>
    /// <c>UseArchivedStreamPartitioning</c> partitions the table by <c>is_archived</c> with STATICALLY
    /// declared partitions, where <c>UseTenantPartitionedEvents</c> partitions by tenant with partitions
    /// owned by a manager. Both take Weasel's per-partition concurrent-index sequence, so the archived
    /// shape rides on the same code — but it reaches it down a different path, and it was the difference
    /// between declared and manager-owned partitions that broke the tenant case on Weasel 9.26.0
    /// (weasel#520). Assuming the other one is fine because the code is shared is exactly the reasoning
    /// that missed it the first time.
    /// </remarks>
    [Fact]
    public async Task the_concurrent_index_works_under_archived_stream_partitioning()
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(SchemaName);
        }

        var store = StoreFor(hstore: true, ignoreTagIndex: false, concurrentTagIndex: true,
            archivedPartitioning: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        // The archived partition plus the default one. A missing child leaves the parent invalid forever.
        (await AttachedChildIndexCountAsync(EventGraph.HStoreTagIndexName)).ShouldBe(2);
        (await IndexIsValidAsync(EventGraph.HStoreTagIndexName)).ShouldBe(true);

        await Should.NotThrowAsync(() => store.Storage.Database.AssertDatabaseMatchesConfigurationAsync());
    }

    private async Task<int> AttachedChildIndexCountAsync(string indexName)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          select count(*) from pg_inherits h
                          join pg_class parent on parent.oid = h.inhparent
                          join pg_namespace n on n.oid = parent.relnamespace
                          where n.nspname = @schema and parent.relname = @name
                          """;
        cmd.Parameters.AddWithValue("schema", SchemaName);
        cmd.Parameters.AddWithValue("name", indexName);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task tag_queries_still_work_once_the_index_is_built_out_of_band()
    {
        // Guard against a hollow victory: suppressing the index must not suppress the tagging itself.
        await SeedNonHStoreStoreAsync();

        var store = StoreFor(hstore: true, ignoreTagIndex: true);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var studentId = new StudentId(Guid.NewGuid());

        await using (var session = store.LightweightSession())
        {
            var evt = session.Events.BuildEvent(new StudentEnrolled("Bob", "Math"));
            evt.WithTag(studentId);
            session.Events.StartStream(Guid.NewGuid(), evt);
            await session.SaveChangesAsync();
        }

        await using var query = store.LightweightSession();
        var found = await query.Events.QueryByTagsAsync(
            new JasperFx.Events.Tags.EventTagQuery().Or<StudentId>(studentId));

        found.Count.ShouldBe(1);
    }
}
