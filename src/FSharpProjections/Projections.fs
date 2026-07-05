namespace FSharpProjections

open System
open System.Threading
open System.Threading.Tasks
open JasperFx.Events
open Marten
open Marten.Events.Aggregation
open Marten.Events.Projections

// ---------------------------------------------------------------------------
// #4796 — F# projection authoring.
//
// F# supports neither partial classes nor Roslyn source generators, so the
// conventional Apply/Create convention methods (which are dispatched by the
// compile-time JasperFx.Events source generator) are unavailable. Instead, F#
// projections inherit from SingleStreamProjection / MultiStreamProjection /
// EventProjection and OVERRIDE the explicit Evolve / EvolveAsync / ApplyAsync
// methods. Pattern matching over the event data makes this idiomatic in F#.
// ---------------------------------------------------------------------------

// Events
type AccountCredited = { Amount: decimal }
type AccountDebited = { Amount: decimal }

type OrderPlaced = { Quantity: int }
type OrderShipped = { Carrier: string }

type GuestArrived = { Location: string }
type GuestDeparted = { Location: string }

type SaleRecorded = { Region: string; Amount: decimal }

type EmailChanged = { UserId: Guid; Email: string }

// Aggregates / documents (immutable F# records)
type Account = { Id: Guid; Balance: decimal }
type OrderSummary = { Id: Guid; ItemCount: int; Shipped: bool }
type LocationOccupancy = { Id: string; Guests: int }
type RegionRevenue = { Id: string; Total: decimal }
type UserEmail = { Id: Guid; Email: string }

[<AutoOpen>]
module internal NullHelpers =
    /// Returns the current snapshot, or the fallback when the snapshot is null
    /// (the first event for a stream/slice arrives with a null snapshot).
    let inline orDefault (fallback: 'T) (snapshot: 'T) : 'T =
        if isNull (box snapshot) then fallback else snapshot

// begin-snippet: sample_fsharp_self_aggregating_projection
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
// end-snippet

// begin-snippet: sample_fsharp_single_stream_async_projection
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
// end-snippet

// begin-snippet: sample_fsharp_multi_stream_projection
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
// end-snippet

// begin-snippet: sample_fsharp_multi_stream_async_projection
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
// end-snippet

// begin-snippet: sample_fsharp_event_projection
/// Event projection overriding ApplyAsync directly.
type UserEmailProjection() =
    inherit EventProjection()

    override _.ApplyAsync(operations: IDocumentOperations, e: IEvent, _ct: CancellationToken) : ValueTask =
        match e.Data with
        | :? EmailChanged as changed ->
            operations.Store<UserEmail>({ Id = changed.UserId; Email = changed.Email })
            ValueTask.CompletedTask
        | _ -> ValueTask.CompletedTask
// end-snippet
