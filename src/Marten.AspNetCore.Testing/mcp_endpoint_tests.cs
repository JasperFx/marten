using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Alba;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

[Collection("integration")]
public class mcp_endpoint_tests: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public mcp_endpoint_tests(AppFixture fixture) : base(fixture)
    {
        theHost = fixture.Host;
    }

    private async Task<JsonDocument> SendMcpRequest(string method, object? @params = null)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = 1,
            method,
            @params
        };

        var result = await theHost.Scenario(s =>
        {
            s.Post.Json(request).ToUrl("/marten/mcp/");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var body = result.ReadAsText();
        return JsonDocument.Parse(body);
    }

    [Fact]
    public async Task can_initialize()
    {
        using var doc = await SendMcpRequest("initialize");

        var root = doc.RootElement;
        root.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
        root.GetProperty("id").GetInt32().ShouldBe(1);

        var result = root.GetProperty("result");
        result.GetProperty("protocolVersion").GetString().ShouldNotBeNullOrEmpty();

        var capabilities = result.GetProperty("capabilities");
        capabilities.TryGetProperty("tools", out _).ShouldBeTrue();

        var serverInfo = result.GetProperty("serverInfo");
        serverInfo.GetProperty("name").GetString().ShouldBe("marten");
    }

    [Fact]
    public async Task can_list_tools()
    {
        using var doc = await SendMcpRequest("tools/list");

        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().ShouldBeGreaterThanOrEqualTo(5);

        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();

        toolNames.ShouldContain("get_event_store_configuration");
        toolNames.ShouldContain("get_event_metadata_configuration");
        toolNames.ShouldContain("list_known_event_types");
        toolNames.ShouldContain("list_projections");
        toolNames.ShouldContain("get_daemon_settings");

        // Each tool should have a description and input schema
        foreach (var tool in tools.EnumerateArray())
        {
            tool.GetProperty("description").GetString().ShouldNotBeNullOrEmpty();
            tool.GetProperty("inputSchema").GetProperty("type").GetString().ShouldBe("object");
        }
    }

    [Fact]
    public async Task can_get_event_store_configuration()
    {
        using var doc = await SendMcpRequest("tools/call",
            new { name = "get_event_store_configuration" });

        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        content.GetArrayLength().ShouldBe(1);

        var text = content[0].GetProperty("text").GetString()!;
        using var config = JsonDocument.Parse(text);
        var root = config.RootElement;

        root.GetProperty("streamIdentity").GetString().ShouldBe("AsGuid");
        root.GetProperty("tenancyStyle").GetString().ShouldNotBeNullOrEmpty();
        root.GetProperty("appendMode").GetString().ShouldNotBeNullOrEmpty();
        root.GetProperty("eventNamingStyle").GetString().ShouldNotBeNullOrEmpty();
        root.GetProperty("databaseSchemaName").GetString().ShouldNotBeNullOrEmpty();

        // Boolean flags should be present
        root.TryGetProperty("enableUniqueIndexOnEventId", out _).ShouldBeTrue();
        root.TryGetProperty("useArchivedStreamPartitioning", out _).ShouldBeTrue();
        root.TryGetProperty("useOptimizedProjectionRebuilds", out _).ShouldBeTrue();
        root.TryGetProperty("useMandatoryStreamTypeDeclaration", out _).ShouldBeTrue();
        root.TryGetProperty("enableAdvancedAsyncTracking", out _).ShouldBeTrue();
        root.TryGetProperty("enableSideEffectsOnInlineProjections", out _).ShouldBeTrue();
        root.TryGetProperty("useMonitoredAdvisoryLock", out _).ShouldBeTrue();
        root.TryGetProperty("enableEventSkippingInProjectionsOrSubscriptions", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task can_get_event_metadata_configuration()
    {
        using var doc = await SendMcpRequest("tools/call",
            new { name = "get_event_metadata_configuration" });

        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        var text = content[0].GetProperty("text").GetString()!;
        using var config = JsonDocument.Parse(text);
        var root = config.RootElement;

        root.TryGetProperty("correlationIdEnabled", out _).ShouldBeTrue();
        root.TryGetProperty("causationIdEnabled", out _).ShouldBeTrue();
        root.TryGetProperty("headersEnabled", out _).ShouldBeTrue();
        root.TryGetProperty("userNameEnabled", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task can_list_known_event_types()
    {
        using var doc = await SendMcpRequest("tools/call",
            new { name = "list_known_event_types" });

        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        content.GetArrayLength().ShouldBe(1);
        content[0].GetProperty("type").GetString().ShouldBe("text");

        var text = content[0].GetProperty("text").GetString()!;
        using var eventTypes = JsonDocument.Parse(text);
        // Should at minimum have the Archived event type
        eventTypes.RootElement.GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task can_list_projections()
    {
        using var doc = await SendMcpRequest("tools/call",
            new { name = "list_projections" });

        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        var text = content[0].GetProperty("text").GetString()!;
        using var projections = JsonDocument.Parse(text);

        // IssueService registers a Snapshot<Order> inline projection
        projections.RootElement.GetArrayLength().ShouldBeGreaterThanOrEqualTo(1);

        var first = projections.RootElement[0];
        first.TryGetProperty("name", out _).ShouldBeTrue();
        first.TryGetProperty("implementationType", out _).ShouldBeTrue();
        first.TryGetProperty("type", out _).ShouldBeTrue();
        first.TryGetProperty("shards", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task can_get_daemon_settings()
    {
        using var doc = await SendMcpRequest("tools/call",
            new { name = "get_daemon_settings" });

        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        var text = content[0].GetProperty("text").GetString()!;
        using var config = JsonDocument.Parse(text);
        var root = config.RootElement;

        root.GetProperty("asyncMode").GetString().ShouldNotBeNullOrEmpty();
        root.GetProperty("slowPollingTime").GetDouble().ShouldBeGreaterThan(0);
        root.GetProperty("fastPollingTime").GetDouble().ShouldBeGreaterThan(0);
        root.GetProperty("healthCheckPollingTime").GetDouble().ShouldBeGreaterThan(0);
        root.GetProperty("staleSequenceThreshold").GetDouble().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task returns_error_for_unknown_method()
    {
        using var doc = await SendMcpRequest("nonexistent/method");

        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().ShouldBe(-32601);
        error.GetProperty("message").GetString().ShouldContain("Method not found");
    }

    [Fact]
    public async Task returns_error_for_unknown_tool()
    {
        using var doc = await SendMcpRequest("tools/call",
            new { name = "nonexistent_tool" });

        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().ShouldBe(-32602);
        error.GetProperty("message").GetString().ShouldContain("Unknown tool");
    }

    [Fact]
    public async Task returns_parse_error_for_invalid_json()
    {
        var result = await theHost.Scenario(s =>
        {
            s.Post.Text("this is not json").ToUrl("/marten/mcp/");
            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var body = result.ReadAsText();
        using var doc = JsonDocument.Parse(body);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetInt32().ShouldBe(-32700);
    }
}
