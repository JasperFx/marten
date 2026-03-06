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

```cs
var store = DocumentStore.For(opts =>
{
    opts.Connection("connection-string");

    // Register tag types with explicit table suffixes
    opts.Events.RegisterTagType<StudentId>("student");
    opts.Events.RegisterTagType<CourseId>("course");
});
```

Each tag type gets its own table (`mt_event_tag_student`, `mt_event_tag_course`, etc.) with a composite primary key of `(value, seq_id)`.

### Tag Type Requirements

Tag types should be simple wrapper records around a primitive value:

```cs
public record StudentId(Guid Value);
public record CourseId(Guid Value);
public record TenantCode(string Value);
public record OrderNumber(int Value);
```

Supported inner value types: `Guid`, `string`, `int`, `long`, `short`.

Tags work with both **Rich** (default) and **Quick** append modes. In Rich mode, tags are inserted using pre-assigned sequence numbers. In Quick mode, tags are inserted using a subquery that looks up the sequence from the event's id.

## Tagging Events

Use `BuildEvent` and `WithTag` to attach tags before appending:

```cs
await using var session = store.LightweightSession();

var enrolled = session.Events.BuildEvent(new StudentEnrolled("Alice", "Math 101"));
enrolled.WithTag(new StudentId(aliceId), new CourseId(mathId));

session.Events.Append(streamId, enrolled);
await session.SaveChangesAsync();
```

Events can have multiple tags of different types. Tags are persisted to their respective tag tables in the same transaction as the event.

## Querying Events by Tags

Use `EventTagQuery` to build a query, then execute it with `QueryByTagsAsync`:

```cs
await using var session = store.LightweightSession();

// Query by a single tag
var query = new EventTagQuery()
    .Or<StudentId>(new StudentId(aliceId));

var events = await session.Events.QueryByTagsAsync(query);
```

### Multiple Tags (OR)

```cs
// Find events tagged with EITHER student
var query = new EventTagQuery()
    .Or<StudentId>(new StudentId(aliceId))
    .Or<StudentId>(new StudentId(bobId));

var events = await session.Events.QueryByTagsAsync(query);
```

### Filtering by Event Type

```cs
// Only AssignmentSubmitted events for this student
var query = new EventTagQuery()
    .Or<AssignmentSubmitted, StudentId>(new StudentId(aliceId));

var events = await session.Events.QueryByTagsAsync(query);
```

Events are always returned ordered by sequence number (global append order).

## Aggregating by Tags

Build an aggregate from tagged events, similar to `AggregateStreamAsync` but across streams:

```cs
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
}

// Aggregate all events for this student + course
var query = new EventTagQuery()
    .Or<StudentId>(studentId)
    .Or<CourseId>(courseId);

var enrollment = await session.Events.AggregateByTagsAsync<StudentCourseEnrollment>(query);
```

Returns `null` if no matching events are found.

## Fetch for Writing (Consistency Boundary)

`FetchForWritingByTags` loads the aggregate and establishes a consistency boundary. At `SaveChangesAsync` time, Marten checks whether any new events matching the query have been appended since the read, throwing `DcbConcurrencyException` if so:

```cs
await using var session = store.LightweightSession();

var query = new EventTagQuery()
    .Or<StudentId>(studentId);

var boundary = await session.Events.FetchForWritingByTags<StudentCourseEnrollment>(query);

// Read current state
var aggregate = boundary.Aggregate; // may be null if no events yet
var lastSequence = boundary.LastSeenSequence;

// Append new events through the boundary
var submitted = session.Events.BuildEvent(new AssignmentSubmitted("HW1", 95));
submitted.WithTag(studentId, courseId);
boundary.AppendOne(submitted);

// Save -- will throw DcbConcurrencyException if another session
// appended matching events after our read
await session.SaveChangesAsync();
```

### Handling Concurrency Violations

```cs
try
{
    await session.SaveChangesAsync();
}
catch (DcbConcurrencyException ex)
{
    // Reload and retry -- the boundary's tag query had new matching events
    // ex.Query -- the original tag query
    // ex.LastSeenSequence -- the sequence at time of read
}
```

::: tip
The consistency check only detects events that match the **same tag query**. Events appended to unrelated tags or streams will not cause a violation.
:::

## How It Works

### Storage

Each registered tag type creates a PostgreSQL table:

```sql
CREATE TABLE IF NOT EXISTS mt_event_tag_student (
    value uuid NOT NULL,
    seq_id bigint NOT NULL,
    CONSTRAINT pk_mt_event_tag_student PRIMARY KEY (value, seq_id),
    CONSTRAINT fk_mt_event_tag_student_events
        FOREIGN KEY (seq_id) REFERENCES mt_events(seq_id) ON DELETE CASCADE
);
```

### Consistency Check

At `SaveChangesAsync` time, Marten executes an `EXISTS` query checking for new events matching the tag query with `seq_id > lastSeenSequence`. This runs in the same transaction as the event appends, providing serializable consistency for the tagged boundary.

### Tag Routing

Events appended via `IEventBoundary.AppendOne()` are automatically routed to streams based on their tags. Each tag value becomes the stream identity, so events with the same tag value end up in the same stream.
