# MCP Server

Marten ships with a built-in [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server that exposes your event store configuration as read-only tools. This lets AI assistants and agents introspect your Marten setup for diagnostics, code generation guidance, and operational visibility.

The MCP server uses the **Streamable HTTP** transport (stateless POST-based JSON-RPC) and is implemented as ASP.NET Core Minimal API endpoints in the `Marten.AspNetCore` package.

## Installation

Install the `Marten.AspNetCore` NuGet package:

```bash
dotnet add package Marten.AspNetCore
```

## Setup

Register the MCP endpoints in your ASP.NET Core application:

```csharp
using Marten.AspNetCore;

var app = builder.Build();

app.MapMartenMcp();
```

This registers a single POST endpoint at `/marten/mcp/` that handles all MCP JSON-RPC requests.

### Custom Route Prefix

You can change the default route prefix:

```csharp
app.MapMartenMcp("/api/marten-mcp");
```

## Authorization

`MapMartenMcp()` returns a `RouteGroupBuilder`, so you can chain standard ASP.NET Core endpoint configuration including authorization policies:

```csharp
app.MapMartenMcp()
    .RequireAuthorization("AdminPolicy");
```

Or with a specific authorization policy:

```csharp
app.MapMartenMcp()
    .RequireAuthorization(policy =>
    {
        policy.RequireRole("admin");
    });
```

::: warning
The MCP endpoints expose internal configuration details about your event store schema, projections, and event types. You **should** apply authorization to these endpoints in production environments.
:::

## Available Tools

The MCP server exposes five read-only tools:

### get_event_store_configuration

Returns the full event store options snapshot including:

| Property | Description |
| :--- | :--- |
| `streamIdentity` | `AsGuid` or `AsString` |
| `tenancyStyle` | `Single` or `Conjoined` |
| `appendMode` | `Quick` or `Rich` |
| `eventNamingStyle` | Event type naming convention |
| `databaseSchemaName` | Schema for event tables |
| `enableUniqueIndexOnEventId` | Whether Event.Id has a unique index |
| `useArchivedStreamPartitioning` | PostgreSQL list partitioning for archived streams |
| `useOptimizedProjectionRebuilds` | Optimized projection rebuild support |
| `useMandatoryStreamTypeDeclaration` | Whether stream type is required |
| `enableAdvancedAsyncTracking` | Advanced async projection tracking |
| `enableSideEffectsOnInlineProjections` | Side effects on inline projections |
| `useMonitoredAdvisoryLock` | Advisory lock monitoring for HotCold daemon |
| `enableEventSkippingInProjectionsOrSubscriptions` | Event skipping support |

### get_event_metadata_configuration

Returns which optional event metadata fields are enabled:

| Property | Description |
| :--- | :--- |
| `correlationIdEnabled` | Correlation ID tracking |
| `causationIdEnabled` | Causation ID tracking |
| `headersEnabled` | Custom event headers |
| `userNameEnabled` | User name / "last modified by" tracking |

### list_known_event_types

Lists all event types registered with the event store. Each entry includes:

| Property | Description |
| :--- | :--- |
| `eventTypeName` | The alias stored in the database `type` column (e.g. `members_joined`) |
| `dotNetTypeName` | The full .NET type name |

### list_projections

Lists all projections and subscriptions. Each entry includes:

| Property | Description |
| :--- | :--- |
| `name` | Projection class name |
| `implementationType` | Full .NET type name |
| `type` | Subscription type (e.g. `Snapshot`, `MultiStream`, `FlatTableProjection`) |
| `shards` | Array of shard identifiers |

### get_daemon_settings

Returns the async projection daemon configuration:

| Property | Description |
| :--- | :--- |
| `asyncMode` | Daemon mode (`Disabled`, `Solo`, `HotCold`) |
| `slowPollingTime` | Slow polling interval in milliseconds |
| `fastPollingTime` | Fast polling interval in milliseconds |
| `healthCheckPollingTime` | Health check polling interval in milliseconds |
| `staleSequenceThreshold` | Stale sequence detection threshold in milliseconds |

## MCP Protocol Details

The endpoint implements the MCP [Streamable HTTP transport](https://modelcontextprotocol.io/specification/2025-03-26/basic/transports#streamable-http). All requests are JSON-RPC 2.0 POST requests to the single endpoint.

### Supported Methods

| Method | Description |
| :--- | :--- |
| `initialize` | Handshake — returns server capabilities |
| `tools/list` | Lists available tools with descriptions and input schemas |
| `tools/call` | Executes a tool by name and returns results |

### Example Request

```bash
curl -X POST http://localhost:5000/marten/mcp/ \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": { "name": "get_event_store_configuration" }
  }'
```

### Example Response

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "{\"streamIdentity\":\"AsGuid\",\"tenancyStyle\":\"Single\",\"appendMode\":\"Quick\",...}"
      }
    ]
  }
}
```
