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
4. **Stores the updated document in the session** so it will be persisted on the next `SaveChangesAsync()`
5. Returns the result

### Async Projections

Same behavior as inline: loads the stored document, applies pending events, stores the updated
document in the session.

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

// Save happens later — the inline document is already queued for storage
await session.SaveChangesAsync();
```

## When No Pending Events Exist

If there are no uncommitted events for the given stream in the session, `ProjectLatest` behaves
identically to `FetchLatest` — it returns the current committed state.

## Limitations

- **Natural key projections**: `ProjectLatest` with a natural key ID falls back to `FetchLatest`
  because the natural key mapping may not exist yet for uncommitted streams.
- **Read-only sessions**: `ProjectLatest` is only available on `IDocumentSession.Events`
  (not `IQuerySession.Events`) because it may store the updated document for inline projections.
