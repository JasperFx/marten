# Dynamic Consistency Boundary (DCB)

The Dynamic Consistency Boundary (DCB) pattern allows you to query and enforce consistency across events from multiple streams using **tags** -- strong-typed identifiers attached to events at append time. This is useful when your consistency boundary doesn't align with a single event stream.

## Concept

In traditional event sourcing, consistency is enforced per-stream using optimistic concurrency on the stream version. DCB extends this by letting you:

1. **Tag** events with one or more strong-typed identifiers
2. **Query** events across streams by those tags
3. **Aggregate** tagged events into a view (like a live aggregation, but cross-stream)
4. **Enforce consistency** at save time -- detecting if new matching events were appended since you last read

## Registering Tag Types

Tag types are strong-typed identifiers (typically `record` types wrapping a primitive). Register them during store configuration:

<!-- snippet: sample_marten_dcb_registering_tag_types -->
<a id='snippet-sample_marten_dcb_registering_tag_types'></a>
```cs
private void ConfigureStore()
{
    StoreOptions(opts =>
    {
        opts.Events.AddEventType<StudentEnrolled>();
        opts.Events.AddEventType<AssignmentSubmitted>();
        opts.Events.AddEventType<StudentDropped>();
        opts.Events.AddEventType<StudentGraded>();

        // Register tag types -- each gets its own table (mt_event_tag_student, mt_event_tag_course)
        opts.Events.RegisterTagType<StudentId>("student")
            .ForAggregate<StudentCourseEnrollment>();
        opts.Events.RegisterTagType<CourseId>("course")
            .ForAggregate<StudentCourseEnrollment>();

        opts.Projections.LiveStreamAggregation<StudentCourseEnrollment>();
    });
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L68-L87' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_registering_tag_types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Each tag type gets its own table (`mt_event_tag_student`, `mt_event_tag_course`, etc.) with a composite primary key of `(value, seq_id)`.

### Automatic Tag Type Registration

When you register a `SingleStreamProjection<TDoc, TId>` or `MultiStreamProjection<TDoc, TId>` that uses a strong-typed identifier as its `TId`, Marten will **automatically register that type as a tag type** with `ForAggregate()` pointing to `TDoc`. This means you don't need to call `RegisterTagType<TId>()` explicitly in most cases:

```csharp
// The projection's TId (TicketId) is auto-registered as a tag type
opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);

// No need for: opts.Events.RegisterTagType<TicketId>().ForAggregate<TicketSummary>();
```

Auto-discovery only applies to strong-typed identifiers (e.g., `record struct TicketId(Guid Value)`). Primitive types like `Guid`, `string`, `int`, `long`, and `short` are not auto-registered.

If you explicitly register a tag type before auto-discovery runs, your explicit registration takes precedence. This lets you customize the table suffix when needed:

```csharp
// Explicit registration with custom table suffix — auto-discovery won't overwrite this
opts.Events.RegisterTagType<TicketId>("custom_ticket")
    .ForAggregate<TicketSummary>();
opts.Projections.Add<TicketSummaryProjection>(ProjectionLifecycle.Inline);
```

### Tag Type Requirements

Tag types should be simple wrapper records around a primitive value:

<!-- snippet: sample_marten_dcb_tag_type_definitions -->
<a id='snippet-sample_marten_dcb_tag_type_definitions'></a>
```cs
// Strong-typed tag identifiers
public record StudentId(Guid Value);
public record CourseId(Guid Value);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L18-L22' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_tag_type_definitions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Supported inner value types: `Guid`, `string`, `int`, `long`, `short`.

Tags work with both **Rich** and **Quick** append modes (the default in Marten 9 is `QuickWithServerTimestamps`). In Rich mode, tags are inserted using pre-assigned sequence numbers. In Quick mode, tags are inserted using a subquery that looks up the sequence from the event's id.

## Tagging Events

Use `BuildEvent` and `WithTag` to attach tags before appending:

<!-- snippet: sample_marten_dcb_tagging_events -->
<a id='snippet-sample_marten_dcb_tagging_events'></a>
```cs
var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
enrolled.WithTag(studentId, courseId);
theSession.Events.Append(streamId, enrolled);
await theSession.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L112-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_tagging_events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Events can have multiple tags of different types. Tags are persisted to their respective tag tables in the same transaction as the event.

## Querying Events by Tags

Use `EventTagQuery` to build a query, then execute it with `QueryByTagsAsync`:

<!-- snippet: sample_marten_dcb_query_by_single_tag -->
<a id='snippet-sample_marten_dcb_query_by_single_tag'></a>
```cs
var query = new EventTagQuery().Or<StudentId>(studentId);
var events = await theSession.Events.QueryByTagsAsync(query);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L119-L122' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_query_by_single_tag' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Multiple Tags (OR)

<!-- snippet: sample_marten_dcb_query_multiple_tags_or -->
<a id='snippet-sample_marten_dcb_query_multiple_tags_or'></a>
```cs
// Query for either student
var query = new EventTagQuery()
    .Or<StudentId>(student1)
    .Or<StudentId>(student2);

var events = await theSession.Events.QueryByTagsAsync(query);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L149-L156' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_query_multiple_tags_or' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Filtering by Event Type

<!-- snippet: sample_marten_dcb_query_by_event_type -->
<a id='snippet-sample_marten_dcb_query_by_event_type'></a>
```cs
// Query only AssignmentSubmitted events for this student
var query = new EventTagQuery()
    .Or<AssignmentSubmitted, StudentId>(studentId);

var events = await theSession.Events.QueryByTagsAsync(query);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L176-L182' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_query_by_event_type' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Events are always returned ordered by sequence number (global append order).

## Aggregating by Tags

Build an aggregate from tagged events, similar to `AggregateStreamAsync` but across streams. First define an aggregate that applies the tagged events:

<!-- snippet: sample_marten_dcb_aggregate -->
<a id='snippet-sample_marten_dcb_aggregate'></a>
```cs
// Aggregate for DCB
public class StudentCourseEnrollment
{
    public Guid Id { get; set; }
    public string StudentName { get; set; } = "";
    public string CourseName { get; set; } = "";
    public List<string> Assignments { get; set; } = new();
    public bool IsDropped { get; set; }

    public void Apply(StudentEnrolled e)
    {
        StudentName = e.StudentName;
        CourseName = e.CourseName;
    }

    public void Apply(AssignmentSubmitted e)
    {
        Assignments.Add(e.AssignmentName);
    }

    public void Apply(StudentDropped e)
    {
        IsDropped = true;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L37-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Then aggregate across streams by tag query:

<!-- snippet: sample_marten_dcb_aggregate_by_tags -->
<a id='snippet-sample_marten_dcb_aggregate_by_tags'></a>
```cs
var query = new EventTagQuery()
    .Or<StudentId>(studentId)
    .Or<CourseId>(courseId);

var aggregate = await theSession.Events.AggregateByTagsAsync<StudentCourseEnrollment>(query);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L222-L228' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_aggregate_by_tags' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Returns `null` if no matching events are found.

### Identity-less Boundary Aggregates

`StudentCourseEnrollment` above carries a single-stream `Id`, so it doubles as an ordinary aggregate. A **pure boundary aggregate** has no single-stream identity at all — it exists only as the projection of the events selected by a tag query, spanning many streams. Mark such a type with `[BoundaryAggregate]` so the source generator still emits a dispatcher for it even though it has no `Id` property and no `[AggregateIdentity]`:

<!-- snippet: sample_marten_dcb_boundary_aggregate -->
<a id='snippet-sample_marten_dcb_boundary_aggregate'></a>
```cs
// A *pure* DCB boundary aggregate: Apply methods, but no single-stream identity
// (no Id property, no [AggregateIdentity]). It spans multiple streams by tag, so
// the only thing that makes the source generator emit an evolver for it is the
// [BoundaryAggregate] marker. See marten#4510 / jasperfx#324.
[BoundaryAggregate]
public class SubscriptionState
{
    public int EnrollmentCount { get; set; }
    public int ProgressCount { get; set; }

    public void Apply(Enrolled _) => EnrollmentCount++;
    public void Apply(ProgressRecorded _) => ProgressCount++;
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_boundary_aggregate_fetch_for_writing_tests.cs#L22-L36' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_boundary_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Register it with `RegisterTagType<...>().ForAggregate<T>()` only — do **not** add a `LiveStreamAggregation<T>()` / `Snapshot<T>()` registration, since those require a stream identity. `AggregateByTagsAsync<T>` and `FetchForWritingByTags<T>` then work against the boundary aggregate.

::: warning
The `[BoundaryAggregate]` marker is required, and it is an explicit opt-in. Without it, an identity-less aggregate gets no source-generated dispatcher and `FetchForWritingByTags<T>` throws `InvalidProjectionException` ("No source-generated dispatcher found"). This is deliberate: a no-`Id` aggregate is far more often a forgotten identity than an intended boundary aggregate, so the marker distinguishes the two. The aggregate's assembly must reference the `JasperFx.Events.SourceGenerator` analyzer.
:::

## Fetch for Writing (Consistency Boundary)

`FetchForWritingByTags` loads the aggregate and establishes a consistency boundary. At `SaveChangesAsync` time, Marten checks whether any new events matching the query have been appended since the read, throwing `DcbConcurrencyException` if so:

<!-- snippet: sample_marten_dcb_fetch_for_writing_by_tags -->
<a id='snippet-sample_marten_dcb_fetch_for_writing_by_tags'></a>
```cs
// Fetch for writing
await using var session2 = theStore.LightweightSession();
var query = new EventTagQuery().Or<StudentId>(studentId);
var boundary = await session2.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

// Read current state
var aggregate = boundary.Aggregate; // may be null if no events yet
var lastSequence = boundary.LastSeenSequence;

// Append via boundary
var assignment = session2.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
assignment.WithTag(studentId, courseId);
boundary.AppendOne(assignment);

// Save -- will throw DcbConcurrencyException if another session
// appended matching events after our read
await session2.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L258-L276' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_fetch_for_writing_by_tags' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### Handling Concurrency Violations

<!-- snippet: sample_marten_dcb_handling_concurrency -->
<a id='snippet-sample_marten_dcb_handling_concurrency'></a>
```cs
try
{
    await session1.SaveChangesAsync();
}
catch (DcbConcurrencyException ex)
{
    // Reload and retry -- the boundary's tag query had new matching events
    // ex.Query -- the original tag query
    // ex.LastSeenSequence -- the sequence at time of read
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L313-L324' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_handling_concurrency' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: tip
The consistency check only detects events that match the **same tag query**. Events appended to unrelated tags or streams will not cause a violation.
:::

### How the boundary check serializes <Badge type="tip" text="9.4" />

::: warning Upgrading from 9.3 or earlier
Marten 9.4 added a new schema object — `mt_dcb_tag_version` — to fix [#4591](https://github.com/JasperFx/marten/issues/4591). Deployments with `AutoCreate.None` must run `db-patch` / `db-apply` before deploying 9.4. See [Migration Guide → 9.4 schema migration](/migration-guide#required-schema-migration-dcb-tag-version-side-table).
:::

Internally, `FetchForWritingByTags` records the captured version of every tag value referenced by the query in a side table (`mt_dcb_tag_version`, one row per `(tag_table, tag_value, tenant_id)`). At `SaveChangesAsync` time Marten emits an `INSERT … ON CONFLICT DO UPDATE … WHERE version = $captured RETURNING 1` for each captured row. The row-level lock plus the captured-version predicate is the serialization point: two truly-concurrent appenders observing the same captured version both attempt to bump the row — the first wins, the second's `RETURNING` matches no rows and surfaces `DcbConcurrencyException`. This works at PostgreSQL's default `READ COMMITTED` isolation; no `SERIALIZABLE`, no advisory locks.

Every save that appends a tagged event — boundary or otherwise — also queues a producer-side bump against the same row. That's what keeps a plain `session.Events.Append(streamId, taggedEvent)` from silently committing past an in-flight boundary fetch held by another session: the version moves on every commit, not only on boundary saves.

The side table grows with **distinct boundary-tag values**, not with event volume, and is never deleted automatically — the same `StudentId` or `CourseId` reuses its row across every save. Avoid using ephemeral or one-shot values as DCB tags if you want to keep the table compact.

## Checking Event Existence

If you only need to know whether any events matching a tag query exist -- without loading or deserializing them -- use `EventsExistAsync`. This is a lightweight `SELECT EXISTS(...)` query that avoids the overhead of fetching and materializing event data:

<!-- snippet: sample_marten_dcb_events_exist_async -->
<a id='snippet-sample_marten_dcb_events_exist_async'></a>
```cs
[Fact]
public async Task events_exist_returns_true_when_matching_events_found()
{
    var studentId = new StudentId(Guid.NewGuid());
    var courseId = new CourseId(Guid.NewGuid());
    var streamId = Guid.NewGuid();

    var enrolled = theSession.Events.BuildEvent(new StudentEnrolled("Alice", "Math"));
    enrolled.WithTag(studentId, courseId);
    theSession.Events.Append(streamId, enrolled);
    await theSession.SaveChangesAsync();

    // Check existence -- lightweight, no event loading
    var query = new EventTagQuery().Or<StudentId>(studentId);
    var exists = await theSession.Events.EventsExistAsync(query);
    exists.ShouldBeTrue();
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Dcb/dcb_tag_query_and_consistency_tests.cs#L520-L538' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_dcb_events_exist_async' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This is useful for guard clauses and validation logic in DCB workflows where you need to check preconditions before appending new events.

`EventsExistAsync` is also available in batch queries via `batch.Events.EventsExist(query)`.

## How It Works

### Storage Modes

DCB tags can be stored two different ways, controlled by `opts.Events.DcbStorageMode`. The default is `DcbStorageMode.TagTables` — the behavior shipped in Marten 8. Marten 9.0 adds `DcbStorageMode.HStore` as an opt-in alternative that stores all tags inline on the event row using PostgreSQL's [hstore](https://www.postgresql.org/docs/current/hstore.html) key-value type.

The mode is chosen **per database at creation time**. There is no in-place migration between modes — pick one before populating an event store and stick with it.

#### `DcbStorageMode.TagTables` (default)

Each registered tag type creates its own PostgreSQL table:

```sql
CREATE TABLE IF NOT EXISTS mt_event_tag_student (
    value uuid NOT NULL,
    seq_id bigint NOT NULL,
    CONSTRAINT pk_mt_event_tag_student PRIMARY KEY (value, seq_id),
    CONSTRAINT fk_mt_event_tag_student_events
        FOREIGN KEY (seq_id) REFERENCES mt_events(seq_id) ON DELETE CASCADE
);
```

DCB queries `LEFT JOIN` across each referenced tag table. Strengths: native column types preserve `Guid`/`int`/`string`/`long`/`short` semantics, and single-tag `EventsExistAsync` checks hit a small dedicated table via its primary-key index. Trade-offs: every distinct tag type adds a table, two indexes, and a foreign key; queries spanning N tag types pay N JOINs.

#### `DcbStorageMode.HStore` (opt-in)

Tags are stored inline on a new `mt_events.tags hstore` column, covered by a single GIN index that handles every registered tag type. The `hstore` extension is registered automatically as part of schema creation:

```csharp
opts.Events.DcbStorageMode = DcbStorageMode.HStore;
```

The resulting schema adds one column and one index instead of N per-type tables:

```sql
-- Single column on mt_events; tag-type suffix is the hstore key, value is text
ALTER TABLE mt_events ADD COLUMN tags hstore;
CREATE INDEX idx_mt_events_tags ON mt_events USING gin (tags);

-- Auto-registered as part of schema-create:
-- CREATE EXTENSION IF NOT EXISTS hstore;
```

DCB queries become single-table containment lookups using Postgres' `@>` operator — no JOINs. The same GIN index serves every tag type and every query shape (1 tag, N tags OR'd, with or without an event-type filter).

```sql
-- Single tag: e.tags @> hstore('student', 'STU-001')
-- Two tags OR: e.tags @> hstore('student', 'STU-001') OR e.tags @> hstore('course', 'CS-101')
```

Trade-offs:

- All tag values are stored as text — Npgsql automatically converts `Dictionary<string, string>` to `hstore` via `NpgsqlDbType.Hstore`, and `Guid`/`int`/`long`/`short` are stringified at the database boundary. Tag-type **registration** (`RegisterTagType<StudentId>("student")`) and **usage** (`event.WithTag(new StudentId(...))`) are unchanged — only the on-disk representation is different.
- The `hstore` extension must be installable on the target database. Most managed Postgres providers ship it; bare-metal installations may need `CREATE EXTENSION` privileges on first run.
- The `mt_quick_append_events` Postgres function does not take per-tag-type arrays — Marten writes the inline hstore as a follow-up `UPDATE` after the event INSERT.
- **Each tag type is single-valued per event.** An hstore is a map with unique keys, and Marten uses the registered tag's table-suffix as the key. If you call `AssignTagWhere` twice on the same event with two different values of the *same* tag type, the second value overwrites the first. The TagTables layout permits multiple values of the same tag type per event (the underlying table PK is `(value, seq_id)`); HStore does not. Cross-type merging (e.g. adding a `StudentId` tag to an event that already has a `RegionId` tag) works correctly in both modes — HStore uses Postgres' `hstore || hstore` concatenation to preserve the existing keys.

### Choosing a Storage Mode

The HStore mode trades native column types and small-table primary-key lookups for index-of-one and JOIN elimination. The right choice depends on your DCB query shape.

A reproducible side-by-side benchmark lives at `src/DcbLoadTest` and can be re-run with `dotnet run --project src/DcbLoadTest -c Release` against any Marten dev Postgres. Numbers below were measured against PostgreSQL 15 with 10,000 seeded tagged events and 200 iterations per scenario after a warmup pass:

| Scenario                              | TagTables (ms/op) | HStore (ms/op) | HStore vs TagTables |
| ------------------------------------- | ----------------: | -------------: | ------------------- |
| `append`, no tags                     |             0.157 |          0.155 | 1% faster           |
| `append`, 2 tags/event                |             0.194 |          0.137 | **29% faster**      |
| `QueryByTagsAsync`, 1 tag             |             0.622 |          0.601 | 3% faster           |
| `QueryByTagsAsync`, 2 tags OR         |            12.880 |          0.989 | **92% faster**      |
| `EventsExistAsync`, 1 tag             |             0.404 |          0.910 | 125% slower         |
| `EventsExistAsync`, 2 tags OR         |             3.164 |          0.962 | **70% faster**      |
| `FetchForWritingByTags + commit`      |             0.931 |          0.571 | **39% faster**      |

Guidance:

- **Prefer HStore** when your DCB queries match on **two or more tag types** (the common case — most projection boundaries combine an aggregate-id tag with one or more domain tags). The JOIN cost on TagTables grows with each additional tag type; HStore stays flat.
- **Prefer HStore** when your hot path is `FetchForWritingByTags` (consistency-boundary read-modify-write). The fetch round-trip drops because the events `SELECT` is a single-table lookup instead of an N-way JOIN.
- **Stay on TagTables** if your DCB workload is dominated by **single-tag `EventsExistAsync` probes**. That case is what the per-type tables are optimized for — a primary-key lookup on a small dedicated table — and HStore's GIN containment is slightly slower per probe.
- **Either mode is fine** for append throughput. With tags, HStore is about 30% faster than TagTables because it issues one `UPDATE` per tagged event instead of one `INSERT` per `(event, tag)` pair.

If you're starting a new event store on Marten 9.0 and most of your projections key off `(aggregateId, someOtherTag)`, HStore is the recommended choice. If you're upgrading from Marten 8 and already have a populated TagTables-mode store, there is no compelling reason to switch.

### Consistency Check <Badge type="tip" text="9.4" />

At `SaveChangesAsync` time, Marten emits a per-tag `UPDATE … WHERE version = $captured` against the `mt_dcb_tag_version` side table — one statement per distinct `(tag_table, tag_value)` tuple in the boundary query, in deterministic sort order. The row-level write lock plus the version predicate is the serialization point: two concurrent appenders capturing the same version both try to bump it; one wins, the other's `UPDATE` matches zero rows and surfaces `DcbConcurrencyException`. This works at PostgreSQL's default `READ COMMITTED` isolation — no advisory locks, no `SERIALIZABLE` transactions.

The check shape no longer depends on `DcbStorageMode`. Both `TagTables` and `HStore` share the same side-table mechanism for the consistency check; the storage mode only affects how tags are physically read at fetch time and written at append time.

### Tag Routing

Events appended via `IEventBoundary.AppendOne()` are automatically routed to streams based on their tags. Each tag value becomes the stream identity, so events with the same tag value end up in the same stream.
