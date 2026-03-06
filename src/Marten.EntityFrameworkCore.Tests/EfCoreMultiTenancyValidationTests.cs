using System;
using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.EntityFrameworkCore.Tests;

public class EfCoreMultiTenancyValidationTests
{
    private string errorMessageFor(Action<StoreOptions> configure)
    {
        var ex = Should.Throw<InvalidProjectionException>(() =>
        {
            DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                configure(opts);
            });
        });

        return ex.Message;
    }

    private void shouldNotThrow(Action<StoreOptions> configure)
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            configure(opts);
        });
    }

    [Fact]
    public void single_stream_should_fail_when_conjoined_but_no_ITenanted()
    {
        errorMessageFor(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Add(new OrderAggregate(), ProjectionLifecycle.Inline);
        }).ShouldContain("must implement ITenanted");
    }

    [Fact]
    public void multi_stream_should_fail_when_conjoined_but_no_ITenanted()
    {
        errorMessageFor(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Add(new CustomerOrderHistoryProjection(), ProjectionLifecycle.Inline);
        }).ShouldContain("must implement ITenanted");
    }

    [Fact]
    public void single_stream_should_pass_when_conjoined_with_ITenanted()
    {
        shouldNotThrow(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Add(new TenantedOrderAggregate(), ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public void single_stream_should_pass_without_conjoined_tenancy()
    {
        shouldNotThrow(opts =>
        {
            opts.Add(new OrderAggregate(), ProjectionLifecycle.Inline);
        });
    }
}

// Tenanted single-stream projection for validation tests
public class TenantedOrderAggregate: EfCoreSingleStreamProjection<TenantedOrder, Guid, TenantedTestDbContext>
{
    public override TenantedOrder? ApplyEvent(TenantedOrder? snapshot, Guid identity, IEvent @event,
        TenantedTestDbContext dbContext, IQuerySession session)
    {
        switch (@event.Data)
        {
            case OrderPlaced placed:
                return new TenantedOrder
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
