using System;
using System.Threading.Tasks;
using Alba;
using IssueService.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

[Collection("integration")]
public class write_latest_guid_streaming_tests: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public write_latest_guid_streaming_tests(AppFixture fixture): base(fixture)
    {
        theHost = fixture.Host;
    }

    [Fact]
    public async Task stream_latest_aggregate_by_guid_hit()
    {
        var orderId = Guid.NewGuid();

        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Order>(orderId, new OrderPlaced("Widget", 99.95m));
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/order/{orderId}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Order>();
        read.ShouldNotBeNull();
        read.Description.ShouldBe("Widget");
        read.Amount.ShouldBe(99.95m);
        read.Shipped.ShouldBeFalse();
    }

    [Fact]
    public async Task stream_latest_aggregate_by_guid_miss()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/order/{Guid.NewGuid()}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task stream_latest_aggregate_by_guid_with_multiple_events()
    {
        var orderId = Guid.NewGuid();

        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<Order>(orderId,
                new OrderPlaced("Gadget", 149.99m),
                new OrderShipped());
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/order/{orderId}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Order>();
        read.ShouldNotBeNull();
        read.Description.ShouldBe("Gadget");
        read.Amount.ShouldBe(149.99m);
        read.Shipped.ShouldBeTrue();
    }
}

[Collection("string_stream_integration")]
public class write_latest_string_streaming_tests: IAsyncLifetime
{
    private readonly IAlbaHost theHost;
    private readonly IDocumentStore Store;

    public write_latest_string_streaming_tests(StringStreamAppFixture fixture)
    {
        theHost = fixture.Host;
        Store = theHost.Services.GetRequiredService<IDocumentStore>();
    }

    public async Task InitializeAsync()
    {
        await Store.Advanced.ResetAllData();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task stream_latest_aggregate_by_string_hit()
    {
        var orderId = "order-" + Guid.NewGuid().ToString("N");

        await using (var session = Store.LightweightSession())
        {
            session.Events.StartStream<NamedOrder>(orderId, new OrderPlaced("Widget", 99.95m));
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/named-order/{orderId}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<NamedOrder>();
        read.ShouldNotBeNull();
        read.Description.ShouldBe("Widget");
        read.Amount.ShouldBe(99.95m);
        read.Shipped.ShouldBeFalse();
    }

    [Fact]
    public async Task stream_latest_aggregate_by_string_miss()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/named-order/nonexistent-{Guid.NewGuid():N}");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task stream_latest_aggregate_by_string_with_multiple_events()
    {
        var orderId = "order-" + Guid.NewGuid().ToString("N");

        await using (var session = Store.LightweightSession())
        {
            session.Events.StartStream<NamedOrder>(orderId,
                new OrderPlaced("Gadget", 149.99m),
                new OrderShipped());
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/named-order/{orderId}");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<NamedOrder>();
        read.ShouldNotBeNull();
        read.Description.ShouldBe("Gadget");
        read.Amount.ShouldBe(149.99m);
        read.Shipped.ShouldBeTrue();
    }
}
