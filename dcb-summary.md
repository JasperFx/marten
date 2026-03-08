# Marten DCB Implementation Summary

## Shared Types (JasperFx.Events)
- **`EventTag`** — `readonly record struct(Type TagType, object Value)` stored on each event
- **`TagTypeRegistration`** — Wraps a strong-typed ID type (e.g. `StudentId(Guid Value)`) with `ValueTypeInfo`, `TableSuffix`, `SimpleType`, and optional `AggregateType`
- **`EventTagQuery`** — Builds OR'd conditions of `(EventType?, TagType, TagValue)` via fluent `Or<TTag>(value)` API
- **`IEvent.WithTag()`** — Extension methods to attach tags to events; uses `TagValueExtractor` (compiled lambdas) to unwrap inner values

## Schema & Registration
- **`EventGraph`** — Holds `List<TagTypeRegistration>`, exposes `RegisterTagType<T>()`, `FindTagType()`, `TagTypes`
- **`EventTagTable`** — One table per tag type: `mt_event_tag_{suffix}` with composite PK `(value, seq_id)`, FK to `mt_events.seq_id`. Value column type maps from SimpleType (Guid→uuid, string→text, int→integer, etc.)
- **`EventGraph.FeatureSchema`** — Yields `EventTagTable` instances in schema object generation

## Tag Insert Operations (3 paths)
1. **`InsertEventTagOperation`** — Rich mode: direct `INSERT (value, seq_id) VALUES (...)` using pre-assigned sequence
2. **`InsertEventTagByEventIdOperation`** — Quick mode fallback: `INSERT ... SELECT seq_id FROM mt_events WHERE id = @eventId` for individual insert paths (Start/ExpectedVersion)
3. **`QuickAppendEventFunction`** — Quick mode optimized: tag value arrays passed as `varchar[]` parameters to the PL/pgSQL function; tags inserted inline in the same loop using the already-available `seq` variable
- **`EventTagOperations`** — Static helper dispatching to `QueueTagOperations()` (Rich) or `QueueTagOperationsByEventId()` (Quick individual paths)

## Code Generation (Quick Append)
- **`EventDocumentStorageGenerator.buildQuickAppendOperation()`** — Conditionally emits `writeAllTagValues(parameterBuilder)` when `graph.TagTypes.Count > 0`
- **`QuickAppendEventsOperationBase.writeAllTagValues()`** — Builds parallel `string?[]` arrays per tag type from event tags, appends as `NpgsqlDbType.Array | Varchar`
- **`QuickEventAppender`** — Routes: function path skips separate tag ops (handled in-function); Start/ExpectedVersion paths queue `InsertEventTagByEventIdOperation`

## Query & Consistency
- **`EventStore.Dcb.cs`** — Session APIs: `QueryByTagsAsync()`, `AggregateByTagsAsync<T>()`, `FetchForWritingByTags<T>()`. Builds SQL with INNER JOINs to tag tables + OR'd WHERE conditions
- **`AssertDcbConsistency`** — `IStorageOperation` queued at fetch time, runs at `SaveChangesAsync`: `EXISTS` query checks for events with `seq_id > lastSeenSequence` matching the tag query. Throws `DcbConcurrencyException` if violated
- **`IEventBoundary<T>`** / **`EventBoundary<T>`** — Wraps query result: `Aggregate`, `Events`, `LastSeenSequence`. `AppendOne()`/`AppendMany()` route new events to streams by tag values
- **`FetchForWritingByTagsHandler<T>`** — `IQueryHandler` for batch query support; same SQL building + assertion registration

## Batch Querying
- **`IBatchEvents.FetchForWritingByTags<T>()`** — Interface on `IBatchedQuery`
- **`BatchedQuery.Events.cs`** — Delegates to `FetchForWritingByTagsHandler<T>` via `AddItem()`

## Key File Locations

### JasperFx.Events (shared)
| File | Purpose |
|------|---------|
| `JasperFx.Events/EventTag.cs` | Core tag value type |
| `JasperFx.Events/Tags/TagTypeRegistration.cs` | Tag type registration & value extraction |
| `JasperFx.Events/Tags/EventTagQuery.cs` | Query specification with OR'd conditions |
| `JasperFx.Events/IEvent.cs` | `WithTag()` extension methods |
| `JasperFx.Events/Event.cs` | Tag storage on event instances |

### Marten
| File | Purpose |
|------|---------|
| `Marten/Events/EventGraph.cs` | `RegisterTagType<T>()`, tag type list |
| `Marten/Events/EventGraph.FeatureSchema.cs` | Schema generation for tag tables |
| `Marten/Events/Schema/EventTagTable.cs` | Tag table DDL (per tag type) |
| `Marten/Events/Schema/QuickAppendEventFunction.cs` | PL/pgSQL function with inline tag inserts |
| `Marten/Events/Operations/InsertEventTagOperation.cs` | Rich mode tag insert |
| `Marten/Events/Operations/InsertEventTagByEventIdOperation.cs` | Quick mode tag insert (by event ID) |
| `Marten/Events/Operations/EventTagOperations.cs` | Static tag operation dispatcher |
| `Marten/Events/Operations/QuickAppendEventsOperationBase.cs` | `writeAllTagValues()` for function path |
| `Marten/Events/QuickEventAppender.cs` | Append routing (function vs individual) |
| `Marten/Events/CodeGeneration/EventDocumentStorageGenerator.cs` | Conditional tag codegen |
| `Marten/Events/EventStore.Dcb.cs` | Session-level DCB APIs |
| `Marten/Events/Dcb/IEventBoundary.cs` | Public boundary interface |
| `Marten/Events/Dcb/EventBoundary.cs` | Boundary implementation & event routing |
| `Marten/Events/Dcb/AssertDcbConsistency.cs` | Consistency check operation |
| `Marten/Events/Dcb/DcbConcurrencyException.cs` | Concurrency violation exception |
| `Marten/Events/Dcb/FetchForWritingByTagsHandler.cs` | Batch query handler |
| `Marten/Services/BatchQuerying/BatchedQuery.Events.cs` | Batch query integration |
