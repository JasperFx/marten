using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public class OrderSummaryProjection: EfCoreEventProjection<TestDbContext>
{
    protected override async Task ProjectAsync(IEvent @event,
        TestDbContext dbContext,
        IDocumentOperations operations, CancellationToken token)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                // Write to EF Core
                dbContext.OrderSummaries.Add(new OrderSummary
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items,
                    Status = "Placed"
                });

                // Also write to Marten if you want
                operations.Store(new Order
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items
                });
                break;

            case OrderShipped shipped:
                var summary = await dbContext.OrderSummaries
                    .FindAsync(new object[] { shipped.OrderId }, token);
                if (summary != null)
                {
                    summary.Status = "Shipped";
                }
                break;
        }
    }
}

public class EfCoreEventProjectionTests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "efcore_tests";
            opts.Projections.Add(new OrderSummaryProjection(), ProjectionLifecycle.Inline);
            // Register EF Core entity tables for Weasel migration
            opts.AddEntityTablesFromDbContext<TestDbContext>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task can_project_event_to_ef_core_and_marten()
    {
        var orderId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Alice", 99.99m, 3));
        await session.SaveChangesAsync();

        // Verify Marten document
        var order = await session.LoadAsync<Order>(orderId);
        order.ShouldNotBeNull();
        order.CustomerName.ShouldBe("Alice");
        order.TotalAmount.ShouldBe(99.99m);

        // Verify EF Core entity
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var setSchema = conn.CreateCommand();
        setSchema.CommandText = "SET search_path TO efcore_tests";
        await setSchema.ExecuteNonQueryAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT customer_name, status FROM ef_order_summaries WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Alice");
        reader.GetString(1).ShouldBe("Placed");
    }

    [Fact]
    public async Task can_project_multiple_events()
    {
        var orderId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Bob", 50.00m, 1),
            new OrderShipped(orderId));
        await session.SaveChangesAsync();

        // Verify EF Core entity was updated
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var setSchema = conn.CreateCommand();
        setSchema.CommandText = "SET search_path TO efcore_tests";
        await setSchema.ExecuteNonQueryAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM ef_order_summaries WHERE id = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        var status = (string?)await cmd.ExecuteScalarAsync();
        status.ShouldBe("Shipped");
    }
}
