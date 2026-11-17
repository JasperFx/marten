using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public class Bug_6033_fetching_an_ef_backed_aggregate: IAsyncLifetime
{
    private const string SchemaName = "efcore_bug_6033";
    private DocumentStore _store = null!;

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = SchemaName;
            opts.Add(new OrderAggregate(), ProjectionLifecycle.Inline);
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return default;
    }

    private async Task<Guid> placeOrder(string customer)
    {
        var orderId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        session.Events.StartStream(orderId, new OrderPlaced(orderId, customer, 10m, 1));
        await session.SaveChangesAsync();
        return orderId;
    }

    private async Task<long> scalar(string sql, Guid? orderId = null)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (orderId.HasValue) cmd.Parameters.AddWithValue("id", orderId.Value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task fetch_latest_returns_the_inline_ef_aggregate()
    {
        var orderId = await placeOrder("Ann");

        await using var query = _store.LightweightSession();
        var order = await query.Events.FetchLatest<Order>(orderId);
        order.ShouldNotBeNull();
        order.CustomerName.ShouldBe("Ann");

        // and it did not go looking in a Marten document table for it
        (await scalar(
            $"select count(*) from information_schema.tables where table_schema = '{SchemaName}' and table_name = 'mt_doc_order'"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task fetch_for_writing_and_save_on_an_inline_ef_aggregate()
    {
        var orderId = await placeOrder("Bob");

        await using (var session = _store.LightweightSession())
        {
            var stream = await session.Events.FetchForWriting<Order>(orderId);
            stream.Aggregate.ShouldNotBeNull();
            stream.Aggregate.CustomerName.ShouldBe("Bob");

            // OrderAggregate.ApplyEvent adds an OrderSummary for OrderPlaced. Folding the stream again here
            // must not write that row a second time when the session saves.
            stream.AppendOne(new OrderShipped(orderId));
            await session.SaveChangesAsync();
        }

        await using var query = _store.LightweightSession();
        var order = await query.Events.FetchLatest<Order>(orderId);
        order.ShouldNotBeNull();
        order.IsShipped.ShouldBeTrue();

        (await scalar($"select count(*) from {SchemaName}.ef_order_summaries where id = @id", orderId)).ShouldBe(1);
        (await scalar($"select count(*) from {SchemaName}.ef_orders where id = @id and is_shipped", orderId)).ShouldBe(1);
    }

    [Fact]
    public async Task aggregating_live_in_a_session_writes_nothing_at_its_next_save()
    {
        var orderId = await placeOrder("Cat");

        await using var session = _store.LightweightSession();
        var order = await session.Events.AggregateStreamAsync<Order>(orderId);
        order.ShouldNotBeNull();

        session.Events.Append(orderId, new OrderShipped(orderId));
        await session.SaveChangesAsync();

        (await scalar($"select count(*) from {SchemaName}.ef_order_summaries where id = @id", orderId)).ShouldBe(1);
    }
}
