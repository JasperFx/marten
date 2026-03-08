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
<!-- endSnippet -->

Each tag type gets its own table (`mt_event_tag_student`, `mt_event_tag_course`, etc.) with a composite primary key of `(value, seq_id)`.

### Tag Type Requirements

Tag types should be simple wrapper records around a primitive value:

<!-- snippet: sample_marten_dcb_tag_type_definitions -->
<!-- endSnippet -->

Supported inner value types: `Guid`, `string`, `int`, `long`, `short`.

Tags work with both **Rich** (default) and **Quick** append modes. In Rich mode, tags are inserted using pre-assigned sequence numbers. In Quick mode, tags are inserted using a subquery that looks up the sequence from the event's id.

## Tagging Events

Use `BuildEvent` and `WithTag` to attach tags before appending:

<!-- snippet: sample_marten_dcb_tagging_events -->
<!-- endSnippet -->

Events can have multiple tags of different types. Tags are persisted to their respective tag tables in the same transaction as the event.

## Querying Events by Tags

Use `EventTagQuery` to build a query, then execute it with `QueryByTagsAsync`:

<!-- snippet: sample_marten_dcb_query_by_single_tag -->
<!-- endSnippet -->

### Multiple Tags (OR)

<!-- snippet: sample_marten_dcb_query_multiple_tags_or -->
<!-- endSnippet -->

### Filtering by Event Type

<!-- snippet: sample_marten_dcb_query_by_event_type -->
<!-- endSnippet -->

Events are always returned ordered by sequence number (global append order).

## Aggregating by Tags

Build an aggregate from tagged events, similar to `AggregateStreamAsync` but across streams. First define an aggregate that applies the tagged events:

<!-- snippet: sample_marten_dcb_aggregate -->
<!-- endSnippet -->

Then aggregate across streams by tag query:

<!-- snippet: sample_marten_dcb_aggregate_by_tags -->
<!-- endSnippet -->

Returns `null` if no matching events are found.

## Fetch for Writing (Consistency Boundary)

`FetchForWritingByTags` loads the aggregate and establishes a consistency boundary. At `SaveChangesAsync` time, Marten checks whether any new events matching the query have been appended since the read, throwing `DcbConcurrencyException` if so:

<!-- snippet: sample_marten_dcb_fetch_for_writing_by_tags -->
<!-- endSnippet -->

### Handling Concurrency Violations

<!-- snippet: sample_marten_dcb_handling_concurrency -->
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
