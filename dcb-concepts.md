# DCB (Dynamic Consistency Boundary) Implementation in Marten

## What is DCB?

DCB is a technique for enforcing consistency in event-driven systems without rigid aggregate-based transactional boundaries. It allows events to be assigned to **multiple domain concepts** via tags, and enforces consistency across them using conditional appends.

**Core spec (https://dcb.events/):**
- **Read**: Filter events by event type (OR) and/or tags (AND within a query item, OR across items)
- **Write**: Atomically persist events, failing if any event matching a query exists after a given global sequence position

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Coexistence | DCB coexists with traditional stream-based event sourcing | Purely additive — users tag some events, use streams for others |
| Stream requirement | DCB events always belong to a stream | Keeps existing stream infrastructure intact |
| Condition model | First-class "condition" pattern | Encapsulates query + state + consistency check as a formal concept |
| Append atomicity | Condition check within `SaveChangesAsync()` transaction | True consistency, no check-then-act race |
| Append modes | Both Rich and Quick | Full compatibility |
| Tenancy | Scoped to current tenant by default | Opt-in for cross-tenant queries |
| Portability | Abstractions in JasperFx.Events, PostgreSQL impl in Marten | Maximizes portability |
| Tag extraction | Write-time with backfill tool | Pre-computed for query performance; backfill for migration |
| Schema | Opt-in `tags TEXT[]` column + GIN index on `mt_events` | No impact to existing users |
| Performance target | ~1ms per event at 1M events | Aligned with DCB FAQ benchmarks |

## Storage Approach

### Recommended: `TEXT[]` column with GIN index

When DCB is enabled, add to `mt_events`:

```sql
ALTER TABLE mt_events ADD COLUMN tags TEXT[];
CREATE INDEX ix_mt_events_tags ON mt_events USING GIN (tags);
```

**Why this over alternatives:**
- **vs normalized junction table**: No JOINs, single-table queries, simpler writes
- **vs JSONB headers**: Dedicated column with native array operators, proper GIN indexing, no mixing concerns
- **vs query-time computation**: Pre-computed tags enable indexed conditional append checks — the core DCB performance requirement

### Conditional Append SQL

The append condition check runs within the same transaction as event insertion:

```sql
SELECT EXISTS (
  SELECT 1 FROM mt_events
  WHERE seq_id > @after
  AND (
    -- Query Item 1 (types OR'd, tags AND'd within item)
    (type = ANY(@types1) AND tags @> @tags1)
    OR
    -- Query Item 2
    (type = ANY(@types2) AND tags @> @tags2)
  )
  -- Tenant scoping (default)
  AND tenant_id = @tenantId
)
```

If this returns `true`, the append fails with a concurrency exception.

## API Syntax

### Tag Registration

**Fluent configuration in StoreOptions:**

```csharp
opts.Events.EnableDcb(); // opt-in, triggers schema addition

opts.Events.TagEvent<StudentSubscribedToCourse>(e => new[]
{
    $"student:{e.StudentId}",
    $"course:{e.CourseId}"
});

opts.Events.TagEvent<CourseCapacityChanged>(e => new[]
{
    $"course:{e.CourseId}"
});
```

**Interface on the event (alternative):**

```csharp
public record StudentSubscribedToCourse(Guid StudentId, Guid CourseId) : ITaggedEvent
{
    public IEnumerable<string> GetTags() => [$"student:{StudentId}", $"course:{CourseId}"];
}
```

### Condition Pattern

A "condition" is a first-class concept that encapsulates:
1. **Query**: What event types and tags to look for
2. **State**: Projecting matching events into a decision model
3. **Check**: Whether the append should proceed
4. **Enforcement**: Atomic validation during `SaveChangesAsync()`

```csharp
public class CourseSubscriptionCondition : AppendCondition
{
    public bool CourseExists { get; set; }
    public int Capacity { get; set; }
    public int Subscriptions { get; set; }

    // Define which events this condition queries
    public override void ConfigureQuery(DcbQueryBuilder query, Guid courseId, Guid studentId)
    {
        query
            .Match<CourseDefined>([$"course:{courseId}"])
            .Match<CourseCapacityChanged>([$"course:{courseId}"])
            .Match<StudentSubscribedToCourse>([$"course:{courseId}"])
            .Match<StudentSubscribedToCourse>([$"student:{studentId}"]);
    }

    // Build state from matching events
    public void Apply(CourseDefined e) => CourseExists = true;
    public void Apply(CourseCapacityChanged e) => Capacity = e.NewCapacity;
    public void Apply(StudentSubscribedToCourse e) => Subscriptions++;

    // Evaluate whether append is allowed
    public override bool CanAppend() =>
        CourseExists && Subscriptions < Capacity;
}
```

**Usage:**

```csharp
var condition = await session.Events
    .BuildCondition<CourseSubscriptionCondition>(courseId, studentId);

if (condition.CanAppend())
{
    session.Events.AppendWithCondition(
        streamId,
        condition,
        new StudentSubscribedToCourse(studentId, courseId)
    );
    await session.SaveChangesAsync();
    // ^ condition check + event insert in same transaction
    // throws concurrency exception if condition violated
}
```

### Backfill Tool

For existing events when enabling DCB or adding new tag definitions:

```csharp
await store.Events.BackfillTagsAsync(CancellationToken.None);
```

This reads events in batches, applies registered tag extractors, and updates the `tags` column.

## Architecture Split

### JasperFx.Events (portable)

- `ITaggedEvent` interface
- `AppendCondition` base class / condition model
- `DcbQueryBuilder` — query construction
- `IDcbQuery` / `DcbQueryItem` — query representation
- Tag extraction registration abstractions
- Apply method conventions for condition state

### Marten (PostgreSQL-specific)

- `EnableDcb()` opt-in configuration
- `tags TEXT[]` column addition to `EventsTable`
- GIN index creation
- SQL generation for conditional append check
- Integration with Rich and Quick append paths
- `BackfillTagsAsync()` migration tool
- Tenant scoping in condition queries

## DCB Spec Mapping

| DCB Spec Concept | Marten Implementation |
|---|---|
| Sequence Position | `seq_id` (existing global sequence) |
| Event Type | `type` column (existing) |
| Tags | `tags TEXT[]` column (new, opt-in) |
| Query | `DcbQueryBuilder` → SQL with `type = ANY(...)` and `tags @> ARRAY[...]` |
| Query Item (types OR, tags AND) | Single `WHERE` clause per item, items combined with `OR` |
| Append Condition `failIfEventsMatch` | `SELECT EXISTS(...)` check in `SaveChangesAsync()` transaction |
| Append Condition `after` position | `WHERE seq_id > @after` in the condition query |
| Atomic append | PostgreSQL transaction wrapping condition check + event INSERT |
