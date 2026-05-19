# Composite Configuration Across Satellite Assemblies

Marten supports a "modular monolith" deployment shape where projection types, event types, and `StoreOptions` tweaks live in **satellite assemblies** owned by individual feature teams, and a main host composes them together via dependency injection. Each satellite contributes its own `IConfigureMarten` (sync) or `IAsyncConfigureMarten` (async) implementation; the main host's `AddMarten(...)` call carries only shared infrastructure (connection string, default serializer, etc.).

This page documents the contracts that compose those satellites into a single `DocumentStore`.

## The pattern

Each satellite assembly:

1. Carries `[assembly: JasperFx.JasperFxAssembly]` in an `AssemblyInfo.cs` file.
2. Declares its projection classes as `partial`.
3. References `JasperFx.Events.SourceGenerator` as an analyzer-only `PackageReference` so `[GeneratedEvolver]` attributes are emitted at compile time for the satellite's own projection types:

   ```xml
   <PackageReference Include="JasperFx.Events.SourceGenerator"
                     OutputItemType="Analyzer"
                     ReferenceOutputAssembly="false" />
   ```

4. Exposes one or more `IConfigureMarten` / `IAsyncConfigureMarten` implementations that register the satellite's projections, event types, or option tweaks.

The main host:

```csharp
var builder = Host.CreateApplicationBuilder();

// Each satellite's IConfigureMarten gets wired into DI.
builder.Services.AddSingleton<IConfigureMarten, OrdersConfig>();         // SatelliteA
// For IAsyncConfigureMarten, use ConfigureMartenWithServices<T>() so the
// hosted service that drains async configs gets registered. A raw
// AddSingleton<IAsyncConfigureMarten>() would add the type to DI but
// never invoke its Configure method.
builder.Services.ConfigureMartenWithServices<ReportingConfig>();         // SatelliteB

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

A host can mix both. Sync contributions apply during the `StoreOptions` factory's resolution (synchronous, on first `IDocumentStore` resolution). Async contributions apply inside the `AsyncConfigureMartenApplication` hosted service, which is inserted ahead of `MartenActivator` in the `IHostedService` chain — so async configs are visible by the time anything else consumes the store.

The registration APIs are asymmetric:

| Contract | Sync | Async |
| --- | --- | --- |
| Bare `AddSingleton<...>` works | ✅ | ❌ (hosted service not registered) |
| Extension API | `services.AddSingleton<IConfigureMarten, T>()` or `services.ConfigureMarten(...)` | `services.ConfigureMartenWithServices<T>()` |

Pin test: `src/ModularConfigTests/AsyncComposeTests.cs`.

## Required satellite setup checklist

| Step | Why |
| --- | --- |
| `[assembly: JasperFx.JasperFxAssembly]` in an `AssemblyInfo.cs` | Forward-compat with Critter Stack scanning surfaces |
| Projection classes marked `partial` | Post-#276, the SG-emitted dispatcher merges into the projection class via partial; non-partial silently skips SG emission and the runtime fail-fast at `AssembleAndAssertValidity` throws |
| `JasperFx.Events.SourceGenerator` as analyzer-only `PackageReference` | Marten's own csproj sets `PrivateAssets="all"` on the SG so the analyzer doesn't flow transitively. Each satellite that declares its own projection types needs the analyzer wired locally |
| Satellite ProjectReference'd from the main host (or referenced via type) | `AppDomain.CurrentDomain.GetAssemblies()` only returns LOADED assemblies. A `typeof(SatelliteType)` reference or an `IConfigureMarten` singleton registration is enough to force the load |

## Out of scope

* NuGet-package distribution scenarios (satellite as a `.nupkg` consumed by downstream apps) are tracked separately. The contracts above hold for ProjectReference-composed assemblies.
* The order of `IConfigureMarten` execution relative to `IAsyncConfigureMarten` execution is not part of the locked contracts — sync configs apply at store-build time, async configs apply during host start. Don't write code that depends on the relative order.

## See also

* The regression fixture: [`src/ModularConfigTests/SmokeTest.cs`](https://github.com/JasperFx/marten/blob/master/src/ModularConfigTests/SmokeTest.cs) (end-to-end)
* The four pin tests: `OrderingTests.cs`, `LastWinsTests.cs`, `AddMartenTimingTests.cs`, `AsyncComposeTests.cs` in the same directory
* [Bootstrapping Marten](./hostbuilder.md) for the basic `AddMarten` shape this page builds on top of
