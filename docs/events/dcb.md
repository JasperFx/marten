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

Tags work with both **Rich** (default) and **Quick** append modes. In Rich mode, tags are inserted using pre-assigned sequence numbers. In Quick mode, tags are inserted using a subquery that looks up the sequence from the event's id.

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
