# ProjectLatest — Include Pending Events <Badge type="tip" text="8.x" />

`ProjectLatest<T>()` returns the projected state of an aggregate including any events that have been
appended in the current session but not yet committed. This eliminates the need for a forced
`SaveChangesAsync()` + `FetchLatest()` round-trip when you need the projected result immediately
after appending events.

## Motivation

A common pattern in command handlers looks like this:

```csharp
// Today's pattern: forced flush + re-read
session.Events.StartStream<Report>(id, new ReportCreated("Q1"));
await session.SaveChangesAsync(ct);  // forced flush
var report = await session.Events.FetchLatest<Report>(id, ct);  // re-read
return report;
```

With `ProjectLatest`, this becomes:

```csharp
// Better: project locally including pending events
session.Events.StartStream<Report>(id, new ReportCreated("Q1"));
var report = await session.Events.ProjectLatest<Report>(id, ct);
// SaveChangesAsync happens later (e.g., Wolverine AutoApplyTransactions)
return report;
```

## API

```csharp
// On IDocumentSession.Events (IEventStoreOperations)
ValueTask<T?> ProjectLatest<T>(Guid id, CancellationToken cancellation = default);
ValueTask<T?> ProjectLatest<T>(string id, CancellationToken cancellation = default);
```

## Behavior by Projection Lifecycle

### Live Projections

1. Fetches all committed events from the database and builds the aggregate
2. Finds any pending (uncommitted) events for that stream in the current session
3. Applies the pending events on top of the committed state
4. Returns the result (no storage — live projections are ephemeral)

### Inline Projections

1. Loads the pre-projected document from the database
2. Finds any pending events for that stream in the current session
3. Applies the pending events on top using the aggregate's Apply/Create methods
4. **Ejects the aggregate from the session identity map** so the inline projection can apply the
   same pending events cleanly during `SaveChangesAsync()` without double-applying them
5. Returns the result

### Async Projections

1. Loads the stored document, and any committed events the async daemon has not applied to it yet
2. Applies the pending events on top
3. Returns the result. Nothing is stored in the session: the async daemon updates the stored
   document from the committed events, as it would without the call

## Example

```csharp
public record ReportCreated(string Title);
public record SectionAdded(string SectionName);
public record ReportPublished;

public class Report
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public int SectionCount { get; set; }
    public bool IsPublished { get; set; }

    public static Report Create(ReportCreated e) => new Report { Title = e.Title };
    public void Apply(SectionAdded e) => SectionCount++;
    public void Apply(ReportPublished e) => IsPublished = true;
}

// In a command handler:
await using var session = store.LightweightSession();

session.Events.StartStream(streamId,
    new ReportCreated("Q1 Report"),
    new SectionAdded("Revenue"),
    new SectionAdded("Costs")
);

// Get the projected state WITHOUT saving first
var report = await session.Events.ProjectLatest<Report>(streamId);

// report.Title == "Q1 Report"
// report.SectionCount == 2
// report.IsPublished == false

// Save happens later. ProjectLatest stored nothing, so an Inline or Async
// projection writes the document from the committed events as usual
await session.SaveChangesAsync();
```

## When No Pending Events Exist

If there are no uncommitted events for the given stream in the session, `ProjectLatest` behaves
identically to `FetchLatest` — it returns the current committed state.

## Limitations

- **Natural key projections**: `ProjectLatest` with a natural key ID falls back to `FetchLatest`
  because the natural key mapping may not exist yet for uncommitted streams.
- **Read-only sessions**: `ProjectLatest` is only available on `IDocumentSession.Events`
  (not `IQuerySession.Events`) because only a document session has pending events to project.

## ProjectLatestForCommit — The Aggregate as the Commit Writes It

Use `ProjectLatestForCommit` when something written or returned before the commit has to match what a
read after the commit returns. Two common cases:

- A document or outgoing message stored in the same transaction as the events, which carries the
  aggregate's version or last-modified time.
- An API response built before the commit, whose version the client sends back as the expected version
  of its next command.

An aggregate that takes those values from its events gets version 0 and no timestamp from `ProjectLatest`
in both cases. Committing first and then calling `FetchLatest` returns the right values, but it costs
another round trip, and it is too late for anything that has to be part of the same transaction. If the
aggregate doesn't read event metadata, `ProjectLatest` returns the same aggregate with fewer restrictions,
so use that.

`ProjectLatest` folds the pending events as they stand before `SaveChangesAsync()`, when their version,
timestamp and metadata are not assigned yet. An aggregate that reads `IEvent.Version`, `IEvent.Timestamp`,
the user name, the correlation id or the causation id therefore differs from the one the commit stores.

`ProjectLatestForCommit<T>()` assigns those values to the stream's pending events at the call, the same way
`SaveChangesAsync()` does, and the commit keeps them. It returns the aggregate folded from those events.
Nothing is stored.

```csharp
// On IDocumentSession.Events (IEventStoreOperations)
ValueTask<T?> ProjectLatestForCommit<T>(Guid id, CancellationToken cancellation = default);
ValueTask<T?> ProjectLatestForCommit<T>(string id, CancellationToken cancellation = default);
```

An aggregate that copies event metadata:

<!-- snippet: sample_project_latest_for_commit_aggregate -->
<a id='snippet-sample_project_latest_for_commit_aggregate'></a>
```cs
public record ShipmentStarted(string Origin);

public record ShipmentScanned(string Location);

// Copies the metadata of the last event it applied
public abstract record TrackedShipment
{
    public int Scans { get; set; }
    public long LastEventVersion { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public string? LastModifiedBy { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? TenantId { get; set; }

    protected void track(IEvent e)
    {
        LastEventVersion = e.Version;
        LastModified = e.Timestamp;
        LastModifiedBy = e.UserName;
        CorrelationId = e.CorrelationId;
        CausationId = e.CausationId;
        TenantId = e.TenantId;
    }
}

public record Shipment: TrackedShipment
{
    public Guid Id { get; set; }

    public static Shipment Create(IEvent<ShipmentStarted> e)
    {
        var shipment = new Shipment();
        shipment.track(e);
        return shipment;
    }

    public void Apply(IEvent<ShipmentScanned> e)
    {
        Scans++;
        track(e);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/project_latest_for_commit_tests.cs#L23-L69' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_project_latest_for_commit_aggregate' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Using it:

<!-- snippet: sample_project_latest_for_commit -->
<a id='snippet-sample_project_latest_for_commit'></a>
```cs
var stream = await session.Events.FetchForWriting<Shipment>(shipmentId);
stream.AppendOne(new ShipmentScanned("Leeds"));

// The pending event's version, timestamp, user name, correlation id and causation id are assigned now,
// and SaveChangesAsync() keeps them
var shipment = await session.Events.ProjectLatestForCommit<Shipment>(shipmentId);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/EventSourcingTests/Projections/project_latest_for_commit_tests.cs#L345-L354' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_project_latest_for_commit' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

### What It Guarantees

When the session commits, the returned aggregate equals the aggregate built from the stored events, and the
one `FetchLatest` returns after the commit. This covers:

- the event versions
- the event timestamps, at the precision PostgreSQL stores (whole microseconds)
- the user name, correlation id and causation id the session holds at the call, and any value set on a pending
  event directly
- the tenant id, including for a `ForTenant()` session, whose events take their metadata from the session
  that calls `SaveChangesAsync()`

When the stream has no pending events in the session, it behaves as `ProjectLatest`.

### When It Throws

`ProjectLatestForCommit` throws an `InvalidOperationException`, and leaves the pending events unchanged, when:

- the store uses `EventAppendMode.Quick`, where the database assigns each event's timestamp at commit. Use
  `EventAppendMode.QuickWithServerTimestamps` (the default) or `EventAppendMode.Rich`.
- the stream's version is not known before the commit, as after `Append()` with no expected version. Use
  `FetchForWriting()`, `StartStream()`, or `Append()` with an expected version.
- it is given an aggregate's natural key, which cannot be resolved to the stream's pending events before the
  commit. Pass the stream's id.

Under `EventAppendMode.Rich` it can also throw the version conflict the commit would throw, after it has
assigned the pending events their versions, as for `StartStream()` followed by `FetchForWriting()` on an id that
already exists.

### Limits

- `IEvent.Sequence` is assigned by the database at commit, so it is 0 in the answer.
- The answer covers the pending events as of the call. An event appended later is not in it, and the commit
  stamps that event with its own time. Call again to include it.
- The timestamp is the time of the call, not of the commit.
- A user name, correlation id or causation id set on the session after the call does not replace the value
  assigned at the call. One that is not set on the session at the call is filled in by the commit, so the
  answer has no value where the stored event has one. Session headers always take their values at commit.
- Changing the stream's expected version after the call, by fetching it for writing again or appending with a
  new expected version, makes the answer wrong.
- The answer is folded onto the same starting state `ProjectLatest` uses: the stored snapshot or the committed
  events.
- For a global aggregate (`AddGlobalProjection`), each event's tenant id in the answer is the one the
  projection sees at commit. The events are stored under the default tenant, so read back they carry that
  tenant instead.
