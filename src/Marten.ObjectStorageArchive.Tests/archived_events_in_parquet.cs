using JasperFx;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Archiving;
using Marten.Events.Archiving.ObjectStorage;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.ObjectStorageArchive.Tests;

public record OrderPlaced(string Number);
public record OrderShipped(string Carrier);
public record OrderClosed;

/// <summary>
///     Needs PostgreSQL with pg_duckdb (pgduckdb/pgduckdb:17-v1.1.1), see docker-compose.pgduckdb.yml.
/// </summary>
[CollectionDefinition("pg_duckdb", DisableParallelization = true)]
public class PgDuckDbCollection;

[Collection("pg_duckdb")]
public abstract class archived_events_in_parquet: IAsyncLifetime
{
    private const string ArchiveView = "mt_events_with_archive";

    private readonly bool _archivedStreamPartitioning;
    private DocumentStore _store = null!;

    protected archived_events_in_parquet(bool archivedStreamPartitioning)
    {
        _archivedStreamPartitioning = archivedStreamPartitioning;
    }

    public async ValueTask InitializeAsync()
    {
        var schema = _archivedStreamPartitioning ? "parquet_archive_partitioned" : "parquet_archive";

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.UseArchivedStreamPartitioning = _archivedStreamPartitioning;
            opts.Events.AddEventType<OrderPlaced>();
            opts.Events.AddEventType<OrderShipped>();
            opts.Events.AddEventType<OrderClosed>();

            // Files are written by the database server, inside its container. A fresh prefix per run
            // keeps the files of an earlier run out of this one.
            opts.UseObjectStorageForArchivedEvents(
                new PgDuckDbParquetArchive($"/var/lib/postgresql/{schema}_{Guid.NewGuid():N}_"));
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync(TestContext.Current.CancellationToken);
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task archived_events_are_read_back_from_parquet_after_leaving_mt_events()
    {
        var token = TestContext.Current.CancellationToken;
        var closed = Guid.NewGuid();
        var open = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(closed, new OrderPlaced("1"), new OrderShipped("PostNL"), new OrderClosed());
            session.Events.StartStream(open, new OrderPlaced("2"), new OrderShipped("DHL"));
            await session.SaveChangesAsync(token);

            session.Events.ArchiveStream(closed);
            await session.SaveChangesAsync(token);
        }

        (await _store.MoveArchivedEventsToObjectStorageAsync(token)).ShouldBe(3);
        (await CountInMtEventsAsync(closed)).ShouldBe(0);

        await using var query = _store.QuerySession();

        var archived = await query.Events.QueryAllRawEvents()
            .Where(x => x.MaybeArchived() && x.StreamId == closed)
            .OrderBy(x => x.Version)
            .ToListAsync(token);

        archived.Select(x => x.Version).ShouldBe([1L, 2L, 3L]);
        archived.ShouldAllBe(x => x.IsArchived);
        archived[1].Data.ShouldBe(new OrderShipped("PostNL"));

        (await query.Events.QueryAllRawEvents().Where(x => x.IsArchived).CountAsync(token)).ShouldBe(3);
        (await query.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).CountAsync(token)).ShouldBe(5);

        var live = await query.Events.QueryAllRawEvents().ToListAsync(token);
        live.ShouldAllBe(x => x.StreamId == open);
        live.Count.ShouldBe(2);
    }

    [Fact]
    public async Task archived_events_not_yet_moved_are_still_read_from_mt_events()
    {
        var token = TestContext.Current.CancellationToken;
        var moved = Guid.NewGuid();
        var notYetMoved = Guid.NewGuid();

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(moved, new OrderPlaced("1"), new OrderClosed());
            await session.SaveChangesAsync(token);

            session.Events.ArchiveStream(moved);
            await session.SaveChangesAsync(token);
        }

        await _store.MoveArchivedEventsToObjectStorageAsync(token);

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(notYetMoved, new OrderPlaced("2"), new OrderClosed());
            await session.SaveChangesAsync(token);

            session.Events.ArchiveStream(notYetMoved);
            await session.SaveChangesAsync(token);
        }

        await using var query = _store.QuerySession();
        var archived = await query.Events.QueryAllRawEvents().Where(x => x.IsArchived).ToListAsync(token);

        archived.Count(x => x.StreamId == moved).ShouldBe(2);
        archived.Count(x => x.StreamId == notYetMoved).ShouldBe(2);
    }

    [Fact]
    public async Task moving_with_nothing_archived_moves_nothing_and_archive_queries_still_work()
    {
        var token = TestContext.Current.CancellationToken;

        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream(Guid.NewGuid(), new OrderPlaced("1"));
            await session.SaveChangesAsync(token);
        }

        (await _store.MoveArchivedEventsToObjectStorageAsync(token)).ShouldBe(0);

        await using var query = _store.QuerySession();
        (await query.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).CountAsync(token)).ShouldBe(1);
    }

    [Fact]
    public async Task only_a_query_that_asks_for_archived_events_reads_the_archive()
    {
        await using var query = _store.QuerySession();

        query.Events.QueryAllRawEvents().ToCommand().CommandText
            .ShouldNotContain(ArchiveView);
        query.Events.QueryAllRawEvents().Where(x => !x.IsArchived).ToCommand().CommandText
            .ShouldNotContain(ArchiveView);

        query.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).ToCommand().CommandText
            .ShouldContain(ArchiveView);
        query.Events.QueryAllRawEvents().Where(x => x.IsArchived).ToCommand().CommandText
            .ShouldContain(ArchiveView);
    }

    private async Task<long> CountInMtEventsAsync(Guid streamId)
    {
        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select count(*) from {_store.Options.Events.DatabaseSchemaName}.mt_events where stream_id = @id";
        cmd.Parameters.AddWithValue("id", streamId);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}

public class archived_events_in_parquet_without_partitioning(): archived_events_in_parquet(false);

public class archived_events_in_parquet_with_archived_stream_partitioning(): archived_events_in_parquet(true);
