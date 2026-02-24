using System;
using System.Threading.Tasks;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Testing.Harness;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public class OrderAggregate: EfCoreSingleStreamProjection<Order, Guid, TestDbContext>
{
    // TODO -- the signature of
    public override Order ApplyEvent(Order snapshot, Guid identity, IEvent @event,
        TestDbContext dbContext, IQuerySession session)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
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

public class EfCoreSingleStreamProjectionTests: IAsyncLifetime
{
    private DocumentStore _store = null!;
    private NpgsqlConnection _adminConnection = null!;

    public async Task InitializeAsync()
    {
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
            opts.DatabaseSchemaName = "efcore_ss_tests";
            opts.Projections.Add(new OrderAggregate(), ProjectionLifecycle.Inline);
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
    public async Task single_stream_projection_writes_to_both_marten_and_efcore()
    {
        var orderId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Carol", 200.00m, 5));
        await session.SaveChangesAsync();

        // Verify Marten aggregate
        var order = await session.LoadAsync<Order>(orderId);
        order.ShouldNotBeNull();
        order.CustomerName.ShouldBe("Carol");
        order.ItemCount.ShouldBe(5);

        // Verify EF Core entity
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT \"CustomerName\", \"Status\" FROM ef_order_summaries WHERE \"Id\" = @id";
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetString(0).ShouldBe("Carol");
        reader.GetString(1).ShouldBe("Placed");
    }

    [Fact]
    public async Task single_stream_projection_evolves_aggregate()
    {
        var orderId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        session.Events.StartStream(orderId,
            new OrderPlaced(orderId, "Dave", 75.00m, 2),
            new OrderShipped(orderId));
        await session.SaveChangesAsync();

        var order = await session.LoadAsync<Order>(orderId);
        order.ShouldNotBeNull();
        order.IsShipped.ShouldBeTrue();
    }
}
