// AOT smoke test (marten#4349 / JasperFx/jasperfx#213) plus
// dispatcher-coverage regression guard (marten#4486 / marten#4471
// projection audit).
//
// This program touches a representative cross-section of the AOT-clean
// Marten surface for a Static-TypeLoadMode consumer. The csproj sets
// IsAotCompatible=true, TrimMode=full, and promotes the AOT analyzer warning
// codes to errors, so any change that adds [RequiresDynamicCode] /
// [RequiresUnreferencedCode] to an API exercised here — or any change to
// this file that calls into a reflective Marten surface — fails the build
// in CI.
//
// What's exercised:
//   - services.AddMarten(opts => { ... }) — the DI-facing entry point.
//   - One document type with an Id property — exercises the closed-shape
//     IDocumentStorage / IIdentification path (post-#4404, no Roslyn emit).
//   - Four projection shapes covering both JasperFx.Events.SourceGenerator
//     emission patterns × Apply-vs-Evolve method conventions:
//
//        Pattern A — projection subclass; SG emits a partial declaration
//          merged into the user's class.
//             SmokeProjection            — explicit Evolve override (deliberate SG bypass)
//             ApplyProjection            — conventional Apply method (SG dispatches)
//
//        Pattern B — self-aggregating aggregate; SG emits a sibling
//          XEvolver class plus [assembly: GeneratedEvolver(typeof(X),
//          typeof(XEvolver))] registration. Aggregate type does NOT need
//          to be partial.
//             SelfAggregatingApply       — conventional Apply method on the aggregate
//             SelfAggregatingEvolve      — explicit Evolve method on the aggregate
//
//     ProjectionOptions.SingleStreamProjection<T>() calls
//     source.AssembleAndAssertValidity() at registration time. Post-#276
//     that throws InvalidProjectionException whenever the SG didn't emit
//     a dispatcher for the shape — so a regression in the SG's discovery
//     rules trips at `dotnet run` here and the program exits non-zero.
//     (The build itself doesn't catch a missing emission — the SG silently
//     skips when its preconditions don't hold; the runtime check is the
//     sentinel.)
//   - Host.CreateApplicationBuilder + Build + DI resolution of
//     IDocumentStore — the boot path consumers actually hit.
//
// What's deliberately NOT exercised:
//   - Connecting / querying / persisting anything — this is a build-time
//     analyzer test, not a runtime test. The connection string is a
//     placeholder; the host is built but never actually opens a connection.
//   - Compiled queries — Marten.SourceGenerator covers that surface and has
//     its own analyzer-level tests in Marten.SourceGenerator.Tests.
//   - JasperFx.RuntimeCompiler / services.AddRuntimeCompilation() — Marten 9
//     retired that path (#4454 / #4461). This smoke deliberately does not
//     pull it back in.
//   - Exhaustive enumeration of every projection shape in the test
//     libraries — the static inventory at audit/projections-inventory.md
//     covers that. This program is the regression sentinel.

using JasperFx.Events;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Aggregation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

const string placeholderConnectionString =
    "Host=localhost;Port=5432;Database=marten_aot_smoke;Username=postgres;password=postgres";

var builder = Host.CreateApplicationBuilder();

builder.Services.AddMarten(opts =>
{
    opts.Connection(placeholderConnectionString);

    // Marten 9 retired the runtime-codegen path entirely — there's nothing
    // to pin here for AOT consumers, the closed-shape adapter is always used.

    // Exercise the document-mapping surface — registers the closed-shape
    // IDocumentStorage path (#4404). DocumentMapping infers the Id member
    // and the Id type from SmokeDoc.Id reflectively at registration time,
    // which is part of Marten's [DynamicallyAccessedMembers]-annotated
    // contract; the AOT analyzer is happy when the type is fed as
    // typeof(T) literal as it is here.
    opts.Schema.For<SmokeDoc>();

    // Exercise the event-registration surface — RegisterEventType is the
    // explicit alias-registration path the JasperFx.Events source generator
    // also feeds at module-init time. Done explicitly here so the smoke
    // test doesn't rely on assembly scanning.
    opts.Events.AddEventType<SmokeEvent>();

    // Exercise the projection-registration surface — Projections.Add<T>
    // closes over the user's SingleStreamProjection<,> type, which the
    // JasperFx.Events.SourceGenerator handles via [GeneratedEvolver]
    // discovery (DocumentStore.cs calls DiscoverGeneratedEvolvers at
    // startup post-#4454). Inline lifecycle keeps the smoke test free of
    // the async-daemon surface, which has its own AOT story.
    //
    // Each Add<>/Snapshot<> call below routes through
    // ProjectionOptions.SingleStreamProjection<T>(), which calls
    // source.AssembleAndAssertValidity() and throws post-#276 if the SG
    // didn't emit a dispatcher for the shape. Build failure here is the
    // regression-guard signal (marten#4486).

    // Pattern A — projection subclass with deliberate Evolve override
    // (the user takes responsibility for dispatch; no SG emission expected).
    opts.Projections.Add<SmokeProjection>(ProjectionLifecycle.Inline);

    // Pattern A — projection subclass with conventional Apply method
    // (SG dispatches via partial-class merge).
    opts.Projections.Add<ApplyProjection>(ProjectionLifecycle.Inline);

    // Pattern B — self-aggregating aggregate with conventional Apply
    // method on the aggregate type itself. SG dispatches via a sibling
    // SelfAggregatingApplyEvolver class plus assembly attribute.
    opts.Projections.Snapshot<SelfAggregatingApply>(SnapshotLifecycle.Inline);

    // Pattern B — self-aggregating aggregate with explicit Evolve method
    // on the aggregate type itself (still SG-dispatched because the
    // emission targets the aggregate's Evolve signature directly).
    opts.Projections.Snapshot<SelfAggregatingEvolve>(SnapshotLifecycle.Inline);
});

using var host = builder.Build();
var store = host.Services.GetRequiredService<IDocumentStore>();

if (store is null)
{
    Console.Error.WriteLine("DI resolved a null IDocumentStore — regression in AddMarten.");
    return 1;
}

Console.WriteLine("Marten AOT smoke OK — AddMarten + Schema.For + AddEventType + Projections.Add + DI resolve clean.");
return 0;


public sealed class SmokeDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed record SmokeEvent(int Delta);

public sealed class SmokeProjection : SingleStreamProjection<SmokeDoc, Guid>
{
    public override SmokeDoc Evolve(SmokeDoc? snapshot, Guid id, IEvent e)
    {
        snapshot ??= new SmokeDoc { Id = id };
        if (e.Data is SmokeEvent inc) snapshot.Count += inc.Delta;
        return snapshot;
    }
}

// Pattern A — projection subclass with conventional Apply method. The SG
// emits a partial declaration containing an Evolve override that dispatches
// `e.Data` against Apply(SmokeEvent, ApplyDoc). Source MUST be partial; if
// it isn't, the SG silently skips emission (no CS0260 — the SG checks for
// partial first) and the runtime fail-fast in
// JasperFxAggregationProjectionBase.AssembleAndAssertValidity() throws
// InvalidProjectionException at Projections.Add<>() time. The program's
// non-zero exit is therefore the regression-guard signal — the build alone
// does not catch this.
public sealed partial class ApplyDoc
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

public sealed partial class ApplyProjection : SingleStreamProjection<ApplyDoc, Guid>
{
    public void Apply(SmokeEvent ev, ApplyDoc snapshot) => snapshot.Count += ev.Delta;
}

// Pattern B — self-aggregating aggregate with conventional Apply method
// directly on the aggregate type. The SG emits a sibling
// SelfAggregatingApplyEvolver class plus an
// [assembly: GeneratedEvolver(typeof(SelfAggregatingApply),
// typeof(SelfAggregatingApplyEvolver))] registration. The aggregate type
// does NOT need to be partial — the dispatcher is independent of the
// source declaration.
public sealed class SelfAggregatingApply
{
    public Guid Id { get; set; }
    public int Count { get; set; }
    public void Apply(SmokeEvent ev) => Count += ev.Delta;
}

// Pattern B — self-aggregating aggregate with explicit Evolve method
// directly on the aggregate. SG emission still applies; the Evolve method
// is the dispatch entry point the emitted XEvolver class calls.
public sealed class SelfAggregatingEvolve
{
    public Guid Id { get; set; }
    public int Count { get; set; }

    public SelfAggregatingEvolve Evolve(IEvent e)
    {
        if (e.Data is SmokeEvent inc) Count += inc.Delta;
        return this;
    }
}
