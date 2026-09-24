# Composite Configuration Across Satellite Assemblies

Marten supports a "modular monolith" deployment shape where projection types, event types, and `StoreOptions` tweaks live in **satellite assemblies** owned by individual feature teams, and a main host composes them together via dependency injection. Each satellite contributes its own `IConfigureMarten` (sync) or `IAsyncConfigureMarten` (async) implementation; the main host's `AddMarten(...)` call carries only shared infrastructure (connection string, default serializer, etc.).

This page documents the contracts that compose those satellites into a single `DocumentStore`.

## The pattern

Each satellite assembly:

1. Carries `[assembly: JasperFx.JasperFxAssembly]` in an `AssemblyInfo.cs` file.
2. Declares its projection classes as `partial`.
3. Runs the `JasperFx.Events.SourceGenerator` analyzer, so `[GeneratedEvolver]` attributes are emitted at compile time for the satellite's own projection types. A plain `<PackageReference Include="Marten" />` is normally enough: [#4557](https://github.com/JasperFx/marten/issues/4557) bundles the analyzer inside the Marten package itself, and it reaches satellites that pick Marten up transitively through a `ProjectReference` as well as ones that reference the package directly.

   Wire the analyzer in explicitly only when the satellite has no Marten reference to inherit it from — see [When the analyzer does not reach a satellite](#when-the-analyzer-does-not-reach-a-satellite) below:

   ```xml
   <PackageReference Include="JasperFx.Events.SourceGenerator"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```

4. Exposes one or more `IConfigureMarten` / `IAsyncConfigureMarten` implementations that register the satellite's projections, event types, or option tweaks.

The main host:

```csharp
var builder = Host.CreateApplicationBuilder();

// Each satellite's IConfigureMarten / IAsyncConfigureMarten gets wired into DI.
builder.Services.AddSingleton<IConfigureMarten, OrdersConfig>();           // SatelliteA
builder.Services.AddSingleton<IAsyncConfigureMarten, ReportingConfig>();   // SatelliteB

// Main host carries only shared infrastructure.
builder.Services.AddMarten(opts =>
{
    opts.Connection(connectionString);
    opts.DatabaseSchemaName = "modular_monolith";
});

using var host = builder.Build();
await host.StartAsync();
```

The canonical worked example lives under `src/ModularConfigTests/` in the Marten repo — that's the regression-gate fixture the rest of this page links back to.

## `[assembly: JasperFxAssembly]`

The marker isn't required for Marten's `DiscoverGeneratedEvolvers` to find a satellite's `[GeneratedEvolver]` attributes — that scan walks every loaded assembly in `AppDomain.CurrentDomain.GetAssemblies()` regardless. It IS required for other Critter Stack scanning surfaces (`CommandFactory`, extension discovery). Mark every satellite that participates in modular Marten composition with it for forward-compat with those surfaces.

## Locked-in design contracts

These four behaviors are pinned by the regression-gate fixture in `src/ModularConfigTests/`. Any change that breaks them surfaces in CI.

### 1. Registration order = invocation order

`IEnumerable<IConfigureMarten>` is resolved from DI; `Configure` is invoked in DI registration order. If two satellites both write to the same `StoreOptions` scalar property, the **later-registered** call wins.

```csharp
builder.Services.AddSingleton<IConfigureMarten>(new SetNameLength(100));
builder.Services.AddSingleton<IConfigureMarten>(new SetNameLength(250));
// → final NameDataLength == 250
```

Pin test: `src/ModularConfigTests/OrderingTests.cs`.

### 2. Last-wins on scalar setter conflicts; idempotent on event-type registration

Two satellites registering the same scalar `StoreOptions` setter (`NameDataLength`, `DatabaseSchemaName`, etc.) end up with the last-registered value. Two satellites registering the same event type via `options.Events.AddEventType(typeof(SomeEvent))` is **idempotent** — no exception, the event is registered once.

Projection registration is the exception: two satellites registering the same projection class throws `DuplicateSubscriptionNamesException` at host build. The error message points to the `Name` property to disambiguate; set it explicitly on each satellite's projection class to coexist.

Pin test: `src/ModularConfigTests/LastWinsTests.cs`.

### 3. `AddMarten` timing is order-independent

`IConfigureMarten` registered **after** `services.AddMarten(...)` still applies. The `StoreOptions` factory resolves `IEnumerable<IConfigureMarten>` at store-build time from the final DI snapshot — not at `AddMarten` time. Teams can register their satellite contributions in any order relative to the main `AddMarten` call.

Pin test: `src/ModularConfigTests/AddMartenTimingTests.cs`.

### 4. `IConfigureMarten` and `IAsyncConfigureMarten` compose

A host can mix both. Sync contributions apply during the `StoreOptions` factory's resolution (synchronous, on first `IDocumentStore` resolution). Async contributions apply inside the `AsyncConfigureMartenApplication` hosted service, which is inserted ahead of `MartenActivator` in the `IHostedService` chain — so async configs are visible by the time anything else consumes the store. `AddMarten` registers the hosted service unconditionally (#4494), so bare `AddSingleton<IAsyncConfigureMarten, T>()` works the same as the sync sibling.

| Contract | Bare `AddSingleton<...>` | Extension API |
| --- | --- | --- |
| `IConfigureMarten` | ✅ | `services.AddSingleton<IConfigureMarten, T>()` or `services.ConfigureMarten(...)` |
| `IAsyncConfigureMarten` | ✅ | `services.ConfigureMartenWithServices<T>()` (still available; equivalent to the bare form) |

Pin test: `src/ModularConfigTests/AsyncComposeTests.cs`.

## Required satellite setup checklist

| Step | Why |
| --- | --- |
| `[assembly: JasperFx.JasperFxAssembly]` in an `AssemblyInfo.cs` | Forward-compat with Critter Stack scanning surfaces |
| Projection classes marked `partial` | Post-#276, the SG-emitted dispatcher merges into the projection class via partial; non-partial silently skips SG emission and the runtime fail-fast at `AssembleAndAssertValidity` throws |
| The `JasperFx.Events.SourceGenerator` analyzer runs in the satellite | The runtime looks up `[GeneratedEvolver]` in the assembly that *declares* the aggregate, so the analyzer has to run there. A normal Marten `PackageReference` anywhere in the satellite's reference chain carries it; an analyzer-only `PackageReference` is the fallback when it doesn't |
| Satellite ProjectReference'd from the main host (or referenced via type) | `AppDomain.CurrentDomain.GetAssemblies()` only returns LOADED assemblies. A `typeof(SatelliteType)` reference or an `IConfigureMarten` singleton registration is enough to force the load |

## When the analyzer does not reach a satellite

The symptom is always the same runtime exception, thrown the first time something needs the dispatcher:

```text
JasperFx.Events.Projections.InvalidProjectionException : No source-generated dispatcher found for
Marten.Events.Aggregation.SingleStreamProjection<MySatellite.OrderSummary, System.Guid>. ...
```

Two configurations cut the analyzer off from a satellite while leaving the build perfectly green:

* An intermediate project references Marten with `PrivateAssets="all"`, hiding it from everything downstream.
* Anything in the chain — including a repo-wide `Directory.Build.props` — sets `ExcludeAssets="analyzers"` on Marten.

Both are silent, and the reason is worth understanding: aggregate types are usually plain POCOs that name no Marten type at all, so a project declaring nothing but aggregates compiles cleanly with **0 warnings, 0 errors, and 0 generated evolvers**. The most common place this bites is a test project whose aggregates are defined alongside the tests while Marten is referenced only by the library under test. See [#5495](https://github.com/JasperFx/marten/issues/5495).

To check whether the generator actually ran in a given project, look for it on the compiler command line:

```bash
dotnet build path/to/Satellite.csproj -v:n | grep -o '/analyzer:[^ ]*JasperFx[^ ]*'
```

or set `<EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>` and inspect
`obj/Debug/<tfm>/generated/JasperFx.Events.SourceGenerator/`. Empty output from either means the generator never ran, and the fix is a Marten or analyzer-only `PackageReference` on that project — not a change to the aggregate.

## Out of scope

* NuGet-package distribution scenarios (satellite as a `.nupkg` consumed by downstream apps) are tracked separately. The contracts above hold for ProjectReference-composed assemblies.
* The order of `IConfigureMarten` execution relative to `IAsyncConfigureMarten` execution is not part of the locked contracts — sync configs apply at store-build time, async configs apply during host start. Don't write code that depends on the relative order.

## See also

* The regression fixture: [`src/ModularConfigTests/SmokeTest.cs`](https://github.com/JasperFx/marten/blob/master/src/ModularConfigTests/SmokeTest.cs) (end-to-end)
* The four pin tests: `OrderingTests.cs`, `LastWinsTests.cs`, `AddMartenTimingTests.cs`, `AsyncComposeTests.cs` in the same directory
* [Bootstrapping Marten](./hostbuilder.md) for the basic `AddMarten` shape this page builds on top of
