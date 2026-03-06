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

// Multi-stream events
public record CustomerOrderPlaced(Guid OrderId, string CustomerName, decimal Amount);
public record CustomerOrderCompleted(Guid OrderId, string CustomerName);

// Multi-stream aggregate keyed by customer name
public class CustomerOrderHistory
{
    public string Id { get; set; } = string.Empty;
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
}

public class CustomerOrderHistoryProjection
    : EfCoreMultiStreamProjection<CustomerOrderHistory, string, TestDbContext>
{
    public CustomerOrderHistoryProjection()
    {
        Identity<CustomerOrderPlaced>(e => e.CustomerName);
        Identity<CustomerOrderCompleted>(e => e.CustomerName);
    }

    public override CustomerOrderHistory? ApplyEvent(CustomerOrderHistory? snapshot,
        string identity, IEvent @event, TestDbContext dbContext)
    {
        snapshot ??= new CustomerOrderHistory { Id = identity };

        switch (@event.Data)
        {
            case CustomerOrderPlaced placed:
                snapshot.TotalOrders++;
                snapshot.TotalSpent += placed.Amount;
                break;
        }

        return snapshot;
    }
}

public abstract class EfCoreMultiStreamProjectionTestsBase: IAsyncLifetime
{
    protected DocumentStore Store = null!;

    protected abstract ProjectionLifecycle Lifecycle { get; }

    public async Task InitializeAsync()
    {
        Store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = $"efcore_ms_{Lifecycle.ToString().ToLower()}";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            // Use the new extension method that sets up EF Core storage + Weasel migrations
            opts.Add(new CustomerOrderHistoryProjection(), Lifecycle);
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

    private string SchemaName => $"efcore_ms_{Lifecycle.ToString().ToLower()}";

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
    public async Task multi_stream_projection_aggregates_across_streams()
    {
        await using var session = Store.LightweightSession();

        var stream1 = Guid.NewGuid().ToString();
        var stream2 = Guid.NewGuid().ToString();

        session.Events.StartStream(stream1,
            new CustomerOrderPlaced(Guid.NewGuid(), "Eve", 100.00m));
        session.Events.StartStream(stream2,
            new CustomerOrderPlaced(Guid.NewGuid(), "Eve", 50.00m));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        // Verify via EF Core table
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT total_orders, total_spent FROM ef_customer_order_histories WHERE id = @id";
        cmd.Parameters.AddWithValue("id", "Eve");
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(2);
        reader.GetDecimal(1).ShouldBe(150.00m);
    }

    [Fact]
    public async Task multi_stream_projection_creates_separate_aggregates_per_identity()
    {
        await using var session = Store.LightweightSession();

        session.Events.StartStream(Guid.NewGuid().ToString(),
            new CustomerOrderPlaced(Guid.NewGuid(), "Alice", 80.00m));
        session.Events.StartStream(Guid.NewGuid().ToString(),
            new CustomerOrderPlaced(Guid.NewGuid(), "Bob", 120.00m));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        await using var conn = await OpenConnectionAsync();

        // Check Alice
        await using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "SELECT total_orders, total_spent FROM ef_customer_order_histories WHERE id = @id";
        cmd1.Parameters.AddWithValue("id", "Alice");
        await using var reader1 = await cmd1.ExecuteReaderAsync();
        (await reader1.ReadAsync()).ShouldBeTrue();
        reader1.GetInt32(0).ShouldBe(1);
        reader1.GetDecimal(1).ShouldBe(80.00m);
        await reader1.CloseAsync();

        // Check Bob
        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT total_orders, total_spent FROM ef_customer_order_histories WHERE id = @id";
        cmd2.Parameters.AddWithValue("id", "Bob");
        await using var reader2 = await cmd2.ExecuteReaderAsync();
        (await reader2.ReadAsync()).ShouldBeTrue();
        reader2.GetInt32(0).ShouldBe(1);
        reader2.GetDecimal(1).ShouldBe(120.00m);
    }

    [Fact]
    public async Task multi_stream_projection_handles_subsequent_appends()
    {
        await using var session = Store.LightweightSession();

        var stream1 = Guid.NewGuid().ToString();
        session.Events.StartStream(stream1,
            new CustomerOrderPlaced(Guid.NewGuid(), "Charlie", 60.00m));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        var stream2 = Guid.NewGuid().ToString();
        session.Events.StartStream(stream2,
            new CustomerOrderPlaced(Guid.NewGuid(), "Charlie", 40.00m));
        await session.SaveChangesAsync();

        await WaitForProjectionAsync();

        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT total_orders, total_spent FROM ef_customer_order_histories WHERE id = @id";
        cmd.Parameters.AddWithValue("id", "Charlie");
        await using var reader = await cmd.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        reader.GetInt32(0).ShouldBe(2);
        reader.GetDecimal(1).ShouldBe(100.00m);
    }
}

public class EfCoreMultiStreamProjectionInlineTests: EfCoreMultiStreamProjectionTestsBase
{
    protected override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Inline;
}

public class EfCoreMultiStreamProjectionAsyncTests: EfCoreMultiStreamProjectionTestsBase
{
    protected override ProjectionLifecycle Lifecycle => ProjectionLifecycle.Async;

    protected override async Task WaitForProjectionAsync()
    {
        using var daemon = await Store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();
        await Store.WaitForNonStaleProjectionDataAsync(15.Seconds());
    }
}

public class EfCoreMultiStreamProjectionLiveTests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "efcore_ms_live";
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Projections.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Live);
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task live_multi_stream_projection_does_not_persist_aggregate()
    {
        await using var session = _store.LightweightSession();

        var stream1 = Guid.NewGuid().ToString();
        session.Events.StartStream(stream1,
            new CustomerOrderPlaced(Guid.NewGuid(), "Dana", 70.00m));
        await session.SaveChangesAsync();

        // Live multi-stream projections are not persisted
        var history = await session.LoadAsync<CustomerOrderHistory>("Dana");
        history.ShouldBeNull();
    }

    [Fact]
    public async Task live_multi_stream_projection_can_store_events_without_error()
    {
        // Verify the store can be configured and events appended with Live lifecycle
        await using var session = _store.LightweightSession();

        var streamKey = Guid.NewGuid().ToString();
        session.Events.StartStream(streamKey,
            new CustomerOrderPlaced(Guid.NewGuid(), "Eve", 100.00m));
        await session.SaveChangesAsync();

        // Events are stored successfully even with a Live multi-stream projection registered
        var events = await session.Events.FetchStreamAsync(streamKey);
        events.ShouldNotBeEmpty();
    }
}
