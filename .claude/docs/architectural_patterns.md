# Architectural Patterns

Recurring design patterns and conventions in the Marten codebase. Understanding these is essential for making changes that fit the existing architecture.

## 1. Fluent DI Registration

Marten integrates with `Microsoft.Extensions.DependencyInjection` through `AddMarten()` extension methods that return a builder for chained configuration.

- Entry point: `MartenServiceCollectionExtensions.cs:34`
- Multi-stage config: `IConfigureMarten` (sync) and `IAsyncConfigureMarten` (async, `MartenServiceCollectionExtensions.cs:43-48`) allow independent modules to register configuration hooks executed before the store initializes.
- Secondary stores: `AddMartenStore<T>()` registers additional independent document stores with their own configuration scope.

## 2. Session / Unit of Work

Sessions implement the Unit of Work pattern. All mutations are queued and flushed in a single `SaveChangesAsync()` call.

**Session hierarchy:**
- `QuerySession` (`Internal/Sessions/QuerySession.cs:18`) - Read-only base
- `DocumentSessionBase` (`Internal/Sessions/DocumentSessionBase.cs:17`) - Extends QuerySession with write tracking via `UnitOfWork` (`Internal/UnitOfWork.cs:16`)
- Concrete variants: `LightweightSession` (no tracking), `IdentityMapDocumentSession` (identity map), `DirtyCheckingDocumentSession` (automatic change detection)

**Convention:** Users select session type at open-time (`store.LightweightSession()`, `store.IdentitySession()`, `store.DirtyTrackedSession()`). Code generation produces optimized storage variants per tracking mode.

## 3. Document Storage Variants

Each document type gets a `DocumentProvider<T>` with four pre-built storage variants optimized for different tracking modes.

- Provider: `Internal/Storage/DocumentStorage.cs` - Holds `QueryOnly`, `Lightweight`, `IdentityMap`, and `DirtyTracking` variants
- Interface: `IDocumentStorage` (`Internal/Storage/IDocumentStorage.cs:27`) - Handles SQL generation, filtering, and metadata
- Sessions pick the matching variant at creation, eliminating runtime branching.

## 4. Storage Operations

All database mutations are modeled as `IStorageOperation` objects collected by the `UnitOfWork` and executed in batch.

- Interface: `IStorageOperation` (`Internal/Operations/IStorageOperation.cs:11`) extends `IQueryHandler` - operations both configure SQL commands and postprocess results
- `OperationRole` enum classifies operations as Insert, Update, Upsert, or Delete
- `UnitOfWork` (`Internal/UnitOfWork.cs:16`) collects and sorts operations for correct execution ordering within a single round-trip

## 5. Runtime Code Generation

Marten uses JasperFx's code generation framework to produce optimized C# code at build time or runtime, eliminating reflection overhead.

- `StoreOptions` implements `ICodeFileCollection` (`StoreOptions.GeneratesCode.cs:15`)
- `DocumentProviderBuilder` (`Internal/CodeGeneration/DocumentProviderBuilder.cs`) generates storage variants, bulk loaders, and selectors per document type
- Generated code can be written to disk via the CLI (`CommandLineRunner`) for AOT-friendly deployment: `dotnet run -- codegen write`
- `TypeLoadMode` controls behavior: `Auto` (generate if missing), `Static` (require pre-built), `Dynamic` (always generate)

## 6. LINQ Query Translation

LINQ queries are translated to SQL through a handler pipeline that separates query planning from execution.

- `IQueryHandler<T>` (`Linq/QueryHandlers/IQueryHandler.cs:18`) - Configures SQL command and reads results from `DbDataReader`
- `IMaybeStatefulHandler` (`Linq/QueryHandlers/IQueryHandler.cs:28`) - Handlers that need session-specific cloning (e.g., for identity map tracking)
- Compiled queries (`ICompiledQuery<TDoc, TOut>` in `Linq/ICompiledQuery.cs`) cache query plans for repeated execution with different parameters

## 7. Document Policy Convention

Cross-cutting document behaviors are applied through `IDocumentPolicy` implementations scanned at startup.

- Interface: `IDocumentPolicy` (`IDocumentPolicy.cs:8`) - Single method `Apply(DocumentMapping mapping)`
- Built-in policies in `Metadata/`:
  - `VersionedPolicy` - Auto-detect `IVersioned` / `IRevisioned` interfaces
  - `SoftDeletedPolicy` - Auto-detect `ISoftDeleted` interface
  - `TrackedPolicy` - Auto-detect `ICreatedAt` / `IUpdatedAt` interfaces
  - `TenancyPolicy` - Auto-detect `ITenanted` interface
- Custom policies registered via `StoreOptions.Policies.Add<T>()`

## 8. Event Sourcing Architecture

Event sourcing is built around `EventGraph` configuration and pluggable projections.

**Event appending:**
- `IEventAppender` (`Events/IEventAppender.cs`) - Processes events with inline projections during `SaveChangesAsync()`
- Decorator pattern: `GlobalEventAppenderDecorator` wraps base appender to filter by event/aggregate type

**Projections:**
- `SingleStreamProjection<TDoc>` - Aggregates events for one stream into a document
- `MultiStreamProjection<TDoc, TId>` - Cross-stream aggregation
- `EventProjection` (`Events/Projections/EventProjection.cs:18`) - Custom event-to-operation mapping
- Lifecycle modes: `Inline` (during SaveChanges), `Async` (via daemon), `Live` (on-demand)

**Async Daemon:**
- Manages asynchronous projection processing and event subscriptions
- `ISubscription` (`Subscriptions/ISubscription.cs:17`) for custom event processing pipelines

## 9. Multi-Tenancy Strategy

Tenancy uses the Strategy pattern with pluggable implementations.

- `ITenancy` (`Storage/ITenancy.cs`) - Resolves tenant databases/schemas
- Strategies: `SingleServerMultiTenancy` (schema-per-tenant), `StaticMultiTenancy` (database-per-tenant), or default single-tenant
- `TenancyStyle` enum on `IDocumentStorage` drives per-document tenant filtering
- Sessions resolve tenant context through `SessionOptions.TenantId`

## 10. Session Listener / Observer

Session lifecycle events are observed through listeners for cross-cutting concerns.

- `IDocumentSessionListener` (`IDocumentSessionListener.cs`) - Hooks for before/after insert, update, delete
- Registered globally via `StoreOptions.Listeners` or per-session via `SessionOptions`
- Used for auditing, validation, and event publishing without modifying session internals

## 11. Session Factory

Abstract factory pattern for customizing session creation.

- `ISessionFactory` (`ISessionFactory.cs`) - Creates `IDocumentSession` and `IQuerySession` instances
- Default registration in DI, replaceable for per-tenant or per-request session setup
- Sessions carry their own `IConnectionLifetime` for connection management

## 12. Testing Conventions

Tests follow a consistent base-class hierarchy to manage shared infrastructure.

- **Collection fixtures** share `DocumentStore` instances across test classes (`Marten.Testing/Harness/StoreFixture.cs`)
- **`IntegrationContext`** (`Marten.Testing/Harness/IntegrationContext.cs:49`) provides `theStore` and `theSession` properties, deletes data between tests
- **`SessionTypesAttribute`** (`Marten.Testing/Harness/IntegrationContext.cs:36`) enables `[Theory]` tests across all three tracking modes (None, IdentityOnly, DirtyTracking)
- **`OneOffConfigurationsContext`** creates isolated stores with unique schemas derived from test class name (`IntegrationContext.cs:122`)
- Tests use the `StoreOptions()` method (`IntegrationContext.cs:106`) to customize store config per test while maintaining proper disposal
- Test projects are organized by feature domain (CoreTests, DocumentDbTests, EventSourcingTests, etc.)

## 13. Partial Class Organization

Large classes are split across multiple files using C# partial classes, with each file handling a specific concern.

Examples:
- `StoreOptions.cs` + `StoreOptions.GeneratesCode.cs` + `StoreOptions.Registration.cs`
- `DocumentSessionBase.cs` is partial, with event-specific behavior split out
- `QuerySession.cs` splits tenancy handling into `QuerySession.Tenancy.cs`

**Convention:** When a class file gets large, split by concern using the `ClassName.Concern.cs` naming pattern.

## 14. Documentation Snippet Integration

Source code samples are embedded in VitePress docs through MarkdownSnippets.

- Mark regions in C# code: `#region sample_my_example` / `#endregion`
- Reference in docs: `<!-- snippet: sample_my_example -->`
- Run `npm run mdsnippets` to sync
- When modifying sample code, update both the source region and re-run mdsnippets
