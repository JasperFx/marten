using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Alba;
using IssueService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

/// <summary>
/// Alba-based tests for <see cref="StreamEventState"/> and <see cref="StreamEvents"/> executing
/// against plain Minimal API endpoints (no Wolverine required).
/// </summary>
[Collection("integration")]
public class stream_event_result_types_tests: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public stream_event_result_types_tests(AppFixture fixture): base(fixture)
    {
        theHost = fixture.Host;
    }

    private async Task<Guid> anOrderStream(params object[] events)
    {
        var orderId = Guid.NewGuid();
        var store = theHost.Services.GetRequiredService<IDocumentStore>();
        await using var session = store.LightweightSession();
        session.Events.StartStream<Order>(orderId, events);
        await session.SaveChangesAsync();

        return orderId;
    }

    // ───────────────────────── StreamEventState ─────────────────────────

    [Fact]
    public async Task stream_event_state_returns_metadata_for_an_existing_stream()
    {
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m), new OrderShipped());

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/state");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var state = result.ReadAsJson<StreamStateResponse>();
        state.ShouldNotBeNull();
        state.Id.ShouldBe(orderId);
        state.Version.ShouldBe(2);
        state.IsArchived.ShouldBeFalse();
        state.Created.ShouldBeGreaterThan(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task stream_event_state_serializes_the_aggregate_type_as_a_name()
    {
        // StreamState.AggregateType is a System.Type, which System.Text.Json flatly refuses to
        // serialize. StreamStateResponse projects it to a simple name instead.
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m));

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/state");
            s.StatusCodeShouldBe(200);
        });

        var state = result.ReadAsJson<StreamStateResponse>();
        state.AggregateTypeName.ShouldBe(nameof(Order));
    }

    [Fact]
    public async Task stream_event_state_sets_content_length()
    {
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m));

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/state");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.ContentLength.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task stream_event_state_returns_404_for_a_missing_stream()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{Guid.NewGuid()}/state");
            s.StatusCodeShouldBe(404);
        });
    }

    // ───────────────────────── StreamEvents ─────────────────────────

    [Fact]
    public async Task stream_events_returns_the_raw_events_as_a_json_array()
    {
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m), new OrderShipped());

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/events");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var events = result.ReadAsJson<List<EventResponse>>();
        events.Count.ShouldBe(2);

        events[0].Version.ShouldBe(1);
        events[0].StreamId.ShouldBe(orderId);
        events[0].EventTypeName.ShouldNotBeNullOrEmpty();
        events[0].Timestamp.ShouldBeGreaterThan(DateTimeOffset.MinValue);

        events[1].Version.ShouldBe(2);
        events[1].EventTypeName.ShouldNotBe(events[0].EventTypeName);
    }

    [Fact]
    public async Task stream_events_returns_404_for_a_missing_stream_by_default()
    {
        await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{Guid.NewGuid()}/events");
            s.StatusCodeShouldBe(404);
        });
    }

    [Fact]
    public async Task stream_events_honors_a_version_cap_through_the_plan_constructor()
    {
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m), new OrderShipped());

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/events/upto/1");
            s.StatusCodeShouldBe(200);
        });

        var events = result.ReadAsJson<List<EventResponse>>();
        events.Count.ShouldBe(1);
        events[0].Version.ShouldBe(1);
    }

    [Fact]
    public async Task stream_events_can_opt_into_an_empty_array_instead_of_404()
    {
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m));

        // fromVersion past the end of the stream — expected when paging forward, so this
        // endpoint sets OnEmptyStatus to 200 rather than reporting the order as missing.
        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/events/from/99");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        result.ReadAsJson<List<EventResponse>>().ShouldBeEmpty();
    }

    [Fact]
    public async Task stream_events_carries_the_event_body_on_the_data_property()
    {
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m));

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/events");
            s.StatusCodeShouldBe(200);
        });

        var events = result.ReadAsJson<List<OrderPlacedEventResponse>>();
        events[0].Data.Description.ShouldBe("Widget");
        events[0].Data.Amount.ShouldBe(99.95m);
    }

    [Fact]
    public async Task stream_events_sets_content_length()
    {
        var orderId = await anOrderStream(new OrderPlaced("Widget", 99.95m));

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/minimal/order/{orderId}/events");
            s.StatusCodeShouldBe(200);
        });

        result.Context.Response.ContentLength.HasValue.ShouldBeTrue();
    }

    // ───────────────────────── OpenAPI metadata ─────────────────────────

    [Fact]
    public void stream_event_state_endpoint_advertises_produces_response_and_404()
    {
        var metadata = EndpointMetadataFor("GET", "/minimal/order/{id:guid}/state");

        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 200 && m.Type == typeof(StreamStateResponse));
        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 404);
    }

    [Fact]
    public void stream_events_endpoint_advertises_produces_array_and_404()
    {
        var metadata = EndpointMetadataFor("GET", "/minimal/order/{id:guid}/events");

        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 200 && m.Type == typeof(EventResponse[]));
        metadata.OfType<IProducesResponseTypeMetadata>()
            .ShouldContain(m => m.StatusCode == 404);
    }

    private EndpointMetadataCollection EndpointMetadataFor(string method, string pattern)
    {
        var endpoint = theHost.Services.GetServices<EndpointDataSource>()
            .SelectMany(x => x.Endpoints)
            .OfType<RouteEndpoint>()
            .FirstOrDefault(x =>
                x.RoutePattern.RawText == pattern &&
                x.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));

        endpoint.ShouldNotBeNull($"No endpoint found for {method} {pattern}");
        return endpoint.Metadata;
    }
}

/// <summary>
/// Strongly-typed view of the wire shape so the test can assert on the event body itself.
/// </summary>
public class OrderPlacedEventResponse
{
    public long Version { get; set; }
    public OrderPlaced Data { get; set; } = default!;
}
