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
        EfCoreOperations<TestDbContext> operations, CancellationToken token)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                // Write to EF Core
                operations.DbContext.OrderSummaries.Add(new OrderSummary
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items,
                    Status = "Placed"
                });
                // Also write to Marten
                operations.Marten.Store(new Order
                {
                    Id = placed.OrderId,
                    CustomerName = placed.CustomerName,
                    TotalAmount = placed.Amount,
                    ItemCount = placed.Items
                });
                break;

            case OrderShipped shipped:
                var summary = await operations.DbContext.OrderSummaries
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
    private NpgsqlConnection _adminConnection = null!;

    public async Task InitializeAsync()
    {
        // Create the EF table
        _adminConnection = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await _adminConnection.OpenAsync();

        await using var cmd = _adminConnection.CreateCommand();
        cmd.CommandText = @"
            DROP TABLE IF EXISTS ef_order_summaries;
            CREATE TABLE ef_order_summaries (
                ""Id"" uuid PRIMARY KEY,
                ""CustomerName"" text NOT NULL DEFAULT '',
                ""TotalAmount"" numeric NOT NULL DEFAULT 0,
                ""ItemCount"" integer NOT NULL DEFAULT 0,
                ""Status"" text NOT NULL DEFAULT 'Pending'
            );";
        await cmd.ExecuteNonQueryAsync();

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "efcore_tests";
            opts.Projections.Add(new OrderSummaryProjection(), ProjectionLifecycle.Inline);
        });
    }

    public async Task DisposeAsync()
    {
        _store?.Dispose();
        if (_adminConnection != null)
        {
            await _adminConnection.CloseAsync();
            await _adminConnection.DisposeAsync();
        }
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
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"CustomerName\", \"Status\" FROM ef_order_summaries WHERE \"Id\" = @id";
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
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"Status\" FROM ef_order_summaries WHERE \"Id\" = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        var status = (string?)await cmd.ExecuteScalarAsync();
        status.ShouldBe("Shipped");
    }
}
