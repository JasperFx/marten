using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Storage;
using Xunit;

namespace DaemonTests;

// #4685 PR 3 — BulkWriter (binary COPY) flush path for INSERT-only rebuild batches.
// Split into white-box batch-level facts (proving the COPY dispatch, the graceful
// per-row fallback for mixed batches, and the continuous-mode gate) and end-to-end
// daemon rebuilds (proving the rebuilt end state matches the per-row path, including
// conjoined multi-tenancy and the metadata columns).
public class rebuild_with_bulk_copy_inserts: OneOffConfigurationsContext
{
    private static readonly CancellationToken ct = CancellationToken.None;

    #region white-box batch facts

    private (ProjectionUpdateBatch batch, DocumentSessionBase runner) buildBatch(ShardExecutionMode mode)
    {
        var db = theStore.Tenancy.Default.Database;
        var runner = (DocumentSessionBase)theStore.LightweightSession(SessionOptions.ForDatabase(db));
        var batch = new ProjectionUpdateBatch(theStore.Options.Projections, runner, mode, ct);
        return (batch, runner);
    }

    private ProjectionDocumentSession sessionFor(ProjectionUpdateBatch batch, ShardExecutionMode mode,
        string tenantId = null)
    {
        var db = theStore.Tenancy.Default.Database;
        var options = tenantId.IsEmpty()
            ? SessionOptions.ForDatabase(db)
            : SessionOptions.ForDatabase(tenantId, db);
        options.Tracking = DocumentTracking.None;

        return new ProjectionDocumentSession(theStore, batch, options, mode);
    }

    [Fact]
    public async Task insert_only_rebuild_batch_flushes_through_copy_not_command_pages()
    {
        StoreOptions(opts => opts.Projections.RebuildWithBulkCopy = true);

        var (batch, runner) = buildBatch(ShardExecutionMode.Rebuild);
        batch.AcceptsBulkInserts.ShouldBeTrue();

        var docs = Enumerable.Range(0, 100)
            .Select(i => new BulkCopyDoc { Id = Guid.NewGuid(), Number = i, Name = $"doc-{i}" })
            .ToArray();

        await using (var projectionSession = sessionFor(batch, ShardExecutionMode.Rebuild))
        {
            projectionSession.Insert(docs);

            await batch.WaitForCompletion();

            // The proof that the BulkWriter path owns these documents: none of them were
            // compiled onto the per-row command pages...
            batch.BuildPages(runner)
                .SelectMany(x => x.Operations)
                .OfType<IDocumentStorageOperation>()
                .Any().ShouldBeFalse();

            await runner.ExecuteBatchAsync(batch, ct);
        }

        await batch.DisposeAsync();

        // ...and yet every row landed, written by the COPY flush inside the batch transaction.
        await using var query = theStore.QuerySession();
        var loaded = await query.Query<BulkCopyDoc>().ToListAsync();
        loaded.Count.ShouldBe(docs.Length);
        loaded.Select(x => x.Id).OrderBy(x => x).ShouldBe(docs.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task mixed_batch_drains_back_to_the_per_row_path_in_order()
    {
        StoreOptions(opts => opts.Projections.RebuildWithBulkCopy = true);

        var (batch, runner) = buildBatch(ShardExecutionMode.Rebuild);

        var inserted = new BulkCopyDoc { Id = Guid.NewGuid(), Number = 1, Name = "inserted" };
        var stored = new BulkCopyDoc { Id = Guid.NewGuid(), Number = 2, Name = "stored" };

        await using (var projectionSession = sessionFor(batch, ShardExecutionMode.Rebuild))
        {
            projectionSession.Insert(inserted);

            // A non-insert document operation invalidates the insert-only premise. The
            // buffered insert must drain back onto the command pages ahead of it.
            projectionSession.Store(stored);

            await batch.WaitForCompletion();

            var operations = batch.BuildPages(runner)
                .SelectMany(x => x.Operations)
                .OfType<IDocumentStorageOperation>()
                .ToArray();

            operations.Length.ShouldBe(2);
            operations[0].Document.ShouldBeSameAs(inserted);
            operations[1].Document.ShouldBeSameAs(stored);

            await runner.ExecuteBatchAsync(batch, ct);
        }

        await batch.DisposeAsync();

        await using var query = theStore.QuerySession();
        var loaded = await query.Query<BulkCopyDoc>().ToListAsync();
        loaded.Count.ShouldBe(2);
    }

    [Fact]
    public async Task inserts_arriving_after_demotion_stay_on_the_per_row_path()
    {
        StoreOptions(opts => opts.Projections.RebuildWithBulkCopy = true);

        var (batch, runner) = buildBatch(ShardExecutionMode.Rebuild);

        await using (var projectionSession = sessionFor(batch, ShardExecutionMode.Rebuild))
        {
            projectionSession.Store(new BulkCopyDoc { Id = Guid.NewGuid(), Number = 1, Name = "upsert-first" });
            projectionSession.Insert(new BulkCopyDoc { Id = Guid.NewGuid(), Number = 2, Name = "insert-after" });

            await batch.WaitForCompletion();

            batch.BuildPages(runner)
                .SelectMany(x => x.Operations)
                .OfType<IDocumentStorageOperation>()
                .Count().ShouldBe(2);

            await runner.ExecuteBatchAsync(batch, ct);
        }

        await batch.DisposeAsync();

        await using var query = theStore.QuerySession();
        (await query.Query<BulkCopyDoc>().CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task continuous_mode_batches_never_opt_in()
    {
        StoreOptions(opts => opts.Projections.RebuildWithBulkCopy = true);

        var (batch, runner) = buildBatch(ShardExecutionMode.Continuous);
        batch.AcceptsBulkInserts.ShouldBeFalse();

        await using (var projectionSession = sessionFor(batch, ShardExecutionMode.Continuous))
        {
            projectionSession.Insert(new BulkCopyDoc { Id = Guid.NewGuid(), Number = 1, Name = "continuous" });

            await batch.WaitForCompletion();

            // Continuous batches keep the per-row path even with the flag on
            batch.BuildPages(runner)
                .SelectMany(x => x.Operations)
                .OfType<IDocumentStorageOperation>()
                .Count().ShouldBe(1);

            await runner.ExecuteBatchAsync(batch, ct);
        }

        await batch.DisposeAsync();
    }

    [Fact]
    public async Task rebuild_mode_batches_without_the_flag_keep_the_per_row_path()
    {
        StoreOptions(opts => opts.Projections.RebuildWithBulkCopy.ShouldBeFalse());

        var (batch, runner) = buildBatch(ShardExecutionMode.Rebuild);
        batch.AcceptsBulkInserts.ShouldBeFalse();

        await using (var projectionSession = sessionFor(batch, ShardExecutionMode.Rebuild))
        {
            projectionSession.Insert(new BulkCopyDoc { Id = Guid.NewGuid(), Number = 1, Name = "default-off" });

            await batch.WaitForCompletion();

            batch.BuildPages(runner)
                .SelectMany(x => x.Operations)
                .OfType<IDocumentStorageOperation>()
                .Count().ShouldBe(1);

            await runner.ExecuteBatchAsync(batch, ct);
        }

        await batch.DisposeAsync();
    }

    [Fact]
    public async Task copy_flush_handles_multiple_tenants_in_one_batch()
    {
        StoreOptions(opts =>
        {
            opts.Projections.RebuildWithBulkCopy = true;
            opts.Schema.For<BulkCopyDoc>().MultiTenanted();
        });

        var (batch, runner) = buildBatch(ShardExecutionMode.Rebuild);

        var blueDoc = new BulkCopyDoc { Id = Guid.NewGuid(), Number = 1, Name = "blue-doc" };
        var greenDoc = new BulkCopyDoc { Id = Guid.NewGuid(), Number = 2, Name = "green-doc" };

        await using (var blue = sessionFor(batch, ShardExecutionMode.Rebuild, "blue"))
        await using (var green = sessionFor(batch, ShardExecutionMode.Rebuild, "green"))
        {
            blue.Insert(blueDoc);
            green.Insert(greenDoc);

            await batch.WaitForCompletion();
            await runner.ExecuteBatchAsync(batch, ct);
        }

        await batch.DisposeAsync();

        await using (var blueQuery = theStore.QuerySession("blue"))
        {
            var docs = await blueQuery.Query<BulkCopyDoc>().ToListAsync();
            docs.Single().Id.ShouldBe(blueDoc.Id);
        }

        await using (var greenQuery = theStore.QuerySession("green"))
        {
            var docs = await greenQuery.Query<BulkCopyDoc>().ToListAsync();
            docs.Single().Id.ShouldBe(greenDoc.Id);
        }
    }

    #endregion

    #region end to end daemon rebuilds

    [Fact]
    public async Task end_to_end_rebuild_matches_the_per_row_end_state()
    {
        StoreOptions(opts =>
        {
            opts.Projections.RebuildWithBulkCopy = true;
            opts.Projections.Add(new BulkCopyInsertProjection(), ProjectionLifecycle.Async);
        });

        var expected = await publishItemAddedEvents(theStore, 20, 10);

        // Continuous daemon processing writes through the ordinary per-row path
        // (continuous batches never opt into the COPY flush)
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());
            await daemon.StopAllAsync();
        }

        var afterContinuous = await snapshotDocs(theStore);
        afterContinuous.Count.ShouldBe(expected.Count);

        // The rebuild truncates and replays through the BulkWriter (binary COPY) flush
        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(BulkCopyInsertProjection.ProjectionName, ct);
        }

        var afterRebuild = await snapshotDocs(theStore);

        // Same documents, same content as both the source events and the per-row pass
        afterRebuild.Count.ShouldBe(expected.Count);
        foreach (var doc in afterRebuild)
        {
            var source = expected[doc.Id];
            doc.Name.ShouldBe(source.Name);
            doc.Number.ShouldBe(source.Number);
        }

        // Metadata parity: the COPY stream carries the same metadata columns the
        // per-row INSERT writes
        await assertMetadataColumnsArePopulated(theStore, expected.Count);
    }

    [Fact]
    public async Task end_to_end_rebuild_with_conjoined_multi_tenancy()
    {
        StoreOptions(opts =>
        {
            opts.Projections.RebuildWithBulkCopy = true;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Schema.For<BulkCopyDoc>().MultiTenanted();
            opts.Projections.Add(new BulkCopyInsertProjection(), ProjectionLifecycle.Async);
        });

        var blueExpected = await publishItemAddedEvents(theStore, 10, 5, "blue");
        var greenExpected = await publishItemAddedEvents(theStore, 7, 4, "green");

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.StartAllAsync();
            await theStore.WaitForNonStaleProjectionDataAsync(30.Seconds());
            await daemon.StopAllAsync();
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(BulkCopyInsertProjection.ProjectionName, ct);
        }

        await assertTenantDocs(theStore, "blue", blueExpected);
        await assertTenantDocs(theStore, "green", greenExpected);

        // And the tenant_id column itself is correct row by row
        var table = theStore.Options.Storage.MappingFor(typeof(BulkCopyDoc)).TableName.QualifiedName;
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var mismatches = (long)(await conn
            .CreateCommand(
                $"select count(*) from {table} where (data ->> 'Name' like 'blue%' and tenant_id != 'blue') or (data ->> 'Name' like 'green%' and tenant_id != 'green')")
            .ExecuteScalarAsync())!;
        mismatches.ShouldBe(0);
    }

    [Fact]
    public async Task end_to_end_rebuild_with_mixed_operations_still_correct()
    {
        // A projection that mixes Insert with Delete — the COPY path must stand down
        // (per-row fallback) and the rebuild must still produce the right end state
        StoreOptions(opts =>
        {
            opts.Projections.RebuildWithBulkCopy = true;
            opts.Projections.Add(new InsertAndDeleteProjection(), ProjectionLifecycle.Async);
        });

        var streamId = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamId,
                new ItemAdded(Guid.NewGuid(), "keep-1", 1),
                new ItemAdded(Guid.NewGuid(), "keep-2", 2));

            var removed = new ItemAdded(Guid.NewGuid(), "removed", 3);
            session.Events.Append(streamId, removed, new ItemRemoved(removed.Id));

            await session.SaveChangesAsync();
        }

        using (var daemon = await theStore.BuildProjectionDaemonAsync())
        {
            await daemon.RebuildProjectionAsync(InsertAndDeleteProjection.ProjectionName, ct);
        }

        await using var query = theStore.QuerySession();
        var docs = await query.Query<BulkCopyDoc>().ToListAsync();
        docs.Count.ShouldBe(2);
        docs.Select(x => x.Name).OrderBy(x => x).ShouldBe(new[] { "keep-1", "keep-2" });
    }

    private static async Task<Dictionary<Guid, ItemAdded>> publishItemAddedEvents(IDocumentStore store,
        int streams, int eventsPerStream, string tenantId = null)
    {
        var expected = new Dictionary<Guid, ItemAdded>();

        await using var session = tenantId.IsEmpty()
            ? store.LightweightSession()
            : store.LightweightSession(tenantId);

        var prefix = tenantId.IsEmpty() ? "item" : tenantId;

        for (var i = 0; i < streams; i++)
        {
            var events = Enumerable.Range(0, eventsPerStream)
                .Select(j => new ItemAdded(Guid.NewGuid(), $"{prefix}-{i}-{j}", (i * eventsPerStream) + j))
                .ToArray();

            foreach (var e in events)
            {
                expected[e.Id] = e;
            }

            session.Events.StartStream(Guid.NewGuid(), events.Cast<object>().ToArray());
        }

        await session.SaveChangesAsync();

        return expected;
    }

    private static async Task<IReadOnlyList<BulkCopyDoc>> snapshotDocs(IDocumentStore store)
    {
        await using var query = store.QuerySession();
        return await query.Query<BulkCopyDoc>().ToListAsync();
    }

    private static async Task assertTenantDocs(IDocumentStore store, string tenantId,
        Dictionary<Guid, ItemAdded> expected)
    {
        await using var query = store.QuerySession(tenantId);
        var docs = await query.Query<BulkCopyDoc>().ToListAsync();

        docs.Count.ShouldBe(expected.Count);
        foreach (var doc in docs)
        {
            var source = expected[doc.Id];
            doc.Name.ShouldBe(source.Name);
            doc.Number.ShouldBe(source.Number);
        }
    }

    private async Task assertMetadataColumnsArePopulated(IDocumentStore store, int expectedCount)
    {
        var table = ((DocumentStore)store).Options.Storage.MappingFor(typeof(BulkCopyDoc)).TableName.QualifiedName;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        var populated = (long)(await conn
            .CreateCommand(
                $"select count(*) from {table} where mt_version is not null and mt_last_modified is not null and mt_dotnet_type is not null")
            .ExecuteScalarAsync())!;

        populated.ShouldBe(expectedCount);
    }

    #endregion
}

public record ItemAdded(Guid Id, string Name, int Number);

public record ItemRemoved(Guid Id);

public class BulkCopyDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public int Number { get; set; }
}

// Event-to-row transform that only ever *inserts* — the canonical shape that benefits
// from the #4685 BulkWriter rebuild flush (one new document per event, never re-stored,
// so the insert-only premise holds across event pages even before the Phase 4 deferred
// flush lands for aggregations)
public partial class BulkCopyInsertProjection: EventProjection
{
    public const string ProjectionName = "BulkCopyInserts";

    public BulkCopyInsertProjection()
    {
        Name = ProjectionName;
    }

    public void Project(ItemAdded e, IDocumentOperations ops)
    {
        ops.Insert(new BulkCopyDoc { Id = e.Id, Name = e.Name, Number = e.Number });
    }
}

// Deliberately mixes inserts with deletes so rebuild batches are *not* insert-only and
// the COPY dispatch has to fall back to the ordinary per-row path
public partial class InsertAndDeleteProjection: EventProjection
{
    public const string ProjectionName = "InsertAndDeletes";

    public InsertAndDeleteProjection()
    {
        Name = ProjectionName;
    }

    public void Project(ItemAdded e, IDocumentOperations ops)
    {
        ops.Insert(new BulkCopyDoc { Id = e.Id, Name = e.Name, Number = e.Number });
    }

    public void Project(ItemRemoved e, IDocumentOperations ops)
    {
        ops.Delete<BulkCopyDoc>(e.Id);
    }
}
