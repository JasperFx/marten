# Dynamic Consistency Boundary (DCB) Implementation Plan

**GitHub Issue**: https://github.com/JasperFx/marten/issues/4159
**Scope**: JasperFx.Events abstractions + Marten storage/querying (Phase 1)
**Wolverine integration**: Deferred to Phase 2

---

## Context

Marten already handles multi-stream consistency better than most event stores via `FetchForWriting` + `AlwaysEnforceConsistency`. DCB adds a complementary pattern: querying events by **tags** (cross-stream identifiers) rather than by stream, with consistency assertions over those tag-based queries. The main value-add is simpler code for certain cross-cutting patterns and joining the DCB ecosystem.

Most DCB use cases (constraints across entities, global uniqueness, idempotency) are already well-served by Marten's existing capabilities. DCB primarily benefits scenarios where you want to project and enforce consistency over a set of events identified by shared tags rather than by stream identity.

---

## Design Decisions (Resolved)

1. **Tag value type resolution**: Use `JasperFx.Core.Reflection.ValueTypeInfo` to validate tag types and extract inner values. `ValueTypeInfo.ForType()` resolves the inner primitive type (`SimpleType`), provides `ValueProperty` for extraction, and compiles fast `UnWrapper`/`CreateWrapper` delegates. No need for user-supplied lambdas or Vogen-specific knowledge.

2. **Tag-to-stream routing**: Derived from Marten's existing document mappings. If `Student` has identity type `StudentId` and `StudentId` is a registered tag type, then events tagged with a `StudentId` value route to the `Student` stream with that identity — but only if that stream is already open in the current session via `FetchForWriting()`. An event tagged with multiple tag types (e.g., `StudentId` + `CourseId`) is appended to all matching open streams.

3. **DCB event stream type**: Use a **new `IEventBoundary<T>` type**, separate from `IEventStream<T>`. The behavior is fundamentally different — sequence-based (global `seq_id`) rather than version-based (per-stream), and consistency assertion mechanism differs.

4. **Abstraction layering**: JasperFx.Events gets the abstractions and specs (`EventTag`, tag type registry, `EventTagQuery`). Marten gets all execution (SQL generation, tag table management, DCB fetch/assert operations).

5. **Tag table storage**: Separate tables per registered tag type (e.g., `mt_event_tag_student_id`). Better query performance and simpler indexes than a shared discriminated table. The tables should have a composite primary key of the sequence and the value, but put the value first in the primary key

6. **Tag table schema management**: Tag tables are `ISchemaObject` instances yielded from the existing `EventGraph.FeatureSchema.createAllSchemaObjects()`, so they are created/migrated alongside `mt_events`, `mt_streams`, and other event store schema objects.

7. **DCB assertion performance**: Must be tightly constrained SQL to avoid unnecessary database work under concurrent load. Use `EXISTS` rather than `COUNT(*)`, composite indexes on `(value, seq_id)`, and narrow the assertion to only the relevant tag values and event types from the original query. Load testing required to validate the approach under contention.

---

## Phase 1: JasperFx.Events — Tag Abstractions

### 1a. Tag Value Model

Add to `JasperFx.Events`:

```csharp
/// <summary>
/// Represents a single tag on an event — a (TagType, Value) pair where TagType
/// is a strong-typed identifier (e.g., StudentId) and Value is the unwrapped primitive.
/// </summary>
public readonly record struct EventTag(Type TagType, object Value);
```

Extend `IEvent`:
- Add `IReadOnlyList<EventTag>? Tags { get; }` — lazy, like `Headers`
- Add `IEvent WithTag<TTag>(TTag value)` fluent method
- Add multi-tag convenience: `IEvent WithTag(params object[] tags)`

Extend `Event<T>`:
- Add backing `List<EventTag>? _tags` field (lazy)
- Implement `WithTag<TTag>()` — uses `ValueTypeInfo.ForType(typeof(TTag))` to extract the inner value and store `new EventTag(typeof(TTag), innerValue)`

Any time we are extracting the inner value from a strong typed identifier, use a memoized copy of the UnWrapper() Lambda created by ValueTypeInfo to eliminate
the usage of Reflection at runtime

### 1b. Tag Type Registry

```csharp
public interface ITagTypeRegistration
{
    Type TagType { get; }               // e.g., typeof(StudentId)
    ValueTypeInfo ValueTypeInfo { get; } // resolved via ValueTypeInfo.ForType()
    string TableSuffix { get; }          // e.g., "student_id" for table naming

    // Convenience
    Type SimpleType { get; }             // e.g., typeof(string) — the inner primitive
}
```

Correction: just make this a concrete type with no interface abstraction

Registration API on the event store options (in JasperFx.Events or Marten — TBD, but try to place in JasperFx.Events):

```csharp
StoreOptions.Events.RegisterTagType<StudentId>();
// Internally: ValueTypeInfo.ForType(typeof(StudentId)) validates the type
```

The registry is an `IReadOnlyList<ITagTypeRegistration>` accessible from event store configuration.

Automatically register tag types for any SingleStreamProjection or MultiStreamProjection registered in the system
that uses a strong typed identifier for the identity type of its document.

### 1c. DCB Query Specification

```csharp
public class EventTagQuery
{
    /// <summary>
    /// Add condition: events of type TEvent tagged with the given tag value
    /// </summary>
    public EventTagQuery Or<TEvent, TTag>(TTag tagValue);

    /// <summary>
    /// Add condition: any event tagged with the given tag value
    /// </summary>
    public EventTagQuery Or<TTag>(TTag tagValue);

    internal IReadOnlyList<EventTagQueryCondition> Conditions { get; }
}

public record EventTagQueryCondition(Type? EventType, Type TagType, object TagValue);
```

This is the query spec that Marten translates to SQL with INNER JOINs on tag tables.

---

## Phase 2: Marten — Tag Table Schema

### 2a. EventTagTable Schema Object

For each registered tag type, create a table:

```sql
CREATE TABLE {schema}.mt_event_tag_{suffix} (
    seq_id BIGINT NOT NULL REFERENCES {schema}.mt_events(seq_id),
    value  {pg_type} NOT NULL,
    PRIMARY KEY (seq_id)
);
CREATE INDEX ix_mt_event_tag_{suffix}_value
    ON {schema}.mt_event_tag_{suffix} (value, seq_id);
```

- `{suffix}` derived from tag type name via snake_case (e.g., `StudentId` → `student_id`)
- `{pg_type}` derived from `ValueTypeInfo.SimpleType` → PostgreSQL type mapping (string→text, Guid→uuid, int→integer, long→bigint)
- Composite index on `(value, seq_id)` optimizes both tag queries and DCB assertion range scans
- Handle conjoined tenancy: add `tenant_id` column + adjust PK/indexes

Implementation:
- New class `EventTagTable : Table` in `Marten.Events.Schema`
- Yielded from `EventGraph.FeatureSchema.createAllSchemaObjects()`:

```csharp
// In createAllSchemaObjects():
foreach (var tagRegistration in RegisteredTagTypes)
{
    yield return new EventTagTable(this, tagRegistration);
}
```

---

## Phase 3: Marten — Tag Persistence on Append

### 3a. InsertEventTagOperation

New `IStorageOperation` that inserts a row into a tag table:

```sql
INSERT INTO {schema}.mt_event_tag_{suffix} (seq_id, value) VALUES (@seq_id, @value);
```

### 3b. Integration with Appenders

**Quick path** (`QuickEventAppender`):
- After `QuickAppendEvents`, iterate events with tags
- For each tag on each event, queue an `InsertEventTagOperation` using the event's assigned `seq_id`

**Rich path** (`RichEventAppender`):
- After assigning sequences via `EventSequenceFetcher`, iterate events with tags
- Queue `InsertEventTagOperation` for each tag
- Tags are written in the same transaction as events

Both paths ensure tag inserts happen atomically with event inserts within the same `SaveChangesAsync()` transaction.

---

## Phase 4: Marten — DCB Event Querying

### 4a. Query API

Add to `IEventStore`:

```csharp
Task<IReadOnlyList<IEvent>> QueryByTagsAsync(EventTagQuery query, CancellationToken ct = default);
Task<T?> AggregateByTagsAsync<T>(EventTagQuery query, CancellationToken ct = default) where T : class;
```

### 4b. SQL Generation

For a query like `query.Or<StudentRegistered, StudentId>(studentId).Or<CourseCapacityChanged, CourseId>(courseId)`:

```sql
SELECT e.*
FROM {schema}.mt_events e
INNER JOIN {schema}.mt_event_tag_student_id t1 ON e.seq_id = t1.seq_id
INNER JOIN {schema}.mt_event_tag_course_id t2 ON e.seq_id = t2.seq_id
WHERE (e.type = 'student_registered' AND t1.value = @p0)
   OR (e.type = 'course_capacity_changed' AND t2.value = @p1)
ORDER BY e.seq_id
```

When tag type is the same across conditions, only one JOIN is needed. Multiple JOINs only when querying across different tag types.

### 4c. AggregateByTagsAsync

Runs the standard `AggregateTo<T>()` pipeline (live fold) over the events returned by `QueryByTagsAsync`. Always a live aggregation — no inline projection support for DCB queries.

---

## Phase 5: Marten — DCB FetchForWriting

This is the key DCB primitive — load events by tag query, aggregate them, and assert no new matching events were added by `SaveChangesAsync()` time.

### 5a. IEventBoundary<T>

```csharp
public interface IEventBoundary<out T> where T : notnull
{
    /// <summary>
    /// The aggregate projected from the events matching the tag query
    /// </summary>
    T? Aggregate { get; }

    /// <summary>
    /// The maximum seq_id from the tag query results.
    /// Used as the consistency boundary marker.
    /// </summary>
    long LastSeenSequence { get; }

    /// <summary>
    /// The events that matched the tag query
    /// </summary>
    IReadOnlyList<IEvent> Events { get; }

    /// <summary>
    /// Append an event. The event MUST have tags set via WithTag()
    /// so Marten can route it to the appropriate stream(s).
    /// </summary>
    void AppendOne(object @event);
    void AppendMany(params object[] events);
    void AppendMany(IEnumerable<object> events);
}
```

Key differences from `IEventStream<T>`:
- No stream identity (`Id`/`Key`) — this is a cross-stream query result
- Sequence-based assertion rather than version-based
- Events route to streams by their tags, not to a single predetermined stream
- Consistency is always enforced — no opt-in flag

### 5b. FetchForWritingByTags API

Add to `IEventStore`:

```csharp
Task<IEventBoundary<T>> FetchForWritingByTags<T>(
    EventTagQuery query,
    CancellationToken ct = default) where T : class;
```

Implementation:
1. Execute the tag query (same SQL as `QueryByTagsAsync`)
2. Record `LastSeenSequence` = max `seq_id` from results
3. Aggregate events into `T` via live fold
4. Return `IEventBoundary<T>` wrapping the aggregate, events, and sequence marker
5. Register the DCB assertion operation with the session's work tracker

### 5c. DCB Assertion Operation

New `IStorageOperation` that runs at `SaveChangesAsync()` time:

```sql
SELECT EXISTS (
    SELECT 1 FROM {schema}.mt_event_tag_{suffix} t
    WHERE t.value = @tagValue AND t.seq_id > @lastSeenSeqId
    AND EXISTS (
        SELECT 1 FROM {schema}.mt_events e
        WHERE e.seq_id = t.seq_id AND e.type = ANY(@eventTypes)
    )
    LIMIT 1
)
```

- If `true` → throw `ConcurrencyException` (or a DCB-specific subclass)
- One assertion per condition group in the original `EventTagQuery`
- Uses the `(value, seq_id)` composite index on the tag table for efficient range scans
- `EXISTS` + `LIMIT 1` avoids scanning all matching rows

### 5d. Event Routing on Append

When `IEventBoundary<T>.AppendOne(event)` is called:
1. The event must have tags (set via `WithTag()`)
2. For each tag on the event:
   - Resolve tag type → aggregate type (from document mapping: aggregate's identity type matches tag type)
   - Look up `WorkTracker.TryFindStream()` for a `StreamAction` with matching aggregate type and identity value
   - If found → append the event to that stream's `StreamAction`
   - If no matching stream exists → create a new stream (or error — TBD, see open questions)
3. An event with multiple tags may be appended to multiple streams
4. Tag insert operations are also queued for persistence

---

## Phase 6: Retroactive Tagging (Lower Priority)

For migrating existing event stores to use tags:

```csharp
session.Events.TagEvent(long sequenceId, params object[] tags);
session.Events.TagEvents(IEnumerable<long> sequenceIds, params object[] tags);
```

Simple `INSERT` operations into tag tables. Does not participate in DCB consistency assertions.

Add a second option that is destructive and completely rewrites any possible tag values for a single type of tags like:

```csharp
session.Events.ReplaceTags<T>(long sequenceId, params T[] tags);
session.Events.ReplaceTags<T>(IEnumerable<long> sequenceIds, params T[] tags);
```

---

## Open Questions

### Tag Mutability and DCB Consistency

Retroactive tagging (`TagEvent`) adds tags to existing events. If retroactive tagging is used concurrently with DCB operations, the assertion query (`seq_id > @lastSeenSeqId`) would miss tags added to older events after the read point. Options:

- **Option A**: Retroactive tagging does not participate in DCB consistency (simplest). Tags added retroactively are for querying only, not for consistency boundaries.
- **Option B**: Add a `tag_added_at` timestamp or sequence to tag tables and include it in the assertion. More complex but fully consistent.

**Recommendation**: Option A for now. Retroactive tagging is a migration/backfill tool, not a concurrent operation pattern.

**Answer**: Use Option A. This is a very low level of risk

### Multiple Tags of Same Type on One Event

Is it valid to tag an event with two different values of the same tag type? E.g., an event tagged with `StudentId("s1")` AND `StudentId("s2")`?

If yes: the tag table PK must be `(seq_id, value)` composite instead of `(seq_id)` alone.
If no: PK on `(seq_id)` alone is correct and simpler.

**Answer**: Yes, we will need to support one to many

### Auto-Tag from IEventBoundary

Should `IEventBoundary<T>.AppendOne()` auto-tag appended events based on the query that loaded the stream? Or must callers always explicitly tag via `WithTag()`? Auto-tagging reduces boilerplate but is implicit.

**Answer**: Require users to explicitly set tags. we may have to revisit this

### Stream Auto-Creation on Tag Routing

When an event is tagged with a `StudentId` value but no `Student` stream exists yet in the session, should Marten auto-create the stream (via `StartStream`)? Or should it require the stream to already be open via `FetchForWriting()`?

**Answer**: yes.

### Tag Table Naming Collisions

Using short type name for table suffix (`student_id` from `StudentId`). If two different tag types in different namespaces have the same short name, this would collide. Options:
- Short name (simpler, collision risk)
- Allow explicit table name override in `RegisterTagType<T>()`
- Use full namespace-qualified name (verbose)

**Recommendation**: Short name by default with optional override.

**Answer**: use the recommendation

### Tag Type to Aggregate Type Association

Should the mapping from tag type to aggregate type be:
- Always inferred from document mapping (aggregate's identity type = tag type)
- Optionally explicit via `RegisterTagType<StudentId>().ForAggregate<Student>()`
- Both (infer by default, allow explicit override)

**Recommendation**: Infer by default, allow explicit override for edge cases where the identity type doesn't match.

**Answer**: yes, use the recommendation

---

## Implementation Order

| Step | Repo | What | Depends On |
|------|------|------|------------|
| 1 | JasperFx.Events | `EventTag` record, `IEvent.Tags`, `WithTag()` | — |
| 2 | JasperFx.Events | Tag type registry using `ValueTypeInfo` | Step 1 |
| 3 | JasperFx.Events | `EventTagQuery` specification | Step 2 |
| 4 | Marten | `EventTagTable` schema object + DDL generation | Steps 1-2 |
| 5 | Marten | `InsertEventTagOperation` + appender integration | Steps 1, 4 |
| 6 | Marten | `QueryByTagsAsync` + `AggregateByTagsAsync` | Steps 3-5 |
| 7 | Marten | `IEventBoundary<T>` + `FetchForWritingByTags` | Step 6 |
| 8 | Marten | DCB assertion operation | Step 7 |
| 9 | Marten | Event routing by tags to open streams | Steps 7-8 |
| 10 | Marten | Retroactive tagging API | Step 4 |
| 11 | Marten | Load testing DCB assertions under contention | Steps 8-9 |
| 12 | Marten | Create a new documentation page under the event sourcing documentation for the usage of DCB with Marten. In the section on FetchForWriting, mention the new DCB support and link to the DCB content |
---

## Future Phase: Wolverine Integration

Deferred to a follow-up phase. Will include:
- `[DcbAggregate]` attribute for handler parameters
- `LoadDcbAggregateFrame` code generation
- Convention-based tag discovery from command properties
- Tag-aware event routing in handler workflow
- Integration with existing `MartenBatchingPolicy` for batched loads
- Documentation and samples

We will need to discuss possible usages for APIs that allow you to go from an incoming Wolverine message to the inputs to Marten
after the initial implementation.

---

## Key Files to Modify

### JasperFx.Events
- `src/JasperFx.Events/IEvent.cs` — add `Tags` property, `WithTag()` methods
- `src/JasperFx.Events/Event.cs` — implement tag storage
- New: `src/JasperFx.Events/EventTag.cs` — tag record
- New: `src/JasperFx.Events/Tags/TagTypeRegistration.cs` — registry
- New: `src/JasperFx.Events/Tags/EventTagQuery.cs` — query spec

### Marten
- `src/Marten/Events/EventGraph.cs` — tag type registration API
- `src/Marten/Events/EventGraph.FeatureSchema.cs` — yield tag table schema objects
- New: `src/Marten/Events/Schema/EventTagTable.cs` — tag table DDL
- New: `src/Marten/Events/Operations/InsertEventTagOperation.cs` — tag persistence
- `src/Marten/Events/QuickEventAppender.cs` — queue tag inserts
- `src/Marten/Events/RichEventAppender.cs` — queue tag inserts
- New: `src/Marten/Events/Dcb/IEventBoundary.cs` — DCB stream interface
- New: `src/Marten/Events/Dcb/EventBoundary.cs` — DCB stream implementation
- New: `src/Marten/Events/Dcb/AssertDcbConsistency.cs` — assertion operation
- `src/Marten/Events/EventStore.cs` — new query/fetch APIs
- `src/Marten/Events/IEventStore.cs` — new API surface
