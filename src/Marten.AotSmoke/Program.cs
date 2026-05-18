// AOT smoke test (marten#4349 / JasperFx/jasperfx#213).
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
//   - One event type + one SingleStreamProjection<,> registration — the
//     primary projection-apply surface that Marten 9 routes through
//     JasperFx.Events.SourceGenerator's [GeneratedEvolver] discovery
//     (Options.Projections.DiscoverGeneratedEvolvers is called at startup
//     in DocumentStore.cs).
//   - StoreOptions.GeneratedCodeMode = TypeLoadMode.Static — pins the
//     consumer profile that AOT-publishing apps would set.
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

using JasperFx.CodeGeneration;
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

    // Pin the consumer profile an AOT-publishing app would set. Marten 9
    // retired the runtime-codegen path entirely, so this setting is a
    // documented no-op kept for source-compatibility — exercising it here
    // pins that compile-time contract.
    opts.GeneratedCodeMode = TypeLoadMode.Static;

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
    opts.Projections.Add<SmokeProjection>(ProjectionLifecycle.Inline);
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
