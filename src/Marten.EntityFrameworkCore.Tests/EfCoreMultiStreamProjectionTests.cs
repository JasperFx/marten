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

public class EfCoreMultiStreamProjectionTests: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "efcore_ms_tests";
            opts.Events.StreamIdentity = JasperFx.Events.StreamIdentity.AsString;
            opts.Projections.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Inline);
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
    }

    public Task DisposeAsync()
    {
        _store?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task multi_stream_projection_aggregates_across_streams()
    {
        await using var session = _store.LightweightSession();

        var stream1 = Guid.NewGuid().ToString();
        var stream2 = Guid.NewGuid().ToString();

        session.Events.StartStream(stream1,
            new CustomerOrderPlaced(Guid.NewGuid(), "Eve", 100.00m));
        session.Events.StartStream(stream2,
            new CustomerOrderPlaced(Guid.NewGuid(), "Eve", 50.00m));
        await session.SaveChangesAsync();

        var history = await session.LoadAsync<CustomerOrderHistory>("Eve");
        history.ShouldNotBeNull();
        history.TotalOrders.ShouldBe(2);
        history.TotalSpent.ShouldBe(150.00m);
    }
}
