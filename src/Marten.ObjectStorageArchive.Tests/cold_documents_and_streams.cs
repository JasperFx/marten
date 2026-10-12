using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.ColdStorage;
using Marten.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.ObjectStorageArchive.Tests;

public class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
}

public record CartOpened;
public record ItemAdded(string Sku);

public class Cart
{
    public Guid Id { get; set; }
    public List<string> Items { get; set; } = [];

    public static Cart Create(CartOpened _) => new();
    public void Apply(ItemAdded e) => Items.Add(e.Sku);
}

public record TicketOpened(string Title);
public record CommentAdded(string Text);

public class Ticket
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public int Comments { get; set; }

    public static Ticket Create(TicketOpened e) => new() { Title = e.Title };
    public void Apply(CommentAdded _) => Comments++;
}

public class AsyncTicket
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public int Comments { get; set; }

    public static AsyncTicket Create(TicketOpened e) => new() { Title = e.Title };
    public void Apply(CommentAdded _) => Comments++;
}

[Collection("pg_duckdb")]
public class cold_documents_and_streams: IAsyncLifetime
{
    private DocumentStore _store = null!;
    private CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "cold_storage";
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            opts.UseColdStorage(new PgDuckDbParquetColdStorage($"/var/lib/postgresql/cold_storage_{Guid.NewGuid():N}_"));

            opts.Schema.For<Customer>().Duplicate(x => x.Region).AllowColdStorage();

            opts.Events.AddEventType<CartOpened>();
            opts.Events.AddEventType<ItemAdded>();

            opts.Projections.Snapshot<Ticket>(SnapshotLifecycle.Inline);
            opts.Schema.For<Ticket>().AllowColdStorage();

            opts.Projections.Snapshot<AsyncTicket>(SnapshotLifecycle.Async);
            opts.Schema.For<AsyncTicket>().AllowColdStorage();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync(Token);
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await _store.InitializeColdStorageAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task a_cold_document_is_only_read_when_asked_for_and_a_hot_write_shadows_it()
    {
        var north = new Customer { Id = Guid.NewGuid(), Name = "Old name", Region = "north" };
        var south = new Customer { Id = Guid.NewGuid(), Name = "Stays hot", Region = "south" };
        await using (var session = _store.LightweightSession())
        {
            session.Store(north, south);
            await session.SaveChangesAsync(Token);
        }

        (await _store.MoveDocumentsToColdStorageAsync<Customer>(x => x.Region == "north", token: Token)).ShouldBe(1);
        (await CountHotAsync("mt_doc_customer", north.Id)).ShouldBe(0);

        await using (var query = _store.QuerySession())
        {
            (await query.LoadAsync<Customer>(north.Id, Token)).ShouldBeNull();
            (await query.Query<Customer>().ToListAsync(Token)).Select(x => x.Id).ShouldBe([south.Id]);

            (await query.LoadMaybeColdAsync<Customer>(north.Id, Token))!.Name.ShouldBe("Old name");
            (await query.Query<Customer>().Where(x => x.MaybeCold()).CountAsync(Token)).ShouldBe(2);
        }

        await using (var session = _store.LightweightSession())
        {
            session.Store(new Customer { Id = north.Id, Name = "New name", Region = "north" });
            await session.SaveChangesAsync(Token);
        }

        await using (var query = _store.QuerySession())
        {
            (await query.LoadAsync<Customer>(north.Id, Token))!.Name.ShouldBe("New name");
            (await query.LoadMaybeColdAsync<Customer>(north.Id, Token))!.Name.ShouldBe("New name");

            var both = await query.Query<Customer>().Where(x => x.MaybeCold() && x.Id == north.Id).ToListAsync(Token);
            both.Single().Name.ShouldBe("New name");
        }

        // Moved again: two cold versions of one id, and only the latest is visible.
        (await _store.MoveDocumentsToColdStorageAsync<Customer>(x => x.Id == north.Id, token: Token)).ShouldBe(1);
        await using (var query = _store.QuerySession())
        {
            (await query.LoadAsync<Customer>(north.Id, Token)).ShouldBeNull();
            var cold = await query.Query<Customer>().Where(x => x.MaybeCold() && x.Id == north.Id).ToListAsync(Token);
            cold.Single().Name.ShouldBe("New name");
        }
    }

    [Fact]
    public async Task a_predicate_on_a_duplicated_field_or_on_the_json_is_evaluated_against_cold_documents()
    {
        await using (var session = _store.LightweightSession())
        {
            session.Store(
                new Customer { Id = Guid.NewGuid(), Name = "A", Region = "north" },
                new Customer { Id = Guid.NewGuid(), Name = "B", Region = "north" },
                new Customer { Id = Guid.NewGuid(), Name = "C", Region = "south" });
            await session.SaveChangesAsync(Token);
        }

        await _store.MoveDocumentsToColdStorageAsync<Customer>(x => x.Name != "", token: Token);

        await using var query = _store.QuerySession();

        var command = query.Query<Customer>().Where(x => x.MaybeCold() && x.Region == "north").ToCommand();
        command.CommandText.ShouldContain("d.region");

        (await query.Query<Customer>().Where(x => x.MaybeCold() && x.Region == "north").CountAsync(Token)).ShouldBe(2);
        (await query.Query<Customer>().Where(x => x.MaybeCold() && x.Name == "C").Select(x => x.Region).ToListAsync(Token))
            .ShouldBe(["south"]);
    }

    [Fact]
    public async Task a_live_aggregation_folds_the_moved_events_and_the_stream_stays_writable()
    {
        var cartId = Guid.NewGuid();
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<Cart>(cartId, new CartOpened(), new ItemAdded("a"), new ItemAdded("b"), new ItemAdded("c"));
            await session.SaveChangesAsync(Token);
        }

        (await _store.MoveStreamEventsToColdStorageAsync(cartId, Token)).ShouldBe(3);
        (await CountHotEventsAsync(cartId)).ShouldBe(1);

        await using (var session = _store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<Cart>(cartId, Token);
            stream.Aggregate!.Items.ShouldBe(["a", "b", "c"]);
            stream.CurrentVersion.ShouldBe(4);

            stream.AppendOne(new ItemAdded("d"));
            await session.SaveChangesAsync(Token);
        }

        await using var query = _store.QuerySession();
        var events = await query.Events.FetchStreamAsync(cartId, token: Token);
        events.Select(x => x.Version).ShouldBe([1L, 2L, 3L, 4L, 5L]);

        (await query.Events.AggregateStreamAsync<Cart>(cartId, token: Token))!.Items.ShouldBe(["a", "b", "c", "d"]);
        (await query.Events.AggregateStreamAsync<Cart>(cartId, version: 2, token: Token))!.Items.ShouldBe(["a"]);

        await using var session2 = _store.LightweightSession();
        (await session2.Events.FetchLatest<Cart>(cartId, Token))!.Items.ShouldBe(["a", "b", "c", "d"]);
    }

    [Fact]
    public async Task an_inline_projection_starts_from_the_cold_snapshot_and_stores_the_result_hot()
    {
        var ticketId = Guid.NewGuid();
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<Ticket>(ticketId, new TicketOpened("Printer"), new CommentAdded("1"));
            await session.SaveChangesAsync(Token);
        }

        (await _store.MoveDocumentsToColdStorageAsync<Ticket>(x => x.Id == ticketId, token: Token)).ShouldBe(1);

        await using (var session = _store.LightweightSession())
        {
            session.Events.Append(ticketId, new CommentAdded("2"));
            await session.SaveChangesAsync(Token);
        }

        (await CountHotAsync("mt_doc_ticket", ticketId)).ShouldBe(1);
        await using var query = _store.QuerySession();
        var ticket = await query.LoadAsync<Ticket>(ticketId, Token);
        ticket!.Title.ShouldBe("Printer");
        ticket.Comments.ShouldBe(2);
    }

    [Fact]
    public async Task an_async_projection_starts_from_the_cold_snapshot_and_stores_the_result_hot()
    {
        using var daemon = await _store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        var ticketId = Guid.NewGuid();
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<AsyncTicket>(ticketId, new TicketOpened("Printer"), new CommentAdded("1"));
            await session.SaveChangesAsync(Token);
        }

        await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
        (await _store.MoveDocumentsToColdStorageAsync<AsyncTicket>(x => x.Id == ticketId, token: Token)).ShouldBe(1);

        await using (var session = _store.LightweightSession())
        {
            session.Events.Append(ticketId, new CommentAdded("2"));
            await session.SaveChangesAsync(Token);
        }

        await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));
        await daemon.StopAllAsync();

        (await CountHotAsync("mt_doc_asyncticket", ticketId)).ShouldBe(1);
        await using var query = _store.QuerySession();
        var ticket = await query.LoadAsync<AsyncTicket>(ticketId, Token);
        ticket!.Title.ShouldBe("Printer");
        ticket.Comments.ShouldBe(2);
    }

    private Task<long> CountHotAsync(string table, Guid id) =>
        ScalarAsync($"select count(*) from cold_storage.{table} where id = @id", id);

    private Task<long> CountHotEventsAsync(Guid streamId) =>
        ScalarAsync("select count(*) from cold_storage.mt_events where stream_id = @id", streamId);

    private async Task<long> ScalarAsync(string sql, Guid id)
    {
        await using var conn = _store.Storage.Database.CreateConnection();
        await conn.OpenAsync(Token);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync(Token))!;
    }
}
