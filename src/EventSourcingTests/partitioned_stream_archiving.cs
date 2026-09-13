using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Archiving;
using Marten.Storage;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests;

public class partitioned_stream_archiving: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task archiving_twice_preserves_the_stream_and_events(bool stringIdentity, bool conjoined)
    {
        configure(stringIdentity, conjoined);
        object id = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();
        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        await using var session = theStore.LightweightSession(tenant);
        startStream(session, id);
        await session.SaveChangesAsync();
        var eventIds = (await session.Events.QueryAllRawEvents().ToListAsync()).Select(x => x.Id).ToArray();

        if (conjoined)
        {
            await using var other = theStore.LightweightSession("two");
            startStream(other, id);
            await other.SaveChangesAsync();
        }

        archiveStream(session, id);
        await session.SaveChangesAsync();
        archiveStream(session, id);
        await session.SaveChangesAsync();

        var events = await session.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).ToListAsync();
        events.Select(x => x.Id).ShouldBe(eventIds, ignoreOrder: true);
        events.All(x => x.IsArchived).ShouldBeTrue();

        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        (await countRows(connection, "mt_streams", id, tenant, true)).ShouldBe(1);
        (await countRows(connection, "mt_streams", id, tenant, false)).ShouldBe(0);

        if (conjoined)
        {
            (await countRows(connection, "mt_streams", id, "two", false)).ShouldBe(1);
            (await countRows(connection, "mt_events", id, "two", false)).ShouldBe(2);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task concurrent_archives_preserve_one_copy(bool stringIdentity, bool conjoined)
    {
        configure(stringIdentity, conjoined);
        object id = stringIdentity ? Guid.NewGuid().ToString() : Guid.NewGuid();
        var tenant = conjoined ? "one" : StorageConstants.DefaultTenantId;
        await using (var session = theStore.LightweightSession(tenant))
        {
            startStream(session, id);
            await session.SaveChangesAsync();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(15.Seconds());
        var token = timeout.Token;
        await using var first = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await using var second = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await using var observer = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await first.OpenAsync(token);
        await second.OpenAsync(token);
        await observer.OpenAsync(token);
        await using var transaction = await first.BeginTransactionAsync(token);
        await using var firstCommand = archiveCommand(first, id, tenant, conjoined);
        await firstCommand.ExecuteNonQueryAsync(token);

        await using var secondCommand = archiveCommand(second, id, tenant, conjoined);
        var secondArchive = secondCommand.ExecuteNonQueryAsync(token);
        try
        {
            // Observe the database lock before committing, so these calls cannot run sequentially.
            await using var blocked = observer.CreateCommand("select :first = any(pg_blocking_pids(:second))")
                .With("first", first.ProcessID).With("second", second.ProcessID);
            while (!(bool)(await blocked.ExecuteScalarAsync(token)))
            {
                secondArchive.IsCompleted.ShouldBeFalse("the second archive must wait for the first transaction");
                await Task.Delay(25, token);
            }

            await transaction.CommitAsync(token);
            await secondArchive;
        }
        finally
        {
            await timeout.CancelAsync();
            try
            {
                await secondArchive;
            }
            catch (Exception) when (timeout.IsCancellationRequested)
            {
                // Observe cancellation if the lock assertion failed; the assertion still fails the test.
            }
        }

        (await countRows(observer, "mt_streams", id, tenant, true)).ShouldBe(1);
        (await countRows(observer, "mt_streams", id, tenant, false)).ShouldBe(0);
        (await countRows(observer, "mt_events", id, tenant, true)).ShouldBe(2);
        (await countRows(observer, "mt_events", id, tenant, false)).ShouldBe(0);
    }

    [Theory]
    [InlineData(EventAppendMode.Rich)]
    [InlineData(EventAppendMode.Quick)]
    public async Task async_projection_can_process_a_stream_archived_inline(EventAppendMode appendMode)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.AppendMode = appendMode;
            opts.Projections.Snapshot<ArchivedOrder>(SnapshotLifecycle.Inline);
            opts.Projections.Add(new ArchivedOrderViewProjection(), ProjectionLifecycle.Async);
        });

        var id = Guid.NewGuid();
        theSession.Events.StartStream<ArchivedOrder>(id, new Opened(id), new Finished(), new Archived("Complete"));
        await theSession.SaveChangesAsync();
        (await theSession.Events.FetchStreamStateAsync(id)).IsArchived.ShouldBeTrue();

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await daemon.WaitForNonStaleData(15.Seconds());

        await using var query = theStore.QuerySession();
        var view = await query.LoadAsync<ArchivedOrderView>(id);
        view.ShouldNotBeNull();
        view.IsFinished.ShouldBeTrue();
        var events = await query.Events.QueryAllRawEvents().Where(x => x.MaybeArchived()).ToListAsync();
        events.Count.ShouldBe(3);
        events.All(x => x.IsArchived).ShouldBeTrue();
    }

    [Fact]
    public async Task a_reused_stream_id_still_reports_an_archive_collision()
    {
        configure(false, false);
        var id = Guid.NewGuid();
        startStream(theSession, id);
        await theSession.SaveChangesAsync();
        archiveStream(theSession, id);
        await theSession.SaveChangesAsync();

        await using (var session = theStore.LightweightSession())
        {
            startStream(session, id);
            await session.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await connection.OpenAsync();
        await using var command = archiveCommand(connection, id, StorageConstants.DefaultTenantId, false);
        var exception = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        exception.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await countRows(connection, "mt_streams", id, StorageConstants.DefaultTenantId, false)).ShouldBe(1);
        (await countRows(connection, "mt_streams", id, StorageConstants.DefaultTenantId, true)).ShouldBe(1);
        (await countRows(connection, "mt_events", id, StorageConstants.DefaultTenantId, false)).ShouldBe(2);
        (await countRows(connection, "mt_events", id, StorageConstants.DefaultTenantId, true)).ShouldBe(2);
    }

    private void configure(bool stringIdentity, bool conjoined)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.StreamIdentity = stringIdentity ? StreamIdentity.AsString : StreamIdentity.AsGuid;
            opts.Events.TenancyStyle = conjoined ? TenancyStyle.Conjoined : TenancyStyle.Single;
        });
    }

    private static void startStream(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.StartStream(guid, new AEvent(), new BEvent());
        else session.Events.StartStream((string)id, new AEvent(), new BEvent());
    }

    private static void archiveStream(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.ArchiveStream(guid);
        else session.Events.ArchiveStream((string)id);
    }

    private NpgsqlCommand archiveCommand(NpgsqlConnection connection, object id, string tenant, bool conjoined)
    {
        var command = connection.CreateCommand($"select {SchemaName}.mt_archive_stream(:id{(conjoined ? ", :tenant" : "")})")
            .With("id", id);
        if (conjoined) command.With("tenant", tenant);
        return command;
    }

    private async Task<long> countRows(NpgsqlConnection connection, string table, object id, string tenant, bool archived)
    {
        var idColumn = table == "mt_events" ? "stream_id" : "id";
        await using var command = connection.CreateCommand($"select count(*) from {SchemaName}.{table} where {idColumn} = :id and tenant_id = :tenant and is_archived = :archived")
            .With("id", id).With("tenant", tenant).With("archived", archived);
        return (long)(await command.ExecuteScalarAsync());
    }

    public record Opened(Guid Id);
    public record Finished;

    public class ArchivedOrder
    {
        public Guid Id { get; set; }
        public bool IsFinished { get; set; }
        public void Apply(Opened e) => Id = e.Id;
        public void Apply(Finished e) => IsFinished = true;
        public void Apply(Archived e) { }
    }

    public class ArchivedOrderView
    {
        public Guid Id { get; set; }
        public bool IsFinished { get; set; }
    }

    public class ArchivedOrderViewProjection: SingleStreamProjection<ArchivedOrderView, Guid>
    {
        public ArchivedOrderViewProjection()
        {
            IncludeArchivedEvents = true;
            IncludeType<Opened>();
            IncludeType<Finished>();
        }

        public override ArchivedOrderView Evolve(ArchivedOrderView snapshot, Guid id, IEvent e)
        {
            if (e.Data is Opened) return new ArchivedOrderView { Id = id };
            if (snapshot != null && e.Data is Finished) snapshot.IsFinished = true;
            return snapshot;
        }
    }
}
