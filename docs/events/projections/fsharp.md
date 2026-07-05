# F# Projections

Marten is usable from F#, including for event sourcing projections — but F# projections have to be
authored a little differently than their C# counterparts.

## Why F# projections are different

Starting with Marten 9.0, the runtime Roslyn code generation used in earlier versions was removed.
Projection dispatch now happens one of two ways:

1. The **conventional** `Apply` / `Create` / `ShouldDelete` methods are wired up at compile time by the
   `JasperFx.Events` **Roslyn source generator**. Projection subclasses that use these convention methods
   must be declared `partial` so the generator can emit the dispatcher into the other half of the class.
2. The **explicit** `Evolve` / `EvolveAsync` (aggregations) and `ApplyAsync` (event projections) methods
   are plain `virtual` methods that you `override`. No source generation is involved.

F# supports **neither `partial` classes nor Roslyn source generators**, so the conventional
`Apply`/`Create` path is unavailable. F# projections must therefore inherit from
`SingleStreamProjection<TDoc, TId>`, `MultiStreamProjection<TDoc, TId>`, or `EventProjection` and
**override the explicit `Evolve` / `EvolveAsync` / `ApplyAsync` methods**. This turns out to be a good fit
for F#, since these methods lend themselves to idiomatic pattern matching over the event data.

::: tip
The same limitation applies to "self-aggregating" snapshot types (a document type that carries its own
`Apply`/`Create`/`Evolve` methods and is registered with `Projections.Snapshot<T>()`). Those rely on the
source generator, so for F# you should instead write a `SingleStreamProjection<TDoc, TId>` subclass that
overrides `Evolve`, as shown below.
:::

All of the examples below live in the `FSharpProjections` project in the Marten repository and are
exercised by the `EventSourcingTests` suite so that F# projection authoring stays a first-class,
regression-tested citizen.

## Single stream aggregation (synchronous `Evolve`)

A single stream projection aggregates the events of one stream into one document. Override the synchronous
`Evolve` method and return the new snapshot. The incoming `snapshot` is `null` for the first event of a
stream, so provide a default:

<!-- snippet: sample_fsharp_self_aggregating_projection -->
<a id='snippet-sample_fsharp_self_aggregating_projection'></a>
```fs
/// Self-aggregating single stream projection using the synchronous Evolve path.
/// This is the F# replacement for a conventional self-aggregating snapshot type.
type AccountProjection() =
    inherit SingleStreamProjection<Account, Guid>()

    override _.Evolve(snapshot: Account, id: Guid, e: IEvent) : Account =
        let current = snapshot |> orDefault { Id = id; Balance = 0m }
        match e.Data with
        | :? AccountCredited as credited -> { current with Balance = current.Balance + credited.Amount }
        | :? AccountDebited as debited -> { current with Balance = current.Balance - debited.Amount }
        | _ -> current
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/FSharpProjections/Projections.fs#L50-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fsharp_self_aggregating_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Single stream aggregation (asynchronous `EvolveAsync`)

Override `EvolveAsync` when you need asynchronous work while aggregating — for example, looking up
reference data through the supplied `IQuerySession`:

<!-- snippet: sample_fsharp_single_stream_async_projection -->
<a id='snippet-sample_fsharp_single_stream_async_projection'></a>
```fs
/// Single stream projection using the asynchronous EvolveAsync path. The
/// IQuerySession is available for reference data lookups if needed.
type OrderSummaryProjection() =
    inherit SingleStreamProjection<OrderSummary, Guid>()

    override _.EvolveAsync
        (snapshot: OrderSummary, id: Guid, _session: IQuerySession, e: IEvent, _ct: CancellationToken)
        : ValueTask<OrderSummary> =
        let current = snapshot |> orDefault { Id = id; ItemCount = 0; Shipped = false }
        let updated =
            match e.Data with
            | :? OrderPlaced as placed -> { current with ItemCount = current.ItemCount + placed.Quantity }
            | :? OrderShipped -> { current with Shipped = true }
            | _ -> current
        ValueTask<OrderSummary>(updated)
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/FSharpProjections/Projections.fs#L64-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fsharp_single_stream_async_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Multi stream aggregation

A multi stream projection aggregates events from *many* streams into a single document, grouped by a
document identity. Register the grouping in the constructor with `Identity<TEvent>(...)` (or
`Identities<TEvent>(...)` when one event fans out to several documents), then override `Evolve`:

<!-- snippet: sample_fsharp_multi_stream_projection -->
<a id='snippet-sample_fsharp_multi_stream_projection'></a>
```fs
/// Multi stream projection using the synchronous Evolve path. Events from many
/// streams are grouped by a document identity via Identity<TEvent>(...).
type LocationOccupancyProjection() as self =
    inherit MultiStreamProjection<LocationOccupancy, string>()

    do
        self.Identity<GuestArrived>(fun e -> e.Location)
        self.Identity<GuestDeparted>(fun e -> e.Location)

    override _.Evolve(snapshot: LocationOccupancy, id: string, e: IEvent) : LocationOccupancy =
        let current = snapshot |> orDefault { Id = id; Guests = 0 }
        match e.Data with
        | :? GuestArrived -> { current with Guests = current.Guests + 1 }
        | :? GuestDeparted -> { current with Guests = current.Guests - 1 }
        | _ -> current
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/FSharpProjections/Projections.fs#L82-L98' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fsharp_multi_stream_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The asynchronous `EvolveAsync` path is available for multi stream projections too:

<!-- snippet: sample_fsharp_multi_stream_async_projection -->
<a id='snippet-sample_fsharp_multi_stream_async_projection'></a>
```fs
/// Multi stream projection using the asynchronous EvolveAsync path.
type RegionRevenueProjection() as self =
    inherit MultiStreamProjection<RegionRevenue, string>()

    do self.Identity<SaleRecorded>(fun e -> e.Region)

    override _.EvolveAsync
        (snapshot: RegionRevenue, id: string, _session: IQuerySession, e: IEvent, _ct: CancellationToken)
        : ValueTask<RegionRevenue> =
        let current = snapshot |> orDefault { Id = id; Total = 0m }
        let updated =
            match e.Data with
            | :? SaleRecorded as sale -> { current with Total = current.Total + sale.Amount }
            | _ -> current
        ValueTask<RegionRevenue>(updated)
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/FSharpProjections/Projections.fs#L100-L116' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fsharp_multi_stream_async_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Event projections (`ApplyAsync`)

An `EventProjection` does not aggregate into a single document — it reacts to each event and writes
whatever documents it likes through the supplied `IDocumentOperations`. Override `ApplyAsync`:

<!-- snippet: sample_fsharp_event_projection -->
<a id='snippet-sample_fsharp_event_projection'></a>
```fs
/// Event projection overriding ApplyAsync directly.
type UserEmailProjection() =
    inherit EventProjection()

    override _.ApplyAsync(operations: IDocumentOperations, e: IEvent, _ct: CancellationToken) : ValueTask =
        match e.Data with
        | :? EmailChanged as changed ->
            operations.Store<UserEmail>({ Id = changed.UserId; Email = changed.Email })
            ValueTask.CompletedTask
        | _ -> ValueTask.CompletedTask
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/FSharpProjections/Projections.fs#L118-L129' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_fsharp_event_projection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Registering the projections

F# projections are registered exactly like C# projections. From C#:

```csharp
using var store = DocumentStore.For(opts =>
{
    opts.Connection(connectionString);

    opts.Projections.Add(new FSharpProjections.AccountProjection(), ProjectionLifecycle.Inline);
    opts.Projections.Add(new FSharpProjections.LocationOccupancyProjection(), ProjectionLifecycle.Async);
    opts.Projections.Add(new FSharpProjections.UserEmailProjection(), ProjectionLifecycle.Inline);
});
```

Or from F#:

```fsharp
let store =
    DocumentStore.For(fun opts ->
        opts.Connection(connectionString)

        opts.Projections.Add(AccountProjection(), ProjectionLifecycle.Inline)
        opts.Projections.Add(LocationOccupancyProjection(), ProjectionLifecycle.Async)
        opts.Projections.Add(UserEmailProjection(), ProjectionLifecycle.Inline))
```

::: tip
Returning `null` from an aggregation's `Evolve`/`EvolveAsync` deletes the document (when one previously
existed). Because F# records are non-nullable by default, handle the incoming `null` snapshot explicitly —
the samples above use a small `orDefault` helper to substitute a fresh record for the first event.
:::
