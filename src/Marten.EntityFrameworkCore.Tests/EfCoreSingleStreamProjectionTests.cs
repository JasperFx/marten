using System;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events;
using Marten.Testing.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public class OrderAggregate: EfCoreSingleStreamProjection<Order, Guid, TestDbContext>
{
    public override Order ApplyEvent(Order snapshot, Guid identity, IEvent @event,
        TestDbContext dbContext, IQuerySession session)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                // Also write an OrderSummary side effect through the DbContext
                dbContext.OrderSummaries.Add(new OrderSummary
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items,
                    Status = "Placed"
                });
                return new Order
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items
                };

            case OrderShipped:
                if (snapshot != null)
                {
                    snapshot.IsShipped = true;
                }
                return snapshot;

            case OrderCancelled:
                if (snapshot != null)
                {
                    snapshot.IsCancelled = true;
                }
                return snapshot;
        }

        return snapshot;
    }
}

public abstract class EfCoreSingleStreamProjectionTestsBase: IAsyncLifetime
{
    protected DocumentStore Store = null!;

    protected abstract ProjectionLifecycle Lifecycle { get; }

    public async Task InitializeAsync()
    {
        Store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"efcore_ss_{Lifecycle.ToString().ToLower()}";
            // Use the new extension method that sets up EF Core storage + Weasel migrations
            opts.Add(new OrderAggregate(), Lifecycle);
        });

        await Store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        Store?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// After appending events, ensure the projection has been applied.
    /// For Inline this is a no-op; for Async this waits for the daemon.
    /// </summary>
    protected virtual Task WaitForProjectionAsync() => Task.CompletedTask;

    private string SchemaName => $"efcore_ss_{Lifecycle.ToString().ToLower()}";

    protected async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var setSchema = conn.CreateCommand();
        setSchema.CommandText = $"SET search_path TO {SchemaName}";
        await setSchema.ExecuteNonQueryAsync();
        return conn;
    }

    [Fact]
    public async Task single_stream_projection_writes_aggregate_on_create()
    {
        var orderId = Guid.NewGuid();
        await using var session = Store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Carol", 200.00m, 5));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        // Verify aggregate was persisted via EF Core
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_name, total_amount, item_count FROM ef_orders WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Carol");
        reader.GetDecimal(1).ShouldBe(200.00m);
        reader.GetInt32(2).ShouldBe(5);
    }

    [Fact]
    public async Task single_stream_projection_evolves_aggregate_with_subsequent_events()
    {
        var orderId = Guid.NewGuid();
        await using var session = Store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Dave", 75.00m, 2),
            new OrderShipped(orderId));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        // Verify via EF Core
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_name, is_shipped FROM ef_orders WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Dave");
        reader.GetBoolean(1).ShouldBeTrue();
    }

    [Fact]
    public async Task single_stream_projection_handles_multiple_appends()
    {
        var orderId = Guid.NewGuid();

        await using var session = Store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Eve", 120.00m, 3));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        session.Events.Append(orderId, new OrderShipped(orderId));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        // Verify via EF Core
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_name, is_shipped FROM ef_orders WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Eve");
        reader.GetBoolean(1).ShouldBeTrue();
    }

    [Fact]
    public async Task single_stream_projection_writes_ef_core_side_effects()
    {
        var orderId = Guid.NewGuid();
        await using var session = Store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Frank", 300.00m, 10));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        // Verify the OrderSummary side-effect was also written via EF Core
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_name, status, total_amount FROM ef_order_summaries WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Frank");
        reader.GetString(1).ShouldBe("Placed");
        reader.GetDecimal(2).ShouldBe(300.00m);
    }
}

public class EfCoreSingleStreamProjectionInlineTests: EfCoreSingleStreamProjectionTestsBase
{
    protected override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Inline;
}

public class EfCoreSingleStreamProjectionAsyncTests: EfCoreSingleStreamProjectionTestsBase
{
    protected override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Async;

    protected override async Task WaitForProjectionAsync()
    {
        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await Store.WaitForNonStaleProjectionDataAsync(15.Seconds());
    }
}

public class EfCoreSingleStreamProjectionLiveTests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "efcore_ss_live";
            opts.Projections.Add(new OrderAggregate(), ProjectionLifecycle.Live);
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task live_aggregation_builds_aggregate_on_the_fly()
    {
        var orderId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Grace", 50.00m, 1),
            new OrderShipped(orderId));
        await session.SaveChangesAsync();

        var order = await session.Events.AggregateStreamAsync<Order>(orderId);
        order.ShouldNotBeNull();
        order.CustomerName.ShouldBe("Grace");
        order.IsShipped.ShouldBeTrue();
    }

    [Fact]
    public async Task live_aggregation_returns_null_for_unknown_stream()
    {
        await using var session = _store.LightweightSession();
        var order = await session.Events.AggregateStreamAsync<Order>(Guid.NewGuid());
        order.ShouldBeNull();
    }

    [Fact]
    public async Task live_aggregation_does_not_persist_aggregate()
    {
        var orderId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Heidi", 90.00m, 4));
        await session.SaveChangesAsync();

        // Live aggregation rebuilds on the fly
        var order = await session.Events.AggregateStreamAsync<Order>(orderId);
        order.ShouldNotBeNull();

        // But the aggregate is NOT stored in the document table
        var loaded = await session.LoadAsync<Order>(orderId);
        loaded.ShouldBeNull();
    }
}
