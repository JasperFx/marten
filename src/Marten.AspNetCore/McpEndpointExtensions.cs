using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.AspNetCore;

public static class McpEndpointExtensions
{
    private const string McpProtocolVersion = "2025-03-26";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Maps MCP (Model Context Protocol) endpoints that expose read-only event store configuration
    /// as MCP tools. Returns a <see cref="RouteGroupBuilder"/> so callers can apply authorization
    /// policies, rate limiting, or other endpoint metadata.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder</param>
    /// <param name="pattern">The route prefix for MCP endpoints. Defaults to "/marten/mcp".</param>
    /// <returns>A <see cref="RouteGroupBuilder"/> for further configuration (e.g. RequireAuthorization)</returns>
    public static RouteGroupBuilder MapMartenMcp(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/marten/mcp")
    {
        var group = endpoints.MapGroup(pattern);

        group.MapPost("/", HandleMcpRequest);

        return group;
    }

    private static async Task HandleMcpRequest(HttpContext context)
    {
        JsonRpcRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                context.Request.Body, JsonOptions, context.RequestAborted).ConfigureAwait(false);
        }
        catch
        {
            await WriteJsonRpcError(context, null, -32700, "Parse error").ConfigureAwait(false);
            return;
        }

        if (request == null)
        {
            await WriteJsonRpcError(context, null, -32600, "Invalid Request").ConfigureAwait(false);
            return;
        }

        switch (request.Method)
        {
            case "initialize":
                await HandleInitialize(context, request).ConfigureAwait(false);
                break;
            case "tools/list":
                await HandleToolsList(context, request).ConfigureAwait(false);
                break;
            case "tools/call":
                await HandleToolsCall(context, request).ConfigureAwait(false);
                break;
            default:
                await WriteJsonRpcError(context, request.Id, -32601, $"Method not found: {request.Method}").ConfigureAwait(false);
                break;
        }
    }

    private static Task HandleInitialize(HttpContext context, JsonRpcRequest request)
    {
        var result = new
        {
            protocolVersion = McpProtocolVersion,
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "marten",
                version = "1.0.0"
            }
        };

        return WriteJsonRpcResult(context, request.Id, result);
    }

    private static Task HandleToolsList(HttpContext context, JsonRpcRequest request)
    {
        var tools = new[]
        {
            new McpToolDefinition
            {
                Name = "get_event_store_configuration",
                Description =
                    "Returns the current Marten event store configuration including stream identity, tenancy style, append mode, schema name, and all feature flags.",
                InputSchema = new McpToolInputSchema
                {
                    Type = "object", Properties = new Dictionary<string, object>()
                }
            },
            new McpToolDefinition
            {
                Name = "get_event_metadata_configuration",
                Description =
                    "Returns which optional event metadata fields are enabled (correlation ID, causation ID, headers, user name).",
                InputSchema = new McpToolInputSchema
                {
                    Type = "object", Properties = new Dictionary<string, object>()
                }
            },
            new McpToolDefinition
            {
                Name = "list_known_event_types",
                Description =
                    "Lists all event types registered with the Marten event store, including their type name aliases and .NET type names.",
                InputSchema = new McpToolInputSchema
                {
                    Type = "object", Properties = new Dictionary<string, object>()
                }
            },
            new McpToolDefinition
            {
                Name = "list_projections",
                Description =
                    "Lists all projections and subscriptions configured in the Marten event store, including their lifecycle and shard information.",
                InputSchema = new McpToolInputSchema
                {
                    Type = "object", Properties = new Dictionary<string, object>()
                }
            },
            new McpToolDefinition
            {
                Name = "get_daemon_settings",
                Description =
                    "Returns the async projection daemon configuration settings.",
                InputSchema = new McpToolInputSchema
                {
                    Type = "object", Properties = new Dictionary<string, object>()
                }
            }
        };

        return WriteJsonRpcResult(context, request.Id, new { tools });
    }

    private static Task HandleToolsCall(HttpContext context, JsonRpcRequest request)
    {
        var store = context.RequestServices.GetRequiredService<IDocumentStore>();
        var options = store.Options.Events;

        string? toolName = null;
        if (request.Params is JsonElement paramsElement &&
            paramsElement.TryGetProperty("name", out var nameElement))
        {
            toolName = nameElement.GetString();
        }

        return toolName switch
        {
            "get_event_store_configuration" => HandleGetEventStoreConfiguration(context, request, options),
            "get_event_metadata_configuration" => HandleGetEventMetadataConfiguration(context, request, options),
            "list_known_event_types" => HandleListKnownEventTypes(context, request, options),
            "list_projections" => HandleListProjections(context, request, options),
            "get_daemon_settings" => HandleGetDaemonSettings(context, request, options),
            _ => WriteJsonRpcError(context, request.Id, -32602, $"Unknown tool: {toolName}")
        };
    }

    private static Task HandleGetEventStoreConfiguration(
        HttpContext context, JsonRpcRequest request, IReadOnlyEventStoreOptions options)
    {
        var config = new Dictionary<string, object>
        {
            ["streamIdentity"] = options.StreamIdentity.ToString(),
            ["tenancyStyle"] = options.TenancyStyle.ToString(),
            ["appendMode"] = options.AppendMode.ToString(),
            ["eventNamingStyle"] = options.EventNamingStyle.ToString(),
            ["databaseSchemaName"] = options.DatabaseSchemaName,
            ["enableUniqueIndexOnEventId"] = options.EnableUniqueIndexOnEventId,
            ["useArchivedStreamPartitioning"] = options.UseArchivedStreamPartitioning,
            ["useOptimizedProjectionRebuilds"] = options.UseOptimizedProjectionRebuilds,
            ["useMandatoryStreamTypeDeclaration"] = options.UseMandatoryStreamTypeDeclaration,
            ["enableAdvancedAsyncTracking"] = options.EnableAdvancedAsyncTracking,
            ["enableSideEffectsOnInlineProjections"] = options.EnableSideEffectsOnInlineProjections,
            ["useMonitoredAdvisoryLock"] = options.UseMonitoredAdvisoryLock,
            ["enableEventSkippingInProjectionsOrSubscriptions"] = options.EnableEventSkippingInProjectionsOrSubscriptions
        };

        var content = new[] { new McpTextContent { Text = JsonSerializer.Serialize(config, JsonOptions) } };
        return WriteJsonRpcResult(context, request.Id, new { content });
    }

    private static Task HandleGetEventMetadataConfiguration(
        HttpContext context, JsonRpcRequest request, IReadOnlyEventStoreOptions options)
    {
        var metadata = options.MetadataConfig;
        var config = new Dictionary<string, object>
        {
            ["correlationIdEnabled"] = metadata.CorrelationIdEnabled,
            ["causationIdEnabled"] = metadata.CausationIdEnabled,
            ["headersEnabled"] = metadata.HeadersEnabled,
            ["userNameEnabled"] = metadata.UserNameEnabled
        };

        var content = new[] { new McpTextContent { Text = JsonSerializer.Serialize(config, JsonOptions) } };
        return WriteJsonRpcResult(context, request.Id, new { content });
    }

    private static Task HandleListKnownEventTypes(
        HttpContext context, JsonRpcRequest request, IReadOnlyEventStoreOptions options)
    {
        var eventTypes = options.AllKnownEventTypes().Select(et => new Dictionary<string, string>
        {
            ["eventTypeName"] = et.EventTypeName,
            ["dotNetTypeName"] = et.DotNetTypeName
        }).ToList();

        var content = new[] { new McpTextContent { Text = JsonSerializer.Serialize(eventTypes, JsonOptions) } };
        return WriteJsonRpcResult(context, request.Id, new { content });
    }

    private static Task HandleListProjections(
        HttpContext context, JsonRpcRequest request, IReadOnlyEventStoreOptions options)
    {
        var projections = options.Projections().Select(p => new Dictionary<string, object>
        {
            ["name"] = p.ImplementationType.Name,
            ["implementationType"] = p.ImplementationType.FullName ?? p.ImplementationType.Name,
            ["type"] = p.Type.ToString(),
            ["shards"] = p.ShardNames().Select(s => s.Identity).ToArray()
        }).ToList();

        var content = new[] { new McpTextContent { Text = JsonSerializer.Serialize(projections, JsonOptions) } };
        return WriteJsonRpcResult(context, request.Id, new { content });
    }

    private static Task HandleGetDaemonSettings(
        HttpContext context, JsonRpcRequest request, IReadOnlyEventStoreOptions options)
    {
        var daemon = options.Daemon;
        var config = new Dictionary<string, object>
        {
            ["asyncMode"] = daemon.AsyncMode.ToString(),
            ["slowPollingTime"] = daemon.SlowPollingTime.TotalMilliseconds,
            ["fastPollingTime"] = daemon.FastPollingTime.TotalMilliseconds,
            ["healthCheckPollingTime"] = daemon.HealthCheckPollingTime.TotalMilliseconds,
            ["staleSequenceThreshold"] = daemon.StaleSequenceThreshold.TotalMilliseconds
        };

        var content = new[] { new McpTextContent { Text = JsonSerializer.Serialize(config, JsonOptions) } };
        return WriteJsonRpcResult(context, request.Id, new { content });
    }

    private static async Task WriteJsonRpcResult(HttpContext context, object? id, object result)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;

        var response = new JsonRpcResponse { Id = id, Result = result };
        await JsonSerializer.SerializeAsync(context.Response.Body, response, JsonOptions, context.RequestAborted).ConfigureAwait(false);
    }

    private static async Task WriteJsonRpcError(HttpContext context, object? id, int code, string message)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;

        var response = new JsonRpcErrorResponse
        {
            Id = id,
            Error = new JsonRpcErrorDetail { Code = code, Message = message }
        };
        await JsonSerializer.SerializeAsync(context.Response.Body, response, JsonOptions, context.RequestAborted).ConfigureAwait(false);
    }

    #region JSON-RPC types

    internal class JsonRpcRequest
    {
        [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
        [JsonPropertyName("id")] public object? Id { get; set; }
        [JsonPropertyName("method")] public string Method { get; set; } = "";
        [JsonPropertyName("params")] public object? Params { get; set; }
    }

    internal class JsonRpcResponse
    {
        [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
        [JsonPropertyName("id")] public object? Id { get; set; }
        [JsonPropertyName("result")] public object? Result { get; set; }
    }

    internal class JsonRpcErrorResponse
    {
        [JsonPropertyName("jsonrpc")] public string Jsonrpc { get; set; } = "2.0";
        [JsonPropertyName("id")] public object? Id { get; set; }
        [JsonPropertyName("error")] public JsonRpcErrorDetail? Error { get; set; }
    }

    internal class JsonRpcErrorDetail
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; } = "";
    }

    internal class McpToolDefinition
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("inputSchema")] public McpToolInputSchema InputSchema { get; set; } = new();
    }

    internal class McpToolInputSchema
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "object";
        [JsonPropertyName("properties")] public Dictionary<string, object> Properties { get; set; } = new();
    }

    internal class McpTextContent
    {
        [JsonPropertyName("type")] public string Type { get; set; } = "text";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }

    #endregion
}
